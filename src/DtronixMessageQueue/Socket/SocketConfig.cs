﻿namespace DtronixMessageQueue.Socket
{
    /// <summary>
    /// Configurations for the server/client.
    /// </summary>
    public class SocketConfig
    {
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
        public int SendAndReceiveBufferSize { get; set; } = 1024 * 16;

        /// <summary>
        /// Time in milliseconds it takes for the sending event to fail.
        /// </summary>
        public int SendTimeout { get; set; } = 5000;

        /// <summary>
        /// (Client) Time in milliseconds it takes to timeout a connection attempt.
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

        /// <summary>
        /// Time it takes for the thread pool to timeout idle threads.
        /// </summary>
        public int ThreadPoolTimeout { get; set; } = 60000;

        /// <summary>
        /// Minimum number of threads used to read and write.
        /// </summary>
        public int MinWorkingThreads { get; set; } = 2;

        /// <summary>
        /// Maximum number of threads used to read and write.
        /// </summary>
        public int MaxWorkingThreads { get; set; } = 20;

        /// <summary>
        /// (Server/Client)
        /// Max milliseconds since the last received packet before the session is disconnected.
        /// 0 disables the automatic disconnection functionality.
        /// </summary>
        public int PingTimeout { get; set; } = 60000;
    }
}