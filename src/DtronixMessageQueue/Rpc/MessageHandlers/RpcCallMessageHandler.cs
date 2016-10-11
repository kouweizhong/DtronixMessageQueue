﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using System.Threading.Tasks;
using DtronixMessageQueue.Rpc.DataContract;
using DtronixMessageQueue.Socket;

namespace DtronixMessageQueue.Rpc.MessageHandlers {
	public class RpcCallMessageHandler<TSession, TConfig> : MessageHandler<TSession, TConfig>
		where TSession : RpcSession<TSession, TConfig>, new()
		where TConfig : RpcConfig {

		/// <summary>
		/// Id byte which precedes all messages all messages of this type.
		/// </summary>
		public sealed override byte Id => 1;

		/// <summary>
		/// Contains all services that can be remotely executed on this session.
		/// </summary>
		public readonly Dictionary<string, IRemoteService<TSession, TConfig>> Services =
			new Dictionary<string, IRemoteService<TSession, TConfig>>();

		/// <summary>
		/// Proxy objects to be invoked on this session and proxied to the recipient session.
		/// </summary>
		public readonly Dictionary<Type, IRemoteService<TSession, TConfig>> RemoteServicesProxy =
			new Dictionary<Type, IRemoteService<TSession, TConfig>>();

		/// <summary>
		///  Base proxy to the used for servicing of the proxy interface.
		/// </summary>
		public readonly Dictionary<Type, RealProxy> RemoteServiceRealproxy = new Dictionary<Type, RealProxy>();

		public readonly ResponseWait<TSession, TConfig> WaitOperations;

		public RpcCallMessageHandler(TSession session) : base(session) {
			WaitOperations = new ResponseWait<TSession, TConfig>(Id, Session);
		}

		

		public override bool HandleMessage(MqMessage message) {
			if (message[0][0] != Id) {
				return false;
			}

			// Read the type of message.
			var message_type = (RpcCallMessageType)message[0].ReadByte(1);

			switch (message_type) {

				case RpcCallMessageType.MethodCancel:

					// Remotely called to cancel a rpc call on this session.
					var cancellation_id = message[0].ReadUInt16(2);
					WaitOperations.RemoteCancel(cancellation_id);
					break;

				case RpcCallMessageType.MethodCallNoReturn:
				case RpcCallMessageType.MethodCall:
					ProcessRpcCall(message, message_type);
					break;

				case RpcCallMessageType.MethodException:
				case RpcCallMessageType.MethodReturn:
					ProcessRpcReturn(message);
					break;

				default:
					// Unknown message type passed.  Disconnect the connection.
					Session.Close(SocketCloseReason.ProtocolError);
					break;
			}

			return true;

		}

		/// <summary>
		/// Processes the incoming Rpc call from the recipient connection.
		/// </summary>
		/// <param name="message">Message containing the Rpc call.</param>
		/// <param name="message_type">Type of call this message is.</param>
		private void ProcessRpcCall(MqMessage message, RpcCallMessageType message_type) {

			// Execute the processing on the worker thread.
			Task.Run(() => {

				// Retrieve a serialization cache to work with.
				var serialization = Session.SerializationCache.Get(message);
				ushort rec_message_return_id = 0;

				try {
					// Skip Handler.Id & RpcMessageType
					serialization.MessageReader.Skip(2);

					// Determine if this call has a return value.
					if (message_type == RpcCallMessageType.MethodCall) {
						rec_message_return_id = serialization.MessageReader.ReadUInt16();
					}

					// Read the string service name, method and number of arguments.
					var rec_service_name = serialization.MessageReader.ReadString();
					var rec_method_name = serialization.MessageReader.ReadString();
					var rec_argument_count = serialization.MessageReader.ReadByte();

					// Verify that the requested service exists.
					if (Services.ContainsKey(rec_service_name) == false) {
						throw new Exception($"Service '{rec_service_name}' does not exist.");
					}

					// Get the service from the instance list.
					var service = Services[rec_service_name];

					// Get the actual method.  TODO: Might want to cache this for performance purposes.
					var method_info = service.GetType().GetMethod(rec_method_name);
					var method_parameters = method_info.GetParameters();

					// Determine if the last parameter is a cancellation token.
					var last_param = method_info.GetParameters().LastOrDefault();

					ResponseWaitHandle cancellation_wait = null;

					// If the past parameter is a cancellation token, setup a return wait for this call to allow for remote cancellation.
					if (rec_message_return_id != 0 && last_param?.ParameterType == typeof(CancellationToken)) {
						
						cancellation_wait = WaitOperations.CreateRemoteWaitHandle(rec_message_return_id);

						cancellation_wait.TokenSource = new CancellationTokenSource();
						cancellation_wait.Token = cancellation_wait.TokenSource.Token;
					}

					// Setup the parameters to pass to the invoked method.
					object[] parameters = new object[rec_argument_count + (cancellation_wait == null ? 0 : 1)];

					// Determine if we have any parameters to pass to the invoked method.
					if (rec_argument_count > 0) {

						serialization.PrepareDeserializeReader();


						// Parse each parameter to the parameter list.
						for (int i = 0; i < rec_argument_count; i++) {
							parameters[i] = serialization.DeserializeFromReader(method_parameters[i].ParameterType, i);
						}
					}

					// Add the cancellation token to the parameters.
					if (cancellation_wait != null) {
						parameters[parameters.Length - 1] = cancellation_wait.Token;
					}


					object return_value;
					try {
						// Invoke the requested method.
						return_value = method_info.Invoke(service, parameters);
					} catch (Exception ex) {
						// Determine if this method was waited on.  If it was and an exception was thrown,
						// Let the recipient session know an exception was thrown.
						if (rec_message_return_id != 0 && ex.InnerException?.GetType() != typeof(OperationCanceledException)) {
							SendRpcException(serialization, ex, rec_message_return_id);
						}
						return;
					} finally {
						WaitOperations.RemoteComplete(rec_message_return_id);
					}


					// Determine what to do with the return value.
					if (message_type == RpcCallMessageType.MethodCall) {
						// Reset the stream.
						serialization.Stream.SetLength(0);

						// Write the Rpc call type and the id.
						serialization.MessageWriter.Clear();
						serialization.MessageWriter.Write(Id);
						serialization.MessageWriter.Write((byte)RpcCallMessageType.MethodReturn);
						serialization.MessageWriter.Write(rec_message_return_id);

						// Serialize the return value and add it to the stream.

						serialization.SerializeToWriter(return_value, 0);

						// Send the return value message to the recipient.
						Session.Send(serialization.MessageWriter.ToMessage(true));
					}

					// Return the serialization to the cache to be reused.
					Session.SerializationCache.Put(serialization);


				} catch (Exception ex) {
					// If an exception occurred, notify the recipient connection.
					SendRpcException(serialization, ex, rec_message_return_id);
					Session.SerializationCache.Put(serialization);
				}
			});

		}


		/// <summary>
		/// Processes the incoming return value message from the recipient connection.
		/// </summary>
		/// <param name="message">Message containing the frames for the return value.</param>
		private void ProcessRpcReturn(MqMessage message) {

			// Execute the processing on the worker thread.
			Task.Run(() => {

				// Retrieve a serialization cache to work with.
				var serialization = Session.SerializationCache.Get(message);
				try {

					// Skip message type byte and message type.
					serialization.MessageReader.Skip(2);

					// Read the return Id.
					var return_id = serialization.MessageReader.ReadUInt16();


					ResponseWaitHandle call_wait_handle = WaitOperations.LocalGet(return_id);
					// Try to get the outstanding wait from the return id.  If it does not exist, the has already completed.
					if (call_wait_handle != null) {
						call_wait_handle.ReturnMessage = message;

						// Release the wait event.
						call_wait_handle.ReturnResetEvent.Set();
					}

				} finally {
					Session.SerializationCache.Put(serialization);
				}
			});
		}

		/// <summary>
		/// Takes an exception and serializes the important information and sends it to the recipient connection.
		/// </summary>
		/// <param name="serialization">Serialization information to use for this response.</param>
		/// <param name="ex">Exception which occurred to send to the recipient session.</param>
		/// <param name="message_return_id">Id used to reference this call on the recipient session.</param>
		private void SendRpcException(SerializationCache.Serializer serialization, Exception ex, ushort message_return_id) {
			// Reset the length of the stream to clear it.
			serialization.Stream.SetLength(0);

			// Clear the message writer of any previously stored data.
			serialization.MessageWriter.Clear();

			// Writer the Rpc call type and the return Id.
			serialization.MessageWriter.Write(Id);
			serialization.MessageWriter.Write((byte)RpcCallMessageType.MethodException);
			serialization.MessageWriter.Write(message_return_id);

			// Get the exception information in a format that we can serialize.
			var exception = new RpcRemoteExceptionDataContract(ex is TargetInvocationException ? ex.InnerException : ex);

			// Serialize the class with a length prefix.
			serialization.SerializeToWriter(exception, 0);

			// Send the message to the recipient connection.
			Session.Send(serialization.MessageWriter.ToMessage(true));

		}


	}
}