using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DtronixMessageQueue.Tests.Performance {
	public class MqTest {
		private readonly string description;

		private enum State {
			Idle,
			Ready,
			Running,
			Complete
		}

		private readonly int id;
		public readonly MqClient<SimpleMqSession, MqConfig> Client;
		public readonly SimpleMqSession Server;
		private readonly Action<MqTest, MqMessage> run;
		private readonly Action<MqTest> complete;
		private readonly Stopwatch stopwatch;
		private long total_server_bytes;
		private int total_server_messages;
		private long total_server_elapsed;

		public object Tag;

		private State TestState;

		public MqMessageReader message_reader = new MqMessageReader();
		public MqMessageWriter message_writer;

		public MqTest(int id,
			string description,
			MqClient<SimpleMqSession, MqConfig> client,
			Action<MqTest, MqMessage> run,
			Action<MqTest> complete) : this(id, client, null, run, complete) {
			this.description = description;
		}

		public MqTest(int id,
			SimpleMqSession server,
			Action<MqTest, MqMessage> run) : this(id, null, server, run, null) {
		}

		private MqTest(int id,
			MqClient<SimpleMqSession, MqConfig> client,
			SimpleMqSession server,
			Action<MqTest, MqMessage> run,
			Action<MqTest> complete) {

			TestState = State.Idle;
			this.id = id;
			Client = client;
			Server = server;
			this.run = run;
			this.complete = complete;
			stopwatch = new Stopwatch();

			message_writer = new MqMessageWriter(client.Config);
		}

		public void Start() {
			if (TestState != State.Idle) {
				throw new InvalidOperationException($"Test is already in the {TestState} state.");
			}

			if (Client != null) {
				Client.IncomingMessage += OnIncomingMessage;
			} else {
				Server.IncomingMessage += OnIncomingMessage;
				message_writer.Write("READY");
				message_writer.Write(id);
				Server.Send(message_writer.ToMessage());
			}

		}

		/// <summary>
		/// Invoked on the server then a test is completed.
		/// </summary>
		public void Complete() {
			Server.IncomingMessage -= OnIncomingMessage;
			message_writer.Write("COMPLETE");
			message_writer.Write(id);
			message_writer.Write(total_server_bytes);
			message_writer.Write(total_server_messages);
			message_writer.Write(stopwatch.ElapsedMilliseconds);
			Server.Send(message_writer.ToMessage());

		}

		private void OnIncomingMessage(object sender, IncomingMessageEventArgs<SimpleMqSession, MqConfig> args) {

			MqMessage msg;
			while (args.Messages.Count > 0) {
				msg = args.Messages.Dequeue();
				message_reader.Message = msg;
				total_server_messages++;

				// Count the total number of bytes received.
				if (Server != null) {
					total_server_bytes += msg.Size;
				}
				

				if (TestState == State.Running) {
					run(this, msg);
				} else if (message_reader.ReadString() == "READY") {
					run(this, msg);
				} else if (message_reader.ReadString() == "COMPLETE") {
					stopwatch.Stop();
					Client.IncomingMessage -= OnIncomingMessage;

					message_reader.Skip(4); // Skip the ID
					total_server_bytes = message_reader.ReadInt64();
					total_server_messages = message_reader.ReadInt32();
					total_server_elapsed = message_reader.ReadInt32();

					var messages_per_second = (int)((double)total_messages / stopwatch.ElapsedMilliseconds * 1000);
					var msg_size_no_header = message_size;
					var mbps = total_messages * (double)(msg_size_no_header) / stopwatch.ElapsedMilliseconds / 1000;
					Console.WriteLine("| {0,10:N0} | {1,9:N0} | {2,12:N0} | {3,10:N0} | {4,8:N2} |", total_messages,
						msg_size_no_header, stopwatch.ElapsedMilliseconds, messages_per_second, mbps);

					total_values[0] += stopwatch.ElapsedMilliseconds;
					total_values[1] += messages_per_second;
					total_values[2] += mbps; */


			 Console.WriteLine("| {0,10:N0} | {1,9:N0} | {2,12:N0} | {3,10:N0} | {4,8:N2} |", description,
								msg_size_no_header, stopwatch.ElapsedMilliseconds, messages_per_second, mbps);
				}

			}
		}			

		private void OnComplete() {
			complete?.Invoke(this);
		}
	}
}
