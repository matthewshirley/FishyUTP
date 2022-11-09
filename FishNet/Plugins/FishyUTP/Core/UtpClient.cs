using System;
using FishNet.Managing.Logging;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace FishNet.Transporting.FishyUTPPlugin
{
    public class UtpClient : CommonSocket
    {
        ~UtpClient()
        {
            StopConnection();
        }

        #region Private
        private NetworkConnection _connection;
        #endregion

        private void InternalStartConnection(NetworkEndPoint endpoint, NetworkSettings settings)
        {
            SetLocalConnectionState(LocalConnectionState.Starting, false);
            
            Driver = NetworkDriver.Create(settings);
            
            ReliablePipeline = Driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            UnreliablePipeline = Driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));

            _connection = Driver.Connect(endpoint);
            
            SetLocalConnectionState(LocalConnectionState.Started, false);
        }
        
        /// <summary>
        /// Starts the client connection.
        /// </summary>
        /// <param name="allocation"></param>
        internal void StartConnection(JoinAllocation allocation)
        {
            if (GetLocalConnectionState() == LocalConnectionState.Started || GetLocalConnectionState() == LocalConnectionState.Starting)
            {
                return;
            }
            
            var settings = new NetworkSettings();
            
            var relayServerData = RelaySupport.PlayerRelayData(allocation);
            settings.WithRelayParameters(ref relayServerData);
            
            InternalStartConnection(relayServerData.Endpoint, settings);
        }
        
        /// <summary>
        /// Starts the client connection.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        internal void StartConnection(string address, ushort port)
        {
            if (GetLocalConnectionState() == LocalConnectionState.Started || GetLocalConnectionState() == LocalConnectionState.Starting)
            {
                return;
            }
            
            if (!NetworkEndPoint.TryParse(address, port, out var endpoint))
            {
                if (Transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError("Unable to parse listen server address and port.");
                return;
            }
            
            var settings = new NetworkSettings();
            InternalStartConnection(endpoint, settings);
        }
        
        /// <summary>
        /// Stops the client connection.
        /// </summary>
        internal bool StopConnection()
        {
            if (GetLocalConnectionState() == LocalConnectionState.Stopped ||
                GetLocalConnectionState() == LocalConnectionState.Stopping)
                return false;

            SetLocalConnectionState(LocalConnectionState.Stopping, false);

            if (_connection.IsCreated)
            {
                _connection.Disconnect(Driver);
                _connection = default;
            }
            
            Driver.ScheduleUpdate().Complete();

            if (Driver.IsCreated)
            {
                Driver.Dispose();
                Driver = default;
            }

            SetLocalConnectionState(LocalConnectionState.Stopped, false);
            return true;
        }

        /// <summary>
        /// Iterates through all incoming packets and handles them.
        /// </summary>
        internal void IterateIncoming()
        {
            if (GetLocalConnectionState() == LocalConnectionState.Stopped || GetLocalConnectionState() == LocalConnectionState.Stopping)
                return;
            
            Driver.ScheduleUpdate().Complete();
            
            NetworkEvent.Type incomingEvent;
            while ((incomingEvent = _connection.PopEvent(Driver, out var stream, out var pipeline)) !=
                   NetworkEvent.Type.Empty)
            {
                switch (incomingEvent)
                {
                    case NetworkEvent.Type.Data:
                        Receive(stream, _connection, pipeline, out var data, out var channel, out _);
                        Transport.HandleClientReceivedDataArgs(new ClientReceivedDataArgs(data, channel, Transport.Index));
                        break;
                    case NetworkEvent.Type.Connect:
                        SetLocalConnectionState(LocalConnectionState.Started, false);
                        break;
                    case NetworkEvent.Type.Disconnect:
                        StopConnection();
                        break;
                }
            }
        }

        /// <summary>
        /// Sends a packet to the server.
        /// </summary>
        internal void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            if (GetLocalConnectionState() != LocalConnectionState.Started || !_connection.IsCreated)
            {
                return;
            }
            
            var pipeline = channelId == (int) Channel.Reliable ? ReliablePipeline : UnreliablePipeline;
            Send(pipeline, _connection, segment);
        }
    }
}