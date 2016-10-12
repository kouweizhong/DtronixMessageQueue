﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DtronixMessageQueue.Socket;
using DtronixMessageQueue.Tests.Rpc.Services.Server;
using Xunit;
using Xunit.Abstractions;

namespace DtronixMessageQueue.Tests.Rpc {
	public class RpcClientTests : RpcTestsBase {


		public RpcClientTests(ITestOutputHelper output) : base(output) {

		}

		public class Test {
			public string TestStr { get; set; }
			public int Length { get; set; }

		}

		[Fact]
		public void Client_calls_proxy_method() {

			Server.Ready += (sender, args) => {
				args.Session.AddService(new CalculatorService());
			};

			Client.Authenticate += (sender, args) => {

			};


			Client.Ready += (sender, args) => {
				args.Session.AddProxy<ICalculatorService>(new CalculatorService());
				var service = Client.Session.GetProxy<ICalculatorService>();
				var result = service.Add(100, 200);

				if (result != 300) {
					LastException = new Exception("Service returned wrong result.");
				}

				TestStatus.Set();
			};

			StartAndWait();
		}

		[Fact]
		public void Client_calls_proxy_method_sequential() {

			Server.SessionSetup += (sender, args) => {
				args.Session.AddService(new CalculatorService());
			};


			Client.Ready += (sender, args) => {
				args.Session.AddProxy<ICalculatorService>(new CalculatorService());
				var service = Client.Session.GetProxy<ICalculatorService>();
				Stopwatch stopwatch = Stopwatch.StartNew();

				int added_int = 0;
				for (int i = 0; i < 10; i++) {
					added_int = service.Add(added_int, 1);
				}

				Output.WriteLine($"{stopwatch.ElapsedMilliseconds}");
				TestStatus.Set();
			};

			StartAndWait();
		}

		[Fact]
		public void Client_calls_proxy_method_and_canceles() {

			Server.SessionSetup += (sender, args) => {
				var service = new CalculatorService();
				args.Session.AddService<ICalculatorService>(service);

				service.LongRunningTaskCanceled += (o, event_args) => {
					TestStatus.Set();
				};
			};


			Client.Ready += (sender, args) => {
				args.Session.AddProxy<ICalculatorService>(new CalculatorService());
				var service = Client.Session.GetProxy<ICalculatorService>();
				var token_source = new CancellationTokenSource();

				
				bool threw = false;
				try {
					token_source.CancelAfter(500);
					service.LongRunningTask(1, 2, token_source.Token);
				} catch (OperationCanceledException) {
					threw = true;
				}

				if (threw != true) {
					LastException = new Exception("Operation did not cancel.");
				}


			};

			StartAndWait();
		}

		[Fact]
		public void Server_requests_authentication() {

			Server.Config.RequireAuthentication = true;


			Client.Authenticate += (sender, e) => {
				TestStatus.Set();
			};


			StartAndWait();
		}

		[Fact]
		public void Server_does_not_request_authentication() {

			Server.Config.RequireAuthentication = false;

			Server.SessionSetup += (sender, args) => {
				args.Session.AddService<ICalculatorService>(new CalculatorService());
			};

			Client.Authenticate += (sender, e) => {

			};


			Client.Ready += (sender, e) => {
				e.Session.AddProxy<ICalculatorService>(new CalculatorService());
				var service = Client.Session.GetProxy<ICalculatorService>();

				var result = service.Add(100, 200);

				if (result != 300) {
					LastException = new Exception("Client authenticated.");
				}
				TestStatus.Set();
			};


			StartAndWait();
		}


		[Fact]
		public void Server_verifies_authentication() {
			var auth_data = new byte[] { 1, 2, 3, 4, 5 };
			
			Server.Config.RequireAuthentication = true;

			Server.SessionSetup += (sender, args) => {
				args.Session.AddService<ICalculatorService>(new CalculatorService());
			};

			Server.Authenticate += (sender, e) => {
				try {
					Assert.Equal(auth_data, e.AuthData);
				} catch (Exception ex) {
					LastException = ex;
				} finally {
					TestStatus.Set();
				}
				
			};

			Client.Authenticate += (sender, e) => {
				e.AuthData = auth_data;
			};


			StartAndWait();
		}

