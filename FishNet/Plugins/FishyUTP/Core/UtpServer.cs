using System;
using FishNet.Managing.Logging;
using Unity.Collections;
using Unity.Networking.Transport;
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
        public bool StartConnection(ushort port, int maximumClients)
        {
            if (GetLocalConnectionState() != LocalConnectionState.Stopped)
            {
                if (transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError("Attempting to start a server that is already active.");
                return false;
            }

            _maximumClients = maximumClients;
            
            var settings = new NetworkSettings();
            driver = NetworkDriver.Create(settings);
            
            reliablePipeline = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            unreliablePipeline = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));

            var endpoint = NetworkEndPoint.AnyIpv4;
            endpoint.Port = port;

            driver.Bind(endpoint);
            if (!driver.Bound)
            {
                if (transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError($"Unable to bind to the specified port {port}.");
                return false;
            }

            SetLocalConnectionState(LocalConnectionState.Starting, true);

            // Then we try to bind our driver to a specific network address and port, and if that does not fail, we call the Listen method.
            driver.Listen();

            // Finally we create a NativeList to hold all the connections.
            _connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
            
            SetLocalConnectionState(LocalConnectionState.Started, true);
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

            if (driver.IsCreated)
            {
                driver.Dispose();
                driver = default;
            }

            SetLocalConnectionState(LocalConnectionState.Stopped, true);
            return true;
        }

        /// <summary>
        /// Stops a remote client from the server, disconnecting the client.
        /// </summary>
        /// <param name="connectionId"></param>
        internal bool StopConnection(int connectionId)
        {
            if (!GetConnection(connectionId, out var connection)) return true;
            
            transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Stopped, connectionId, transport.Index));

            connection.Disconnect(driver);
            _connections.RemoveAt(_connections.IndexOf(connection));

            driver.ScheduleUpdate().Complete();

            return true;
        }

        private bool GetConnection(int connectionId, out NetworkConnection connection)
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
            if (!GetConnection(connectionId, out var connection)) return string.Empty;

            var endpoint = driver.RemoteEndPoint(connection);
            return endpoint.Address;
        }

        /// <summary>
        /// Gets the current ConnectionState of a remote client on the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId to get ConnectionState for.</param>
        internal RemoteConnectionState GetConnectionState(int connectionId)
        {
            return !GetConnection(connectionId, out _) ? RemoteConnectionState.Stopped : RemoteConnectionState.Started;
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
        public void SendToClient(int channelId, ArraySegment<byte> segment, int connectionId)
        {
            if (!GetConnection(connectionId, out var connection)) return;
            
            var pipeline = channelId == (int)Channel.Reliable ? reliablePipeline : unreliablePipeline;
            Send(pipeline, connection, segment);
        }

        /// <summary>
        /// Iterates through all incoming packets and handles them.
        /// </summary>
        internal void IterateIncoming()
        {
            //Stopped or trying to stop.
            if (GetLocalConnectionState() == LocalConnectionState.Stopped || GetLocalConnectionState() == LocalConnectionState.Stopping)
                return;
            
            // This method closely follows what is in the Unity transport documentation:
            // https://docs-multiplayer.unity3d.com/transport/current/minimal-workflow#server-update-loop
            driver.ScheduleUpdate().Complete();
            
            // Clean up connections
            for (var i = 0; i < _connections.Length; i++)
            {
                if (_connections[i].IsCreated) continue;
                
                _connections.RemoveAtSwapBack(i);
                --i;
            }
            
            // Accept new connections
            NetworkConnection incomingConnection;
            while ((incomingConnection = driver.Accept()) != default)
            {
                _connections.Add(incomingConnection);

                if (_connections.Length > _maximumClients)
                {
                    StopConnection(incomingConnection.GetHashCode());
                    return;
                }
                
                transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Started, incomingConnection.GetHashCode(), transport.Index));
            }

            foreach (var connection in _connections)
            {
                NetworkEvent.Type netEvent;
                while ((netEvent = driver.PopEventForConnection(connection, out var stream, out var pipeline)) !=
                       NetworkEvent.Type.Empty)
                {
                    switch (netEvent)
                    {
                        case NetworkEvent.Type.Data:
                            Receive(stream, connection, pipeline, out var data, out var channel, out var connectionId);
                            transport.HandleServerReceivedDataArgs(new ServerReceivedDataArgs(data, channel, connectionId, transport.Index));
                            break;
                        
                        case NetworkEvent.Type.Disconnect:
                            StopConnection(connection.GetHashCode());
                            break;
                    }
                }
            }
        }
    }
}
