// ARCH TAG: SHARED
// ARCH SCOPE: Relay allocation and join only. Session lifecycle owned by SessionManager.
// ARCH STATUS: TARGET_3D_ACTIVE

using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace ArenaCombat.Core.Network
{
    /// <summary>
    /// Relay session manager.
    /// Handles Relay allocation/join and NGO transport setup.
    /// Scene transitions and disconnect are owned by SessionManager.
    /// </summary>
    public class RelayManager : MonoBehaviour
    {
        public static RelayManager Instance { get; private set; }

        public event Action<string> OnRelayCreated;
        public event Action OnRelayJoined;
        public event Action<string> OnError;

        public bool IsRelayConnected { get; private set; }
        public string CurrentJoinCode { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        #region Host

        /// <summary>
        /// Creates Relay allocation and starts NGO host.
        /// </summary>
        public async Task<string> StartHostWithRelayAsync(int maxConnections = 4)
        {
            try
            {
                Debug.Log($"[RelayManager] Creating Relay allocation... (maxConnections: {maxConnections})");

                var networkManager = NetworkManager.Singleton;
                if (networkManager == null)
                {
                    throw new Exception("NetworkManager.Singleton not found.");
                }

                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);

                CurrentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                Debug.Log($"[RelayManager] Relay Join Code: {CurrentJoinCode}");

                var transport = networkManager.GetComponent<UnityTransport>();
                if (transport == null)
                {
                    throw new Exception("UnityTransport component not found.");
                }

                var relayServerData = new RelayServerData(allocation, "dtls");
                transport.SetRelayServerData(relayServerData);

                if (!networkManager.StartHost())
                {
                    throw new Exception("NetworkManager.StartHost failed.");
                }

                IsRelayConnected = true;
                Debug.Log("[RelayManager] Relay host started");

                OnRelayCreated?.Invoke(CurrentJoinCode);
                return CurrentJoinCode;
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"[RelayManager] Relay host start failed: {e.Message}");
                OnError?.Invoke($"Relay host start failed: {e.Message}");
                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RelayManager] Host start failed: {e.Message}");
                OnError?.Invoke($"Host start failed: {e.Message}");
                return null;
            }
        }

        #endregion

        #region Client

        /// <summary>
        /// Joins Relay with join code and starts NGO client.
        /// </summary>
        public async Task<bool> JoinRelayAsync(string joinCode)
        {
            try
            {
                Debug.Log($"[RelayManager] Joining Relay... (JoinCode: {joinCode})");

                var networkManager = NetworkManager.Singleton;
                if (networkManager == null)
                {
                    throw new Exception("NetworkManager.Singleton not found.");
                }

                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

                var transport = networkManager.GetComponent<UnityTransport>();
                if (transport == null)
                {
                    throw new Exception("UnityTransport component not found.");
                }

                var relayServerData = new RelayServerData(joinAllocation, "dtls");
                transport.SetRelayServerData(relayServerData);

                if (!networkManager.StartClient())
                {
                    throw new Exception("NetworkManager.StartClient failed.");
                }

                CurrentJoinCode = joinCode;
                IsRelayConnected = true;
                LobbyManager.Instance?.SetGameSessionActive(true);
                Debug.Log("[RelayManager] Relay client connected");

                OnRelayJoined?.Invoke();
                return true;
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"[RelayManager] Relay join failed: {e.Message}");
                OnError?.Invoke($"Relay join failed: {e.Message}");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RelayManager] Client start failed: {e.Message}");
                OnError?.Invoke($"Client start failed: {e.Message}");
                return false;
            }
        }

        #endregion

        #region State

        /// <summary>
        /// Clears relay connection state. Called by SessionManager during disconnect.
        /// </summary>
        public void ClearState()
        {
            IsRelayConnected = false;
            CurrentJoinCode = null;
        }

        #endregion

        #region Debug

        public void PrintRelayInfo()
        {
            Debug.Log("========== Relay Info ==========");
            Debug.Log($"IsRelayConnected: {IsRelayConnected}");
            Debug.Log($"JoinCode: {CurrentJoinCode ?? "None"}");

            if (NetworkManager.Singleton != null)
            {
                Debug.Log($"IsHost: {NetworkManager.Singleton.IsHost}");
                Debug.Log($"IsClient: {NetworkManager.Singleton.IsClient}");
                Debug.Log($"IsServer: {NetworkManager.Singleton.IsServer}");
                Debug.Log($"ConnectedClients: {NetworkManager.Singleton.ConnectedClientsList?.Count ?? 0}");
            }
            Debug.Log("================================");
        }

        #endregion
    }
}
