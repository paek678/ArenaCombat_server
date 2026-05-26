// ARCH TAG: SHARED
// ARCH SCOPE: Lobby lifecycle manager shared across gameplay modes.
// ARCH STATUS: TARGET_3D_PENDING

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

// Unity Lobby Player DevNull Player   
using LobbyPlayer = Unity.Services.Lobbies.Models.Player;

namespace ArenaCombat.Core.Network
{
    /// <summary>
    /// Unity Lobby   
    ///  , ,   
    /// </summary>
    public class LobbyManager : MonoBehaviour
    {
        public static LobbyManager Instance { get; private set; }

        [Header("Lobby Settings")]
        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private float heartbeatInterval = 15f;
        [SerializeField] private float lobbyPollInterval = 0.5f;

        //   
        private Lobby currentLobby;
        private float heartbeatTimer;
        private float pollTimer;
        private bool isHost;
        private bool isGameSessionActive;

        // 
        public event Action<Lobby> OnLobbyCreated;
        public event Action<Lobby> OnLobbyJoined;
        public event Action<Lobby> OnLobbyUpdated;
        public event Action OnLobbyLeft;
        public event Action<string> OnError;

        // 
        public Lobby CurrentLobby => currentLobby;
        public bool IsHost => isHost;
        public bool IsInLobby => currentLobby != null;
        public bool IsGameSessionActive => isGameSessionActive;
        public string PlayerId => AuthenticationService.Instance?.PlayerId;
        public int MaxPlayers
        {
            get => maxPlayers;
            set => maxPlayers = Mathf.Clamp(value, 2, 8);
        }

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

        private async void Start()
        {
            await InitializeServices();
        }

        private void Update()
        {
            HandleHeartbeat();
            HandleLobbyPoll();
        }

        private void OnApplicationQuit()
        {
            _ = LeaveLobbyAsync();
        }

        #region Initialization

