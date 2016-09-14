﻿namespace DtronixMessageQueue.Socket {
	/// <summary>
	/// Configurations for the server/client.
	/// </summary>
	public class SocketConfig {

		/// <summary>
		/// Maximum number of connections allowed.  Only used by the server.
		/// </summary>
		public int MaxConnections { get; set; } = 1000;

		/// <summary>
		/// Maximum backlog for pending connections.
		/// The default value is 100.
		/// </summary>
		public int ListenerBacklog { get; set; } = 100;

		/// <summary>
		/// Size of the buffer for the sockets.
		/// </summary>
		public int SendAndReceiveBufferSize { get; set; } = 1024*16;

		/// <summary>
		/// Time in milliseconds it takes for the sending event to fail.
		/// </summary>
		public int SendTimeout { get; set; } = 5000;

		/// <summary>
		/// Time in milliseconds it takes to timeout a connection attempt.
		/// </summary>
		public int ConnectionTimeout { get; set; } = 60000;

		/// <summary>
		/// IP address to bind or connect to.
		/// </summary>
		public string Ip { get; set; }

		/// <summary>
		/// Port to bind or connect to.
		/// </summary>
		public int Port { get; set; }
	}
}