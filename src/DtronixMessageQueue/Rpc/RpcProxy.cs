﻿using System;
using System.CodeDom;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using ProtoBuf;
using ProtoBuf.Meta;

namespace DtronixMessageQueue.Rpc {

	/// <summary>
	/// Proxy class which will handle a method call from the specified class and execute it on a remote connection.
	/// </summary>
	/// <typeparam name="T">Type of class to proxy. method calls.</typeparam>
	/// <typeparam name="TSession">Session to proxy the method calls over.</typeparam>
	public class RpcProxy<T, TSession, TConfig> : RealProxy
		where T : IRemoteService<TSession, TConfig>
		where TSession : RpcSession<TSession, TConfig>, new()
		where TConfig : RpcConfig {

		/// <summary>
		/// Internal reference to the class which is being proxied.
		/// </summary>
		private readonly T decorated;

		/// <summary>
		/// Session used to convey the proxied methods over.
		/// </summary>
		private readonly TSession session;

		/// <summary>
		/// Creates instance with the specified proxied class and session.
		/// </summary>
		/// <param name="decorated">Class to proxy method calls from.</param>
		/// <param name="session">Session to convey proxied method calls over.</param>
		public RpcProxy(T decorated, RpcSession<TSession, TConfig> session) : base(typeof(T)) {
			this.decorated = decorated;
			this.session = (TSession) session;
		}

		/// <summary>
		/// Method invoked when a proxied method is called.
		/// </summary>
		/// <param name="msg">Information about the method called.</param>
		/// <returns>Method call result.</returns>
		public override IMessage Invoke(IMessage msg) {
			var method_call = msg as IMethodCallMessage;
			var method_info = method_call.MethodBase as MethodInfo;

			var store = session.Store.Get();

			// Get the called method's arguments.
			object[] arguments = method_call.Args;
			CancellationToken cancellation_token = CancellationToken.None;

			// Check to see if the last argument of the method is a CancellationToken.
			if (method_call.ArgCount > 0) {
				var last_argument = method_call.Args.Last();

				if (last_argument is CancellationToken) {
					cancellation_token = (CancellationToken) last_argument;

					// Remove the last argument from being serialized.
					if (method_call.ArgCount > 1) {
						arguments = method_call.Args.Take(method_call.ArgCount - 1).ToArray();
					}
				}
			}


			RpcOperationWait return_wait = null;

			// Determine what kind of method we are calling.
			if (method_info.ReturnType == typeof(void)) {

				// Byte[0] The call has no return value so we are not waiting.
				store.MessageWriter.Write((byte) RpcMessageType.RpcCallNoReturn);
			} else {

				// Byte[0] The call has a return value so we are going to need to wait on the resposne.
				store.MessageWriter.Write((byte) RpcMessageType.RpcCall);

				// Create a wait operation to wait for the response.
				return_wait = session.CreateWaitOperation();

				// Byte[1,2] Wait Id which is used for returning the value and cancellation.
				store.MessageWriter.Write(return_wait.Id);
				return_wait.Token = cancellation_token;
			}

			// Write the name of this service class.
			store.MessageWriter.Write(decorated.Name);

			// Method name which will be remotely invoked.
			store.MessageWriter.Write(method_call.MethodName);

			// Total number of arguments being serialized and sent.
			store.MessageWriter.Write((byte) arguments.Length);

			// Serialize all arguments to the message.
			for (int i = 0; i < arguments.Length; i++) {
				var arg = arguments[i];

				// Serialize with a length prefix to allow for simplification of deserilization.
				RuntimeTypeModel.Default.SerializeWithLengthPrefix(store.Stream, arg, arg.GetType(), PrefixStyle.Base128, i);

				// Write the stream data to the message.
				store.MessageWriter.Write(store.Stream.ToArray());

				// Should always read the entire buffer in one go.
				store.Stream.SetLength(0);
			}

			// Send the message over the session.
			session.Send(store.MessageWriter.ToMessage(true));

			// If there is no return wait, our work on this session is complete.
			if (return_wait == null) {
				return new ReturnMessage(null, null, 0, method_call.LogicalCallContext, method_call);
			}

			// Wait for the completion of the remote call.
			try {
				return_wait.ReturnResetEvent.Wait(return_wait.Token);
			} catch (OperationCanceledException) {

				// If the operation was canceled, cancel the wait on this end and notify the other end.
				session.CancelWaitOperation(return_wait.Id);
				throw new OperationCanceledException("Wait handle was canceled while waiting for a response.");
			}
			
			// If the wait times out, alert the callee.
			if (return_wait.ReturnResetEvent.IsSet == false) {
				throw new TimeoutException("Wait handle timed out waiting for a response.");
			}



			try {

				// Start parsing the received message.
				store.MessageReader.Message = return_wait.ReturnMessage;
				
				// Read the first byte which dictates the type of message.
				var return_type = (RpcMessageType)store.MessageReader.ReadByte();

				// Skip 2 bytes for the return ID
				store.MessageReader.ReadBytes(2);

				// Reads the rest of the message for the return value.
				var return_bytes = store.MessageReader.ReadToEnd();

				// Set the stream to 0 and write the content of the return value for deserialization
				store.Stream.SetLength(0);
				store.Stream.Write(return_bytes, 0, return_bytes.Length);
				store.Stream.Position = 0;


				switch (return_type) {
					case RpcMessageType.RpcCallReturn:

						// Deserialize the return value and return it to the local method call.
						var return_value = RuntimeTypeModel.Default.DeserializeWithLengthPrefix(store.Stream, null, method_info.ReturnType, PrefixStyle.Base128, 0);
						return new ReturnMessage(return_value, null, 0, method_call.LogicalCallContext, method_call);

					case RpcMessageType.RpcCallException:

						// Deserialize the exception and let the local method call receive it.
						var return_exception = RuntimeTypeModel.Default.DeserializeWithLengthPrefix(store.Stream, null, typeof(RpcRemoteExceptionDataContract), PrefixStyle.Base128, 0);
						return new ReturnMessage(new RpcRemoteException((RpcRemoteExceptionDataContract)return_exception), method_call);

					default:
						throw new ArgumentOutOfRangeException();
				}

			} finally {
				
				// Always return the store to the holder.
				session.Store.Put(store);
			}
		}
	}
}
