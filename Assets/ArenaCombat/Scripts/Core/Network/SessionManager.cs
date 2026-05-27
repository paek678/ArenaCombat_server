// ARCH TAG: SHARED
// ARCH SCOPE: Session lifecycle — game start, disconnect, network callback ownership.
// ARCH STATUS: TARGET_3D_ACTIVE

using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArenaCombat.Core.Network
{
    public class SessionManager : MonoBehaviour
    {
        public static SessionManager Instance { get; private set; }

        [SerializeField] private string gameSceneName = "Chapter1";
        [SerializeField] private string titleSceneName = "SampleScene";

        public event Action OnGameStarted;

        private bool networkCallbacksRegistered;
        private bool sceneLoadCallbackRegistered;
        private bool isDisconnecting;

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

        private void OnEnable() => TryRegisterNetworkCallbacks();
        private void Start() => TryRegisterNetworkCallbacks();

        private void Update()
        {
            if (!networkCallbacksRegistered)
                TryRegisterNetworkCallbacks();
        }

        private void OnDisable()
        {
            UnregisterNetworkCallbacks();
            UnregisterSceneLoadCallback();
        }

        private void OnDestroy()
        {
            UnregisterNetworkCallbacks();
            UnregisterSceneLoadCallback();
        }

        #region Network Callbacks

        private void TryRegisterNetworkCallbacks()
        {
            if (networkCallbacksRegistered) return;

            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            nm.OnClientStopped += OnClientStopped;
            nm.OnTransportFailure += OnTransportFailure;
            networkCallbacksRegistered = true;
        }

        private void UnregisterNetworkCallbacks()
        {
            if (!networkCallbacksRegistered) return;

            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientConnectedCallback -= OnClientConnected;
                nm.OnClientDisconnectCallback -= OnClientDisconnected;
                nm.OnClientStopped -= OnClientStopped;
                nm.OnTransportFailure -= OnTransportFailure;
            }

            networkCallbacksRegistered = false;
        }

        #endregion

        #region Scene Load

        private void RegisterSceneLoadCallback()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.SceneManager == null) return;

            nm.SceneManager.OnLoadComplete -= OnSceneLoadComplete;
            nm.SceneManager.OnLoadComplete += OnSceneLoadComplete;
            sceneLoadCallbackRegistered = true;
        }

        private void UnregisterSceneLoadCallback()
        {
            if (!sceneLoadCallbackRegistered) return;

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SceneManager != null)
                nm.SceneManager.OnLoadComplete -= OnSceneLoadComplete;

            sceneLoadCallbackRegistered = false;
        }

        private void OnSceneLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            if (clientId == nm.LocalClientId)
            {
                Debug.Log($"[SessionManager] Local scene load complete - ClientId: {clientId}, Scene: {sceneName}");
                OnGameStarted?.Invoke();
                UnregisterSceneLoadCallback();
            }
        }

        #endregion

        #region Game Start

        public void StartGame()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogError("[SessionManager] NetworkManager missing. Cannot start game.");
                return;
            }

            if (!nm.IsHost)
            {
                Debug.LogWarning("[SessionManager] Only host can start the game.");
                return;
            }

            if (nm.SceneManager == null)
            {
                Debug.LogError("[SessionManager] SceneManager missing. Cannot load game scene.");
                return;
            }

            Debug.Log("[SessionManager] Starting game scene transition");
            LobbyManager.Instance?.SetGameSessionActive(true);

            RegisterSceneLoadCallback();
            var status = nm.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[SessionManager] Scene load failed: {status}");
                UnregisterSceneLoadCallback();
                LobbyManager.Instance?.SetGameSessionActive(false);
            }
        }

        #endregion

        #region Disconnect

        public void Disconnect()
        {
            if (isDisconnecting) return;
            isDisconnecting = true;

            try
            {
                UnregisterSceneLoadCallback();

                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    NetworkManager.Singleton.Shutdown();
                    Debug.Log("[SessionManager] NetworkManager shutdown complete");
                }

                if (RelayManager.Instance != null)
                    RelayManager.Instance.ClearState();

                if (LobbyManager.Instance != null)
                    LobbyManager.Instance.ForceCleanup();

                SceneManager.LoadScene(titleSceneName);
                Debug.Log("[SessionManager] Returned to title scene");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SessionManager] Disconnect cleanup error: {e.Message}");

                if (RelayManager.Instance != null)
                    RelayManager.Instance.ClearState();

                try { SceneManager.LoadScene(titleSceneName); } catch { }
            }
            finally
            {
                isDisconnecting = false;
            }
        }

        #endregion

        #region Callback Handlers

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[SessionManager] Client connected - ClientId: {clientId}");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;

            if (nm == null)
            {
                Debug.Log("[SessionManager] NetworkManager gone during disconnect. Returning to lobby.");
                Disconnect();
                return;
            }

            if (!(RelayManager.Instance?.IsRelayConnected ?? false)) return;

            if (!nm.IsServer && clientId == nm.LocalClientId)
            {
                Debug.Log("[SessionManager] Server connection lost. Returning to lobby.");
                Disconnect();
            }
        }

        private void OnClientStopped(bool isHost)
        {
            if (!(RelayManager.Instance?.IsRelayConnected ?? false)) return;

            Debug.Log($"[SessionManager] OnClientStopped (IsHost: {isHost}). Returning to lobby.");
            Disconnect();
        }

        private void OnTransportFailure()
        {
            Debug.LogWarning("[SessionManager] Transport failure. Returning to lobby.");
            Disconnect();
        }

        #endregion
    }
}
