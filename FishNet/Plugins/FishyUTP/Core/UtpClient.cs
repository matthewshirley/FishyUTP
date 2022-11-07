using System;
using FishNet.Managing.Logging;
using Unity.Networking.Transport;
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

        /// <summary>
        /// Starts the client connection.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        internal void StartConnection(string address, ushort port)
        {
            if (GetLocalConnectionState() == LocalConnectionState.Started)
            {
                return;
            }
            
            if (!NetworkEndPoint.TryParse(address, port, out var endpoint))
            {
                if (transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError("Unable to parse listen server address and port.");
                return;
            }
            
            SetLocalConnectionState(LocalConnectionState.Starting, false);
            
            var settings = new NetworkSettings();
            driver = NetworkDriver.Create(settings);
            
            reliablePipeline = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            unreliablePipeline = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));

            _connection = driver.Connect(endpoint);
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
                _connection.Disconnect(driver);
                _connection = default;
            }
            
            driver.ScheduleUpdate().Complete();

            if (driver.IsCreated)
            {
                driver.Dispose();
                driver = default;
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
            
            driver.ScheduleUpdate().Complete();
            
            NetworkEvent.Type incomingEvent;
            while ((incomingEvent = _connection.PopEvent(driver, out var stream, out var pipeline)) !=
                   NetworkEvent.Type.Empty)
            {
                switch (incomingEvent)
                {
                    case NetworkEvent.Type.Data:
                        Receive(stream, _connection, pipeline, out var data, out var channel, out _);
                        transport.HandleClientReceivedDataArgs(new ClientReceivedDataArgs(data, channel, transport.Index));
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
            
            var pipeline = channelId == (int) Channel.Reliable ? reliablePipeline : unreliablePipeline;
            Send(pipeline, _connection, segment);
        }
    }
}