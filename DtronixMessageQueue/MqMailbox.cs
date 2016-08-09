﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SuperSocket.SocketBase;

namespace DtronixMessageQueue {

	/// <summary>
	/// Mailbox containing inbox, outbox and the logic to process both.
	/// </summary>
	public class MqMailbox : IDisposable {
		
		/// <summary>
		/// The postmaster for this client/server.
		/// </summary>
		private readonly MqPostmaster postmaster;


		private readonly MqClient client;

		/// <summary>
		/// Client reference.  If this mailbox is run as a server mailbox, this is then null.
		/// </summary>
		public MqClient Client => client;


		private readonly MqSession session;

		/// <summary>
		/// Session reference.  If this mailbox is run as a client mailbox, this is then null.
		/// </summary>
		public MqSession Session => session;

		/// <summary>
		/// Internal framebuilder for this instance.
		/// </summary>
		private readonly MqFrameBuilder frame_builder;

		//private static readonly Logger logger = LogManager.GetCurrentClassLogger();

		/// <summary>
		/// Total bytes the inbox has remaining to process.
		/// </summary>
		private int inbox_byte_count;

		/// <summary>
		/// Reference to the current message being processed by the inbox.
		/// </summary>
		private MqMessage message;

		/// <summary>
		/// Outbox message queue.  Internally used to store Messages before being sent to the wire by the postmaster.
		/// </summary>
		private readonly ConcurrentQueue<MqMessage> outbox = new ConcurrentQueue<MqMessage>();

		/// <summary>
		/// Inbox byte queue.  Internally used to store the raw frame bytes before while waiting to be processed by the postmaster.
		/// </summary>
		private readonly ConcurrentQueue<byte[]> inbox_bytes = new ConcurrentQueue<byte[]>();

		/// <summary>
		/// Event fired when a new message has been processed by the postmaster and ready to be read.
		/// </summary>
		public event EventHandler<IncomingMessageEventArgs> IncomingMessage;

		/// <summary>
		/// Inbox to containing new messages received.
		/// </summary>
		public ConcurrentQueue<MqMessage> Inbox { get; } = new ConcurrentQueue<MqMessage>();

		/// <summary>
		/// Initializes a new instance of the MqMailbox class.
		/// </summary>
		/// <param name="postmaster">Reference to the postmaster for this instance.</param>
		/// <param name="session">Session from the server for this instance.</param>
		public MqMailbox(MqPostmaster postmaster, MqSession session) {
			this.postmaster = postmaster;
			this.session = session;
			frame_builder = new MqFrameBuilder(postmaster.MaxFrameSize);
		}

		/// <summary>
		/// Initializes a new instance of the MqMailbox class.
		/// </summary>
		/// <param name="postmaster">Reference to the postmaster for this instance.</param>
		/// <param name="client">Client reference for this instance.</param>
		public MqMailbox(MqPostmaster postmaster, MqClient client) {
			this.postmaster = postmaster;
			this.client = client;
			frame_builder = new MqFrameBuilder(postmaster.MaxFrameSize);
		}

		/// <summary>
		/// Adds bytes from the client/server reading methods to be processed by the postmaster.
		/// </summary>
		/// <param name="buffer">Buffer of bytes to read. Does not copy the bytes to the buffer.</param>
		internal void EnqueueIncomingBuffer(byte[] buffer) {
			inbox_bytes.Enqueue(buffer);

			postmaster.SignalRead(this);

			Interlocked.Add(ref inbox_byte_count, buffer.Length);
		}


		/// <summary>
		/// Adds a message to the outbox to be processed by the postmaster.
		/// </summary>
		/// <param name="message">Message to send.</param>
		internal void EnqueueOutgoingMessage(MqMessage message) {
			outbox.Enqueue(message);

			// Signal the workers that work is to be done.
			postmaster.SignalWrite(this);
		}

		
		/// <summary>
		/// Sends a queue of bytes to the connected client/server.
		/// </summary>
		/// <param name="buffer_queue">Queue of bytes to send to the wire.</param>
		/// <param name="length">Total length of the bytes in the queue to send.</param>
		private void SendBufferQueue(Queue<byte[]> buffer_queue, int length) {
			var buffer = new byte[length + 3];

			// Setup the header for the packet.
			var length_bytes = BitConverter.GetBytes((ushort) length);
			buffer[0] = 0;
			buffer[1] = length_bytes[0];
			buffer[2] = length_bytes[1];

			var offset = 3;

			while (buffer_queue.Count > 0) {
				var bytes = buffer_queue.Dequeue();
				Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);

				// Increment the offset.
				offset += bytes.Length;
			}

			if (client != null) {
				client.Send(buffer);
			} else {
				session.Send(buffer, 0, buffer.Length);
			}
		}


		/// <summary>
		/// Internally called method by the postmaster on a different thread to send all messages in the outbox.
		/// </summary>
		internal void ProcessOutbox() {
			MqMessage result;
			var length = 0;
			var buffer_queue = new Queue<byte[]>();

			while (outbox.TryDequeue(out result)) {
				foreach (var frame in result.Frames) {
					var frame_size = frame.FrameSize;
					// If this would overflow the max client buffer size, send the full buffer queue.
					if (length + frame_size > postmaster.MaxFrameSize + 3) {
						SendBufferQueue(buffer_queue, length);

						// Reset the length to 0;
						length = 0;
					}
					buffer_queue.Enqueue(frame.RawFrame());

					// Increment the total buffer length.
					length += frame_size;
				}
			}

			if (buffer_queue.Count > 0) {
				// Send the last of the buffer queue.
				SendBufferQueue(buffer_queue, length);
			}
		}

		/// <summary>
		/// Internal method called by the postmaster on a different thread to process all bytes in the inbox.
		/// </summary>
		internal async void ProcessIncomingQueue() {
			if (message == null) {
				message = new MqMessage();
			}

			bool new_message = false;
			byte[] buffer;
			while (inbox_bytes.TryDequeue(out buffer)) {
				// Update the total bytes this 
				Interlocked.Add(ref inbox_byte_count, -buffer.Length);

				try {
					frame_builder.Write(buffer, 0, buffer.Length);
				} catch (InvalidDataException) {
					//logger.Error(ex, "Connector {0}: Client send invalid data.", Connection.Id);

					if (client != null) {
						await client.Close();
					} else {
						session.Close(CloseReason.ApplicationError);
					}

					break;
				}

				var frame_count = frame_builder.Frames.Count;
				//logger.Debug("Connector {0}: Parsed {1} frames.", Connection.Id, frame_count);

				for (var i = 0; i < frame_count; i++) {
					var frame = frame_builder.Frames.Dequeue();
					message.Add(frame);

					if (frame.FrameType != MqFrameType.EmptyLast && frame.FrameType != MqFrameType.Last) {
						continue;
					}
					Inbox.Enqueue(message);
					message = new MqMessage();
					new_message = true;
				}
			}
			if (new_message) {
				IncomingMessage?.Invoke(this, new IncomingMessageEventArgs(this));
			}
		}

		/// <summary>
		/// Releases all resources held by this object.
		/// </summary>
		public void Dispose() {
			IncomingMessage = null;
		}
	}
}