// ARCH TAG: SHARED
// ARCH SCOPE: Match state authority shared across legacy 2D and target 3D.
// ARCH STATUS: TARGET_3D_PENDING

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using ArenaCombat.Core.AI;
using ArenaCombat.Core.Skill;
using ArenaCombat.Core.UI;

namespace ArenaCombat.Core.Network
{
    public class GameStateManager : NetworkBehaviour
    {
        private const int MaxDraftPlayers = 2;
        private const int MaxDraftRounds = 4;
        private const int OfferChoices = 3;
        private const int InvalidCard = -1;

        private enum InProgressTransitionReason : byte
        {
            None = 0,
            CountdownExpired = 1,
            ForceStartMatch = 2,
            ResumeMatch = 3,
            DirectTransition = 4,
            AutoStartImmediate = 5
        }

        private enum MatchAutoStartMode : byte
        {
            Manual = 0,
            CountdownWhenReady = 1,
            ImmediateWhenReady = 2
        }

        public static GameStateManager Instance { get; private set; }

        [Header("=== Match Settings ===")]
        [SerializeField] private float countdownDuration = 3f;
        [SerializeField] private float roundEndDuration = 5f;
        [SerializeField] private int minPlayersToStart = 2;
        [SerializeField] private MatchAutoStartMode autoStartMode = MatchAutoStartMode.ImmediateWhenReady;
        [SerializeField] private bool autoStartAllowHostOnly = false;

        [Header("=== Global Card Draft (Network Sync) ===")]
        [SerializeField] private bool enableGlobalCardDraft = true;
        [SerializeField] private float cardDraftInterval = 175f;
        [SerializeField] private float cardDraftDuration = 8f;
        [SerializeField] private bool emitCardDraftDebugLogs = true;

        [Header("=== Runtime Debug: InProgress Transition (Read-Only) ===")]
        [SerializeField] private MatchState debugCurrentMatchState = MatchState.None;
        [SerializeField] private InProgressTransitionReason debugLastInProgressReason = InProgressTransitionReason.None;
        [SerializeField] private MatchState debugLastInProgressFrom = MatchState.None;
        [SerializeField] private ulong debugLastInProgressRequesterClientId = ulong.MaxValue;
        [SerializeField] private double debugLastInProgressServerTime = -1d;
        [SerializeField] private int debugInProgressTransitionCount = 0;

        [Header("=== Runtime Debug: Timers (Read-Only) ===")]
        [SerializeField] private bool debugIsMatchTimerRunning = false;
        [SerializeField] private float debugMatchTimerSeconds = 0f;
        [SerializeField] private bool debugIsCardDraftActive = false;
        [SerializeField] private int debugCardDraftRound = 0;
        [SerializeField] private float debugCardDraftTimerSeconds = 0f;
        [SerializeField] private float debugServerNextCardDraftIntervalSeconds = -1f;
        [SerializeField] private float debugServerActiveCardDraftRemainingSeconds = -1f;
        [SerializeField] private double debugServerNowSeconds = -1d;

