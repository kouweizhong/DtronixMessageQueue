﻿using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DtronixMessageQueue;
using DtronixMessageQueue.Socket;
using Xunit;
using Xunit.Abstractions;

namespace DtronixMessageQueue.Tests {
	public class MqTestsBase : IDisposable {
		private Random random = new Random();
		public ITestOutputHelper Output;

		public MqServer Server { get; }
		public MqClient Client { get; }
		public int Port { get; }

		private MqSocketConfig config;

		public Exception LastException { get; set; }

		public TimeSpan TestTimeout { get; set; } = new TimeSpan(0, 0, 60);

		public ManualResetEventSlim TestStatus { get; set; } = new ManualResetEventSlim(false);

		public MqTestsBase(ITestOutputHelper output) {
			this.Output = output;
			Port = FreeTcpPort();

			config = new MqSocketConfig {
				Ip = "127.0.0.1",
				Port = Port
			};

			Server = new MqServer(config);


			Client = new MqClient(config);
		}

		static int FreeTcpPort() {
			TcpListener l = new TcpListener(IPAddress.Loopback, 0);
			l.Start();
			int port = ((IPEndPoint) l.LocalEndpoint).Port;
			l.Stop();
			return port;
		}

		public void StartAndWait() {
			Server.Start();
			Client.Connect();


			TestStatus.Wait(TestTimeout);

			if (TestStatus.IsSet == false) {
				throw new TimeoutException("Test timed out.");
			}

			if (LastException != null) {
				throw LastException;
			}

			try {
				Server.Stop();
				Client.Close();
			} catch {
				// ignored
			}
		}

		public void CompareMessages(MqMessage expected, MqMessage actual) {
			try {


				// Total frame count comparison.
				Assert.Equal(expected.Count, actual.Count);

				for (int i = 0; i < expected.Count; i++) {
					// Frame length comparison.
					Assert.Equal(expected[i].DataLength, actual[i].DataLength);

					Assert.Equal(expected[i].Buffer, actual[i].Buffer);
				}
			} catch (Exception e) {
				LastException = e;
			}
		}

		public MqMessage GenerateRandomMessage(int frames = -1, int frame_length = -1) {
			var frame_count = frames == -1 ? random.Next(8, 16) : frames;
			var message = new MqMessage();
			for (int i = 0; i < frame_count; i++) {
				MqFrame frame;

				if (frame_length == -1) {
					frame = new MqFrame(Utilities.SequentialBytes(random.Next(50, 1024*16 - 3)),
						(i + 1 < frame_count) ? MqFrameType.More : MqFrameType.Last, config);
				} else {
					frame = new MqFrame(Utilities.SequentialBytes(frame_length),
						(i + 1 < frame_count) ? MqFrameType.More : MqFrameType.Last, config);
				}
				message.Add(frame);
			}

			return message;

		}


		public void Dispose() {
			try {
				Server.Stop();
			} catch { }
			try {
				Client.Close();
			} catch { }
			
		}
	}
}