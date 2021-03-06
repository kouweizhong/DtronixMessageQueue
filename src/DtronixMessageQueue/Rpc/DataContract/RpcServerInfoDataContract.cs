﻿using System;
using ProtoBuf;

namespace DtronixMessageQueue.Rpc.DataContract
{
    /// <summary>
    /// Used to transport teh description of the server to the client.
    /// </summary>
    [ProtoContract]
    public class RpcServerInfoDataContract
    {
        /// <summary>
        /// Version of this server.
        /// </summary>
        [ProtoMember(1)]
        public string Version { get; set; } = new Version(1, 0).ToString();

        /// <summary>
        /// Message that the server send to the client.
        /// </summary>
        [ProtoMember(2)]
        public string Message { get; set; } = "RpcServer";

        /// <summary>
        /// Arbitrary data that is sent along in the MqFrame
        /// </summary>
        [ProtoMember(3)]
        public byte[] Data { get; set; }

        /// <summary>
        /// True if the server requires authentication before proceeding; False otherwise.
        /// Value is overriden based upon server configurations.
        /// </summary>
        [ProtoMember(4)]
        public bool RequireAuthentication { get; set; }
    }
}