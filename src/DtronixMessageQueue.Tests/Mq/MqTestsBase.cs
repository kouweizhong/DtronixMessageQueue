﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace DtronixMessageQueue.Tests.Mq
{
    public class MqTestsBase : IDisposable
    {
        private Random _random = new Random();
        public ITestOutputHelper Output;

        public MqServer<SimpleMqSession, MqConfig> Server { get; protected set; }
        public MqClient<SimpleMqSession, MqConfig> Client { get; protected set; }
        public int Port { get; }

        protected MqConfig Config;

        public Exception LastException { get; set; }

        public TimeSpan TestTimeout { get; set; } = new TimeSpan(0, 0, 0, 0, 2000);

        public ManualResetEventSlim TestStatus { get; set; } = new ManualResetEventSlim(false);

        public MqTestsBase(ITestOutputHelper output)
        {
            Output = output;
            Port = FreeTcpPort();

            Config = new MqConfig
            {
                Ip = "127.0.0.1",
                Port = Port
            };

            Server = new MqServer<SimpleMqSession, MqConfig>(Config);
            Client = new MqClient<SimpleMqSession, MqConfig>(Config);
        }


        public static int FreeTcpPort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint) l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void StartAndWait(bool timeoutError = true, int timeoutLength = -1, bool startServer = true)
        {
            if (startServer && Server.IsRunning == false)
            {
                Server.Start();
            }
            if (Client.IsRunning == false)
            {
                Client.Connect();
            }

            timeoutLength = timeoutLength != -1 ? timeoutLength : (int) TestTimeout.TotalMilliseconds;

            TestStatus.Wait(new TimeSpan(0, 0, 0, 0, timeoutLength));

            if (timeoutError && TestStatus.IsSet == false)
            {
                throw new TimeoutException("Test timed out.");
            }

            if (LastException != null)
            {
                throw LastException;
            }

            try
            {
                Server.Stop();
                Client.Close();
            }
            catch
            {
                // ignored
            }
        }

        public void CompareMessages(MqMessage expected, MqMessage actual)
        {
            try
            {
                // Total frame count comparison.
                Assert.Equal(expected.Count, actual.Count);

                for (int i = 0; i < expected.Count; i++)
                {
                    // Frame length comparison.
                    Assert.Equal(expected[i].DataLength, actual[i].DataLength);

                    Assert.Equal(expected[i].Buffer, actual[i].Buffer);
                }
            }
            catch (Exception e)
            {
                LastException = e;
            }
        }

        public MqMessage GenerateRandomMessage(int frames = -1, int frameLength = -1)
        {
            var frameCount = frames == -1 ? _random.Next(8, 16) : frames;
            var message = new MqMessage();
            for (int i = 0; i < frameCount; i++)
            {
                MqFrame frame;

                if (frameLength == -1)
                {
                    frame = new MqFrame(Utilities.SequentialBytes(_random.Next(50, 1024 * 16 - 3)),
                        (i + 1 < frameCount) ? MqFrameType.More : MqFrameType.Last, Config);
                }
                else
                {
                    frame = new MqFrame(Utilities.SequentialBytes(frameLength),
                        (i + 1 < frameCount) ? MqFrameType.More : MqFrameType.Last, Config);
                }
                message.Add(frame);
            }

            return message;
        }


        public void Dispose()
        {
            try
            {
                Server.Stop();
            }
            catch
            {
            }
            try
            {
                Client.Close();
            }
            catch
            {
            }
        }
    }
}