// ARCH TAG: SHARED
// ARCH SCOPE: Lobby/relay test UI shared across gameplay modes.
// ARCH STATUS: TARGET_3D_PENDING

using System;
using System.Collections.Generic;
using ArenaCombat.Core.Network;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

// Avoid name collision between Unity Lobby Player and local Player types.
using LobbyPlayer = Unity.Services.Lobbies.Models.Player;

namespace ArenaCombat.UI.TestUI
{
    /// <summary>
    /// Test lobby UI controller.
    /// Handles panel transitions and dynamic player-slot rendering.
    /// </summary>
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
        [SerializeField] private Transform playerSlotsContainer;
        [SerializeField] private TMP_Text debugLogText;
        [SerializeField] private ScrollRect debugLogScrollRect;
        [SerializeField] private Button leaveLobbyButton;
        [SerializeField] private Button startGameButton;  // Host-only start game button

        [Header("=== Game Panel ===")]
        [SerializeField] private GameObject gamePanel;
        [SerializeField] private TextMeshProUGUI gameStatusText;
        [SerializeField] private TMP_Text outgoingDataLogText;       // Debug log
        [SerializeField] private ScrollRect outgoingDataScrollRect;  // Scroll view
        [SerializeField] private Button disconnectButton;
        [SerializeField] private Button freezePlayerButton;          // Freeze player toggle (Host only)

        [Header("=== Settings ===")]
        [SerializeField] private int maxPlayers = 4;

        [Header("=== Slot Style ===")]
        [SerializeField] private float slotHeight = 60f;
        [SerializeField] private float slotSpacing = 10f;
        [SerializeField] private Color emptySlotColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        [SerializeField] private Color filledSlotColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
        [SerializeField] private Color mySlotColor = new Color(0.2f, 0.6f, 0.3f, 0.8f);
        [SerializeField] private Color hostSlotColor = new Color(0.6f, 0.5f, 0.2f, 0.8f);

        private LobbyManager lobbyManager;
        private RelayManager relayManager;
        private List<PlayerSlotData> playerSlots = new List<PlayerSlotData>();
        private string myPlayerId;
        private bool isConnectingToRelay = false;

        // Tracks each player's latest action to avoid duplicate UI logs.
        private Dictionary<string, string> lastKnownActions = new Dictionary<string, string>();

        // Runtime slot data model (replaces a separate PlayerSlotUI component).
        private class PlayerSlotData
        {
            public GameObject slotObject;
            public Image background;
            public TextMeshProUGUI slotNumberText;
            public TextMeshProUGUI playerNameText;
            public TextMeshProUGUI statusText;
            public Button actionButton;
            public string playerId;
            public string playerName;
            public bool isOccupied;
            public bool isMe;
        }

        private void Start()
        {
            lobbyManager = LobbyManager.Instance;
            relayManager = RelayManager.Instance;

            if (lobbyManager == null)
            {
                AddDebugLog("[ERROR] LobbyManager not found. Add LobbyManager to the scene.");
                return;
            }

            if (relayManager == null)
            {
                AddDebugLog("[WARNING] RelayManager not found. Start game will be disabled.");
            }

            lobbyManager.MaxPlayers = maxPlayers;

            SetupButtons();
            SetupEvents();
            ShowMainPanel();

            AddDebugLog("Lobby test UI initialized");
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
                startGameButton.gameObject.SetActive(false); // Hidden by default
            }

            if (disconnectButton != null)
                disconnectButton.onClick.AddListener(OnDisconnectClicked);

