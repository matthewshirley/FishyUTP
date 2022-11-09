using FishNet.Managing;
using FishNet.Managing.Logging;
using Unity.Networking.Transport;
using System;
using UnityEngine;

namespace FishNet.Transporting.FishyUTPPlugin
{
    [AddComponentMenu("FishNet/Transport/FishyUTP")]
    public class FishyUTP : Transport
    {
        #region Public.
        [Header("Server")]
        /// <summary>
        /// Port to use.
        /// </summary>
        [Tooltip("Port to use.")]
        [SerializeField]
        private ushort port = 7777;
        
        /// <summary>
        /// Maximum number of players which may be connected at once.
        /// </summary>
        [Tooltip("Maximum number of players which may be connected at once.")]
        [Range(1, 9999)]
        [SerializeField]
        private int maximumClients = 16;
        
        [Header("Client")]
        /// <summary>
        /// Address to connect.
        /// </summary>
        [Tooltip("Address to connect.")]
        [SerializeField]
        private string clientAddress = "127.0.0.1";
        
        [Header("Relay")]
        
        /// <summary>
        /// Whether to use Relay with UTP.
        /// </summary>
        [Tooltip("Automatically sign in to Unity services when transport is initialized?")]
        [SerializeField]
        public bool useRelay;
        
        /// <summary>
        /// Automatically sign in to Unity services when transport is initialized.
        /// </summary>
        [Tooltip("Automatically sign in to Unity services when transport is initialized?")]
        [SerializeField]
        private bool loginToUnityServices = true;
        
        /// <summary>
        /// Component to manage Unity Relay allocations.
        /// </summary>
        public FishyRelayManager relayManager;
        #endregion

        #region Private.
        /// <summary>
        /// Server for the transport.
        /// </summary>
        private readonly UtpServer _server = new();
        
        /// <summary>
        /// Client for the transport.
        /// </summary>
        private readonly UtpClient _client = new();
        #endregion
        
        #region Initialization and unity.
        /// <summary>
        /// Initializes the transport. Use this instead of Awake.
        /// </summary>
        public override void Initialize(NetworkManager networkManager, int transportIndex)
        {
            base.Initialize(networkManager, transportIndex);
            
            _server.Initialize(this);
            _client.Initialize(this);
            
            InitializeRelay();
        }

