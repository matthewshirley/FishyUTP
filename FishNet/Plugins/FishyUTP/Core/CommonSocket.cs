using System;
using System.Collections.Generic;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;

namespace FishNet.Transporting.FishyUTPPlugin
{
    public struct SendTarget : IEquatable<SendTarget>
    {
        public readonly NetworkConnection Connection;
        public readonly NetworkPipeline Pipeline;

        public SendTarget(NetworkConnection connection, NetworkPipeline pipeline)
        {
            Connection = connection;
            Pipeline = pipeline;
        }

        public bool Equals(SendTarget other)
        {
            return Connection.Equals(other.Connection) && Pipeline.Equals(other.Pipeline);
        }
    }
    
    public abstract class CommonSocket
    {
        ~CommonSocket()
        {
            Dispose();
        }
        
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

        #region Queues
        /// <summary>
        /// SendQueue dictionary is used to batch events instead of sending them immediately.
        /// </summary>
        protected readonly Dictionary<SendTarget, BatchedSendQueue> _SendQueue = new();

        /// <summary>
        /// SendQueue dictionary is used to batch events instead of sending them immediately.
        /// </summary>
        private readonly Dictionary<int, BatchedReceiveQueue> _reliableRecieveQueue = new();
        #endregion

        private void Dispose()
        {
            foreach (var queue in _SendQueue.Values)
            {
                queue.Dispose();
            }
            
            _SendQueue.Clear();
        }
        
        /// <summary>
        /// Initializes this for use.
        /// </summary>
        /// <param name="transport"></param>
        internal void Initialize(FishyUTP transport)
        {
            Transport = transport;
        }
        
        /// <summary>
        /// Sets a new connection state.
        /// </summary>
        /// <param name="connectionState"></param>
        /// <param name="server"></param>
        protected void SetLocalConnectionState(LocalConnectionState connectionState, bool server)
        {
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
        /// Queue a message to be sent via the transport
        /// </summary>
        protected void Send(int channelId, ArraySegment<byte> message, NetworkConnection connection)
        {
            if (GetLocalConnectionState() != LocalConnectionState.Started)
                return;
            
            var pipeline = channelId == (int) Channel.Reliable ? ReliablePipeline : UnreliablePipeline;
            var target = new SendTarget(connection, pipeline);

            if (!_SendQueue.TryGetValue(target, out var queue))
            {
                // The maximum reliable throughput, assuming the full reliable window can be sent on every
                // tick. This will be a large over-estimation in any realistic scenario.
                var maxReliableThroughput = (NetworkParameterConstants.MTU * Transport.NetworkManager.TimeManager.TickRate * 32) / 1000;
                var maxCapacity = NetworkParameterConstants.DisconnectTimeoutMS * maxReliableThroughput;

                queue = new BatchedSendQueue(maxCapacity);
                _SendQueue.Add(target, queue);
            }

            queue.PushMessage(message);
        }
        
        /// <summary>
        /// Send all queued messages
        /// </summary>
        protected void SendMessages(SendTarget target, BatchedSendQueue queue)
        {
            var pipeline = target.Pipeline;
            var connection = target.Connection;
            
            while (!queue.IsEmpty)
            {
                var status = Driver.BeginSend(pipeline, connection, out var writer);
                if (status != (int)StatusCode.Success) return;
                
                var sendSize = pipeline == ReliablePipeline ?  queue.FillWriterWithBytes(ref writer) : queue.FillWriterWithMessages(ref writer);
                Driver.EndSend(writer);
                
                queue.Consume(sendSize);
            }
        }
        
        /// <summary>
        /// Returns a message from the transport
        /// </summary>
        protected void Receive(int connectionId, NetworkPipeline pipeline, DataStreamReader reader, bool server = true)
        {
            BatchedReceiveQueue queue;
            if (pipeline == ReliablePipeline)
            {
                if (_reliableRecieveQueue.TryGetValue(connectionId, out queue))
                {
                    queue.PushReader(reader);
                }
                else
                {
                    queue = new BatchedReceiveQueue(reader);
                    _reliableRecieveQueue[connectionId] = queue;
                }
            }
            else
            {
                queue = new BatchedReceiveQueue(reader);
            }

            while (!queue.IsEmpty)
            {
                var message = queue.PopMessage();
                if (message == default)
                {
                    break;
                }
                
                var channel = pipeline == ReliablePipeline ? Channel.Reliable : Channel.Unreliable;

                if (server)
                {
                    Transport.HandleServerReceivedDataArgs(new ServerReceivedDataArgs(message, channel, connectionId, Transport.Index));

                }
                else
                {
                    Transport.HandleClientReceivedDataArgs(new ClientReceivedDataArgs(message, channel, Transport.Index));
                }
            }
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