        private readonly NetworkVariable<MatchState> networkMatchState = new(MatchState.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<GameMode> networkGameMode = new(GameMode.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> networkTimer = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> networkRoundNumber = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> networkCardDraftActive = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> networkCardDraftRound = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> networkCardDraftTimer = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<MatchEndReason> networkMatchEndReason = new(MatchEndReason.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public MatchEndReason CurrentMatchEndReason => networkMatchEndReason.Value;
        public NetworkVariable<MatchEndReason> NetworkMatchEndReason => networkMatchEndReason;

        public event Action<MatchState, MatchState> OnMatchStateChanged;
        public event Action<GameMode> OnGameModeChanged;
        public event Action<float> OnTimerUpdated;
        public event Action<int> OnRoundChanged;

        public event Action<int, float> OnCardDraftStarted;
        public event Action<int> OnCardDraftEnded;
        public event Action<int> OnCardDraftEndedServer;
        public event Action<float> OnCardDraftTimerUpdated;
        public event Action<ulong, int, int> OnCardSelectionResolved;
        public event Action<int, int, string> OnCardSelectionRejected;

        public MatchState CurrentMatchState => networkMatchState.Value;
        public GameMode CurrentGameMode => networkGameMode.Value;
        public float CurrentTimer => networkTimer.Value;
        public int CurrentRound => networkRoundNumber.Value;

        public bool IsGlobalCardDraftActive => networkCardDraftActive.Value;
        public int CurrentCardDraftRound => networkCardDraftRound.Value;
        public float CurrentCardDraftTimer => networkCardDraftTimer.Value;
        public int MaxCardDraftRounds => MaxDraftRounds;

        private bool isTimerRunning;
        private float nextCardDraftIntervalTimer;
        private float activeCardDraftRemaining;

        private int serverCardCatalogSize;
        private ulong serverHostClientId = ulong.MaxValue;
        private ulong serverGuestClientId = ulong.MaxValue;
        private readonly int[] serverHostOffer = { InvalidCard, InvalidCard, InvalidCard };
        private readonly int[] serverGuestOffer = { InvalidCard, InvalidCard, InvalidCard };
        private readonly HashSet<ulong> serverSelectedThisDraft = new();

        private ulong cachedDraftHostClientId = ulong.MaxValue;
        private ulong cachedDraftGuestClientId = ulong.MaxValue;
        private int cachedDraftRound;
        private readonly int[] cachedHostOffer = { InvalidCard, InvalidCard, InvalidCard };
        private readonly int[] cachedGuestOffer = { InvalidCard, InvalidCard, InvalidCard };
        private bool hasCachedDraftOffer;

        private readonly Dictionary<ulong, List<int>> syncedSelectedCardsByPlayer = new();
        private InProgressTransitionReason pendingInProgressReason = InProgressTransitionReason.None;
        private ulong pendingInProgressRequesterClientId = ulong.MaxValue;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            networkMatchState.OnValueChanged += HandleMatchStateChanged;
            networkGameMode.OnValueChanged += HandleGameModeChanged;
            networkTimer.OnValueChanged += HandleTimerChanged;
            networkRoundNumber.OnValueChanged += HandleRoundChanged;
            networkCardDraftTimer.OnValueChanged += HandleCardDraftTimerChanged;

            ResetLocalDraftOfferCache();

            if (IsServer)
            {
                networkMatchState.Value = MatchState.WaitingForPlayers;
                debugCurrentMatchState = MatchState.WaitingForPlayers;
                ResetCardDraftStateServer(resetRound: true, clearHistory: true);
                Debug.Log("[GameStateManager] Server initialized, waiting for players");
            }

            UpdateRuntimeTimerDebugFields();
            EnsureMatchEndUI();
        }

        private void EnsureMatchEndUI()
        {
            if (MatchEndUI.Instance == null)
            {
                var go = new GameObject("MatchEndUI");
                go.AddComponent<MatchEndUI>();
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            networkMatchState.OnValueChanged -= HandleMatchStateChanged;
            networkGameMode.OnValueChanged -= HandleGameModeChanged;
            networkTimer.OnValueChanged -= HandleTimerChanged;
            networkRoundNumber.OnValueChanged -= HandleRoundChanged;
            networkCardDraftTimer.OnValueChanged -= HandleCardDraftTimerChanged;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsServer)
            {
                TryAutoStartMatchServer();

                float dt = Time.unscaledDeltaTime;

                if (isTimerRunning)
                {
                    networkTimer.Value -= dt;
                    if (networkTimer.Value <= 0f)
                    {
                        OnTimerExpired();
                    }
                }

                UpdateGlobalCardDraftServer(dt);
            }

            UpdateRuntimeTimerDebugFields();
        }

        private void TryAutoStartMatchServer()
        {
            if (!IsServer || autoStartMode == MatchAutoStartMode.Manual)
            {
                return;
            }

            if (networkMatchState.Value != MatchState.WaitingForPlayers)
            {
                return;
            }

            // Block auto-start until all players have finished loading the scene.
            if (SceneLoadSyncManager.Instance != null && !SceneLoadSyncManager.Instance.AllPlayersLoaded)
            {
                return;
            }

            int connectedPlayers = GetConnectedPlayerCount();
            int requiredPlayers = autoStartAllowHostOnly ? 1 : Mathf.Max(1, minPlayersToStart);
            if (connectedPlayers < requiredPlayers)
            {
                return;
            }

            switch (autoStartMode)
            {
                case MatchAutoStartMode.CountdownWhenReady:
                    TransitionToState(MatchState.Countdown);
                    break;
                case MatchAutoStartMode.ImmediateWhenReady:
                {
                    ulong requester = NetworkManager != null ? NetworkManager.ServerClientId : ulong.MaxValue;
                    TransitionToInProgressWithDebug(InProgressTransitionReason.AutoStartImmediate, requester);
                    break;
                }
            }
        }

        private void HandleMatchStateChanged(MatchState oldState, MatchState newState)
        {
            debugCurrentMatchState = newState;
            Debug.Log($"[GameStateManager] Match state changed: {oldState} -> {newState}");
            OnMatchStateChanged?.Invoke(oldState, newState);
        }

        private void HandleGameModeChanged(GameMode oldMode, GameMode newMode)
        {
            Debug.Log($"[GameStateManager] Game mode changed: {oldMode} -> {newMode}");
            OnGameModeChanged?.Invoke(newMode);
        }

        private void HandleTimerChanged(float oldTime, float newTime)
        {
            debugMatchTimerSeconds = newTime;
            OnTimerUpdated?.Invoke(newTime);
        }

        private void HandleRoundChanged(int oldRound, int newRound)
        {
            Debug.Log($"[GameStateManager] Round changed: {oldRound} -> {newRound}");
            OnRoundChanged?.Invoke(newRound);
        }

        private void HandleCardDraftTimerChanged(float oldTimer, float newTimer)
        {
            debugCardDraftTimerSeconds = newTimer;
            OnCardDraftTimerUpdated?.Invoke(newTimer);
        }
        public bool TransitionToState(MatchState newState)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[GameStateManager] Only server can change match state");
                return false;
            }

            MatchState currentState = networkMatchState.Value;
            if (!IsValidTransition(currentState, newState))
            {
                Debug.LogWarning($"[GameStateManager] Invalid transition: {currentState} -> {newState}");
                ClearPendingInProgressContext();
                return false;
            }

            if (newState == MatchState.InProgress)
            {
                RecordInProgressTransitionDebug(currentState);
            }
            else
            {
                ClearPendingInProgressContext();
            }

            networkMatchState.Value = newState;
            OnStateEntered(newState);
            return true;
        }