            if (freezePlayerButton != null)
            {
                freezePlayerButton.onClick.AddListener(OnFreezePlayerClicked);
                freezePlayerButton.gameObject.SetActive(false); // Hidden until game starts
            }
        }

        private void SetupEvents()
        {
            lobbyManager.OnLobbyCreated += OnLobbyEntered;
            lobbyManager.OnLobbyJoined += OnLobbyEntered;
            lobbyManager.OnLobbyUpdated += OnLobbyUpdated;
            lobbyManager.OnLobbyLeft += OnLobbyLeft;
            lobbyManager.OnError += OnError;

            // RelayManager event binding
            if (relayManager != null)
            {
                relayManager.OnRelayCreated += OnRelayCreated;
                relayManager.OnRelayJoined += OnRelayJoined;
                relayManager.OnGameStarted += OnGameStarted;
                relayManager.OnError += OnRelayError;
            }
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
                relayManager.OnGameStarted -= OnGameStarted;
                relayManager.OnError -= OnRelayError;
            }

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
            AddDebugLog("Creating lobby...");
            string lobbyName = $"TestLobby_{UnityEngine.Random.Range(1000, 9999)}";
            await lobbyManager.CreateLobbyAsync(lobbyName);
        }

        private async void OnJoinClicked()
        {
            if (joinCodeInput == null || string.IsNullOrEmpty(joinCodeInput.text))
            {
                AddDebugLog("[ERROR] Please enter a join code.");
                return;
            }

            string code = joinCodeInput.text.ToUpper().Trim();
            AddDebugLog($"Joining lobby... (Code: {code})");

            if (joinButton != null) joinButton.interactable = false;
            if (joinCodeInput != null) joinCodeInput.interactable = false;

            try
            {
                var lobby = await lobbyManager.JoinLobbyByCodeAsync(code);
                if (lobby == null)
                {
                    AddDebugLog("[ERROR] Invalid lobby code. Please check and try again.");
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
                AddDebugLog($"[ERROR] Join failed: {e.Message}. Please try again.");
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
            AddDebugLog("Leaving lobby...");
            await lobbyManager.LeaveLobbyAsync();
        }

        private async void OnStartGameClicked()
        {
            if (!lobbyManager.IsHost || relayManager == null)
            {
                AddDebugLog("[ERROR] Only host can start the game.");
                return;
            }

            if (isConnectingToRelay)
            {
                AddDebugLog("[WARNING] Game start is already in progress.");
                return;
            }

            isConnectingToRelay = true;
            startGameButton.interactable = false;
            AddDebugLog("Starting Relay host...");

            try
            {
                // Start Relay host
                string relayJoinCode = await relayManager.StartHostWithRelayAsync(lobbyManager.MaxPlayers);

                if (string.IsNullOrEmpty(relayJoinCode))
                {
                    AddDebugLog("[ERROR] Failed to start Relay host");
                    isConnectingToRelay = false;
                    startGameButton.interactable = true;
                    return;
                }

                await lobbyManager.SetRelayJoinCodeAsync(relayJoinCode);
                AddDebugLog($"Relay join code saved: {relayJoinCode}");

                // Update game started flag in Lobby data
                await lobbyManager.SetGameStartedAsync(true);

                // Start scene transition
                relayManager.StartGame();
            }
            catch (Exception e)
            {
                AddDebugLog($"[ERROR] Failed to start game: {e.Message}");
                isConnectingToRelay = false;
                startGameButton.interactable = true;
            }
        }

        private void OnDisconnectClicked()
        {
            AddDebugLog("Disconnecting...");

            // Disconnect Relay/NGO session
            if (relayManager != null)
            {
                relayManager.Disconnect();
            }

            // Return to main panel in UI state
            isConnectingToRelay = false;
            ShowMainPanel();
            AddDebugLog("Disconnected");
        }

        #endregion

        #region Lobby Events

        private void OnLobbyEntered(Lobby lobby)
        {
            myPlayerId = lobbyManager.PlayerId;

            string hostText = lobbyManager.IsHost ? " (HOST)" : "";
            AddDebugLog($"Entered lobby successfully{hostText}");
            AddDebugLog($"Code - {lobby.LobbyCode}");

            if (lobbyCodeText != null)
            {
                lobbyCodeText.text = $"Code - {lobby.LobbyCode}";
            }

            // Host can see start game button
            if (startGameButton != null)
            {
                startGameButton.gameObject.SetActive(lobbyManager.IsHost && relayManager != null);
            }

            ShowLobbyPanel();
            CreatePlayerSlots(lobby.MaxPlayers);
            UpdatePlayerSlots(lobby);
        }

        private void OnLobbyUpdated(Lobby lobby)
        {
            UpdatePlayerSlots(lobby);
            CheckForNewActions(lobby);
            CheckForGameStart(lobby);
        }

        /// <summary>
        /// Clients detect game start and connect to Relay.
        /// </summary>
        private async void CheckForGameStart(Lobby lobby)
        {
            // Host already owns the Relay host.
            if (lobbyManager.IsHost) return;

            // Skip when already connecting.
            if (isConnectingToRelay) return;

            // Relay manager required.
            if (relayManager == null) return;

            // Already connected.
            if (relayManager.IsRelayConnected) return;

            // Not started yet.
            if (!lobbyManager.IsGameStarted()) return;

            // Read join code from lobby data.
            string relayJoinCode = lobbyManager.GetRelayJoinCode();
            if (string.IsNullOrEmpty(relayJoinCode)) return;

            isConnectingToRelay = true;
            AddDebugLog($"Game start detected. Connecting to Relay... (JoinCode: {relayJoinCode})");

            try
            {
                bool success = await relayManager.JoinRelayAsync(relayJoinCode);

                if (!success)
                {
                    AddDebugLog("[ERROR] Relay connection failed");
                    isConnectingToRelay = false;
                }
            }
            catch (Exception e)
            {
                AddDebugLog($"[ERROR] Relay connection exception: {e.Message}");
                isConnectingToRelay = false;
            }
        }

        /// <summary>
        /// Detect newly published player actions and print them once.
        /// </summary>
        private void CheckForNewActions(Lobby lobby)
        {
            if (lobby == null) return;

            foreach (var player in lobby.Players)
            {
                if (player.Data == null) continue;
                if (!player.Data.TryGetValue("LastAction", out var actionData)) continue;
                if (string.IsNullOrEmpty(actionData.Value)) continue;

                string currentAction = actionData.Value;

                // Log only if action changed since last poll.
                if (!lastKnownActions.TryGetValue(player.Id, out string lastAction) || lastAction != currentAction)
                {
                    lastKnownActions[player.Id] = currentAction;

                    // Strip timestamp prefix.
                    string message = currentAction;
                    int separatorIndex = currentAction.IndexOf('|');
                    if (separatorIndex >= 0)
                    {
                        message = currentAction.Substring(separatorIndex + 1);
                    }

                    string playerName = GetPlayerName(player);

                    // Append to lobby debug log.
                    AddDebugLog($"[{playerName}] {message}");
                }
            }
        }

        private void OnLobbyLeft()
        {
            AddDebugLog("Left lobby");
            ClearPlayerSlots();
            ShowMainPanel();
        }

        private void OnError(string error)
        {
            AddDebugLog($"[ERROR] {error}");
        }

        #endregion

        #region Relay Events

        private void OnRelayCreated(string joinCode)
        {
            AddDebugLog($"Relay created - JoinCode: {joinCode}");
        }

        private void OnRelayJoined()
        {
            AddDebugLog("Relay connected. Waiting for scene transition.");
            isConnectingToRelay = false;
        }

        private void OnGameStarted()
        {
            // NGO scene manager handles actual scene transition.
            AddDebugLog("Transitioning to game scene...");
        }

        private void OnFreezePlayerClicked()
        {
            // Only host can freeze players
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                AddDebugLog("[ERROR] Only host can freeze players");
                return;
            }

            // Find all active 3D network player controllers.
            int processed = 0;

            var players3D = FindObjectsByType<PlayerNetworkController3D>(FindObjectsSortMode.None);
            foreach (var player in players3D)
            {
                if (player.IsSpawned)
                {
                    AddDebugLog($"Player {player.OwnerClientId} freeze toggled");
                    processed++;
                }
            }

            if (processed == 0)
            {
                AddDebugLog("[WARNING] No spawned 3D player controllers were found.");
            }
        }

        private void OnRelayError(string error)
        {
            AddDebugLog($"[RELAY ERROR] {error}");
            isConnectingToRelay = false;

            if (startGameButton != null)
                startGameButton.interactable = true;
        }

        #endregion

        #region Player Slots - Runtime Build

        private void CreatePlayerSlots(int count)
        {
            ClearPlayerSlots();

            if (playerSlotsContainer == null)
            {
                AddDebugLog("[ERROR] PlayerSlotsContainer is not assigned.");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                PlayerSlotData slotData = CreateSlotUI(i + 1);
                playerSlots.Add(slotData);
            }

            AddDebugLog($"Created {count} player slots");
        }

        private PlayerSlotData CreateSlotUI(int slotNumber)
        {
            PlayerSlotData data = new PlayerSlotData();

            // Root slot object
            GameObject slotObj = new GameObject($"PlayerSlot_{slotNumber}");
            slotObj.transform.SetParent(playerSlotsContainer, false);
            data.slotObject = slotObj;

            // RectTransform setup
            RectTransform slotRect = slotObj.AddComponent<RectTransform>();
            slotRect.sizeDelta = new Vector2(0, slotHeight);
            slotRect.anchorMin = new Vector2(0, 1);
            slotRect.anchorMax = new Vector2(1, 1);
            slotRect.pivot = new Vector2(0.5f, 1);

            // Vertical placement
            float yPos = -(slotNumber - 1) * (slotHeight + slotSpacing);
            slotRect.anchoredPosition = new Vector2(0, yPos);

            // Horizontal layout
            HorizontalLayoutGroup layout = slotObj.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 5, 5);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            // Background
            data.background = slotObj.AddComponent<Image>();
            data.background.color = emptySlotColor;

            data.slotNumberText = CreateTextElement(slotObj.transform, $"#{slotNumber}", 50);
            data.slotNumberText.alignment = TextAlignmentOptions.Center;

            data.playerNameText = CreateTextElement(slotObj.transform, "Empty", 200);
            data.playerNameText.alignment = TextAlignmentOptions.Left;

            data.statusText = CreateTextElement(slotObj.transform, "", 80);
            data.statusText.alignment = TextAlignmentOptions.Center;
            data.statusText.fontSize = 14;

            // Action button
            data.actionButton = CreateButtonElement(slotObj.transform, "Action", 80);
            int capturedSlotNumber = slotNumber;
            data.actionButton.onClick.AddListener(() => OnSlotActionClicked(capturedSlotNumber - 1));
            data.actionButton.gameObject.SetActive(false);

            return data;
        }

        private TextMeshProUGUI CreateTextElement(Transform parent, string text, float width)
        {
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(parent, false);

            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, slotHeight);

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 18;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Keep width fixed with LayoutElement
            LayoutElement layoutElement = textObj.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = width;
            layoutElement.flexibleWidth = 0;

            return tmp;
        }

        private Button CreateButtonElement(Transform parent, string text, float width)
        {
            GameObject buttonObj = new GameObject("ActionButton");
            buttonObj.transform.SetParent(parent, false);

            RectTransform rect = buttonObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, slotHeight - 10);

            Image buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = new Color(0.4f, 0.4f, 0.4f, 1f);

            Button button = buttonObj.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            colors.pressedColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            button.colors = colors;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 16;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            // Keep width fixed with LayoutElement
            LayoutElement layoutElement = buttonObj.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = width;
            layoutElement.flexibleWidth = 0;

            return button;
        }

        private void ClearPlayerSlots()
        {
            foreach (var slot in playerSlots)
            {
                if (slot.slotObject != null)
                {
                    Destroy(slot.slotObject);
                }
            }
            playerSlots.Clear();
        }

        private void UpdatePlayerSlots(Lobby lobby)
        {
            if (lobby == null) return;

            for (int i = 0; i < playerSlots.Count; i++)
            {
                PlayerSlotData slot = playerSlots[i];

                if (i < lobby.Players.Count)
                {
                    LobbyPlayer player = lobby.Players[i];
                    string playerName = GetPlayerName(player);
                    bool isHost = player.Id == lobby.HostId;
                    bool isMe = player.Id == myPlayerId;

                    SetSlotPlayer(slot, player.Id, playerName, isHost, isMe);
                }
                else
                {
                    SetSlotEmpty(slot);
                }
            }
        }

        private void SetSlotPlayer(PlayerSlotData slot, string playerId, string playerName, bool isHost, bool isMe)
        {
            slot.playerId = playerId;
            slot.playerName = playerName;
            slot.isOccupied = true;
            slot.isMe = isMe;

            // Display player name/host/me marks
            string hostMark = isHost ? " [HOST]" : "";
            string meMark = isMe ? " (Me)" : "";
            slot.playerNameText.text = $"{playerName}{hostMark}{meMark}";

            slot.statusText.text = "Online";
            slot.statusText.color = Color.green;

            slot.actionButton.gameObject.SetActive(isMe);

            if (isMe)
                slot.background.color = mySlotColor;
            else if (isHost)
                slot.background.color = hostSlotColor;
            else
                slot.background.color = filledSlotColor;
        }

        private void SetSlotEmpty(PlayerSlotData slot)
        {
            slot.playerId = null;
            slot.playerName = null;
            slot.isOccupied = false;
            slot.isMe = false;

            slot.playerNameText.text = "Empty";
            slot.statusText.text = "";
            slot.actionButton.gameObject.SetActive(false);
            slot.background.color = emptySlotColor;
        }

        private async void OnSlotActionClicked(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= playerSlots.Count) return;

            PlayerSlotData slot = playerSlots[slotIndex];
            if (!slot.isOccupied || !slot.isMe) return;

            // Send a visible action text to other lobby members.
            await lobbyManager.SendActionAsync("Action button clicked!");
        }

        private string GetPlayerName(LobbyPlayer player)
        {
            if (player.Data != null && player.Data.TryGetValue("PlayerName", out var nameData))
            {
                return nameData.Value;
            }
            return $"Player_{player.Id.Substring(0, 6)}";
        }

        #endregion

        #region Debug Log

        private void AddDebugLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string logEntry = $"[{timestamp}] {message}\n";

            Debug.Log($"[LobbyTestUI] {message}");

            if (debugLogText != null)
            {
                debugLogText.text += logEntry;

                string[] lines = debugLogText.text.Split('\n');
                if (lines.Length > 50)
                {
                    debugLogText.text = string.Join("\n", lines, lines.Length - 50, 50);
                }

                if (debugLogScrollRect != null)
                {
                    Canvas.ForceUpdateCanvases();
                    debugLogScrollRect.verticalNormalizedPosition = 0f;
                }
            }
        }

        #endregion

        #region Outgoing Data Log (Client -> Server)

        /// <summary>
        /// Appends a client-to-server payload log row.
        /// Exposed for calls from other scripts.
        /// </summary>
        /// <param name="dataType">Category (e.g. Input, ServerRpc, Position)</param>
        /// <param name="dataContent">Serialized payload detail</param>
        public void LogOutgoingData(string dataType, string dataContent)
        {
            if (outgoingDataLogText == null) return;

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logEntry = $"[{timestamp}] <color=#00FF00>[{dataType}]</color> {dataContent}\n";

            outgoingDataLogText.text += logEntry;

            // Keep only latest 100 lines.
            string[] lines = outgoingDataLogText.text.Split('\n');
            if (lines.Length > 100)
            {
                outgoingDataLogText.text = string.Join("\n", lines, lines.Length - 100, 100);
            }

            // Scroll to bottom.
            if (outgoingDataScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                outgoingDataScrollRect.verticalNormalizedPosition = 0f;
            }

            // Mirror to Unity console.
            Debug.Log($"[OutgoingData] [{dataType}] {dataContent}");
        }

        /// <summary>
        /// Logs ServerRpc call details.
        /// </summary>
        public void LogServerRpc(string rpcName, string parameters = "")
        {
            string content = string.IsNullOrEmpty(parameters) ? rpcName : $"{rpcName}({parameters})";
            LogOutgoingData("ServerRpc", content);
        }

        /// <summary>
        /// Logs input payload.
        /// </summary>
        public void LogInputData(Vector2 moveInput, bool jump, bool attack)
        {
            string content = $"Move({moveInput.x:F2}, {moveInput.y:F2}) Jump:{jump} Attack:{attack}";
            LogOutgoingData("Input", content);
        }

        /// <summary>
        /// Logs transform sync payload.
        /// </summary>
        public void LogPositionSync(Vector3 position, Quaternion rotation)
        {
            string content = $"Pos({position.x:F2}, {position.y:F2}, {position.z:F2}) Rot({rotation.eulerAngles.y:F1}deg)";
            LogOutgoingData("Position", content);
        }

        /// <summary>
        /// Logs custom payload.
        /// </summary>
        public void LogCustomData(string message)
        {
            LogOutgoingData("Custom", message);
        }

        /// <summary>
        /// Logs an error payload in red.
        /// </summary>
        public void LogError(string errorMessage)
        {
            if (outgoingDataLogText == null) return;

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logEntry = $"[{timestamp}] <color=#FF0000>[ERROR]</color> <color=#FFAAAA>{errorMessage}</color>\n";

            outgoingDataLogText.text += logEntry;

            // Keep only latest 100 lines.
            string[] lines = outgoingDataLogText.text.Split('\n');
            if (lines.Length > 100)
            {
                outgoingDataLogText.text = string.Join("\n", lines, lines.Length - 100, 100);
            }

            // Scroll to bottom.
            if (outgoingDataScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                outgoingDataScrollRect.verticalNormalizedPosition = 0f;
            }

            Debug.LogError($"[OutgoingData] [ERROR] {errorMessage}");
        }

        /// <summary>
        /// Logs a warning payload in yellow.
        /// </summary>
        public void LogWarning(string warningMessage)
        {
            if (outgoingDataLogText == null) return;

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logEntry = $"[{timestamp}] <color=#FFFF00>[WARNING]</color> <color=#FFFFAA>{warningMessage}</color>\n";

            outgoingDataLogText.text += logEntry;

            // Keep only latest 100 lines.
            string[] lines = outgoingDataLogText.text.Split('\n');
            if (lines.Length > 100)
            {
                outgoingDataLogText.text = string.Join("\n", lines, lines.Length - 100, 100);
            }

            // Scroll to bottom.
            if (outgoingDataScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                outgoingDataScrollRect.verticalNormalizedPosition = 0f;
            }

            Debug.LogWarning($"[OutgoingData] [WARNING] {warningMessage}");
        }

        /// <summary>
        /// Logs ServerRpc failure detail.
        /// </summary>
        public void LogServerRpcFailed(string rpcName, string reason)
        {
            LogError($"ServerRpc failed: {rpcName} - {reason}");
        }

        /// <summary>
        /// Logs network operation error detail.
        /// </summary>
        public void LogNetworkError(string operation, string errorDetail)
        {
            LogError($"Network error [{operation}]: {errorDetail}");
        }

        /// <summary>
        /// Clears outgoing data logs.
        /// </summary>
        public void ClearOutgoingLog()
        {
            if (outgoingDataLogText != null)
            {
                outgoingDataLogText.text = "";
            }
        }

        #endregion
    }
}

