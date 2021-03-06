﻿using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using DtronixMessageQueue.Rpc.DataContract;
using DtronixMessageQueue.Rpc.MessageHandlers;

namespace DtronixMessageQueue.Rpc
{
    /// <summary>
    /// Proxy class which will handle a method call from the specified class and execute it on a remote connection.
    /// </summary>
    /// <typeparam name="T">Type of class to proxy. method calls.</typeparam>
    /// <typeparam name="TSession">Session to proxy the method calls over.</typeparam>
    /// <typeparam name="TConfig">Configuration for this connection</typeparam>
    public class RpcProxy<T, TSession, TConfig> : RealProxy
        where T : IRemoteService<TSession, TConfig>
        where TSession : RpcSession<TSession, TConfig>, new()
        where TConfig : RpcConfig
    {
        /// <summary>
        /// Name of the service on the remote server.
        /// </summary>
        public readonly string ServiceName;

        private readonly RpcCallMessageHandler<TSession, TConfig> _callMessageHandler;

        /// <summary>
        /// Session used to convey the proxied methods over.
        /// </summary>
        private readonly TSession _session;

        /// <summary>
        /// Creates instance with the specified proxied class and session.
        /// </summary>
        /// <param name="serviceName">Name of this service on the remote server.</param>
        /// <param name="session">Session to convey proxied method calls over.</param>
        /// <param name="callMessageHandler">Call handler for the RPC session.</param>
        public RpcProxy(string serviceName, RpcSession<TSession, TConfig> session,
            RpcCallMessageHandler<TSession, TConfig> callMessageHandler) : base(typeof(T))
        {
            ServiceName = serviceName;
            _callMessageHandler = callMessageHandler;
            _session = (TSession) session;
        }

        /// <summary>
        /// Method invoked when a proxied method is called.
        /// </summary>
        /// <param name="msg">Information about the method called.</param>
        /// <returns>Method call result.</returns>
        public override IMessage Invoke(IMessage msg)
        {
            var methodCall = (IMethodCallMessage)msg;
            var methodInfo = (MethodInfo)methodCall.MethodBase;

            if (_session.Authenticated == false)
            {
                return
                    new ReturnMessage(
                        new InvalidOperationException(
                            "Session is not authenticated.  Must be authenticated before calling proxy methods."),
                        methodCall);
            }

            var serializer = _session.SerializationCache.Get();

            // Get the called method's arguments.
            object[] arguments = methodCall.Args;
            CancellationToken cancellationToken = CancellationToken.None;

            // Check to see if the last argument of the method is a CancellationToken.
            if (methodCall.ArgCount > 0)
            {
                var lastArgument = methodCall.Args.Last();

                if (lastArgument is CancellationToken)
                {
                    cancellationToken = (CancellationToken) lastArgument;

                    // Remove the last argument from being serialized.
                    if (methodCall.ArgCount > 1)
                    {
                        arguments = methodCall.Args.Take(methodCall.ArgCount - 1).ToArray();
                    }
                }
            }


            ResponseWaitHandle returnWait = null;
            RpcCallMessageAction callType;

            // Determine what kind of method we are calling.
            if (methodInfo.ReturnType == typeof(void))
            {
                // Byte[0] The call has no return value so we are not waiting.
                callType = RpcCallMessageAction.MethodCallNoReturn;
            }
            else
            {
                // Byte[0] The call has a return value so we are going to need to wait on the resposne.
                callType = RpcCallMessageAction.MethodCall;

                // Create a wait operation to wait for the response.
                returnWait = _callMessageHandler.ProxyWaitOperations.CreateWaitHandle(null);

                // Byte[0,1] Wait Id which is used for returning the value and cancellation.
                serializer.MessageWriter.Write(returnWait.Id);
                returnWait.Token = cancellationToken;
            }

            // Write the name of this service class.
            serializer.MessageWriter.Write(ServiceName);

            // Method name which will be remotely invoked.
            serializer.MessageWriter.Write(methodCall.MethodName);

            // Total number of arguments being serialized and sent.
            serializer.MessageWriter.Write((byte) arguments.Length);

            // Serialize all arguments to the message.
            for (var i = 0; i < arguments.Length; i++)
            {
                serializer.SerializeToWriter(arguments[i], i);
            }

            // Send the message over the session.
            _callMessageHandler.SendHandlerMessage((byte) callType, serializer.MessageWriter.ToMessage(true));

            // If there is no return wait, our work on this session is complete.
            if (returnWait == null)
            {
                return new ReturnMessage(null, null, 0, methodCall.LogicalCallContext, methodCall);
            }

            // Wait for the completion of the remote call.
            try
            {
                returnWait.ReturnResetEvent.Wait(returnWait.Token);
            }
            catch (OperationCanceledException)
            {
                // If the operation was canceled, cancel the wait on this end and notify the other end.
                _callMessageHandler.ProxyWaitOperations.Cancel(returnWait.Id);

                var frame = new MqFrame(new byte[2], MqFrameType.Last, _session.Config);
                frame.Write(0, returnWait.Id);

                _callMessageHandler.SendHandlerMessage((byte) RpcCallMessageAction.MethodCancel, new MqMessage(frame));
                throw new OperationCanceledException("Wait handle was canceled while waiting for a response.");
            }

            // If the wait times out, alert the callee.
            if (returnWait.ReturnResetEvent.IsSet == false)
            {
                throw new TimeoutException("Wait handle timed out waiting for a response.");
            }


            try
            {
                // Start parsing the received message.
                serializer.MessageReader.Message = returnWait.Message;

                // Skip 2 bytes for the return ID
                serializer.MessageReader.Skip(2);

                // Reads the rest of the message for the return value.
                serializer.PrepareDeserializeReader();


                switch ((RpcCallMessageAction) returnWait.MessageActionId)
                {
                    case RpcCallMessageAction.MethodReturn:

                        // Deserialize the return value and return it to the local method call.
                        var returnValue = serializer.DeserializeFromReader(methodInfo.ReturnType, 0);
                        return new ReturnMessage(returnValue, null, 0, methodCall.LogicalCallContext, methodCall);

                    case RpcCallMessageAction.MethodException:

                        // Deserialize the exception and let the local method call receive it.
                        var returnException = serializer.DeserializeFromReader(typeof(RpcRemoteExceptionDataContract), 0);
                        return
                            new ReturnMessage(new RpcRemoteException((RpcRemoteExceptionDataContract) returnException),
                                methodCall);

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            finally
            {
                // Always return the store to the holder.
                _session.SerializationCache.Put(serializer);
            }
        }
    }
}