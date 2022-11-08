using System;
using System.Collections;
using FishNet.Managing.Logging;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace FishNet.Transporting.FishyUTPPlugin
{
    /// <summary>
    /// A container for everything FishyUTP needs to interact with Unity Relay.
    /// </summary>
    public class FishyRelayManager : MonoBehaviour
    {
        #region Public
        [SerializeField]
        [Tooltip("The join code for the current host, or the code a client should connect to.")]
        public string joinCode;
        
        /// <summary>
        /// The Relay allocation used by the server.
        /// </summary>
        public Allocation HostAllocation;
        
        /// <summary>
        /// The Relay allocation used by the client.
        /// </summary>
        public JoinAllocation ClientAllocation;
        #endregion

        #region Private
        /// <summary>
        /// UTP transport supporting Relay.
        /// </summary>
        private FishyUTP _transport;
        #endregion

        #region Initialize

        public void SetTransport(FishyUTP transport)
        {
            _transport = transport;
        }
        #endregion

        #region Unity Services
        public virtual async void LoginToUnityServices()
        {
            try
            {
                await UnityServices.InitializeAsync();
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                
                if (_transport.NetworkManager.CanLog(LoggingType.Common))
                    Debug.Log($"FishyRelayManager logged into Unity Services. Player ID: {AuthenticationService.Instance.PlayerId}");
            }
            catch (Exception)
            {
                if (_transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.Log($"FishyRelayManager failed to login to Unity Services.");
            }
        }
        #endregion
        
        #region Relay Allocations
        /// <summary>
        /// Get a hosted allocation by the join code set in the container.
        /// </summary>
        public void GetJoinAllocation(Action callback)
        {
            StartCoroutine(GetJoinAllocationTask(callback));
        }

        /// <summary>
        /// Task to get a hosted allocation by the join code set in the container.
        /// </summary>
        private IEnumerator GetJoinAllocationTask(Action callback)
        {
            var allocationTask = RelayService.Instance.JoinAllocationAsync(joinCode);

            while (!allocationTask.IsCompleted)
            {
                yield return null;
            }

            if (allocationTask.IsFaulted)
            {
                if (_transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError("RelayService faulted when obtaining join allocation.");
                yield break;
            }

            ClientAllocation = allocationTask.Result;
            callback?.Invoke();
        }
        
        /// <summary>
        /// Create a Relay allocation.
        /// </summary>
        public void CreateAllocation(int maxPlayers, Action<string> callback)
        {
            StartCoroutine(AllocateRelayServerTask(maxPlayers, callback));
        }
        
        /// <summary>
        /// Task to create a Relay allocation.
        /// </summary>
        private IEnumerator AllocateRelayServerTask(int maxPlayers, Action<string> onAllocation)
        {
            var allocationTask = RelayService.Instance.CreateAllocationAsync(maxPlayers);

            while (!allocationTask.IsCompleted)
            {
                yield return null;
            }

            if (allocationTask.IsFaulted)
            {
                if (_transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError("RelayService faulted when creating an allocation.");
                yield break;
            }
            
            HostAllocation = allocationTask.Result;
            Debug.Log($"Received allocation: {HostAllocation.AllocationId}");

            StartCoroutine(GetJoinCodeTask(onAllocation));
        }
        
        /// <summary>
        /// Task to obtain a join code for the host allocation.
        /// </summary>
        private IEnumerator GetJoinCodeTask(Action<string> onJoinCode)
        {
            var joinCodeTask = RelayService.Instance.GetJoinCodeAsync(HostAllocation.AllocationId);

            while (!joinCodeTask.IsCompleted)
            {
                yield return null;
            }

            if (joinCodeTask.IsFaulted)
            {
                if (_transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError("RelayService faulted when obtaining join code for host allocation.");
                yield break;
            }

            joinCode = joinCodeTask.Result;
            onJoinCode?.Invoke(joinCode);
        }
        #endregion
    }
}