        private bool IsValidTransition(MatchState from, MatchState to)
        {
            if (to == MatchState.Disconnected) return true;

            return (from, to) switch
            {
                (MatchState.None, MatchState.WaitingForPlayers) => true,
                (MatchState.WaitingForPlayers, MatchState.Countdown) => true,
                (MatchState.WaitingForPlayers, MatchState.InProgress) => true,
                (MatchState.Countdown, MatchState.InProgress) => true,
                (MatchState.Countdown, MatchState.WaitingForPlayers) => true,
                (MatchState.InProgress, MatchState.Paused) => true,
                (MatchState.InProgress, MatchState.RoundEnd) => true,
                (MatchState.InProgress, MatchState.MatchEnd) => true,
                (MatchState.Paused, MatchState.InProgress) => true,
                (MatchState.Paused, MatchState.MatchEnd) => true,
                (MatchState.RoundEnd, MatchState.Countdown) => true,
                (MatchState.RoundEnd, MatchState.MatchEnd) => true,
                (MatchState.MatchEnd, MatchState.WaitingForPlayers) => true,
                _ => false
            };
        }

        private void OnStateEntered(MatchState state)
        {
            switch (state)
            {
                case MatchState.WaitingForPlayers:
                    StopTimer();
                    ResetCardDraftStateServer(resetRound: true, clearHistory: true);
                    break;
                case MatchState.Countdown:
                    StartTimer(countdownDuration);
                    if (networkCardDraftActive.Value)
                    {
                        EndGlobalCardDraftPhaseServer();
                    }
                    break;
                case MatchState.InProgress:
                    StopTimer();
                    PrepareNextCardDraftServer();
                    MatchStartedRpc();
                    break;
                case MatchState.RoundEnd:
                    if (networkCardDraftActive.Value)
                    {
                        EndGlobalCardDraftPhaseServer();
                    }
                    StartTimer(roundEndDuration);
                    networkRoundNumber.Value++;
                    break;
                case MatchState.MatchEnd:
                    StopTimer();
                    if (networkCardDraftActive.Value)
                    {
                        EndGlobalCardDraftPhaseServer();
                    }
                    ResetCardDraftStateServer(resetRound: false, clearHistory: false);
                    MatchEndedRpc();
                    break;
            }
        }

        private void OnTimerExpired()
        {
            StopTimer();

            switch (networkMatchState.Value)
            {
                case MatchState.Countdown:
                    TransitionToInProgressWithDebug(InProgressTransitionReason.CountdownExpired);
                    break;
                case MatchState.RoundEnd:
                    TransitionToState(MatchState.Countdown);
                    break;
            }
        }

        private void StartTimer(float duration)
        {
            networkTimer.Value = duration;
            isTimerRunning = true;
            UpdateRuntimeTimerDebugFields();
        }

        private void StopTimer()
        {
            isTimerRunning = false;
            UpdateRuntimeTimerDebugFields();
        }

        public void StartMatchCountdown()
        {
            if (!IsServer) return;

            int connectedPlayers = GetConnectedPlayerCount();
            if (connectedPlayers < minPlayersToStart)
            {
                Debug.LogWarning($"[GameStateManager] Cannot start countdown. Connected players: {connectedPlayers}/{minPlayersToStart}");
                return;
            }

            if (networkMatchState.Value == MatchState.WaitingForPlayers)
            {
                TransitionToState(MatchState.Countdown);
            }
        }

        public void ForceStartMatch()
        {
            if (!IsServer) return;

            if (networkMatchState.Value == MatchState.WaitingForPlayers || networkMatchState.Value == MatchState.Countdown)
            {
                ulong requester = NetworkManager != null ? NetworkManager.ServerClientId : ulong.MaxValue;
                TransitionToInProgressWithDebug(InProgressTransitionReason.ForceStartMatch, requester);
            }
        }