		[Fact]
		public void Server_disconnectes_from_failed_authentication() {
			Server.Config.RequireAuthentication = true;

			Server.SessionSetup += (sender, args) => {
				args.Session.AddService<ICalculatorService>(new CalculatorService());
			};

			Server.Authenticate += (sender, e) => {
				e.Authenticated = false;

			};

			Server.Closed += (sender, e) => {
				if (e.CloseReason != SocketCloseReason.AuthenticationFailure) {
					LastException = new Exception("Server closed session for invalid reason");
				}
				TestStatus.Set();
			};

			Client.Authenticate += (sender, e) => {
				e.AuthData = new byte[] {5, 4, 3, 2, 1};
			};

			StartAndWait();
		}

		[Fact]
		public void Client_disconnectes_from_failed_authentication() {
			Server.Config.RequireAuthentication = true;

			Server.SessionSetup += (sender, args) => {
				args.Session.AddService<ICalculatorService>(new CalculatorService());
			};

			Server.Authenticate += (sender, e) => {
				e.Authenticated = false;

			};

			Client.Closed += (sender, e) => {
				if (e.CloseReason != SocketCloseReason.AuthenticationFailure) {
					LastException = new Exception("Server closed session for invalid reason");
				}
				TestStatus.Set();
			};

			Client.Authenticate += (sender, e) => {
				e.AuthData = new byte[] { 5, 4, 3, 2, 1 };
			};

			StartAndWait();
		}

		[Fact]
		public void Client_notified_of_authentication_failure() {
			Server.Config.RequireAuthentication = true;

			Server.SessionSetup += (sender, args) => {
				args.Session.AddService<ICalculatorService>(new CalculatorService());
			};

			Server.Authenticate += (sender, e) => {
				e.Authenticated = false;
			};

			Client.AuthenticationResult += (sender, e) => {
				if (e.Authenticated) {
					LastException = new Exception("Client notified of authentication wrongly.");
				}
				TestStatus.Set();
			};

			Client.Closed += (sender, e) => {

			};

			Client.Authenticate += (sender, e) => {
				e.AuthData = new byte[] {5, 4, 3, 2, 1};
			};

			StartAndWait();
		}


		[Fact]
		public void Client_notified_of_authentication_success() {
			Server.Config.RequireAuthentication = true;

			Server.SessionSetup += (sender, args) => {
				args.Session.AddService<ICalculatorService>(new CalculatorService());
			};

			Server.Authenticate += (sender, e) => {
				e.Authenticated = true;
			};

			Client.AuthenticationResult += (sender, e) => {
				if (e.Authenticated == false) {
					LastException = new Exception("Client notified of authentication wrongly.");
				}
				TestStatus.Set();
			};

			Client.Authenticate += (sender, e) => {
				e.AuthData = new byte[] { 5, 4, 3, 2, 1 };
			};

			StartAndWait();
		}

		[Fact]
		public void Client_times_out_on_auth_failure() {
			Server.Config.RequireAuthentication = true;
			Server.Config.ConnectionTimeout = 100;
			bool auth_failure_called = false;

			Client.AuthenticationResult += (sender, e) => {
				auth_failure_called = !e.Authenticated;
			};

			Client.Closed += (sender, e) => {
				if (auth_failure_called == false) {
					LastException = new Exception("Client was not notified that the authentication failed.");
				}
				if (e.CloseReason != SocketCloseReason.AuthenticationFailure) {
					LastException = new Exception("Client was disconnected for invalid reason.");
				}
				TestStatus.Set();
			};

			Client.Authenticate += (sender, e) => {
				Thread.Sleep(200);
			};

			StartAndWait();
		}

	}
}
