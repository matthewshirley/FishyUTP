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
        protected FishyUTP transport;

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
        protected NetworkDriver driver;

        /// <summary>
        /// A pipeline on the driver that is sequenced, and ensures messages are delivered.
        /// </summary>
        protected NetworkPipeline reliablePipeline;

        /// <summary>
        /// A pipeline on the driver that is sequenced, but does not ensure messages are delivered.
        /// </summary>
        protected NetworkPipeline unreliablePipeline;
        #endregion
        
        
        /// <summary>
        /// Initializes this for use.
        /// </summary>
        /// <param name="transport"></param>
        internal void Initialize(FishyUTP transport)
        {
            this.transport = transport;
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
                transport.HandleServerConnectionState(new ServerConnectionStateArgs(connectionState,
                    transport.Index));
            else
                transport.HandleClientConnectionState(new ClientConnectionStateArgs(connectionState,
                    transport.Index));
        }

        /// <summary>
        /// Sends a message via the transport
        /// </summary>
        protected void Send(NetworkPipeline pipeline, NetworkConnection connection, ArraySegment<byte> segment)
        {
            var data = new NativeArray<byte>(segment.Count, Allocator.Persistent);
            NativeArray<byte>.Copy(segment.Array, segment.Offset, data, 0, segment.Count);
            
            var writeStatus = driver.BeginSend(pipeline, connection, out var writer);

            //If endpoint was success, write data to stream
            if (writeStatus != (int)StatusCode.Success) return;
            
            writer.WriteBytes(data);
            driver.EndSend(writer);

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
            channel = pipeline == reliablePipeline ? Channel.Reliable : Channel.Unreliable;

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
                    return driver.MaxHeaderSize(reliablePipeline);
                }

                return driver.MaxHeaderSize(unreliablePipeline);
            }

            return 0;
        }
    }
}