﻿using System;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Threading;

namespace DtronixMessageQueue.Socket {

	/// <summary>
	/// Base socket session to be sub-classes by the implementer.
	/// </summary>
	public abstract class SocketSession<TConfig> : IDisposable
		where TConfig : SocketConfig {

		/// <summary>
		/// Current state of the socket.
		/// </summary>
		public enum State : byte {
			/// <summary>
			/// State has not been set.
			/// </summary>
			Unknown,

			/// <summary>
			/// Session is attempting to connect to remote connection.
			/// </summary>
			Connecting,

			/// <summary>
			/// Session has connected to remote session.
			/// </summary>
			Connected,

			/// <summary>
			/// Session is in the process of closing its connection.
			/// </summary>
			Closing,

			/// <summary>
			/// Session has been closed and no longer can be used.
			/// </summary>
			Closed,

			/// <summary>
			/// Socket is in an error state.
			/// </summary>
			Error
		}
		private TConfig config;

		/// <summary>
		/// Configurations for the associated socket.
		/// </summary>
		public TConfig Config => config;

		/// <summary>
		/// Id for this session
		/// </summary>
		public Guid Id { get; }

		/// <summary>
		/// State that this socket is in.  Can only perform most operations when the socket is in a Connected state.
		/// </summary>
		public State CurrentState { get; protected set; }

		/// <summary>
		/// The last time that this session received a message.
		/// </summary>
		private DateTime last_received = DateTime.UtcNow;

		/// <summary>
		/// Last time the session received anything from the socket.  Time in UTC.
		/// </summary>
		public DateTime LastReceived => last_received;

		private System.Net.Sockets.Socket socket;

		/// <summary>
		/// Raw socket for this session.
		/// </summary>
		public System.Net.Sockets.Socket Socket => socket;

		/// <summary>
		/// Async args used to send data to the wire.
		/// </summary>
		private SocketAsyncEventArgs send_args;

		/// <summary>
		/// Async args used to receive data off the wire.
		/// </summary>
		private SocketAsyncEventArgs receive_args;

		/// <summary>
		/// Pool used by all the sessions on this SocketBase.
		/// </summary>
		private SocketAsyncEventArgsPool args_pool;

		/// <summary>
		/// Reset event used to ensure only one MqWorker can write to the socket at a time.
		/// </summary>
		private SemaphoreSlim write_semaphore;

		/// <summary>
		/// This event fires when a connection has been established.
		/// </summary>
		public event EventHandler<SessionConnectedEventArgs<SocketSession<TConfig>, TConfig>> Connected;

		/// <summary>
		/// This event fires when a connection has been shutdown.
		/// </summary>
		public event EventHandler<SessionClosedEventArgs<SocketSession<TConfig>, TConfig>> Closed;

		/// <summary>
		/// Creates a new socket session with a new Id.
		/// </summary>
		protected SocketSession() {
			Id = Guid.NewGuid();
			CurrentState = State.Connecting;
		}

		/// <summary>
		/// Sets up this socket with the specified configurations.
		/// </summary>
		/// <param name="session">The session to setup.</param>
		/// <param name="socket">Socket this session is to use.</param>
		/// <param name="args_pool">Argument pool for this session to use.  Pulls two asyncevents for reading and writing and returns them at the end of this socket's life.</param>
		/// <param name="config">Socket configurations this session is to use.</param>
		public static void Setup(SocketSession<TConfig> session, System.Net.Sockets.Socket socket, SocketAsyncEventArgsPool args_pool, TConfig config) {
			session.config = config;
			session.args_pool = args_pool;
			session.send_args = args_pool.Pop();
			session.send_args.Completed += session.IoCompleted;
			session.receive_args = args_pool.Pop();
			session.receive_args.Completed += session.IoCompleted;

			session.socket = socket;
			session.write_semaphore = new SemaphoreSlim(1, 1);

			if(config.SendTimeout > 0)
				socket.SendTimeout = config.SendTimeout;

			if (config.SendAndReceiveBufferSize > 0)
				socket.ReceiveBufferSize = config.SendAndReceiveBufferSize;

			if (config.SendAndReceiveBufferSize > 0)
				socket.SendBufferSize = config.SendAndReceiveBufferSize;

			socket.NoDelay = true;
			socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);

			session.CurrentState = State.Connecting;

			session.OnSetup();
		}

		internal void Start() {
			if (CurrentState == State.Connecting) {
				// Start receiving data.
				socket.ReceiveAsync(receive_args);
			}
		}

		/// <summary>
		/// Called after the initial setup has occurred on the session.
		/// </summary>
		protected virtual void OnSetup() {

		}

		/// <summary>
		/// Called when this session is connected to the socket.
		/// </summary>
		protected void OnConnected() {
			//logger.Info("Session {0}: Connected", Id);
			Connected?.Invoke(this, new SessionConnectedEventArgs<SocketSession<TConfig>, TConfig>(this));
		}

