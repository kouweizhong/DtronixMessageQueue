﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DtronixMessageQueue.Socket {
	/// <summary>
	/// Mode that the current Socket base is in.
	/// </summary>
	public enum SocketMode {

		/// <summary>
		/// Socket base is running in server mode.
		/// </summary>
		Server,

		/// <summary>
		/// Socket base is running in client mode.
		/// </summary>
		Client
	}
}
