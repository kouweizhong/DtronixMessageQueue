﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DtronixMessageQueue.Rpc;
using DtronixMessageQueue.Socket;
using DtronixMessageQueue.Tests.Gui.Tests;

namespace DtronixMessageQueue.Tests.Gui.Services
{
    class ControllerService : IControllerService
    {
      
        public string Name { get; } = "ControllerService";
        public ControllerSession Session { get; set; }
        private RpcServer<ControllerSession, RpcConfig> _server;

        private List<MqClient<ConnectionPerformanceTestSession, MqConfig>> _connectionTestClientList;
        private PerformanceTest TestBase;

        public ControllerService(PerformanceTest testBase)
        {
            TestBase = testBase;
            _connectionTestClientList = new List<MqClient<ConnectionPerformanceTestSession, MqConfig>>();
        }

        public void ClientReady()
        {
            if (Session.BaseSocket.Mode == SocketMode.Server && _server == null)
            {
                _server = (RpcServer<ControllerSession, RpcConfig>) this.Session.BaseSocket;
            }
        }



        public void StartConnectionTest(int clients, int packageLength, int perioid)
        {

            TestBase.Log("Started Connection Test");

            for (int i = 0; i < clients; i++)
            {
                var client = new MqClient<ConnectionPerformanceTestSession, MqConfig>(new MqConfig
                {
                    Ip = Session.Config.Ip,
                    Port = 2121,
                    PingFrequency = 3000
                });

                client.Connected += (sender, args) =>
                {
                    args.Session.ConfigTest(packageLength);
                    TestBase.Log("Connection test client connected.");

                    ConnectionTestLog();
                    args.Session.StartTest();
                };

                client.Closed += (sender, args) =>
                {
                    ConnectionTestLog();
                };

                TestBase.Log("Connection test client connecting...");
                client.Connect();

                _connectionTestClientList.Add(client);
            }
        }

        private void ConnectionTestLog()
        {
            TestBase.ClearLog();
            TestBase.Log($"Total Connections: {_connectionTestClientList.Count}");
        }

        public void StopConnectionTest()
        {
            if (_connectionTestClientList == null)
                return;

            foreach (var mqClient in _connectionTestClientList)
            {
                mqClient.Close();
            }


        }

        public void StopTest()
        {
            _connectionTestClientList.Clear();
            foreach (var mqClient in _connectionTestClientList)
            {
                mqClient.Close();
            }
        }
    }
}