        private void InitializeRelay()
        {
            if (!useRelay) return;
            
            if (TryGetComponent(out relayManager))
            {
                relayManager.SetTransport(this);
                if (loginToUnityServices)
                {
                    relayManager.LoginToUnityServices();
                }
            }
            else
            {
                if (NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError("Relay services will not function as the RelayAlloc component is not added.");
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }
        #endregion

        #region ConnectionStates.
        /// <summary>
        /// Gets the IP address of a remote connection Id.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        public override string GetConnectionAddress(int connectionId)
        {
            return _server.GetConnectionAddress(connectionId);
        }
        /// <summary>
        /// Called when a connection state changes for the local client.
        /// </summary>
        public override event Action<ClientConnectionStateArgs> OnClientConnectionState;
        /// <summary>
        /// Called when a connection state changes for the local server.
        /// </summary>
        public override event Action<ServerConnectionStateArgs> OnServerConnectionState;
        /// <summary>
        /// Called when a connection state changes for a remote client.
        /// </summary>
        public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;
        /// <summary>
        /// Gets the current local ConnectionState.
        /// </summary>
        /// <param name="server">True if getting ConnectionState for the server.</param>
        public override LocalConnectionState GetConnectionState(bool server)
        {
            if (server)
                return _server.GetLocalConnectionState();
            else
                return _client.GetLocalConnectionState();
        }
        /// <summary>
        /// Gets the current ConnectionState of a remote client on the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId to get ConnectionState for.</param>
        public override RemoteConnectionState GetConnectionState(int connectionId)
        {
            return _server.GetConnectionState(connectionId);
        }
        /// <summary>
        /// Handles a ConnectionStateArgs for the local client.
        /// </summary>
        /// <param name="connectionStateArgs"></param>
        public override void HandleClientConnectionState(ClientConnectionStateArgs connectionStateArgs)
        {
            OnClientConnectionState?.Invoke(connectionStateArgs);
        }
        /// <summary>
        /// Handles a ConnectionStateArgs for the local server.
        /// </summary>
        /// <param name="connectionStateArgs"></param>
        public override void HandleServerConnectionState(ServerConnectionStateArgs connectionStateArgs)
        {
            OnServerConnectionState?.Invoke(connectionStateArgs);
        }
        /// <summary>
        /// Handles a ConnectionStateArgs for a remote client.
        /// </summary>
        /// <param name="connectionStateArgs"></param>
        public override void HandleRemoteConnectionState(RemoteConnectionStateArgs connectionStateArgs)
        {
            OnRemoteConnectionState?.Invoke(connectionStateArgs);
        }
        #endregion

        #region Iterating.
        /// <summary>
        /// Processes data received by the socket.
        /// </summary>
        /// <param name="server">True to process data received on the server.</param>
        public override void IterateIncoming(bool server)
        {
            if (server)
            {
                _server.IterateIncoming();
            }
            else
            {
                _client.IterateIncoming();
            }
        }

        /// <summary>
        /// Processes data to be sent by the socket.
        /// </summary>
        /// <param name="server">True to process data received on the server.</param>
        public override void IterateOutgoing(bool server)
        {
            // Not yet implemented for FishyUTP
        }
        #endregion

        #region ReceivedData.
        /// <summary>
        /// Called when client receives data.
        /// </summary>
        public override event Action<ClientReceivedDataArgs> OnClientReceivedData;
        /// <summary>
        /// Handles a ClientReceivedDataArgs.
        /// </summary>
        /// <param name="receivedDataArgs"></param>
        public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs receivedDataArgs)
        {
            OnClientReceivedData?.Invoke(receivedDataArgs);
        }
        /// <summary>
        /// Called when server receives data.
        /// </summary>
        public override event Action<ServerReceivedDataArgs> OnServerReceivedData;
        /// <summary>
        /// Handles a ClientReceivedDataArgs.
        /// </summary>
        /// <param name="receivedDataArgs"></param>
        public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs receivedDataArgs)
        {
            OnServerReceivedData?.Invoke(receivedDataArgs);
        }
        #endregion

        #region Sending.
        /// <summary>
        /// Sends to the server.
        /// </summary>
        /// <param name="channelId">Channel to use.</param>
        /// <param name="segment">Data to send.</param>
        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            _client.SendToServer(channelId, segment);
        }
        