		/// <summary>
		/// Called when this session is disconnected from the socket.
		/// </summary>
		/// <param name="reason">Reason this socket is disconnecting</param>
		protected void OnDisconnected(SocketCloseReason reason) {
			Closed?.Invoke(this, new SessionClosedEventArgs<SocketSession<TConfig>, TConfig>(this, reason));
		}

		/// <summary>
		/// Overridden to parse incoming bytes from the wire.
		/// </summary>
		/// <param name="buffer">Buffer of bytes to parse.</param>
		protected abstract void HandleIncomingBytes(byte[] buffer);

		/// <summary>
		/// This method is called whenever a receive or send operation is completed on a socket 
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e">SocketAsyncEventArg associated with the completed receive operation</param>
		protected virtual void IoCompleted(object sender, SocketAsyncEventArgs e) {
			// determine which type of operation just completed and call the associated handler
			switch (e.LastOperation) {
				case SocketAsyncOperation.Connect:
					OnConnected();
					break;

				case SocketAsyncOperation.Disconnect:
					Close(SocketCloseReason.ClientClosing);
					break;

				case SocketAsyncOperation.Receive:
					RecieveComplete(e);

					break;

				case SocketAsyncOperation.Send:
					SendComplete(e);
					break;

				default:
					throw new ArgumentException("The last operation completed on the socket was not a receive, send connect or disconnect.");
			}
		}

		/// <summary>
		/// Sends raw bytes to the socket.  Blocks until data is sent.
		/// </summary>
		/// <param name="buffer">Buffer bytes to send.</param>
		/// <param name="offset">Offset in the buffer.</param>
		/// <param name="length">Total bytes to send.</param>
		protected void Send(byte[] buffer, int offset, int length) {
			if (Socket == null || Socket.Connected == false) {
				return;
			}
			write_semaphore.Wait(-1);

			// Copy the bytes to the block buffer
			Buffer.BlockCopy(buffer, offset, send_args.Buffer, send_args.Offset, length);

			//logger.Debug("Session {0}: Sending {1} bytes", Id, length);

			// Update the buffer length.
			send_args.SetBuffer(send_args.Offset, length);

			try {
				if (Socket.SendAsync(send_args) == false) {
					IoCompleted(this, send_args);
				}
			} catch (ObjectDisposedException) {
				Close(SocketCloseReason.SocketError);
			}
		}


		/// <summary>
		/// This method is invoked when an asynchronous send operation completes.  
		/// The method issues another receive on the socket to read any additional data sent from the client
		/// </summary>
		/// <param name="e">Event args of this action.</param>
		private void SendComplete(SocketAsyncEventArgs e) {
			if (e.SocketError != SocketError.Success) {
				Close(SocketCloseReason.SocketError);
			}
			write_semaphore.Release(1);
		}

		/// <summary>
		/// This method is invoked when an asynchronous receive operation completes. 
		/// If the remote host closed the connection, then the socket is closed.
		/// </summary>
		/// <param name="e">Event args of this action.</param>
		protected void RecieveComplete(SocketAsyncEventArgs e) {
			if (CurrentState == State.Closing) {
				return;
			}
			if (e.BytesTransferred == 0 && CurrentState == State.Connected) {
				CurrentState = State.Closing;
				return;
			}
			if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success) {

				//logger.Debug("Session {0}: Received {1} bytes", Id, e.BytesTransferred);

				// Update the last time this session was active to prevent timeout.
				last_received = DateTime.UtcNow;

				// Create a copy of these bytes.
				var buffer = new byte[e.BytesTransferred];

				Buffer.BlockCopy(e.Buffer, e.Offset, buffer, 0, e.BytesTransferred);

				HandleIncomingBytes(buffer);

				try {
					// Re-setup the receive async call.
					if (Socket.ReceiveAsync(e) == false) {
						IoCompleted(this, e);
					}
				} catch (ObjectDisposedException) {
					Close(SocketCloseReason.SocketError);
				}


			} else {
				Close(SocketCloseReason.SocketError);
			}
		}

		/// <summary>
		/// Called when this session is desired or requested to be closed.
		/// </summary>
		/// <param name="reason">Reason this socket is closing.</param>
		public virtual void Close(SocketCloseReason reason) {
			//logger.Info("Session {0}: Closing. Reason: {1}", Id, reason);

			// If this session has already been closed, nothing more to do.
			if (CurrentState == State.Closed) {
				return;
			}

			// close the socket associated with the client
			try {
				Socket.Close(1000);
			} catch (Exception) {
				// ignored
			}

			// Free the SocketAsyncEventArg so they can be reused by another client
			args_pool.Push(send_args);
			args_pool.Push(receive_args);

			send_args.Completed -= IoCompleted;
			receive_args.Completed -= IoCompleted;


			// Notify the session has been closed.
			OnDisconnected(reason);

			CurrentState = State.Closed;
		}

		/// <summary>
		/// Disconnects client and releases resources.
		/// </summary>
		public void Dispose() {
			if (CurrentState == State.Connected) {
				Close(SocketCloseReason.ClientClosing);
			}
		}
	}
}
