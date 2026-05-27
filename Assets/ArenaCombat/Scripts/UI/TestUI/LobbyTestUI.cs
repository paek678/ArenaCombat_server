// ARCH TAG: SHARED
// ARCH SCOPE: Lobby/relay test UI shared across gameplay modes.
// ARCH STATUS: TARGET_3D_PENDING

using System;
using System.Collections.Generic;
using ArenaCombat.Core.Network;
using ArenaCombat.UI.Utility;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

using LobbyPlayer = Unity.Services.Lobbies.Models.Player;

namespace ArenaCombat.UI.TestUI
{
    public class LobbyTestUI : MonoBehaviour
    {
        [Header("=== Main Panel ===")]
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private Button createLobbyButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private TMP_InputField joinCodeInput;

        [Header("=== Lobby Panel ===")]
        [SerializeField] private GameObject lobbyPanel;
        [SerializeField] private TextMeshProUGUI lobbyCodeText;
        [SerializeField] private LobbyPlayerSlotsUI playerSlotsUI;
        [SerializeField] private ScrollableLogDisplay debugLog;
        [SerializeField] private Button leaveLobbyButton;
        [SerializeField] private Button startGameButton;

        [Header("=== Game Panel ===")]
        [SerializeField] private GameObject gamePanel;
        [SerializeField] private TextMeshProUGUI gameStatusText;
        [SerializeField] private OutgoingDataLog outgoingDataLog;
        [SerializeField] private Button disconnectButton;
        [SerializeField] private Button freezePlayerButton;

        [Header("=== Settings ===")]
        [SerializeField] private int maxPlayers = 4;

        private LobbyManager lobbyManager;
        private RelayManager relayManager;
        private SessionManager sessionManager;
        private string myPlayerId;
        private bool isConnectingToRelay;

        private Dictionary<string, string> lastKnownActions = new Dictionary<string, string>();

        private void Start()
        {
            lobbyManager = LobbyManager.Instance;
            relayManager = RelayManager.Instance;
            sessionManager = SessionManager.Instance;

            if (lobbyManager == null)
            {
                Log("[ERROR] LobbyManager not found. Add LobbyManager to the scene.");
                return;
            }

            if (relayManager == null)
            {
                Log("[WARNING] RelayManager not found. Start game will be disabled.");
            }

            if (sessionManager == null)
            {
                Log("[WARNING] SessionManager not found. Start/Disconnect will be disabled.");
            }

            lobbyManager.MaxPlayers = maxPlayers;

            SetupButtons();
            SetupEvents();
            ShowMainPanel();

            Log("Lobby test UI initialized");
        }

        private void SetupButtons()
        {
            if (createLobbyButton != null)
                createLobbyButton.onClick.AddListener(OnCreateLobbyClicked);

            if (joinButton != null)
                joinButton.onClick.AddListener(OnJoinClicked);

            if (leaveLobbyButton != null)
                leaveLobbyButton.onClick.AddListener(OnLeaveLobbyClicked);

            if (startGameButton != null)
            {
                startGameButton.onClick.AddListener(OnStartGameClicked);
                startGameButton.gameObject.SetActive(false);
            }

            if (disconnectButton != null)
                disconnectButton.onClick.AddListener(OnDisconnectClicked);

            if (freezePlayerButton != null)
            {
                freezePlayerButton.onClick.AddListener(OnFreezePlayerClicked);
                freezePlayerButton.gameObject.SetActive(false);
            }
        }

        private void SetupEvents()
        {
            lobbyManager.OnLobbyCreated += OnLobbyEntered;
            lobbyManager.OnLobbyJoined += OnLobbyEntered;
            lobbyManager.OnLobbyUpdated += OnLobbyUpdated;
            lobbyManager.OnLobbyLeft += OnLobbyLeft;
            lobbyManager.OnError += OnError;

            if (relayManager != null)
            {
                relayManager.OnRelayCreated += OnRelayCreated;
                relayManager.OnRelayJoined += OnRelayJoined;
                relayManager.OnError += OnRelayError;
            }

            if (sessionManager != null)
            {
                sessionManager.OnGameStarted += OnGameStarted;
            }

            if (playerSlotsUI != null)
                playerSlotsUI.OnSlotActionClicked += OnSlotActionClicked;
        }

