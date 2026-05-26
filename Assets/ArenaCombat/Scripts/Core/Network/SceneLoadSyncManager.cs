// ARCH TAG: SHARED
// ARCH SCOPE: Scene load synchronization across all connected clients.
// ARCH STATUS: TARGET_3D_ACTIVE

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaCombat.Core.Network
{
    /// <summary>
    /// Synchronizes scene loading across all connected clients.
    /// Shows a loading overlay until every player has finished loading.
    /// Place this on a GameObject with NetworkObject in the game scene (e.g. Chapter1).
    ///
    /// UI Setup:
    ///   1. Create your loading UI as a child Canvas under this GameObject.
    ///   2. Assign the root GameObject of that Canvas to "overlayPrefab".
    ///   3. (Optional) Assign progressBarFill to show loading progress.
    ///   The overlay is activated on spawn and destroyed when all players are loaded.
    /// </summary>
    public class SceneLoadSyncManager : NetworkBehaviour
    {
        public static SceneLoadSyncManager Instance { get; private set; }

        [Header("=== Settings ===")]
        [SerializeField] private float loadTimeout = 30f;
        [SerializeField] private float fadeOutDuration = 0.5f;

        [Header("=== UI References (Assign in Inspector) ===")]
        [Tooltip("Root GameObject of the loading overlay (Canvas). " +
                 "Will be activated on spawn and destroyed when loading completes.")]
        [SerializeField] private GameObject overlayRoot;

        [Tooltip("CanvasGroup on the overlay root for fade-out. " +
                 "If not assigned, the script will try GetComponent on overlayRoot.")]
        [SerializeField] private CanvasGroup overlayCanvasGroup;

        [Tooltip("Image that represents loading progress. " +
                 "Must have Image Type set to 'Filled' in the Inspector. " +
                 "fillAmount is driven from 0 to 1 based on loaded player count.")]
        [SerializeField] private Image progressBarFill;

        /// <summary>
        /// True when all connected clients have reported scene load complete.
        /// </summary>
        public bool AllPlayersLoaded => networkAllPlayersLoaded.Value;

        private readonly NetworkVariable<bool> networkAllPlayersLoaded = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> networkLoadedCount = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> networkTotalCount = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Server-only tracking
        private HashSet<ulong> loadedClients;
        private float timeoutTimer;

        // Fade out state
        private bool isFadingOut;
        private float fadeTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Keep overlay hidden until OnNetworkSpawn decides to show it.
            if (overlayRoot != null)
            {
                overlayRoot.SetActive(false);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            networkLoadedCount.OnValueChanged += OnLoadedCountChanged;
            networkTotalCount.OnValueChanged += OnTotalCountChanged;
            networkAllPlayersLoaded.OnValueChanged += OnAllPlayersLoadedChanged;

            if (IsServer)
            {
                loadedClients = new HashSet<ulong>();
                timeoutTimer = loadTimeout;

                // Use lobby player count as the expected total.
                // ConnectedClientsIds only has the host at this point because
                // clients join Relay AFTER the host starts the scene transition.
                int total = 1;
                if (LobbyManager.Instance != null && LobbyManager.Instance.CurrentLobby != null)
                {
                    total = LobbyManager.Instance.CurrentLobby.Players.Count;
                }
                networkTotalCount.Value = total;
                networkLoadedCount.Value = 0;
                networkAllPlayersLoaded.Value = false;

                Debug.Log($"[SceneLoadSyncManager] Server initialized. Waiting for {total} clients to load.");
            }

            // If already completed (late joiner), skip overlay entirely.
            if (networkAllPlayersLoaded.Value)
            {
                Debug.Log("[SceneLoadSyncManager] Already loaded. Skipping overlay.");
                return;
            }

            ShowOverlay();
            UpdateUI();

            ReportLoadedServerRpc();
        }

        public override void OnNetworkDespawn()
        {
            networkLoadedCount.OnValueChanged -= OnLoadedCountChanged;
            networkTotalCount.OnValueChanged -= OnTotalCountChanged;
            networkAllPlayersLoaded.OnValueChanged -= OnAllPlayersLoadedChanged;

            if (Instance == this)
            {
                Instance = null;
            }

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            // Server: timeout check
            if (IsServer && !networkAllPlayersLoaded.Value)
            {
                timeoutTimer -= Time.unscaledDeltaTime;
                if (timeoutTimer <= 0f)
                {
                    Debug.LogWarning($"[SceneLoadSyncManager] Timeout reached. " +
                                     $"Loaded {networkLoadedCount.Value}/{networkTotalCount.Value}. Forcing start.");
                    networkAllPlayersLoaded.Value = true;
                }
            }

            // Fade out animation
            if (isFadingOut && overlayCanvasGroup != null)
            {
                fadeTimer += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(fadeTimer / fadeOutDuration);
                overlayCanvasGroup.alpha = 1f - t;

                if (t >= 1f)
                {
                    isFadingOut = false;
                    HideOverlay();
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReportLoadedServerRpc(ServerRpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;

            if (!loadedClients.Add(clientId))
            {
                return;
            }

            networkLoadedCount.Value = loadedClients.Count;
            Debug.Log($"[SceneLoadSyncManager] Client {clientId} loaded. ({loadedClients.Count}/{networkTotalCount.Value})");

            if (loadedClients.Count >= networkTotalCount.Value)
            {
                Debug.Log("[SceneLoadSyncManager] All players loaded!");
                networkAllPlayersLoaded.Value = true;
            }
        }

        #region Overlay Control

        private void ShowOverlay()
        {
            if (overlayRoot == null)
            {
                Debug.LogWarning("[SceneLoadSyncManager] overlayRoot is not assigned. No loading UI will be shown.");
                return;
            }

            overlayRoot.SetActive(true);

            // Auto-find CanvasGroup if not explicitly assigned.
            if (overlayCanvasGroup == null)
            {
                overlayCanvasGroup = overlayRoot.GetComponent<CanvasGroup>();
            }

            if (overlayCanvasGroup != null)
            {
                overlayCanvasGroup.alpha = 1f;
                overlayCanvasGroup.blocksRaycasts = true;
            }
        }

        private void HideOverlay()
        {
            if (overlayRoot != null)
            {
                overlayRoot.SetActive(false);
            }
        }

        private void UpdateUI()
        {
            if (progressBarFill == null) return;

            int loaded = networkLoadedCount.Value;
            int total = networkTotalCount.Value;
            float progress = total > 0 ? (float)loaded / total : 0f;

            progressBarFill.fillAmount = progress;
        }

        private void OnLoadedCountChanged(int oldValue, int newValue)
        {
            UpdateUI();
        }

        private void OnTotalCountChanged(int oldValue, int newValue)
        {
            UpdateUI();
        }

        private void OnAllPlayersLoadedChanged(bool oldValue, bool newValue)
        {
            if (newValue)
            {
                Debug.Log("[SceneLoadSyncManager] All players loaded. Fading out overlay.");

                if (overlayCanvasGroup != null)
                {
                    overlayCanvasGroup.blocksRaycasts = false;
                    isFadingOut = true;
                    fadeTimer = 0f;
                }
                else
                {
                    // No CanvasGroup → just hide immediately.
                    HideOverlay();
                }
            }
        }

        #endregion
    }
}
