﻿using System;
using FishNet.Managing.Logging;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using UnityEngine;

namespace FishNet.Transporting.FishyUTPPlugin
{
    public class UtpServer : CommonSocket
    {
        ~UtpServer()
        {
            StopConnection();
        }
        
        #region Private
        /// <summary>
        /// Client connections to this server.
        /// </summary>
        private NativeList<NetworkConnection> _connections;
        
        /// <summary>
        /// Maximum number of connections allowed.
        /// </summary>
        private int _maximumClients = short.MaxValue;
        #endregion
        
        /// <summary>
        /// Starts the server.
        /// </summary>
        public bool StartConnection(ushort port, bool useRelay)
        {
            if (GetLocalConnectionState() != LocalConnectionState.Stopped)
            {
                if (Transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError("Attempting to start a server that is already active.");
                return false;
            }
            
            SetLocalConnectionState(LocalConnectionState.Starting, true);
            
            var endpoint = NetworkEndPoint.AnyIpv4;
            endpoint.Port = port;
            
            var settings = new NetworkSettings();

            if (useRelay)
            {
                // Create the network parameters using the Relay server data
                var relayServerData = RelaySupport.HostRelayData(Transport.relayManager.HostAllocation);
                var relayNetworkParameter = new RelayNetworkParameter { ServerData = relayServerData };
                
                settings.AddRawParameterStruct(ref relayNetworkParameter);
            }

            Driver = NetworkDriver.Create(settings);

            ReliablePipeline = Driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            UnreliablePipeline = Driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            
            // Bind the driver to the endpoint
            Driver.Bind(endpoint);
            if (!Driver.Bound)
            {
                if (Transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError($"Unable to bind to the specified port {port}.");
                
                SetLocalConnectionState(LocalConnectionState.Stopped, true);
                return false;
            }

            // and start listening for new connections.
            Driver.Listen();
            SetLocalConnectionState(LocalConnectionState.Started, true);
            
            // Finally, create a NativeList to hold all the connections
            _connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
            
            return true;
        }
        
        /// <summary>
        /// Stops the server.
        /// </summary>
        public bool StopConnection()
        {
            if (GetLocalConnectionState() == LocalConnectionState.Stopped ||
                GetLocalConnectionState() == LocalConnectionState.Stopping)
                return false;
            
            SetLocalConnectionState(LocalConnectionState.Stopping, true);

            if (_connections.IsCreated)
            {
                _connections.Dispose();
            }

            if (Driver.IsCreated)
            {
                Driver.Dispose();
                Driver = default;
            }
            
            Dispose();

            SetLocalConnectionState(LocalConnectionState.Stopped, true);
            return true;
        }

        /// <summary>
        /// Stops a remote client from the server, disconnecting the client.
        /// </summary>
        /// <param name="connectionId"></param>
        internal bool StopConnection(int connectionId)
        {
            if (!TryGetConnection(connectionId, out var connection)) return true;
            
            Transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Stopped, connectionId, Transport.Index));

            connection.Disconnect(Driver);
            _connections.RemoveAt(_connections.IndexOf(connection));

            Driver.ScheduleUpdate().Complete();

            return true;
        }

        private bool TryGetConnection(int connectionId, out NetworkConnection connection)
        {
            if (_connections.IsCreated)
            {
                foreach (var c in _connections)
                {
                    if (c.GetHashCode() == connectionId)
                    {
                        connection = c;
                        return true;
                    }
                }
            }

            connection = default;
            return false;
        }

        public string GetConnectionAddress(int connectionId)
        {
            if (!TryGetConnection(connectionId, out var connection)) return string.Empty;

            var endpoint = Driver.RemoteEndPoint(connection);
            return endpoint.Address;
        }

        /// <summary>
        /// Gets the current ConnectionState of a remote client on the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId to get ConnectionState for.</param>
        internal RemoteConnectionState GetConnectionState(int connectionId)
        {
            return !TryGetConnection(connectionId, out _) ? RemoteConnectionState.Stopped : RemoteConnectionState.Started;
        }

        /// <summary>
        /// Returns the maximum number of clients allowed to connect to the server.
        /// If the transport does not support this method the value -1 is returned.
        /// </summary>
        public int GetMaximumClients()
        {
            return _maximumClients;
        }

        /// <summary>
        /// Sets the maximum number of clients allowed to connect to the server.
        /// </summary>
        public void SetMaximumClients(int value)
        {
            _maximumClients = value;
        }

        /// <summary>
        /// Send data to a connection over a particular channel.
        /// </summary>
        public void SendToClient(int channelId, ArraySegment<byte> message, int connectionId)
        {
            if (!TryGetConnection(connectionId, out var connection)) return;
            Send(channelId, message, connection);
        }


        private void HandleIncomingConnections()
        {
            NetworkConnection incomingConnection;
            while ((incomingConnection = Driver.Accept()) != default)
            {
                _connections.Add(incomingConnection);

                if (_connections.Length > _maximumClients)
                {
                    StopConnection(incomingConnection.GetHashCode());
                    return;
                }
                
                Transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Started, incomingConnection.GetHashCode(), Transport.Index));
            }
        }

        private void HandleIncomingEvents()
        {
            NetworkEvent.Type netEvent;
            while ((netEvent = Driver.PopEvent(out var connection, out var stream, out var pipeline)) !=
                       NetworkEvent.Type.Empty)
            {
                switch (netEvent)
                {
                    case NetworkEvent.Type.Data:
                        Receive(connection.GetHashCode(), pipeline, stream);
                        break;
                    
                    case NetworkEvent.Type.Disconnect:
                        StopConnection(connection.GetHashCode());
                        break;
                }
            }
        }

        /// <summary>
        /// Iterates through all incoming packets and handles them.
        /// </summary>
        internal void IterateIncoming()
        {
            if (GetLocalConnectionState() == LocalConnectionState.Stopped ||
                GetLocalConnectionState() == LocalConnectionState.Stopping)
                return;

            Driver.ScheduleUpdate().Complete();

            HandleIncomingConnections();
            HandleIncomingEvents();
        }

        /// <summary>
        /// Processes data to be sent by the socket.
        /// </summary>
        internal void IterateOutgoing()
        {
            if (GetLocalConnectionState() == LocalConnectionState.Stopped || GetLocalConnectionState() == LocalConnectionState.Stopping)
                return;
            
            foreach (var kvp in _SendQueue)
            {
                SendMessages(kvp.Key, kvp.Value);
            }
        }
    }
}