        private void OnDestroy()
        {
            if (lobbyManager != null)
            {
                lobbyManager.OnLobbyCreated -= OnLobbyEntered;
                lobbyManager.OnLobbyJoined -= OnLobbyEntered;
                lobbyManager.OnLobbyUpdated -= OnLobbyUpdated;
                lobbyManager.OnLobbyLeft -= OnLobbyLeft;
                lobbyManager.OnError -= OnError;
            }

            if (relayManager != null)
            {
                relayManager.OnRelayCreated -= OnRelayCreated;
                relayManager.OnRelayJoined -= OnRelayJoined;
                relayManager.OnError -= OnRelayError;
            }

            if (sessionManager != null)
            {
                sessionManager.OnGameStarted -= OnGameStarted;
            }

            if (playerSlotsUI != null)
                playerSlotsUI.OnSlotActionClicked -= OnSlotActionClicked;
        }

        #region Panel Control

        private void ShowMainPanel()
        {
            if (mainPanel != null) mainPanel.SetActive(true);
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
            if (gamePanel != null) gamePanel.SetActive(false);
        }

        private void ShowLobbyPanel()
        {
            if (mainPanel != null) mainPanel.SetActive(false);
            if (lobbyPanel != null) lobbyPanel.SetActive(true);
            if (gamePanel != null) gamePanel.SetActive(false);
        }

        private void ShowGamePanel()
        {
            if (mainPanel != null) mainPanel.SetActive(false);
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
            if (gamePanel != null) gamePanel.SetActive(true);
        }

        #endregion

        #region Button Handlers

        private async void OnCreateLobbyClicked()
        {
            Log("Creating lobby...");
            string lobbyName = $"TestLobby_{UnityEngine.Random.Range(1000, 9999)}";
            await lobbyManager.CreateLobbyAsync(lobbyName);
        }

        private async void OnJoinClicked()
        {
            if (joinCodeInput == null || string.IsNullOrEmpty(joinCodeInput.text))
            {
                Log("[ERROR] Please enter a join code.");
                return;
            }

            string code = joinCodeInput.text.ToUpper().Trim();
            Log($"Joining lobby... (Code: {code})");

            if (joinButton != null) joinButton.interactable = false;
            if (joinCodeInput != null) joinCodeInput.interactable = false;

            try
            {
                var lobby = await lobbyManager.JoinLobbyByCodeAsync(code);
                if (lobby == null)
                {
                    Log("[ERROR] Invalid lobby code. Please check and try again.");
                    if (joinCodeInput != null)
                    {
                        joinCodeInput.text = "";
                        joinCodeInput.interactable = true;
                        joinCodeInput.ActivateInputField();
                    }
                    if (joinButton != null) joinButton.interactable = true;
                }
            }
            catch (Exception e)
            {
                Log($"[ERROR] Join failed: {e.Message}. Please try again.");
                if (joinCodeInput != null)
                {
                    joinCodeInput.text = "";
                    joinCodeInput.interactable = true;
                    joinCodeInput.ActivateInputField();
                }
                if (joinButton != null) joinButton.interactable = true;
            }
        }

        private async void OnLeaveLobbyClicked()
        {
            Log("Leaving lobby...");
            await lobbyManager.LeaveLobbyAsync();
        }

        private async void OnStartGameClicked()
        {
            if (!lobbyManager.IsHost || relayManager == null || sessionManager == null)
            {
                Log("[ERROR] Only host can start the game.");
                return;
            }

            if (isConnectingToRelay)
            {
                Log("[WARNING] Game start is already in progress.");
                return;
            }

            isConnectingToRelay = true;
            startGameButton.interactable = false;
            Log("Starting Relay host...");

            try
            {
                string relayJoinCode = await relayManager.StartHostWithRelayAsync(lobbyManager.MaxPlayers);

                if (string.IsNullOrEmpty(relayJoinCode))
                {
                    Log("[ERROR] Failed to start Relay host");
                    isConnectingToRelay = false;
                    startGameButton.interactable = true;
                    return;
                }

                await lobbyManager.SetRelayJoinCodeAsync(relayJoinCode);
                Log($"Relay join code saved: {relayJoinCode}");

                await lobbyManager.SetGameStartedAsync(true);

                sessionManager.StartGame();
            }
            catch (Exception e)
            {
                Log($"[ERROR] Failed to start game: {e.Message}");
                isConnectingToRelay = false;
                startGameButton.interactable = true;
            }
        }

        private void OnDisconnectClicked()
        {
            Log("Disconnecting...");
            isConnectingToRelay = false;

            if (sessionManager != null)
                sessionManager.Disconnect();
        }

        #endregion

        #region Lobby Events

        private void OnLobbyEntered(Lobby lobby)
        {
            myPlayerId = lobbyManager.PlayerId;

            string hostText = lobbyManager.IsHost ? " (HOST)" : "";
            Log($"Entered lobby successfully{hostText}");
            Log($"Code - {lobby.LobbyCode}");

            if (lobbyCodeText != null)
                lobbyCodeText.text = $"Code - {lobby.LobbyCode}";

            if (startGameButton != null)
                startGameButton.gameObject.SetActive(lobbyManager.IsHost && relayManager != null && sessionManager != null);

            ShowLobbyPanel();

            if (playerSlotsUI != null)
            {
                playerSlotsUI.CreateSlots(lobby.MaxPlayers);
                playerSlotsUI.UpdateSlots(lobby, myPlayerId);
            }
        }

