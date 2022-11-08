using System;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;

namespace FishNet.Transporting.FishyUTPPlugin
{
    public abstract class CommonSocket
    {
        /// <summary>
        /// Transport controlling this socket.
        /// </summary>
        protected FishyUTP Transport;

        #region Connection States
        /// <summary>
        /// Current ConnectionState.
        /// </summary>
        private LocalConnectionState _connectionState = LocalConnectionState.Stopped;
        
        /// <summary>
        /// Returns the current ConnectionState.
        /// </summary>
        /// <returns></returns>
        internal LocalConnectionState GetLocalConnectionState()
        {
            return _connectionState;
        }
        #endregion
        
        #region Transport
        /// <summary>
        /// Unity transport driver to send and receive data.
        /// </summary>
        protected NetworkDriver Driver;

        /// <summary>
        /// A pipeline on the driver that is sequenced, and ensures messages are delivered.
        /// </summary>
        protected NetworkPipeline ReliablePipeline;

        /// <summary>
        /// A pipeline on the driver that is sequenced, but does not ensure messages are delivered.
        /// </summary>
        protected NetworkPipeline UnreliablePipeline;
        #endregion
        
        
        /// <summary>
        /// Initializes this for use.
        /// </summary>
        /// <param name="transport"></param>
        internal void Initialize(FishyUTP transport)
        {
            this.Transport = transport;
        }
        
        /// <summary>
        /// Sets a new connection state.
        /// </summary>
        /// <param name="connectionState"></param>
        /// <param name="server"></param>
        protected void SetLocalConnectionState(LocalConnectionState connectionState, bool server)
        {
            //If state hasn't changed.
            if (connectionState == _connectionState)
                return;

            _connectionState = connectionState;

            if (server)
                Transport.HandleServerConnectionState(new ServerConnectionStateArgs(connectionState,
                    Transport.Index));
            else
                Transport.HandleClientConnectionState(new ClientConnectionStateArgs(connectionState,
                    Transport.Index));
        }

        /// <summary>
        /// Sends a message via the transport
        /// </summary>
        protected void Send(NetworkPipeline pipeline, NetworkConnection connection, ArraySegment<byte> segment)
        {
            var data = new NativeArray<byte>(segment.Count, Allocator.Persistent);
            NativeArray<byte>.Copy(segment.Array, segment.Offset, data, 0, segment.Count);
            
            var writeStatus = Driver.BeginSend(pipeline, connection, out var writer);

            //If endpoint was success, write data to stream
            if (writeStatus != (int)StatusCode.Success) return;
            
            writer.WriteBytes(data);
            Driver.EndSend(writer);

            data.Dispose();
        }
        
        /// <summary>
        /// Returns a message from the transport
        /// </summary>
        protected void Receive(DataStreamReader stream, NetworkConnection connection, NetworkPipeline pipeline, out ArraySegment<byte> data, out Channel channel, out int connectionId)
        {
            NativeArray<byte> nativeMessage = new NativeArray<byte>(stream.Length, Allocator.Temp);
            stream.ReadBytes(nativeMessage);
            
            data = new ArraySegment<byte>(nativeMessage.ToArray());
            connectionId = connection.GetHashCode();
            channel = pipeline == ReliablePipeline ? Channel.Reliable : Channel.Unreliable;

            nativeMessage.Dispose();
        }

        /// <summary>
        /// Returns this drivers max header size based on the requested channel.
        /// </summary>
        /// <param name="channelId">The channel to check.</param>
        /// <returns>This client's max header size.</returns>
        public int GetMaxHeaderSize(int channelId = (int) Channel.Reliable)
        {
            if (GetLocalConnectionState() == LocalConnectionState.Started)
            {
                if (channelId == 0)
                {
                    return Driver.MaxHeaderSize(ReliablePipeline);
                }

                return Driver.MaxHeaderSize(UnreliablePipeline);
            }

            return 0;
        }
    }
}