        public void PauseMatch()
        {
            if (!IsServer) return;

            if (networkMatchState.Value == MatchState.InProgress)
            {
                TransitionToState(MatchState.Paused);
            }
        }

        public void ResumeMatch()
        {
            if (!IsServer) return;

            if (networkMatchState.Value == MatchState.Paused)
            {
                ulong requester = NetworkManager != null ? NetworkManager.ServerClientId : ulong.MaxValue;
                TransitionToInProgressWithDebug(InProgressTransitionReason.ResumeMatch, requester);
            }
        }

        public void EndRound()
        {
            if (!IsServer) return;

            if (networkMatchState.Value == MatchState.InProgress)
            {
                TransitionToState(MatchState.RoundEnd);
            }
        }

        public void EndMatch(MatchEndReason reason = MatchEndReason.None)
        {
            if (!IsServer) return;
            if (networkMatchState.Value == MatchState.MatchEnd) return;
            if (!IsValidTransition(networkMatchState.Value, MatchState.MatchEnd)) return;

            networkMatchEndReason.Value = reason;
            TransitionToState(MatchState.MatchEnd);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRestartRpc(RpcParams rpcParams = default)
        {
            if (!IsServer) return;
            if (networkMatchState.Value != MatchState.MatchEnd) return;
            RestartMatch();
        }

        private void RestartMatch()
        {
            if (BossManager.Instance != null)
                BossManager.Instance.DespawnBoss();

            if (CombatManager3D.Instance != null)
            {
                var snapshot = CombatManager3D.Instance.GetAllPlayersSnapshot();
                foreach (var kvp in snapshot)
                {
                    var player = kvp.Value;
                    if (player == null) continue;

                    var skillMgr = player.GetComponent<SkillManager>();
                    if (skillMgr != null)
                    {
                        skillMgr.ClearAll();
                        skillMgr.SetAutoCast(true);
                    }

                    var skillExec = player.GetComponent<SkillExecutor>();
                    if (skillExec != null)
                        skillExec.ResetAll();

                    Vector3 spawnPos = PlayerSpawnManager.Instance != null
                        ? PlayerSpawnManager.Instance.GetRespawnPosition(kvp.Key)
                        : player.transform.position;
                    player.Respawn(spawnPos);

                    // BAL-1: Respawn은 슬롯을 건드리지 않음. ClearAll 후 초기 스킬 재적용 필요.
                    player.ApplyInitialLoadoutServer();
                    player.NotifySkillResetToOwner();
                }
            }

            networkMatchEndReason.Value = MatchEndReason.None;
            ResetCardDraftStateServer(resetRound: true, clearHistory: true);
            networkRoundNumber.Value = 0;

            if (PlayerBiasTracker.Instance != null)
                PlayerBiasTracker.Instance.ResetAllCounters();

            TransitionToState(MatchState.WaitingForPlayers);
            StartMatchCountdown();
        }

        public void ResetToWaiting()
        {
            if (!IsServer) return;

            networkMatchState.Value = MatchState.WaitingForPlayers;
            networkMatchEndReason.Value = MatchEndReason.None;
            networkRoundNumber.Value = 0;
            StopTimer();
            ResetCardDraftStateServer(resetRound: true, clearHistory: true);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MatchStartedRpc()
        {
            Debug.Log("[GameStateManager] Match started!");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MatchEndedRpc()
        {
            Debug.Log("[GameStateManager] Match ended!");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestStartMatchRpc(RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            if (clientId != NetworkManager.ServerClientId)
            {
                Debug.Log($"[GameStateManager] Client {clientId} tried to start match but is not host");
                return;
            }

            StartMatchCountdown();
        }

        private bool TransitionToInProgressWithDebug(InProgressTransitionReason reason, ulong requesterClientId = ulong.MaxValue)
        {
            pendingInProgressReason = reason;
            pendingInProgressRequesterClientId = requesterClientId;
            bool success = TransitionToState(MatchState.InProgress);
            if (!success)
            {
                ClearPendingInProgressContext();
            }

            return success;
        }

        private void RecordInProgressTransitionDebug(MatchState fromState)
        {
            InProgressTransitionReason reason = pendingInProgressReason == InProgressTransitionReason.None
                ? InProgressTransitionReason.DirectTransition
                : pendingInProgressReason;

            debugLastInProgressReason = reason;
            debugLastInProgressFrom = fromState;
            debugLastInProgressRequesterClientId = pendingInProgressRequesterClientId;
            debugLastInProgressServerTime = NetworkManager != null ? NetworkManager.ServerTime.Time : -1d;
            debugInProgressTransitionCount++;

            ClearPendingInProgressContext();
        }

        private void ClearPendingInProgressContext()
        {
            pendingInProgressReason = InProgressTransitionReason.None;
            pendingInProgressRequesterClientId = ulong.MaxValue;
        }

        private void UpdateRuntimeTimerDebugFields()
        {
            debugCurrentMatchState = networkMatchState.Value;
            debugIsMatchTimerRunning = isTimerRunning;
            debugMatchTimerSeconds = networkTimer.Value;

            debugIsCardDraftActive = networkCardDraftActive.Value;
            debugCardDraftRound = networkCardDraftRound.Value;
            debugCardDraftTimerSeconds = networkCardDraftTimer.Value;

            if (IsServer)
            {
                debugServerNextCardDraftIntervalSeconds = nextCardDraftIntervalTimer;
                debugServerActiveCardDraftRemainingSeconds = activeCardDraftRemaining;
                debugServerNowSeconds = NetworkManager != null ? NetworkManager.ServerTime.Time : -1d;
            }
            else
            {
                debugServerNextCardDraftIntervalSeconds = -1f;
                debugServerActiveCardDraftRemainingSeconds = -1f;
                debugServerNowSeconds = -1d;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestPauseRpc(RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            if (clientId != NetworkManager.ServerClientId)
            {
                return;
            }

            PauseMatch();
        }

        public bool IsMatchPlayable()
        {
            return networkMatchState.Value == MatchState.InProgress && !networkCardDraftActive.Value;
        }

        public bool CanPlayersJoin()
        {
            return networkMatchState.Value == MatchState.WaitingForPlayers ||
                   networkMatchState.Value == MatchState.Countdown;
        }

        public string GetTimerString()
        {
            int totalSeconds = Mathf.CeilToInt(networkTimer.Value);
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return $"{minutes:00}:{seconds:00}";
        }

        private int GetConnectedPlayerCount()
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
                return 0;

            int count = 0;
            foreach (var kv in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
            {
                if (kv.Value != null && kv.Value.IsPlayerObject)
                    count++;
            }
            return count;
        }
        public void RegisterCardCatalogSize(int cardCount)
        {
            if (!IsServer)
            {
                return;
            }

            serverCardCatalogSize = Mathf.Max(0, cardCount);
            if (emitCardDraftDebugLogs)
            {
                Debug.Log($"[GameStateManager] Card catalog size registered: {serverCardCatalogSize}");
            }
        }

        public bool TryGetCurrentDraftParticipants(out ulong hostClientId, out ulong guestClientId)
        {
            hostClientId = cachedDraftHostClientId;
            guestClientId = cachedDraftGuestClientId;

            if (hostClientId == ulong.MaxValue && NetworkManager != null)
            {
                hostClientId = NetworkManager.ServerClientId;
            }

            return hostClientId != ulong.MaxValue;
        }

        public bool TryGetCardDraftOfferForPlayer(ulong playerId, out int[] offerCardIndices)
        {
            offerCardIndices = null;

            if (!hasCachedDraftOffer)
            {
                return false;
            }

            if (playerId == cachedDraftHostClientId)
            {
                offerCardIndices = CopyOffer(cachedHostOffer);
                return true;
            }

            if (playerId == cachedDraftGuestClientId)
            {
                offerCardIndices = CopyOffer(cachedGuestOffer);
                return true;
            }

            return false;
        }

        public bool TryGetLocalCardDraftOffer(out int[] offerCardIndices)
        {
            offerCardIndices = null;
            if (NetworkManager.Singleton == null)
            {
                return false;
            }

            return TryGetCardDraftOfferForPlayer(NetworkManager.Singleton.LocalClientId, out offerCardIndices);
        }

        public bool TryGetPlayerCardHistory(ulong playerId, out IReadOnlyList<int> cardHistory)
        {
            if (syncedSelectedCardsByPlayer.TryGetValue(playerId, out List<int> history))
            {
                cardHistory = history.AsReadOnly();
                return true;
            }

            cardHistory = null;
            return false;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CardDraftOfferRpc(
            int round,
            ulong hostClientId,
            ulong guestClientId,
            int hostOffer0,
            int hostOffer1,
            int hostOffer2,
            int guestOffer0,
            int guestOffer1,
            int guestOffer2)
        {
            cachedDraftRound = round;
            cachedDraftHostClientId = hostClientId;
            cachedDraftGuestClientId = guestClientId;

            cachedHostOffer[0] = hostOffer0;
            cachedHostOffer[1] = hostOffer1;
            cachedHostOffer[2] = hostOffer2;

            cachedGuestOffer[0] = guestOffer0;
            cachedGuestOffer[1] = guestOffer1;
            cachedGuestOffer[2] = guestOffer2;

            hasCachedDraftOffer = true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CardDraftStartedRpc(int round, float duration)
        {
            OnCardDraftStarted?.Invoke(round, duration);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CardDraftEndedRpc(int round)
        {
            OnCardDraftEnded?.Invoke(round);
            ClearLocalDraftOffersOnly();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CardSelectionResolvedRpc(ulong playerId, int slotIndex, int cardIndex)
        {
            SetPlayerCardHistorySlot(playerId, slotIndex, cardIndex);
            OnCardSelectionResolved?.Invoke(playerId, slotIndex, cardIndex);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void CardSelectionRejectedRpc(int round, int requestedCardIndex, string reason, RpcParams rpcParams = default)
        {
            OnCardSelectionRejected?.Invoke(round, requestedCardIndex, reason);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitCardSelectionRpc(int round, int cardIndex, RpcParams rpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (!TryValidateCardSelectionRequestServer(senderClientId, round, cardIndex, out string rejectReason))
            {
                SendCardSelectionRejectedToClient(senderClientId, round, cardIndex, rejectReason);
                return;
            }

            if (!CommitCardSelectionServer(senderClientId, cardIndex))
            {
                SendCardSelectionRejectedToClient(senderClientId, round, cardIndex, "CommitFailed");
            }
        }

        public void SubmitLocalCardSelection(int round, int cardIndex)
        {
            if (!IsClient)
            {
                return;
            }

            SubmitCardSelectionRpc(round, cardIndex);
        }

        private void UpdateGlobalCardDraftServer(float dt)
        {
            if (!enableGlobalCardDraft)
            {
                return;
            }

            if (networkMatchState.Value != MatchState.InProgress)
            {
                if (networkCardDraftActive.Value)
                {
                    EndGlobalCardDraftPhaseServer();
                }

                networkCardDraftTimer.Value = 0f;
                return;
            }

            if (!networkCardDraftActive.Value && networkCardDraftRound.Value >= MaxDraftRounds)
            {
                networkCardDraftTimer.Value = 0f;
                return;
            }

            if (networkCardDraftActive.Value)
            {
                activeCardDraftRemaining = Mathf.Max(0f, activeCardDraftRemaining - dt);
                networkCardDraftTimer.Value = activeCardDraftRemaining;
                if (activeCardDraftRemaining <= 0f)
                {
                    EndGlobalCardDraftPhaseServer();
                }

                return;
            }

            nextCardDraftIntervalTimer = Mathf.Max(0f, nextCardDraftIntervalTimer - dt);
            networkCardDraftTimer.Value = nextCardDraftIntervalTimer;
            if (nextCardDraftIntervalTimer <= 0f)
            {
                StartGlobalCardDraftPhaseServer();
            }
        }

        private void PrepareNextCardDraftServer()
        {
            if (!IsServer)
            {
                return;
            }

            nextCardDraftIntervalTimer = Mathf.Max(1f, cardDraftInterval);
            activeCardDraftRemaining = 0f;
            networkCardDraftActive.Value = false;
            networkCardDraftTimer.Value = enableGlobalCardDraft ? nextCardDraftIntervalTimer : 0f;
        }

        private void StartGlobalCardDraftPhaseServer()
        {
            if (!IsServer || networkCardDraftActive.Value)
            {
                return;
            }

            if (networkCardDraftRound.Value >= MaxDraftRounds)
            {
                networkCardDraftTimer.Value = 0f;
                return;
            }

            RefreshDraftParticipantsServer();
            GenerateDraftOffersServer();
            serverSelectedThisDraft.Clear();

            networkCardDraftActive.Value = true;
            networkCardDraftRound.Value += 1;
            activeCardDraftRemaining = Mathf.Max(1f, cardDraftDuration);
            networkCardDraftTimer.Value = activeCardDraftRemaining;

            cachedDraftRound = networkCardDraftRound.Value;
            cachedDraftHostClientId = serverHostClientId;
            cachedDraftGuestClientId = serverGuestClientId;
            CopyOffer(serverHostOffer, cachedHostOffer);
            CopyOffer(serverGuestOffer, cachedGuestOffer);
            hasCachedDraftOffer = true;

            CardDraftOfferRpc(
                networkCardDraftRound.Value,
                serverHostClientId,
                serverGuestClientId,
                serverHostOffer[0], serverHostOffer[1], serverHostOffer[2],
                serverGuestOffer[0], serverGuestOffer[1], serverGuestOffer[2]
            );

            CardDraftStartedRpc(networkCardDraftRound.Value, activeCardDraftRemaining);

            if (emitCardDraftDebugLogs)
            {
                Debug.Log($"[GameStateManager] Card draft started. Round={networkCardDraftRound.Value}, host={serverHostClientId}, guest={serverGuestClientId}");
            }
        }
        private void EndGlobalCardDraftPhaseServer()
        {
            if (!IsServer)
            {
                return;
            }

            bool wasActive = networkCardDraftActive.Value;
            int round = networkCardDraftRound.Value;

            if (wasActive)
            {
                ResolveUnselectedDraftChoicesServer();
            }

            networkCardDraftActive.Value = false;
            activeCardDraftRemaining = 0f;
            nextCardDraftIntervalTimer = Mathf.Max(1f, cardDraftInterval);
            networkCardDraftTimer.Value = (networkMatchState.Value == MatchState.InProgress && networkCardDraftRound.Value < MaxDraftRounds)
                ? nextCardDraftIntervalTimer
                : 0f;

            serverSelectedThisDraft.Clear();

            if (wasActive)
            {
                OnCardDraftEndedServer?.Invoke(round);
                CardDraftEndedRpc(round);
                if (emitCardDraftDebugLogs)
                {
                    Debug.Log($"[GameStateManager] Card draft ended. Round={round}");
                }
            }
        }

        private void ResetCardDraftStateServer(bool resetRound, bool clearHistory)
        {
            if (!IsServer)
            {
                return;
            }

            if (networkCardDraftActive.Value)
            {
                EndGlobalCardDraftPhaseServer();
            }

            networkCardDraftActive.Value = false;
            activeCardDraftRemaining = 0f;
            nextCardDraftIntervalTimer = Mathf.Max(1f, cardDraftInterval);
            networkCardDraftTimer.Value = 0f;

            RefreshDraftParticipantsServer();
            FillOffer(serverHostOffer, InvalidCard);
            FillOffer(serverGuestOffer, InvalidCard);
            serverSelectedThisDraft.Clear();
            ResetLocalDraftOfferCache();

            if (resetRound)
            {
                networkCardDraftRound.Value = 0;
            }

            if (clearHistory)
            {
                syncedSelectedCardsByPlayer.Clear();
            }
        }

        private void ResolveUnselectedDraftChoicesServer()
        {
            ResolveUnselectedPlayerChoiceServer(serverHostClientId);
            ResolveUnselectedPlayerChoiceServer(serverGuestClientId);
        }

        private void ResolveUnselectedPlayerChoiceServer(ulong playerId)
        {
            if (playerId == ulong.MaxValue || serverSelectedThisDraft.Contains(playerId))
            {
                return;
            }

            if (GetPlayerCardHistoryCount(playerId) >= MaxDraftRounds)
            {
                return;
            }

            int[] offer = GetOfferForPlayerServer(playerId);
            int autoCard = FirstValidCard(offer);
            if (autoCard < 0)
            {
                return;
            }

            CommitCardSelectionServer(playerId, autoCard);
        }

        private bool TryValidateCardSelectionRequestServer(ulong senderClientId, int round, int cardIndex, out string rejectReason)
        {
            rejectReason = string.Empty;

            if (!networkCardDraftActive.Value)
            {
                rejectReason = "DraftNotActive";
                return false;
            }

            if (round != networkCardDraftRound.Value)
            {
                rejectReason = "RoundMismatch";
                return false;
            }

            if (!IsDraftParticipant(senderClientId))
            {
                rejectReason = "NotDraftParticipant";
                return false;
            }

            if (serverSelectedThisDraft.Contains(senderClientId))
            {
                rejectReason = "AlreadySelected";
                return false;
            }

            if (GetPlayerCardHistoryCount(senderClientId) >= MaxDraftRounds)
            {
                rejectReason = "SelectionLimitReached";
                return false;
            }

            if (!IsCardInOffer(senderClientId, cardIndex))
            {
                rejectReason = "CardNotInOffer";
                return false;
            }

            return true;
        }

        private bool CommitCardSelectionServer(ulong playerId, int cardIndex)
        {
            int slotIndex = GetPlayerCardHistoryCount(playerId);
            if (slotIndex >= MaxDraftRounds)
            {
                return false;
            }

            SetPlayerCardHistorySlot(playerId, slotIndex, cardIndex);
            serverSelectedThisDraft.Add(playerId);
            CardSelectionResolvedRpc(playerId, slotIndex, cardIndex);

            if (emitCardDraftDebugLogs)
            {
                Debug.Log($"[GameStateManager] Card selected. player={playerId}, slot={slotIndex}, card={cardIndex}");
            }

            if (AllDraftParticipantsSelected())
            {
                activeCardDraftRemaining = 0f;
                networkCardDraftTimer.Value = 0f;
            }

            return true;
        }

        private bool AllDraftParticipantsSelected()
        {
            bool hostDone = serverHostClientId == ulong.MaxValue || serverSelectedThisDraft.Contains(serverHostClientId) || GetPlayerCardHistoryCount(serverHostClientId) >= MaxDraftRounds;
            bool guestDone = serverGuestClientId == ulong.MaxValue || serverSelectedThisDraft.Contains(serverGuestClientId) || GetPlayerCardHistoryCount(serverGuestClientId) >= MaxDraftRounds;
            return hostDone && guestDone;
        }

        private void SendCardSelectionRejectedToClient(ulong targetClientId, int round, int requestedCardIndex, string reason)
        {
            CardSelectionRejectedRpc(round, requestedCardIndex, reason, RpcTarget.Single(targetClientId, RpcTargetUse.Temp));

            if (emitCardDraftDebugLogs)
            {
                Debug.Log($"[GameStateManager] Card selection rejected. player={targetClientId}, round={round}, card={requestedCardIndex}, reason={reason}");
            }
        }

        private void RefreshDraftParticipantsServer()
        {
            serverHostClientId = ulong.MaxValue;
            serverGuestClientId = ulong.MaxValue;

            if (NetworkManager.Singleton == null)
            {
                return;
            }

            serverHostClientId = NetworkManager.ServerClientId;
            if (NetworkManager.Singleton.ConnectedClientsList == null)
            {
                return;
            }

            int assigned = 1;
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.ClientId == serverHostClientId)
                {
                    continue;
                }

                if (assigned >= MaxDraftPlayers)
                {
                    break;
                }

                serverGuestClientId = client.ClientId;
                assigned++;
            }
        }

        private void GenerateDraftOffersServer()
        {
            int[] host = BuildOfferFromCatalog();
            int[] guest = BuildOfferFromCatalog();

            CopyOffer(host, serverHostOffer);
            if (serverGuestClientId != ulong.MaxValue)
            {
                CopyOffer(guest, serverGuestOffer);
            }
            else
            {
                FillOffer(serverGuestOffer, InvalidCard);
            }
        }

        private int[] BuildOfferFromCatalog()
        {
            int[] result = { InvalidCard, InvalidCard, InvalidCard };
            if (serverCardCatalogSize <= 0)
            {
                return result;
            }

            List<int> pool = new List<int>(serverCardCatalogSize);
            for (int i = 0; i < serverCardCatalogSize; i++)
            {
                pool.Add(i);
            }

            for (int i = 0; i < OfferChoices; i++)
            {
                if (pool.Count == 0)
                {
                    break;
                }

                int pick = UnityEngine.Random.Range(0, pool.Count);
                result[i] = pool[pick];
                pool.RemoveAt(pick);
            }

            return result;
        }

        private bool IsDraftParticipant(ulong clientId)
        {
            return clientId == serverHostClientId || clientId == serverGuestClientId;
        }

        private bool IsCardInOffer(ulong playerId, int cardIndex)
        {
            int[] offer = GetOfferForPlayerServer(playerId);
            if (offer == null)
            {
                return false;
            }

            for (int i = 0; i < offer.Length; i++)
            {
                if (offer[i] == cardIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private int[] GetOfferForPlayerServer(ulong playerId)
        {
            if (playerId == serverHostClientId) return serverHostOffer;
            if (playerId == serverGuestClientId) return serverGuestOffer;
            return null;
        }

        private static int FirstValidCard(int[] offer)
        {
            if (offer == null) return InvalidCard;
            for (int i = 0; i < offer.Length; i++)
            {
                if (offer[i] >= 0)
                {
                    return offer[i];
                }
            }

            return InvalidCard;
        }

        private int GetPlayerCardHistoryCount(ulong playerId)
        {
            if (!syncedSelectedCardsByPlayer.TryGetValue(playerId, out List<int> list))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] >= 0) count++;
            }

            return count;
        }

        private void SetPlayerCardHistorySlot(ulong playerId, int slotIndex, int cardIndex)
        {
            if (slotIndex < 0 || slotIndex >= MaxDraftRounds)
            {
                return;
            }

            if (!syncedSelectedCardsByPlayer.TryGetValue(playerId, out List<int> list))
            {
                list = new List<int>(MaxDraftRounds);
                syncedSelectedCardsByPlayer[playerId] = list;
            }

            while (list.Count <= slotIndex)
            {
                list.Add(InvalidCard);
            }

            list[slotIndex] = cardIndex;
        }

        private static void CopyOffer(int[] source, int[] destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            int len = Mathf.Min(source.Length, destination.Length);
            for (int i = 0; i < len; i++)
            {
                destination[i] = source[i];
            }

            for (int i = len; i < destination.Length; i++)
            {
                destination[i] = InvalidCard;
            }
        }

        private static int[] CopyOffer(int[] source)
        {
            if (source == null)
            {
                return null;
            }

            int[] copied = new int[source.Length];
            Array.Copy(source, copied, source.Length);
            return copied;
        }

        private static void FillOffer(int[] arr, int value)
        {
            if (arr == null)
            {
                return;
            }

            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = value;
            }
        }

        private void ResetLocalDraftOfferCache()
        {
            cachedDraftHostClientId = ulong.MaxValue;
            cachedDraftGuestClientId = ulong.MaxValue;
            cachedDraftRound = 0;
            hasCachedDraftOffer = false;
            FillOffer(cachedHostOffer, InvalidCard);
            FillOffer(cachedGuestOffer, InvalidCard);
        }

        private void ClearLocalDraftOffersOnly()
        {
            hasCachedDraftOffer = false;
            FillOffer(cachedHostOffer, InvalidCard);
            FillOffer(cachedGuestOffer, InvalidCard);
        }
    }
}
