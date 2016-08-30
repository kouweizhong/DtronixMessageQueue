﻿using DtronixMessageQueue.Socket;

namespace DtronixMessageQueue {
	public class MqSocketConfig : SocketConfig {

		/// <summary>
		/// Max size of the frame.  Needs to be equal or smaller than SendAndReceiveBufferSize.
		/// Need to exclude the header length for a frame.
		/// </summary>
		public int FrameBufferSize { get; set; } = 1024 * 16 - MqFrame.HeaderLength;

		/// <summary>
		/// (Client) 
		/// Milliseconds between pings.
		/// 0 disables pings.
		/// </summary>
		public int PingFrequency { get; set; } = 0;

		/// <summary>
		/// (Server)
		/// Max milliseconds since the last received packet before the session is disconnected.
		/// 0 disables the automatic disconnection functionality.
		/// </summary>
		public int PingTimeout { get; set; } = 60000;


		/// <summary>
		/// (Server)
		/// Max number of workers used to read/write.
		/// </summary>
		/// <remarks>
		/// Value of 20 would make a maximum of 20 readers and 20 writers.  Total of 40 workers.
		/// </remarks>
		public int MaxReadWriteWorkers { get; set; } = 20;
	}
}