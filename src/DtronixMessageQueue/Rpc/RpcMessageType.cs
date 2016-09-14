﻿namespace DtronixMessageQueue.Rpc {

	/// <summary>
	/// Type of message which is being sent.
	/// </summary>
	public enum RpcMessageType : byte {

		/// <summary>
		/// Unknown default type.
		/// </summary>
		Unset = 0,

		/// <summary>
		/// Type is a Rpc command and consumed internally.
		/// </summary>
		Command = 1,

		/// <summary>
		/// Message is a standard Rpc call with a return value.
		/// </summary>
		RpcCall = 2,

		/// <summary>
		/// Message is a Rpc call with no return value.
		/// </summary>
		RpcCallNoReturn = 3,

		/// <summary>
		/// Message is a Rpc response with a return value.
		/// </summary>
		RpcCallReturn = 4,


		/// <summary>
		/// Message is a Rpc response.  Message contains information about the exception thrown.
		/// </summary>
		RpcCallException = 5,

		/// <summary>
		/// Message used to cancel a pending operation.
		/// </summary>
		RpcCallCancellation = 6
	}
}