        private void OnLobbyUpdated(Lobby lobby)
        {
            if (playerSlotsUI != null)
                playerSlotsUI.UpdateSlots(lobby, myPlayerId);

            CheckForNewActions(lobby);
            CheckForGameStart(lobby);
        }

        private async void CheckForGameStart(Lobby lobby)
        {
            if (lobbyManager.IsHost) return;
            if (isConnectingToRelay) return;
            if (relayManager == null) return;
            if (relayManager.IsRelayConnected) return;
            if (!lobbyManager.IsGameStarted()) return;

            string relayJoinCode = lobbyManager.GetRelayJoinCode();
            if (string.IsNullOrEmpty(relayJoinCode)) return;

            isConnectingToRelay = true;
            Log($"Game start detected. Connecting to Relay... (JoinCode: {relayJoinCode})");

            try
            {
                bool success = await relayManager.JoinRelayAsync(relayJoinCode);
                if (!success)
                {
                    Log("[ERROR] Relay connection failed");
                    isConnectingToRelay = false;
                }
            }
            catch (Exception e)
            {
                Log($"[ERROR] Relay connection exception: {e.Message}");
                isConnectingToRelay = false;
            }
        }

        private void CheckForNewActions(Lobby lobby)
        {
            if (lobby == null) return;

            foreach (var player in lobby.Players)
            {
                if (player.Data == null) continue;
                if (!player.Data.TryGetValue("LastAction", out var actionData)) continue;
                if (string.IsNullOrEmpty(actionData.Value)) continue;

                string currentAction = actionData.Value;

                if (!lastKnownActions.TryGetValue(player.Id, out string lastAction) || lastAction != currentAction)
                {
                    lastKnownActions[player.Id] = currentAction;

                    string message = currentAction;
                    int separatorIndex = currentAction.IndexOf('|');
                    if (separatorIndex >= 0)
                        message = currentAction.Substring(separatorIndex + 1);

                    string playerName = GetPlayerName(player);
                    Log($"[{playerName}] {message}");
                }
            }
        }

        private void OnLobbyLeft()
        {
            Log("Left lobby");
            if (playerSlotsUI != null)
                playerSlotsUI.ClearSlots();
            ResetMainPanelControls();
            ShowMainPanel();
        }

        private void OnError(string error)
        {
            Log($"[ERROR] {error}");
        }

        private void ResetMainPanelControls()
        {
            if (joinButton != null) joinButton.interactable = true;
            if (joinCodeInput != null)
            {
                joinCodeInput.interactable = true;
                joinCodeInput.text = "";
            }
            if (createLobbyButton != null) createLobbyButton.interactable = true;
        }

        #endregion

        #region Relay Events

        private void OnRelayCreated(string joinCode)
        {
            Log($"Relay created - JoinCode: {joinCode}");
        }

        private void OnRelayJoined()
        {
            Log("Relay connected. Waiting for scene transition.");
            isConnectingToRelay = false;
        }

        private void OnGameStarted()
        {
            Log("Transitioning to game scene...");
        }

        private void OnFreezePlayerClicked()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Log("[ERROR] Only host can freeze players");
                return;
            }

            int processed = 0;
            var players3D = FindObjectsByType<PlayerNetworkController3D>(FindObjectsSortMode.None);
            foreach (var player in players3D)
            {
                if (player.IsSpawned)
                {
                    Log($"Player {player.OwnerClientId} freeze toggled");
                    processed++;
                }
            }

            if (processed == 0)
                Log("[WARNING] No spawned 3D player controllers were found.");
        }

        private void OnRelayError(string error)
        {
            Log($"[RELAY ERROR] {error}");
            isConnectingToRelay = false;

            if (startGameButton != null)
                startGameButton.interactable = true;
        }

        #endregion

        #region Slot Action

        private async void OnSlotActionClicked(int slotIndex)
        {
            await lobbyManager.SendActionAsync("Action button clicked!");
        }

        #endregion

        #region Helpers

        private void Log(string message)
        {
            debugLog.Append(message, "LobbyTestUI");
        }

        private static string GetPlayerName(LobbyPlayer player)
        {
            if (player.Data != null && player.Data.TryGetValue("PlayerName", out var nameData))
                return nameData.Value;
            return $"Player_{player.Id.Substring(0, 6)}";
        }

        #endregion
    }
}