        /// <summary>
        /// Sends to a client.
        /// </summary>
        /// <param name="channelId">Channel to use.</param>
        /// <param name="segment">Data to send.</param>
        /// <param name="connectionId">ConnectionId to send to. When sending to clients can be used to specify which connection to send to.</param>
        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            _server.SendToClient(channelId, segment, connectionId);
        }
        #endregion

        #region Configuration.
        /// <summary>
        /// Returns the maximum number of clients allowed to connect to the server. If the transport does not support this method the value -1 is returned.
        /// </summary>
        /// <returns></returns>
        public override int GetMaximumClients()
        {
            return _server.GetMaximumClients();
        }

        /// <summary>
        /// Sets maximum number of clients allowed to connect to the server. If applied at runtime and clients exceed this value existing clients will stay connected but new clients may not connect.
        /// </summary>
        /// <param name="value"></param>
        public override void SetMaximumClients(int value)
        {
            if (_server.GetLocalConnectionState() != LocalConnectionState.Stopped)
            {
                if (NetworkManager.CanLog(LoggingType.Warning))
                    Debug.LogWarning($"Cannot set maximum clients when server is running.");
            }
            else
            {
                _server.SetMaximumClients(value);
            }
        }

        /// <summary>
        /// Sets which address the client will connect to.
        /// </summary>
        /// <param name="address"></param>
        public override void SetClientAddress(string address)
        {
            clientAddress = address;
        }
        
        /// <summary>
        /// Gets which address the client will connect to.
        /// </summary>
        public override string GetClientAddress()
        {
            return clientAddress;
        }

        /// <summary>
        /// Sets which port to use.
        /// </summary>
        /// <param name="port"></param>
        public override void SetPort(ushort port)
        {
            this.port = port;
        }

        public override ushort GetPort()
        {
            return port;
        }
        #endregion

        #region Start and stop.
        /// <summary>
        /// Starts the local server or client using configured settings.
        /// </summary>
        /// <param name="server">True to start server.</param>
        public override bool StartConnection(bool server)
        {
            return server ? StartServer() : StartClient(clientAddress);
        }

        /// <summary>
        /// Stops the local server or client.
        /// </summary>
        /// <param name="server">True to stop server.</param>
        public override bool StopConnection(bool server)
        {
            return server ? StopServer() : StopClient();
        }

        /// <summary>
        /// Stops a remote client from the server, disconnecting the client.
        /// </summary>
        /// <param name="connectionId">ConnectionId of the client to disconnect.</param>
        /// <param name="immediately">True to abrutly stop the client socket. The technique used to accomplish immediate disconnects may vary depending on the transport.
        /// When not using immediate disconnects it's recommended to perform disconnects using the ServerManager rather than accessing the transport directly.
        /// </param>
        public override bool StopConnection(int connectionId, bool immediately)
        {
            return StopClient(connectionId);
        }

        /// <summary>
        /// Stops both client and server.
        /// </summary>
        public override void Shutdown()
        {
            StopConnection(false);
            StopConnection(true);
        }

        #region Privates.
        /// <summary>
        /// Starts server.
        /// </summary>
        /// <returns>True if there were no blocks. A true response does not promise a socket will or has connected.</returns>
        private bool StartServer()
        {
            if (!useRelay) return _server.StartConnection(port, false);
            
            relayManager.CreateAllocation(maximumClients, _ => _server.StartConnection(port, true));
            return true;
        }

        /// <summary>
        /// Stops server.
        /// </summary>
        private bool StopServer()
        {
            if (_server != null)
                return _server.StopConnection();

            return false;
        }

        /// <summary>
        /// Starts the client.
        /// </summary>
        /// <returns>True if there were no blocks. A true response does not promise a socket will or has connected.</returns>
        private bool StartClient(string address)
        {
            if (_client.GetLocalConnectionState() != LocalConnectionState.Stopped)
            {
                if (NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError("Client is already running.");
                return false;
            }

            if (useRelay)
            {
                relayManager.GetJoinAllocation(() => _client.StartConnection(relayManager.ClientAllocation));
                return true;
            }

            _client.StartConnection(address, port);
            return true;
        }

        /// <summary>
        /// Stops the client.
        /// </summary>
        private bool StopClient()
        {
            return _client.StopConnection();
        }

        /// <summary>
        /// Stops a remote client on the server.
        /// </summary>
        /// <param name="connectionId"></param>
        private bool StopClient(int connectionId)
        {
            return _server.StopConnection(connectionId);
        }
        #endregion
        #endregion

        #region Channels.
        /// <summary>
        /// Gets the MTU for a channel.
        /// </summary>
        /// <param name="channel">Channel to get MTU for.</param>
        /// <returns>MTU of channel.</returns>
        public override int GetMTU(byte channel)
        {
            //Check for client activity
            if (_client != null && _client.GetLocalConnectionState() == LocalConnectionState.Started)
            {
                return NetworkParameterConstants.MTU - _client.GetMaxHeaderSize(channel);
            }
            
            if (_server != null && _server.GetLocalConnectionState() == LocalConnectionState.Started)
            {
                return NetworkParameterConstants.MTU - _server.GetMaxHeaderSize(channel);
            }
            
            return NetworkParameterConstants.MTU;
        }
        #endregion

    }
}
