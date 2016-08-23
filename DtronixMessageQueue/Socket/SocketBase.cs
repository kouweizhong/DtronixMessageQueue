﻿using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using NLog;

namespace DtronixMessageQueue.Socket {
	public abstract class SocketBase<TSession> : IDisposable
		where TSession : SocketSession, new() {

		/// <summary>
		/// This event fires when a connection has been established.
		/// </summary>
		public event EventHandler<SessionChangedEventArgs<TSession>> Connected;

		/// <summary>
		/// This event fires when a connection has been closed.
		/// </summary>
		public event EventHandler<SessionChangedEventArgs<TSession>> Disconnected;

		public SocketConfig Config { get; }

		private static readonly Logger logger = LogManager.GetCurrentClassLogger();

		protected System.Net.Sockets.Socket MainSocket; 
		protected bool IsRunning;

		protected SocketAsyncEventArgsPool AsyncPool;

		protected BufferManager BufferManager;  // represents a large reusable set of buffers for all socket operations


		protected void OnConnect(TSession session) {
			Connected?.Invoke(this, new SessionChangedEventArgs<TSession>(session));
		}


		protected void OnDisconnect(TSession session) {
			Disconnected?.Invoke(this, new SessionChangedEventArgs<TSession>(session));
		}

		protected SocketBase(SocketConfig config) {
			Config = config;
			// allocate buffers such that the maximum number of sockets can have one outstanding read and 
			//write posted to the socket simultaneously  
			BufferManager = new BufferManager(config.SendAndReceiveBufferSize * config.MaxConnections * 2, config.SendAndReceiveBufferSize);

			// Allocates one large byte buffer which all I/O operations use a piece of.  This guards against memory fragmentation.
			BufferManager.InitBuffer();

			// preallocate pool of SocketAsyncEventArgs objects
			AsyncPool = new SocketAsyncEventArgsPool(config.MaxConnections * 2);

			for (var i = 0; i < config.MaxConnections * 2; i++) {
				//Pre-allocate a set of reusable SocketAsyncEventArgs
				var event_arg = new SocketAsyncEventArgs();

				// assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
				BufferManager.SetBuffer(event_arg);

				// add SocketAsyncEventArg to the pool
				AsyncPool.Push(event_arg);
			}

			logger.Debug("SocketBase started with {0} readers/writers.", config.MaxConnections * 2);
		}

		protected TSession CreateSession(System.Net.Sockets.Socket socket) {
			var session = new TSession();
			SocketSession.Setup(session, socket, AsyncPool, Config);

			return session;
		}

		public virtual void Stop() {
			if (IsRunning == false) {
				throw new InvalidOperationException("Server is not running.");
			}
			IsRunning = false;
		}


		public void Dispose() {
			if (IsRunning) {
				Stop();
			}
		}
	}
}