        /// <summary>
        /// Unity Services    
        /// ParrelSync   
        /// </summary>
        private async Task InitializeServices()
        {
            try
            {
                var options = new InitializationOptions();

                // ParrelSync      
#if UNITY_EDITOR
                try
                {
                    var clonesManagerType = System.Type.GetType("ParrelSync.ClonesManager, ParrelSync");
                    if (clonesManagerType != null)
                    {
                        var isCloneMethod = clonesManagerType.GetMethod("IsClone", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        var getArgumentMethod = clonesManagerType.GetMethod("GetArgument", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                        if (isCloneMethod != null && (bool)isCloneMethod.Invoke(null, null))
                        {
                            string customArgument = getArgumentMethod?.Invoke(null, null) as string ?? "";
                            string profile = string.IsNullOrEmpty(customArgument) ? "clone" : customArgument;
                            options.SetProfile(profile);
                            Debug.Log($"[LobbyManager] ParrelSync clone detected - Profile: {profile}");
                        }
                    }
                }
                catch (System.Exception)
                {
                    // ParrelSync  
                }
#endif

                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync(options);
                    Debug.Log("[LobbyManager] Unity Services initialized");
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    Debug.Log($"[LobbyManager] Signed in anonymously - PlayerId: {PlayerId}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyManager] Service initialization failed: {e.Message}");
                OnError?.Invoke($"Service initialization failed: {e.Message}");
            }
        }

        #endregion

        #region Lobby Creation

        /// <summary>
        ///   
        /// </summary>
        public async Task<Lobby> CreateLobbyAsync(string lobbyName, bool isPrivate = false)
        {
            try
            {
                //    
                if (currentLobby != null)
                {
                    await LeaveLobbyAsync();
                }

                var options = new CreateLobbyOptions
                {
                    IsPrivate = isPrivate,
                    Player = CreatePlayerData(),
                    Data = new Dictionary<string, DataObject>
                    {
                        { "GameMode", new DataObject(DataObject.VisibilityOptions.Public, "Brawl") },
                        { "HostReady", new DataObject(DataObject.VisibilityOptions.Public, "false") },
                        { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, "") },
                        { "GameStarted", new DataObject(DataObject.VisibilityOptions.Member, "false") }
                    }
                };

                currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
                isHost = true;
                SetGameSessionActive(false);

                Debug.Log($"[LobbyManager] Lobby created - Name: {lobbyName}, Code: {currentLobby.LobbyCode}, MaxPlayers: {maxPlayers}");
                OnLobbyCreated?.Invoke(currentLobby);

                return currentLobby;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Create lobby failed: {e.Message}");
                OnError?.Invoke($"Create lobby failed: {e.Message}");
                return null;
            }
        }

        #endregion

        #region Lobby Join

        /// <summary>
        ///   
        /// </summary>
        public async Task<Lobby> JoinLobbyByCodeAsync(string lobbyCode)
        {
            try
            {
                if (currentLobby != null)
                {
                    await LeaveLobbyAsync();
                }

                var options = new JoinLobbyByCodeOptions
                {
                    Player = CreatePlayerData()
                };

                currentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, options);
                isHost = false;
                SetGameSessionActive(false);

                Debug.Log($"[LobbyManager] Joined lobby by code - Code: {lobbyCode}");
                OnLobbyJoined?.Invoke(currentLobby);

                return currentLobby;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Join by code failed: {e.Message}");
                OnError?.Invoke($"Join by code failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        ///  ID 
        /// </summary>
        public async Task<Lobby> JoinLobbyByIdAsync(string lobbyId)
        {
            try
            {
                if (currentLobby != null)
                {
                    await LeaveLobbyAsync();
                }

                var options = new JoinLobbyByIdOptions
                {
                    Player = CreatePlayerData()
                };

                currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, options);
                isHost = false;
                SetGameSessionActive(false);

                Debug.Log($"[LobbyManager] Joined lobby by ID - ID: {lobbyId}");
                OnLobbyJoined?.Invoke(currentLobby);

                return currentLobby;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Join by ID failed: {e.Message}");
                OnError?.Invoke($"Join by ID failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        ///   (   )
        /// </summary>
        public async Task<Lobby> QuickJoinAsync()
        {
            try
            {
                if (currentLobby != null)
                {
                    await LeaveLobbyAsync();
                }

                var options = new QuickJoinLobbyOptions
                {
                    Player = CreatePlayerData()
                };

                currentLobby = await LobbyService.Instance.QuickJoinLobbyAsync(options);
                isHost = false;
                SetGameSessionActive(false);

                Debug.Log($"[LobbyManager] Quick join succeeded - LobbyId: {currentLobby.Id}");
                OnLobbyJoined?.Invoke(currentLobby);

                return currentLobby;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Quick join failed: {e.Message}");
                OnError?.Invoke($"Quick join failed: {e.Message}");
                return null;
            }
        }

        #endregion

        #region Lobby Query

        /// <summary>
        ///    
        /// </summary>
        public async Task<List<Lobby>> QueryLobbiesAsync()
        {
            try
            {
                var options = new QueryLobbiesOptions
                {
                    Count = 20,
                    Filters = new List<QueryFilter>
                    {
                        new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                    },
                    Order = new List<QueryOrder>
                    {
                        new QueryOrder(false, QueryOrder.FieldOptions.Created)
                    }
                };

                var response = await LobbyService.Instance.QueryLobbiesAsync(options);
                Debug.Log($"[LobbyManager] Lobby query completed - Found: {response.Results.Count}");

                return response.Results;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Query lobbies failed: {e.Message}");
                OnError?.Invoke($"Query lobbies failed: {e.Message}");
                return new List<Lobby>();
            }
        }

        #endregion

        #region Lobby Leave

        /// <summary>
        ///   
        /// </summary>
        public async Task LeaveLobbyAsync()
        {
            if (currentLobby == null) return;

            try
            {
                if (isHost)
                {
                    //   
                    await LobbyService.Instance.DeleteLobbyAsync(currentLobby.Id);
                    Debug.Log("[LobbyManager] Lobby deleted");
                }
                else
                {
                    //   
                    await LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, PlayerId);
                    Debug.Log("[LobbyManager] Left lobby");
                }
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Leave lobby failed: {e.Message}");
            }
            finally
            {
                currentLobby = null;
                isHost = false;
                SetGameSessionActive(false);
                OnLobbyLeft?.Invoke();
            }
        }

        #endregion

        #region Player Data

        /// <summary>
        ///   
        /// </summary>
        private LobbyPlayer CreatePlayerData()
        {
            return new LobbyPlayer
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, $"Player_{PlayerId?.Substring(0, 6) ?? "Unknown"}") },
                    { "IsReady", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "false") },
                    { "LastAction", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "") }
                }
            };
        }

        /// <summary>
        ///   (   /)
        /// </summary>
        public async Task SendActionAsync(string actionMessage)
        {
            if (currentLobby == null) return;

            try
            {
                //    
                string actionWithTime = $"{DateTime.Now.Ticks}|{actionMessage}";

                var options = new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "LastAction", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, actionWithTime) }
                    }
                };

                currentLobby = await LobbyService.Instance.UpdatePlayerAsync(currentLobby.Id, PlayerId, options);
                Debug.Log($"[LobbyManager] Action sent: {actionMessage}");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Send action failed: {e.Message}");
                OnError?.Invoke($"Send action failed: {e.Message}");
            }
        }

        /// <summary>
        ///  Ready  
        /// </summary>
        public async Task SetPlayerReadyAsync(bool isReady)
        {
            if (currentLobby == null) return;

            try
            {
                var options = new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "IsReady", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, isReady.ToString().ToLower()) }
                    }
                };

                currentLobby = await LobbyService.Instance.UpdatePlayerAsync(currentLobby.Id, PlayerId, options);
                Debug.Log($"[LobbyManager] Ready state updated: {isReady}");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Ready update failed: {e.Message}");
                OnError?.Invoke($"Ready update failed: {e.Message}");
            }
        }

        /// <summary>
        ///   
        /// </summary>
        public async Task SetPlayerNameAsync(string playerName)
        {
            if (currentLobby == null) return;

            try
            {
                var options = new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerName) }
                    }
                };

                currentLobby = await LobbyService.Instance.UpdatePlayerAsync(currentLobby.Id, PlayerId, options);
                Debug.Log($"[LobbyManager] Player name updated: {playerName}");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Set player name failed: {e.Message}");
                OnError?.Invoke($"Set player name failed: {e.Message}");
            }
        }

        #endregion

        #region Relay Integration

        /// <summary>
        /// Relay Join Code Lobby Data  ()
        /// </summary>
        public async Task SetRelayJoinCodeAsync(string relayJoinCode)
        {
            if (currentLobby == null || !isHost) return;

            try
            {
                var options = new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
                    }
                };

                currentLobby = await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, options);
                Debug.Log($"[LobbyManager] Relay Join Code : {relayJoinCode}");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Save Relay join code failed: {e.Message}");
                OnError?.Invoke($"Save Relay join code failed: {e.Message}");
            }
        }

        /// <summary>
        /// Lobby Data Relay Join Code 
        /// </summary>
        public string GetRelayJoinCode()
        {
            if (currentLobby?.Data == null) return null;

            if (currentLobby.Data.TryGetValue("RelayJoinCode", out var data))
            {
                return string.IsNullOrEmpty(data.Value) ? null : data.Value;
            }
            return null;
        }

        /// <summary>
        ///    Lobby Data  ()
        /// </summary>
        public async Task SetGameStartedAsync(bool started)
        {
            if (currentLobby == null || !isHost) return;

            try
            {
                var options = new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        { "GameStarted", new DataObject(DataObject.VisibilityOptions.Member, started.ToString().ToLower()) }
                    }
                };

                currentLobby = await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, options);
                SetGameSessionActive(started);
                Debug.Log($"[LobbyManager] Game started flag updated: {started}");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Set game started failed: {e.Message}");
                OnError?.Invoke($"Set game started failed: {e.Message}");
            }
        }

        /// <summary>
        ///   
        /// </summary>
        public bool IsGameStarted()
        {
            if (currentLobby?.Data == null) return false;

            if (currentLobby.Data.TryGetValue("GameStarted", out var data))
            {
                return data.Value == "true";
            }
            return false;
        }

        /// <summary>
        /// Toggle lobby polling/heartbeat while game scene is active.
        /// </summary>
        public void SetGameSessionActive(bool active)
        {
            isGameSessionActive = active;
            heartbeatTimer = heartbeatInterval;
            pollTimer = lobbyPollInterval;
            Debug.Log($"[LobbyManager] Game session active: {isGameSessionActive}");
        }

        #endregion

        #region Heartbeat & Polling

        /// <summary>
        ///  Heartbeat  ( )
        /// </summary>
        private void HandleHeartbeat()
        {
            if (isGameSessionActive) return;
            if (!isHost || currentLobby == null) return;

            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer <= 0f)
            {
                heartbeatTimer = heartbeatInterval;
                SendHeartbeatAsync();
            }
        }

        private async void SendHeartbeatAsync()
        {
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
                Debug.Log("[LobbyManager] Heartbeat sent");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Heartbeat : {e.Message}");
            }
        }

        /// <summary>
        ///    ( )
        /// </summary>
        private void HandleLobbyPoll()
        {
            if (isGameSessionActive) return;
            if (currentLobby == null) return;

            pollTimer -= Time.deltaTime;
            if (pollTimer <= 0f)
            {
                pollTimer = lobbyPollInterval;
                PollLobbyAsync();
            }
        }

        private async void PollLobbyAsync()
        {
            try
            {
                var lobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
                currentLobby = lobby;
                OnLobbyUpdated?.Invoke(currentLobby);
            }
            catch (LobbyServiceException e)
            {
                //    
                if (e.Reason == LobbyExceptionReason.LobbyNotFound)
                {
                    Debug.Log("[LobbyManager] Lobby no longer exists");
                    currentLobby = null;
                    isHost = false;
                    OnLobbyLeft?.Invoke();
                }
                else
                {
                    Debug.LogError($"[LobbyManager] Lobby polling failed: {e.Message}");
                }
            }
        }

        #endregion

        #region Debug

        /// <summary>
        ///     ()
        /// </summary>
        public void PrintLobbyInfo()
        {
            if (currentLobby == null)
            {
                Debug.Log("[LobbyManager] No active lobby");
                return;
            }

            Debug.Log("==========   ==========");
            Debug.Log($"Name: {currentLobby.Name}");
            Debug.Log($"ID: {currentLobby.Id}");
            Debug.Log($"Code: {currentLobby.LobbyCode}");
            Debug.Log($"Players: {currentLobby.Players.Count}/{currentLobby.MaxPlayers}");
            Debug.Log($"IsPrivate: {currentLobby.IsPrivate}");
            Debug.Log($"HostId: {currentLobby.HostId}");
            Debug.Log("----------   ----------");

            foreach (var player in currentLobby.Players)
            {
                var playerName = player.Data != null && player.Data.ContainsKey("PlayerName")
                    ? player.Data["PlayerName"].Value
                    : "Unknown";
                var isReady = player.Data != null && player.Data.ContainsKey("IsReady")
                    ? player.Data["IsReady"].Value
                    : "false";
                var isHostPlayer = player.Id == currentLobby.HostId ? " [HOST]" : "";

                Debug.Log($"  - {playerName} (Ready: {isReady}){isHostPlayer}");
            }
            Debug.Log("================================");
        }

        #endregion
    }
}

