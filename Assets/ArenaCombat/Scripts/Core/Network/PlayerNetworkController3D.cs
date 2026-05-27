// ARCH TAG: TARGET_3D
// ARCH SCOPE: Server-authoritative top-down 3D player controller pre-migration path.
// ARCH STATUS: ACTIVE_PREP

using System;
using System.Collections.Generic;
using ArenaCombat.Core;
using ArenaCombat.Core.Combat;
using ArenaCombat.Core.AI;
using ArenaCombat.Core.Skill;
using ArenaCombat.Core.Stats;
using ArenaCombat.Core.State;
using Unity.Netcode;
using UnityEngine;

namespace ArenaCombat.Core.Network
{
    /// <summary>
    /// Top-down 3D migration controller.
    /// This class implements server-authoritative movement/rope/perk request gates.
    /// Final combat hit judgment integration is intentionally deferred.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(StatManager))]      // X3-2
    [RequireComponent(typeof(StateManager))]     // X3-2
    [RequireComponent(typeof(SkillExecutor))]    // X3-2
    [RequireComponent(typeof(SkillManager))]     // X3-2
    public class PlayerNetworkController3D : NetworkBehaviour, ICombatant
    {
        [Header("=== 3D Movement Settings ===")]
        [SerializeField] private float maxHP = 150f;

        [Header("=== Stat Authority (X3-3) ===")]
        // Designer-assigned PlayerStatsSO. Pre-X1-6 SO import: null OK
        // (StatManager.Initialize handles null → _runtimeStats stays null → safe inert).
        [SerializeField] private PlayerStatsSO _playerStatsSO;
        // Cached StatManager ref (RequireComponent ensures existence). Awake assigns.
        private StatManager _statMgr;
        // Last damage attacker for Die() killerId carry-through (skill kill attribution).
        private ulong _lastAttackerId;
        private System.Action<SkillDefinition, SkillContext> _biasSkillHandler;
        private System.Action<SkillDefinition, SkillContext> _cooldownSyncHandler;

        [Header("=== BT Agent (C2) ===")]
        [SerializeField] bool _isBTControlled;
        public bool IsBTControlled => _isBTControlled;
        [SerializeField] private float moveSpeed = TopDown3DConstants.DEFAULT_MOVE_SPEED;
        [SerializeField] private float rotationLerp = TopDown3DConstants.DEFAULT_ROTATION_LERP;
        [SerializeField] private float inputSendRate = NetworkTickRate.INPUT_SEND_RATE;
#pragma warning disable CS0414
        [SerializeField] private float positionSyncThreshold = 0.15f;
#pragma warning restore CS0414

        [Header("=== Survival Settings ===")]
        [SerializeField] private float respawnTime = CombatConstants.RESPAWN_TIME;
        [SerializeField] private float invulnerabilityDuration = CombatConstants.INVULNERABILITY_AFTER_SPAWN;

        [Header("=== Initial Loadout (BAL-1) ===")]
        [Tooltip("매치 시작/재시작 시 slot 0에 자동 장착되는 starter 스킬. 드래프트는 slots 1..4 채움.")]
        [SerializeField] private ArenaCombat.Core.Skill.SkillDefinition _initialSkillSO;
        private bool _warnedMissingInitialSkill;

        [Header("=== Rope Settings ===")]
        [SerializeField] private float ropeMaxDistance = TopDown3DConstants.DEFAULT_ROPE_MAX_DISTANCE;
        [SerializeField] private float ropeSpeed = TopDown3DConstants.DEFAULT_ROPE_SPEED;
        [SerializeField] private float ropeCooldown = TopDown3DConstants.DEFAULT_ROPE_COOLDOWN;
        [SerializeField] private LayerMask ropeAnchorLayer;

        [Header("=== Parry Settings (3D) ===")]
        [SerializeField, Min(0f)] private float parryWindowDuration = 0.3f;
        [SerializeField, Min(0f)] private float parryCooldown = 0.6f;
        [Tooltip("Server-side stun applied to attacker when their attack is parried. One-off pattern (not a general status duration system). Set to 0 to disable parry-stun (parry still blocks damage).")]
        [SerializeField, Min(0f)] private float parryStunDuration = 1.0f;

        [Header("=== Owner Input Integration ===")]
        [SerializeField] private bool useBuiltInInputHandler = true;
        [SerializeField] private bool autoSendMoveRequests = true;
        [SerializeField] private bool force3DAimProjectionForBuiltInInput = true;
        [SerializeField] private float builtInAimGroundY = 0f;

        [Header("=== Owner Camera Binding ===")]
        [SerializeField] private Camera ownerCameraOverride;
        [SerializeField] private bool rebindOwnerCameraWhenMissing = true;

        [Header("=== Server Queue Settings ===")]
        [SerializeField] private int maxQueuedActions = 32;

        [Header("=== Client Sync Smoothing ===")]
        [SerializeField] private float respawnInterpolationSuppressDuration = 0.15f;

        [Header("=== Inspector Runtime Debug (Read-Only) ===")]
        [SerializeField] private bool inspectorRuntimeDebug = true;
        [SerializeField] private ulong debugOwnerClientId;
        [SerializeField] private bool debugIsSpawned;
        [SerializeField] private bool debugIsServer;
        [SerializeField] private bool debugIsOwner;
        [SerializeField] private bool debugIsHost;
        [SerializeField] private int debugLocalTick;
        [SerializeField] private int debugQueuedActionCount;
        [SerializeField] private Vector2 debugServerMoveInput;
        [SerializeField] private float debugServerLookYaw;
        [SerializeField] private Vector3 debugNetworkPosition;
        [SerializeField] private float debugNetworkYaw;
        [SerializeField] private float debugNetworkHP;
        [SerializeField] private bool debugNetworkIsAlive;
        [SerializeField] private CharacterStateId debugNetworkStateId;
        [SerializeField] private StatusMask debugNetworkStatusMask;
        [SerializeField] private TeamId debugNetworkTeamId;
        [SerializeField] private bool debugNetworkIsRoping;
        [SerializeField] private Vector3 debugNetworkRopeTarget;

        // Components
        private Rigidbody rb;
        private Collider playerCollider;
        private PlayerInputHandler inputHandler;
        private Camera ownerCamera;

        #region NetworkVariables (Server Write, Everyone Read)

        private readonly NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<float> networkYaw = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<float> networkHP = new NetworkVariable<float>(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<bool> networkIsAlive = new NetworkVariable<bool>(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<CharacterStateId> networkStateId = new NetworkVariable<CharacterStateId>(
            CharacterStateId.Idle,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<StatusMask> networkStatusMask = new NetworkVariable<StatusMask>(
            StatusMask.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<TeamId> networkTeamId = new NetworkVariable<TeamId>(
            TeamId.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<bool> networkIsRoping = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<Vector3> networkRopeTarget = new NetworkVariable<Vector3>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<float> networkShield = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<BuffMask> networkBuffMask = new NetworkVariable<BuffMask>(
            BuffMask.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly NetworkVariable<DebuffMask> networkDebuffMask = new NetworkVariable<DebuffMask>(
            DebuffMask.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        #endregion

        #region Public Properties

        public float CurrentHP => networkHP.Value;
        public float CurrentShield => networkShield.Value;
        public BuffMask CurrentBuffs => networkBuffMask.Value;
        public DebuffMask CurrentDebuffs => networkDebuffMask.Value;
        public float MaxHP => maxHP;
        public bool IsAlive => networkIsAlive.Value;
        public CharacterStateId CurrentStateId => networkStateId.Value;
        public StatusMask CurrentStatus => networkStatusMask.Value;
        public TeamId Team => networkTeamId.Value;
        public bool IsRoping => networkIsRoping.Value;
        public Vector3 RopeTarget => networkRopeTarget.Value;
        public float CurrentYaw => networkYaw.Value;
        public bool UseBuiltInInputHandler => useBuiltInInputHandler;
        public bool AutoSendMoveRequests => autoSendMoveRequests;
        public int LocalTick => localTick;
        public Camera OwnerCamera => ownerCamera;
        public Collider PlayerCollider => playerCollider;

        #endregion

        #region Events

        public event Action<float, float> OnHPChanged;
        public event Action<CharacterStateId, CharacterStateId> OnStateChanged;
        public event Action<StatusMask> OnStatusChanged;
        public event Action<bool, string> OnRopeResult;
        public event Action<int, bool, string> OnPerkTriggerResult;

        #endregion

        // Server side movement state
        private Vector2 serverMoveInput;
        private float serverLookYaw;

        // Server side timers and action tracking
        private float respawnTimer;
        private float invulnerabilityTimer;
        private float ropeCooldownTimer;
        private bool isRopeMoving;
        private Vector3 ropeTargetPosition;
        private Vector3 lastValidatedServerPosition;
        private float parryWindowTimer;
        private float parryCooldownTimer;
        private float parryStunTimer;

        public bool IsParrying => parryWindowTimer > 0f;

        /// <summary>
        /// Server-only. Called by CombatManager3D when this player's attack is parried by a defender.
        /// Applies StatusMask.Stunned for parryStunDuration seconds. Auto-cleared by UpdateServerTimers.
        /// No-op when parryStunDuration is 0 (treated as feature disabled).
        /// </summary>
        public void ApplyParryStun()
        {
            if (!IsServer) return;
            if (parryStunDuration <= 0f) return;
            parryStunTimer = parryStunDuration;
            AddStatus(StatusMask.Stunned);
        }

        private enum QueuedActionType
        {
            Rope = 0,
            PerkTrigger = 1,
            Attack = 2,
            Parry = 3
        }

        private struct QueuedServerAction
        {
            public QueuedActionType ActionType;
            public int ClientTick;
            public float ReceivedAt;
            public Vector3 AnchorHint;
            public bool HasAnchorHint;
            public Vector3 Direction;
            public int TriggerId;
            public Vector3 TargetHint;
            public AttackType AttackKind;
        }

        private readonly List<QueuedServerAction> queuedServerActions = new List<QueuedServerAction>();

        // Client side interpolation state
        private float lastInputSendTime;
        private Vector2 cachedMoveInput;
        private float cachedLookYaw;
        private int localTick;
        private float suppressInterpolationUntil;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            playerCollider = GetComponent<Collider>();
            ResolvePlayerReferences();

            // X3-2: bind per-entity managers as ICombatant owner. RequireComponent
            // attributes guarantee these exist (Unity auto-adds on attach + prefab
            // migration in X3-2 history). Initialize call (StatManager._runtimeStats
            // clone from PlayerStatsSO) lands in X3-3 alongside damage routing.
            if (TryGetComponent<StatManager>(out var statMgr))
            {
                _statMgr = statMgr;
                statMgr.BindOwner(this);
            }
            if (TryGetComponent<StateManager>(out var stateMgr))
                stateMgr.BindOwner(this);
            // SkillExecutor: no binding API (cooldown dict self-contained).
            // SkillManager: own Awake auto-detects ICombatant + StatManager + StateManager.

            // Top-down movement assumes no vertical physics-driven gameplay by default.
            rb.useGravity = false;
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void ResolvePlayerReferences()
        {
            if (inputHandler == null)
            {
                inputHandler = GetComponent<PlayerInputHandler>();
            }
        }

        // X3-3: initialize / re-initialize StatManager authority. Called from
        // OnNetworkSpawn (server-only) and Respawn (server-only).
        private void InitializeStatManager()
        {
            if (_statMgr == null) return;
            float shieldMax = _playerStatsSO != null ? _playerStatsSO.ShieldMax   : 0f;
            float hpRegen   = _playerStatsSO != null ? _playerStatsSO.HPRegenRate : 0f;
            _statMgr.Initialize(_playerStatsSO, maxHP, shieldMax, hpRegen, parryWindowDuration, CombatantKind.Player);
        }

        // BAL-1: 매치 시작 + RestartMatch에서 호출. slot 0에 starter 스킬 장착.
        // Respawn은 슬롯을 건드리지 않으므로 별도 호출 불필요 (단일 사망 후 부활 시 슬롯 유지).
        public void ApplyInitialLoadoutServer()
        {
            if (!IsServer) return;
            if (_initialSkillSO == null)
            {
                if (!_warnedMissingInitialSkill)
                {
                    Debug.LogWarning("[PNC3D] _initialSkillSO 미할당 — slot 0 비어있음. Player prefab inspector에서 할당 필요.", this);
                    _warnedMissingInitialSkill = true;
                }
                return;
            }
            SetSkillSlotServer(0, _initialSkillSO);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ResolvePlayerReferences();

            networkHP.OnValueChanged += HandleHPChanged;
            networkStateId.OnValueChanged += HandleStateChanged;
            networkStatusMask.OnValueChanged += HandleStatusChanged;

            if (!IsServer)
            {
                networkPosition.OnValueChanged += HandlePositionChanged;
                networkYaw.OnValueChanged += HandleYawChanged;
            }

            if (IsServer)
            {
                if (_statMgr != null)
                {
                    _statMgr.OnStatusApplied += HandleStatManagerStatusApplied;
                    _statMgr.OnStatusRemoved += HandleStatManagerStatusRemoved;
                    _statMgr.OnStatusBulkCleared += HandleStatManagerStatusBulkCleared;
                    _statMgr.OnBuffApplied += HandleBuffApplied;
                    _statMgr.OnBuffRemoved += HandleBuffRemoved;
                    _statMgr.OnDebuffApplied += HandleDebuffApplied;
                    _statMgr.OnDebuffRemoved += HandleDebuffRemoved;
                    _statMgr.OnBuffDebuffBulkCleared += HandleBuffDebuffBulkCleared;
                }

                InitializeStatManager();

                networkHP.Value = maxHP;
                networkIsAlive.Value = true;
                networkPosition.Value = transform.position;
                networkYaw.Value = transform.eulerAngles.y;
                lastValidatedServerPosition = transform.position;

                if (InputValidator.Instance != null)
                {
                    InputValidator.Instance.RegisterClient(OwnerClientId);
                }

                if (CombatManager3D.Instance != null)
                {
                    CombatManager3D.Instance.RegisterPlayer3D(OwnerClientId, this);
                }

                if (GameManager.Instance != null)
                {
                    GameManager.Instance.RegisterPlayer(gameObject);
                }

                PlayerBiasTracker.Instance?.RegisterPlayer(OwnerClientId);

                ApplyInitialLoadoutServer();

                var biasExec = GetComponent<SkillExecutor>();
                if (biasExec != null)
                {
                    if (PlayerBiasTracker.Instance != null)
                    {
                        ulong clientId = OwnerClientId;
                        _biasSkillHandler = (def, ctx) => PlayerBiasTracker.Instance?.RecordSkillCast(clientId, def, ctx);
                        biasExec.OnExecuted += _biasSkillHandler;
                    }

                    _cooldownSyncHandler = (def, ctx) =>
                    {
                        if (def != null) SkillCooldownStartRpc(def.SkillId);
                    };
                    biasExec.OnExecuted += _cooldownSyncHandler;
                }
            }

            if (IsOwner && !_isBTControlled)
            {
                if (inputHandler != null)
                {
                    inputHandler.enabled = useBuiltInInputHandler;
                    if (useBuiltInInputHandler)
                    {
                        if (force3DAimProjectionForBuiltInInput)
                        {
                            inputHandler.ConfigureAimProjection(true, builtInAimGroundY);
                        }

                        SubscribeInputEvents();
                    }
                }

                SetupTopDownCamera();
                gameObject.name = $"Player3D_LOCAL_{OwnerClientId}";
            }
            else if (IsOwner && _isBTControlled)
            {
                gameObject.name = $"Player3D_BT_{OwnerClientId}";
            }
            else
            {
                if (inputHandler != null)
                {
                    inputHandler.enabled = false;
                }

                gameObject.name = $"Player3D_REMOTE_{OwnerClientId}";
            }

            UpdateInspectorRuntimeDebug();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            networkHP.OnValueChanged -= HandleHPChanged;
            networkStateId.OnValueChanged -= HandleStateChanged;
            networkStatusMask.OnValueChanged -= HandleStatusChanged;

            if (!IsServer)
            {
                networkPosition.OnValueChanged -= HandlePositionChanged;
                networkYaw.OnValueChanged -= HandleYawChanged;
            }

            if (IsOwner && useBuiltInInputHandler)
            {
                UnsubscribeInputEvents();
            }

            if (IsServer && InputValidator.Instance != null)
            {
                InputValidator.Instance.UnregisterClient(OwnerClientId);
            }

            if (IsServer && CombatManager3D.Instance != null)
            {
                CombatManager3D.Instance.UnregisterPlayer3D(OwnerClientId);
            }

            if (IsServer && GameManager.Instance != null)
            {
                GameManager.Instance.UnregisterPlayer(gameObject);
            }

            if (IsServer)
            {
                var biasExec = GetComponent<SkillExecutor>();
                if (biasExec != null)
                {
                    if (_biasSkillHandler != null) biasExec.OnExecuted -= _biasSkillHandler;
                    if (_cooldownSyncHandler != null) biasExec.OnExecuted -= _cooldownSyncHandler;
                }
                _biasSkillHandler = null;
                _cooldownSyncHandler = null;
                PlayerBiasTracker.Instance?.UnregisterPlayer(OwnerClientId);

                queuedServerActions.Clear();
            }

            if (_statMgr != null)
            {
                _statMgr.OnStatusApplied -= HandleStatManagerStatusApplied;
                _statMgr.OnStatusRemoved -= HandleStatManagerStatusRemoved;
                _statMgr.OnStatusBulkCleared -= HandleStatManagerStatusBulkCleared;
                _statMgr.OnBuffApplied -= HandleBuffApplied;
                _statMgr.OnBuffRemoved -= HandleBuffRemoved;
                _statMgr.OnDebuffApplied -= HandleDebuffApplied;
                _statMgr.OnDebuffRemoved -= HandleDebuffRemoved;
                _statMgr.OnBuffDebuffBulkCleared -= HandleBuffDebuffBulkCleared;
            }

            suppressInterpolationUntil = 0f;
            ownerCamera = null;
            ClearInspectorRuntimeDebug();
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsOwner && !_isBTControlled && autoSendMoveRequests)
            {
                SendCachedInputToServer();
            }

            if (IsOwner && !_isBTControlled && rebindOwnerCameraWhenMissing &&
                (ownerCamera == null || !ownerCamera.isActiveAndEnabled))
            {
                SetupTopDownCamera();
            }

            if (!IsServer)
            {
                if (CanPredictMovement)
                {
                    PredictPosition();
                    ReconcileWithServer();
                    Quaternion aimRot = Quaternion.Euler(0f, NormalizeYaw(cachedLookYaw), 0f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, aimRot, Time.deltaTime * rotationLerp);
                }
                else
                {
                    InterpolatePosition();
                    InterpolateRotation();
                }
                ClearPhysicsAngularVelocity();
            }

            UpdateInspectorRuntimeDebug();
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            Vector3 authoritativePos = transform.position;

            if (networkIsAlive.Value)
            {
                ProcessQueuedServerActions();
                ProcessServerMovement();
                _statMgr?.Tick(Time.fixedDeltaTime);

                if (MapBounds3D.Instance != null)
                {
                    Vector3 resolvedPos = MapBounds3D.Instance.ResolveServerPosition(authoritativePos, lastValidatedServerPosition);
                    if ((resolvedPos - authoritativePos).sqrMagnitude > 0.0001f)
                    {
                        rb.MovePosition(resolvedPos);
                        authoritativePos = resolvedPos;
                    }

                    lastValidatedServerPosition = authoritativePos;

                    if (MapBounds3D.Instance.IsBelowKillZone(authoritativePos))
                    {
                        Die(OwnerClientId);
                        return;
                    }
                }
                else
                {
                    lastValidatedServerPosition = authoritativePos;
                }
            }

            authoritativePos = UpdateServerTimers(authoritativePos);

            networkPosition.Value = authoritativePos;
            networkYaw.Value = transform.eulerAngles.y;

            // X3-3: StatManager → NV sync. Mirror HP every tick; trigger Die() on
            // alive→dead transition (covers skill kills since SkillComponents path
            // mutates _statMgr but not networkHP directly).
            if (_statMgr != null)
            {
                float statHP = _statMgr.GetHP();
                if (!Mathf.Approximately(networkHP.Value, statHP))
                    networkHP.Value = statHP;

                float statShield = _statMgr.GetShield();
                if (!Mathf.Approximately(networkShield.Value, statShield))
                    networkShield.Value = statShield;

                if (networkIsAlive.Value && !_statMgr.IsAlive)
                    Die(_lastAttackerId);
            }

            UpdateInspectorRuntimeDebug();
        }

        #region Owner Input Collection

        private void SubscribeInputEvents()
        {
            if (inputHandler == null)
            {
                return;
            }

            inputHandler.OnMoveInput += HandleMoveInput;
            inputHandler.OnMoveInput2D += HandleMoveInput2D;
            inputHandler.OnAimPosition += HandleAimPosition;
            inputHandler.OnLightAttack += HandleLightAttack;
            inputHandler.OnHeavyAttack += HandleHeavyAttack;
            inputHandler.OnParry += HandleParry;
        }

        private void UnsubscribeInputEvents()
        {
            if (inputHandler == null)
            {
                return;
            }

            inputHandler.OnMoveInput -= HandleMoveInput;
            inputHandler.OnMoveInput2D -= HandleMoveInput2D;
            inputHandler.OnAimPosition -= HandleAimPosition;
            inputHandler.OnLightAttack -= HandleLightAttack;
            inputHandler.OnHeavyAttack -= HandleHeavyAttack;
            inputHandler.OnParry -= HandleParry;
        }

        private void HandleLightAttack() => SubmitAttackIntent(AttackType.Light);

        private void HandleHeavyAttack() => SubmitAttackIntent(AttackType.Heavy);

        private void HandleParry() => SubmitParryIntent();

        private void HandleMoveInput(float horizontal)
        {
            // Backward-compatible fallback when only horizontal event is in use.
            cachedMoveInput = new Vector2(horizontal, cachedMoveInput.y);
        }

        private void HandleMoveInput2D(Vector2 input)
        {
            cachedMoveInput = input;
        }

        private void HandleAimPosition(Vector2 aimWorldPos)
        {
            // 3D controller expects XZ-projected aim coordinates.
            // Ignore incompatible XY payloads from 2D projection mode.
            if (inputHandler != null && !inputHandler.Use3DGroundAimProjection)
            {
                return;
            }

            Vector3 toAim = new Vector3(aimWorldPos.x, 0f, aimWorldPos.y) - new Vector3(transform.position.x, 0f, transform.position.z);
            if (toAim.sqrMagnitude > 0.0001f)
            {
                cachedLookYaw = Mathf.Atan2(toAim.x, toAim.z) * Mathf.Rad2Deg;
            }
        }

        /// <summary>
        /// External-client entry point.
        /// Updates local cached move/aim intent that can be auto-sent or manually sent.
        /// </summary>
        public void SetLocalMoveIntent(Vector2 moveInput, float lookYaw)
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            cachedMoveInput = moveInput;
            cachedLookYaw = lookYaw;
        }

        /// <summary>
        /// External-client entry point.
        /// Sends a move request immediately using current cache or provided values.
        /// Recommended when autoSendMoveRequests is disabled.
        /// </summary>
        public void SubmitMoveIntent(Vector2 moveInput, float lookYaw)
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            cachedMoveInput = moveInput;
            cachedLookYaw = lookYaw;
            SendMoveRequestNow();
        }

        /// <summary>
        /// External-client entry point.
        /// Sends one move request immediately and advances local tick.
        /// </summary>
        public void SendMoveRequestNow()
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            if (IsLocalGameplayBlockedByCardDraft())
            {
                return;
            }

            lastInputSendTime = Time.time;
            localTick++;
            RequestMoveRpc(cachedMoveInput, cachedLookYaw, localTick);
        }

        /// <summary>
        /// External-client entry point for rope action request.
        /// </summary>
        public void SubmitRopeIntent(Vector3 anchorHint, Vector3 direction, bool hasAnchorHint)
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            if (IsLocalGameplayBlockedByCardDraft())
            {
                return;
            }

            localTick++;
            RequestRopeRpc(anchorHint, direction, hasAnchorHint, localTick);
        }

        /// <summary>
        /// External-client entry point for perk trigger request.
        /// </summary>
        public void SubmitPerkTriggerIntent(int triggerId, Vector3 targetHint)
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            if (IsLocalGameplayBlockedByCardDraft())
            {
                return;
            }

            localTick++;
            RequestPerkTriggerRpc(triggerId, targetHint, localTick);
        }

        /// <summary>
        /// External-client entry point for 3D attack request (B1-3).
        /// Server validates AttackType, rate-limits, enqueues; resolver is CombatManager3D stub in B1-3.
        /// </summary>
        public void SubmitAttackIntent(AttackType attackType)
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            if (IsLocalGameplayBlockedByCardDraft())
            {
                return;
            }

            localTick++;
            RequestAttackRpc(attackType, localTick);
        }

        /// <summary>
        /// External-client entry point for 3D parry request (B1-3).
        /// Opens server-side parry window on success.
        /// </summary>
        public void SubmitParryIntent()
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            if (IsLocalGameplayBlockedByCardDraft())
            {
                return;
            }

            localTick++;
            RequestParryRpc(localTick);
        }

        /// <summary>
        /// Allows external controller to reset or seed the local tick counter.
        /// </summary>
        public void ResetLocalTick(int newTick = 0)
        {
            localTick = Mathf.Max(0, newTick);
        }

        #region Server-side BT Agent API (C2)

        public void ServerSetMoveIntent(Vector2 move, float yaw)
        {
            if (!IsServer || !IsSpawned) return;
            if (!networkIsAlive.Value || !StatusHelper.CanMove(networkStatusMask.Value) || networkIsRoping.Value)
            {
                serverMoveInput = Vector2.zero;
                return;
            }
            if (float.IsNaN(move.x) || float.IsNaN(move.y) || float.IsInfinity(move.x) || float.IsInfinity(move.y))
                move = Vector2.zero;
            if (float.IsNaN(yaw) || float.IsInfinity(yaw))
                yaw = 0f;
            serverMoveInput = move.sqrMagnitude > 1f ? move.normalized : move;
            serverLookYaw = yaw;
        }

        public void ServerSubmitAttack(AttackType type)
        {
            if (!IsServer || !IsSpawned || !networkIsAlive.Value) return;
            if (!System.Enum.IsDefined(typeof(AttackType), type)) return;
            localTick++;
            var action = new QueuedServerAction
            {
                ActionType = QueuedActionType.Attack,
                ClientTick = localTick,
                ReceivedAt = Time.time,
                AttackKind = type
            };
            TryEnqueueServerAction(action, out _);
        }

        public void ServerSubmitParry()
        {
            if (!IsServer || !IsSpawned || !networkIsAlive.Value) return;
            localTick++;
            var action = new QueuedServerAction
            {
                ActionType = QueuedActionType.Parry,
                ClientTick = localTick,
                ReceivedAt = Time.time
            };
            TryEnqueueServerAction(action, out _);
        }

        #endregion

        private void SendCachedInputToServer()
        {
            if (IsLocalGameplayBlockedByCardDraft())
            {
                return;
            }

            if (Time.time - lastInputSendTime < inputSendRate)
            {
                return;
            }

            lastInputSendTime = Time.time;
            localTick++;

            RequestMoveRpc(cachedMoveInput, cachedLookYaw, localTick);
        }

        #endregion

        #region Server Movement

        [Rpc(SendTo.Server)]
        private void RequestMoveRpc(Vector2 moveInput, float lookYaw, int clientTick, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            if (IsServerGameplayBlockedByCardDraft())
            {
                serverMoveInput = Vector2.zero;
                return;
            }

            if (InputValidator.Instance != null)
            {
                if (!InputValidator.Instance.CheckRateLimit(OwnerClientId, "move3d"))
                {
                    return;
                }

                if (!InputValidator.Instance.ValidateTickOrder(OwnerClientId, clientTick, "move3d"))
                {
                    return;
                }

                InputValidator.Instance.ValidateMoveInput2D(moveInput, out moveInput);
                lookYaw = InputValidator.Instance.SanitizeFloat(lookYaw);
            }

            lookYaw = NormalizeYaw(lookYaw);

            if (!networkIsAlive.Value || !StatusHelper.CanMove(networkStatusMask.Value) || networkIsRoping.Value)
            {
                serverMoveInput = Vector2.zero;
                return;
            }

            serverMoveInput = moveInput;
            serverLookYaw = lookYaw;
        }

        private void ProcessServerMovement()
        {
            if (IsServerGameplayBlockedByCardDraft())
            {
                rb.linearVelocity = Vector3.zero;
                ClearPhysicsAngularVelocity();
                SetStateId(CharacterStateId.Idle);
                return;
            }

            if (networkIsRoping.Value)
            {
                rb.linearVelocity = Vector3.zero;
                ClearPhysicsAngularVelocity();
                return;
            }

            Vector3 desiredVelocity = new Vector3(serverMoveInput.x, 0f, serverMoveInput.y) * moveSpeed;

            if (StatusHelper.HasStatus(networkStatusMask.Value, StatusMask.Slowed))
            {
                desiredVelocity *= 0.6f;
            }

            rb.linearVelocity = new Vector3(desiredVelocity.x, 0f, desiredVelocity.z);

            if (serverMoveInput.sqrMagnitude > 0.001f)
            {
                SetStateId(CharacterStateId.Moving);
            }
            else
            {
                SetStateId(CharacterStateId.Idle);
            }

            float targetYaw = NormalizeYaw(serverLookYaw);
            Quaternion targetRotation = Quaternion.Euler(0f, targetYaw, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.fixedDeltaTime * rotationLerp);

            // B5-1 followup: kill accumulated collision torque so server Slerp is the only
            // rotation driver (Rigidbody.constraints freeze X/Z only — Y must be damped manually).
            ClearPhysicsAngularVelocity();
        }

        // B5-1 followup helper: zero angular velocity so Y-axis physics torque from collisions
        // cannot accumulate against the script-driven rotation Slerp. Called in every server
        // movement exit path and in non-server interpolation paths.
        private void ClearPhysicsAngularVelocity()
        {
            if (rb != null) rb.angularVelocity = Vector3.zero;
        }

        #endregion

        #region Server Action Queue

        private bool TryEnqueueServerAction(QueuedServerAction action, out string rejectReason)
        {
            rejectReason = string.Empty;

            if (queuedServerActions.Count >= Mathf.Max(1, maxQueuedActions))
            {
                rejectReason = "QueueFull";
                return false;
            }

            queuedServerActions.Add(action);
            return true;
        }

        private void ProcessQueuedServerActions()
        {
            if (!IsServer || queuedServerActions.Count == 0)
            {
                return;
            }

            if (IsServerGameplayBlockedByCardDraft())
            {
                queuedServerActions.Clear();
                return;
            }

            queuedServerActions.Sort(CompareQueuedActions);

            foreach (QueuedServerAction action in queuedServerActions)
            {
                switch (action.ActionType)
                {
                    case QueuedActionType.Rope:
                        ExecuteQueuedRopeAction(action);
                        break;

                    case QueuedActionType.PerkTrigger:
                        ExecuteQueuedPerkTriggerAction(action);
                        break;

                    case QueuedActionType.Attack:
                        ExecuteQueuedAttackAction(action);
                        break;

                    case QueuedActionType.Parry:
                        ExecuteQueuedParryAction(action);
                        break;
                }
            }

            queuedServerActions.Clear();
        }

        private int CompareQueuedActions(QueuedServerAction a, QueuedServerAction b)
        {
            int tickCompare = a.ClientTick.CompareTo(b.ClientTick);
            if (tickCompare != 0)
            {
                return tickCompare;
            }

            int priorityCompare = GetQueuedActionPriority(a.ActionType).CompareTo(GetQueuedActionPriority(b.ActionType));
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            return a.ReceivedAt.CompareTo(b.ReceivedAt);
        }

        private int GetQueuedActionPriority(QueuedActionType actionType)
        {
            // Per B1-1 Decision 7: Parry first (open defense window before attack resolves in same player's queue),
            // then Rope (lock movement), then Attack, then PerkTrigger.
            // NOTE: this ordering is WITHIN one PlayerNetworkController3D's queue only. Same-tick cross-player
            // parry vs attack ordering depends on Unity object processing order — see B1-4 NOTE in
            // ExecuteQueuedAttackAction.
            return actionType switch
            {
                QueuedActionType.Parry => 0,
                QueuedActionType.Rope => 1,
                QueuedActionType.Attack => 2,
                QueuedActionType.PerkTrigger => 3,
                _ => 10
            };
        }

        private void ExecuteQueuedRopeAction(QueuedServerAction action)
        {
            if (IsServerGameplayBlockedByCardDraft())
            {
                RopeResultRpc(false, Vector3.zero, "CardDraftActive");
                return;
            }

            if (!networkIsAlive.Value || !StatusHelper.CanAct(networkStatusMask.Value))
            {
                RopeResultRpc(false, Vector3.zero, "CannotAct");
                return;
            }

            if (networkIsRoping.Value || ropeCooldownTimer > 0f)
            {
                RopeResultRpc(false, Vector3.zero, "CooldownOrBusy");
                return;
            }

            Vector3 origin = transform.position + Vector3.up * 0.5f;
            if (!TryResolveRopeCandidateTarget(origin, action.AnchorHint, action.HasAnchorHint, action.Direction, out Vector3 candidateTarget, out string detail))
            {
                RopeResultRpc(false, Vector3.zero, detail);
                return;
            }

            networkIsRoping.Value = true;
            PlayerBiasTracker.Instance?.RecordRope(OwnerClientId);
            networkRopeTarget.Value = candidateTarget;
            ropeTargetPosition = candidateTarget;
            isRopeMoving = true;
            ropeCooldownTimer = ropeCooldown;

            AddStatus(StatusMask.Rooted);
            SetStateId(CharacterStateId.Roping);
            RopeResultRpc(true, candidateTarget, "Started");
        }

        private bool TryResolveRopeCandidateTarget(
            Vector3 origin,
            Vector3 anchorHint,
            bool hasAnchorHint,
            Vector3 direction,
            out Vector3 candidateTarget,
            out string detail)
        {
            if (MapBounds3D.Instance != null)
            {
                return MapBounds3D.Instance.TryResolveRopeTarget(
                    origin,
                    anchorHint,
                    hasAnchorHint,
                    direction,
                    ropeMaxDistance,
                    ropeAnchorLayer,
                    out candidateTarget,
                    out detail
                );
            }

            Vector3 moveDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
            if (moveDirection != Vector3.zero)
            {
                if (!Physics.Raycast(origin, moveDirection, out RaycastHit hit, ropeMaxDistance, ropeAnchorLayer))
                {
                    candidateTarget = Vector3.zero;
                    detail = "NoAnchor";
                    return false;
                }

                candidateTarget = hit.point;
                detail = "Resolved";
                return true;
            }

            if (!hasAnchorHint)
            {
                candidateTarget = Vector3.zero;
                detail = "InvalidAnchorHint";
                return false;
            }

            candidateTarget = anchorHint;
            detail = "Resolved";
            return true;
        }

        private void ExecuteQueuedPerkTriggerAction(QueuedServerAction action)
        {
            if (IsServerGameplayBlockedByCardDraft())
            {
                PerkTriggerResultRpc(action.TriggerId, false, "CardDraftActive");
                return;
            }

            if (!networkIsAlive.Value || !StatusHelper.CanAct(networkStatusMask.Value) || !StatusHelper.CanUseSkill(networkStatusMask.Value))
            {
                PerkTriggerResultRpc(action.TriggerId, false, "CannotAct");
                return;
            }

            if (networkIsRoping.Value)
            {
                PerkTriggerResultRpc(action.TriggerId, false, "Busy");
                return;
            }

            if (CombatManager3D.Instance == null)
            {
                PerkTriggerResultRpc(action.TriggerId, false, "CombatManager3DMissing");
                return;
            }

            bool accepted = CombatManager3D.Instance.TryProcessPerkTrigger3D(
                OwnerClientId,
                action.TriggerId,
                transform.position,
                action.TargetHint,
                out string detail
            );

            if (accepted)
            {
                SetStateId(CharacterStateId.PerkCasting);
            }

            PerkTriggerResultRpc(action.TriggerId, accepted, detail);
        }

        private void ExecuteQueuedAttackAction(QueuedServerAction action)
        {
            if (IsServerGameplayBlockedByCardDraft())
            {
                AttackRejectedFromOwnerRpc(action.AttackKind, "CardDraftActive");
                return;
            }

            if (!networkIsAlive.Value || !StatusHelper.CanAct(networkStatusMask.Value))
            {
                AttackRejectedFromOwnerRpc(action.AttackKind, "CannotAct");
                return;
            }

            if (networkIsRoping.Value)
            {
                AttackRejectedFromOwnerRpc(action.AttackKind, "Busy");
                return;
            }

            if (CombatManager3D.Instance == null)
            {
                AttackRejectedFromOwnerRpc(action.AttackKind, "CombatManager3DMissing");
                return;
            }

            // B1-4 NOTE: same-tick cross-player parry ordering is NOT guaranteed by within-player priority.
            // Defender's parry queued in the same fixed tick may execute before or after this attack
            // depending on Unity object processing order. Defender's parry must be opened in a PRIOR
            // fixed-step to reliably defend against this attack.
            bool accepted = CombatManager3D.Instance.TryProcessAttack3D(
                OwnerClientId,
                action.AttackKind,
                transform.position,
                transform.eulerAngles.y,
                out string detail
            );

            if (!accepted)
            {
                AttackRejectedFromOwnerRpc(action.AttackKind, detail);
                return;
            }

            PlayerBiasTracker.Instance?.RecordMelee(OwnerClientId);
            SetStateId(CharacterStateId.Attacking);
        }

        private void ExecuteQueuedParryAction(QueuedServerAction action)
        {
            if (IsServerGameplayBlockedByCardDraft())
            {
                ParryRejectedFromOwnerRpc("CardDraftActive");
                return;
            }

            if (!networkIsAlive.Value || !StatusHelper.CanAct(networkStatusMask.Value))
            {
                ParryRejectedFromOwnerRpc("CannotAct");
                return;
            }

            if (networkIsRoping.Value)
            {
                ParryRejectedFromOwnerRpc("Busy");
                return;
            }

            if (parryCooldownTimer > 0f)
            {
                ParryRejectedFromOwnerRpc("Cooldown");
                return;
            }

            if (CombatManager3D.Instance == null)
            {
                ParryRejectedFromOwnerRpc("CombatManager3DMissing");
                return;
            }

            bool accepted = CombatManager3D.Instance.TryProcessParry3D(OwnerClientId, out string detail);
            if (!accepted)
            {
                ParryRejectedFromOwnerRpc(detail);
                return;
            }

            PlayerBiasTracker.Instance?.RecordParry(OwnerClientId);
            parryWindowTimer = parryWindowDuration;
            parryCooldownTimer = parryCooldown;
        }

        #endregion

        #region Rope Prework (No Final Anchor Judgment)

        [Rpc(SendTo.Server)]
        private void RequestRopeRpc(Vector3 anchorHint, Vector3 direction, bool hasAnchorHint, int clientTick)
        {
            if (IsServerGameplayBlockedByCardDraft())
            {
                RopeResultRpc(false, Vector3.zero, "CardDraftActive");
                return;
            }

            if (InputValidator.Instance != null)
            {
                if (!InputValidator.Instance.CheckRateLimit(OwnerClientId, "rope3d"))
                {
                    RopeResultRpc(false, Vector3.zero, "RateLimited");
                    return;
                }

                if (!InputValidator.Instance.ValidateTickOrder(OwnerClientId, clientTick, "rope3d"))
                {
                    RopeResultRpc(false, Vector3.zero, "TickRejected");
                    return;
                }

                anchorHint = InputValidator.Instance.SanitizeVector3(anchorHint);
                direction = InputValidator.Instance.SanitizeVector3(direction);
            }

            QueuedServerAction queuedAction = new QueuedServerAction
            {
                ActionType = QueuedActionType.Rope,
                ClientTick = clientTick,
                ReceivedAt = Time.time,
                AnchorHint = anchorHint,
                HasAnchorHint = hasAnchorHint,
                Direction = direction
            };

            if (!TryEnqueueServerAction(queuedAction, out string rejectReason))
            {
                RopeResultRpc(false, Vector3.zero, rejectReason);
            }
        }

        private void EndRopeAction()
        {
            if (!IsServer)
            {
                return;
            }

            networkIsRoping.Value = false;
            isRopeMoving = false;
            bool statMgrRootActive = _statMgr != null && _statMgr.HasStatus(StatusType.Rooted);
            if (!statMgrRootActive)
                RemoveStatus(StatusMask.Rooted);
            SetStateId(CharacterStateId.Idle);
            RopeEndRpc();
        }

        #endregion

        #region Perk Trigger Prework (No Damage Judgment Yet)

        [Rpc(SendTo.Server)]
        private void RequestPerkTriggerRpc(int triggerId, Vector3 targetHint, int clientTick)
        {
            if (IsServerGameplayBlockedByCardDraft())
            {
                PerkTriggerResultRpc(triggerId, false, "CardDraftActive");
                return;
            }

            if (triggerId < 0)
            {
                PerkTriggerResultRpc(triggerId, false, "InvalidTrigger");
                return;
            }

            if (InputValidator.Instance != null)
            {
                if (!InputValidator.Instance.CheckRateLimit(OwnerClientId, "perk3d"))
                {
                    PerkTriggerResultRpc(triggerId, false, "RateLimited");
                    return;
                }

                if (!InputValidator.Instance.ValidateTickOrder(OwnerClientId, clientTick, "perk3d"))
                {
                    PerkTriggerResultRpc(triggerId, false, "TickRejected");
                    return;
                }

                targetHint = InputValidator.Instance.SanitizeVector3(targetHint);
            }

            QueuedServerAction queuedAction = new QueuedServerAction
            {
                ActionType = QueuedActionType.PerkTrigger,
                ClientTick = clientTick,
                ReceivedAt = Time.time,
                TriggerId = triggerId,
                TargetHint = targetHint
            };

            if (!TryEnqueueServerAction(queuedAction, out string rejectReason))
            {
                PerkTriggerResultRpc(triggerId, false, rejectReason);
            }
        }

        #endregion

        #region Attack/Parry Prework (B1-3 — resolver stubs in CombatManager3D, hit logic in B1-4)

        [Rpc(SendTo.Server)]
        private void RequestAttackRpc(AttackType attackType, int clientTick)
        {
            if (IsServerGameplayBlockedByCardDraft())
            {
                AttackRejectedFromOwnerRpc(attackType, "CardDraftActive");
                return;
            }

            if (!System.Enum.IsDefined(typeof(AttackType), attackType))
            {
                AttackRejectedFromOwnerRpc(attackType, "InvalidAttackType");
                return;
            }

            if (InputValidator.Instance != null)
            {
                if (!InputValidator.Instance.CheckRateLimit(OwnerClientId, "attack3d"))
                {
                    AttackRejectedFromOwnerRpc(attackType, "RateLimited");
                    return;
                }

                if (!InputValidator.Instance.ValidateTickOrder(OwnerClientId, clientTick, "attack3d"))
                {
                    AttackRejectedFromOwnerRpc(attackType, "TickRejected");
                    return;
                }
            }

            QueuedServerAction queuedAction = new QueuedServerAction
            {
                ActionType = QueuedActionType.Attack,
                ClientTick = clientTick,
                ReceivedAt = Time.time,
                AttackKind = attackType
            };

            if (!TryEnqueueServerAction(queuedAction, out string rejectReason))
            {
                AttackRejectedFromOwnerRpc(attackType, rejectReason);
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestParryRpc(int clientTick)
        {
            if (IsServerGameplayBlockedByCardDraft())
            {
                ParryRejectedFromOwnerRpc("CardDraftActive");
                return;
            }

            if (InputValidator.Instance != null)
            {
                if (!InputValidator.Instance.CheckRateLimit(OwnerClientId, "parry3d"))
                {
                    ParryRejectedFromOwnerRpc("RateLimited");
                    return;
                }

                if (!InputValidator.Instance.ValidateTickOrder(OwnerClientId, clientTick, "parry3d"))
                {
                    ParryRejectedFromOwnerRpc("TickRejected");
                    return;
                }
            }

            QueuedServerAction queuedAction = new QueuedServerAction
            {
                ActionType = QueuedActionType.Parry,
                ClientTick = clientTick,
                ReceivedAt = Time.time
            };

            if (!TryEnqueueServerAction(queuedAction, out string rejectReason))
            {
                ParryRejectedFromOwnerRpc(rejectReason);
            }
        }

        [Rpc(SendTo.Owner)]
        private void AttackRejectedFromOwnerRpc(AttackType attackType, string reason)
        {
            Debug.Log($"[PlayerNetworkController3D] Attack rejected (owner-only): type={attackType} reason={reason}");
        }

        [Rpc(SendTo.Owner)]
        private void ParryRejectedFromOwnerRpc(string reason)
        {
            Debug.Log($"[PlayerNetworkController3D] Parry rejected (owner-only): reason={reason}");
        }

        #endregion

        #region Survival and Status

        /// <summary>
        /// Server-only damage application. Returns true if damage was applied (passed alive +
        /// invulnerability + status filters), false if upstream-rejected before HP modification.
        /// Caller (CombatManager3D resolver) uses the return for "actually damaged" hitTargets +
        /// assist attribution accuracy. damage=0 still returns true (HP unchanged but not rejected).
        /// </summary>
        public bool TakeDamage(float damage, ulong attackerId, DamageType damageType = DamageType.Physical)
        {
            if (!IsServer)
            {
                return false;
            }

            if (!networkIsAlive.Value)
            {
                return false;
            }

            if (invulnerabilityTimer > 0f || !StatusHelper.CanTakeDamage(networkStatusMask.Value))
            {
                return false;
            }

            // X3-3: route damage through StatManager (single authority).
            // FixedUpdate sync hook mirrors _statMgr.GetHP() → networkHP and detects
            // alive→dead transition → Die(_lastAttackerId).
            _lastAttackerId = attackerId;
            if (_statMgr != null)
            {
                _statMgr.ReceiveDamage(damage, null /* TODO X3-N: attacker ICombatant lookup */);
            }
            else
            {
                Debug.LogWarning("[PNC3D X3-3] StatManager null fallback — direct networkHP mutation");
                networkHP.Value = Mathf.Max(0f, networkHP.Value - damage);
            }
            DamageEventRpc(damage, attackerId, (byte)damageType);

            // Hit interrupt: only if still alive post-damage (StatManager IsAlive authoritative).
            bool stillAlive = _statMgr != null ? _statMgr.IsAlive : networkHP.Value > 0f;
            if (stillAlive && StatusHelper.CanBeInterrupted(networkStatusMask.Value))
            {
                SetStateId(CharacterStateId.Hit);
            }

            return true;
        }

        public void Heal(float amount)
        {
            if (!IsServer || !networkIsAlive.Value)
            {
                return;
            }

            // X3-3 (Codex S-3): route heal through StatManager. Sync hook mirrors to networkHP.
            if (_statMgr != null)
            {
                _statMgr.RecoverHP(amount);
            }
            else
            {
                networkHP.Value = Mathf.Min(maxHP, networkHP.Value + amount);
            }
            HealEventRpc(amount);
        }

        private void Die(ulong killerId)
        {
            networkHP.Value = 0f;
            networkShield.Value = 0f;
            networkIsAlive.Value = false;
            if (_statMgr != null)
            {
                _statMgr.SetHP(0f);
                _statMgr.ClearAllEffects();
            }
            networkStatusMask.Value = StatusMask.None;
            networkBuffMask.Value = BuffMask.None;
            networkDebuffMask.Value = DebuffMask.None;
            SetStateId(CharacterStateId.Dead);
            queuedServerActions.Clear();
            serverMoveInput = Vector2.zero;
            networkIsRoping.Value = false;
            isRopeMoving = false;
            parryWindowTimer = 0f;
            parryCooldownTimer = 0f;
            parryStunTimer = 0f;
            RemoveStatus(StatusMask.Stunned);

            if (playerCollider != null)
            {
                playerCollider.enabled = false;
            }

            rb.linearVelocity = Vector3.zero;
            respawnTimer = respawnTime;
            DeathEventRpc(killerId);

            if (CombatManager3D.Instance != null)
            {
                CombatManager3D.Instance.OnPlayerDeath3D(OwnerClientId, killerId);
            }
        }

        public void Respawn(Vector3 position)
        {
            if (!IsServer)
            {
                return;
            }

            if (MapBounds3D.Instance != null)
            {
                position = MapBounds3D.Instance.GetSafeSpawnPoint(position);
            }

            if (_statMgr != null) _statMgr.ClearAllEffects();

            // X3-3 (Codex C-1): reset StatManager authority on respawn. Without this,
            // _statMgr._currentHP stays 0 and _isAlive stays false from death; next
            // FixedUpdate sync would re-flip networkIsAlive to dead.
            InitializeStatManager();

            networkHP.Value = maxHP;
            networkShield.Value = 0f;
            networkBuffMask.Value = BuffMask.None;
            networkDebuffMask.Value = DebuffMask.None;
            networkIsAlive.Value = true;
            networkStatusMask.Value = StatusMask.None;
            SetStateId(CharacterStateId.Idle);

            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, serverLookYaw, 0f);
            rb.linearVelocity = Vector3.zero;
            lastValidatedServerPosition = position;
            parryWindowTimer = 0f;
            parryCooldownTimer = 0f;
            parryStunTimer = 0f;
            RemoveStatus(StatusMask.Stunned);

            if (playerCollider != null)
            {
                playerCollider.enabled = true;
            }

            invulnerabilityTimer = invulnerabilityDuration;
            AddStatus(StatusMask.Invulnerable);
            RespawnEventRpc(position);
        }

        public void AddStatus(StatusMask status)
        {
            if (!IsServer)
            {
                return;
            }

            networkStatusMask.Value = StatusHelper.AddStatus(networkStatusMask.Value, status);
        }

        public void RemoveStatus(StatusMask status)
        {
            if (!IsServer)
            {
                return;
            }

            networkStatusMask.Value = StatusHelper.RemoveStatus(networkStatusMask.Value, status);
        }

        private void HandleStatManagerStatusApplied(StatusType type, float duration)
        {
            if (!IsServer) return;
            StatusMask mask = StatusHelper.StatusTypeToMask(type);
            if (mask == StatusMask.None) return;
            AddStatus(mask);
        }

        private void HandleStatManagerStatusRemoved(StatusType type)
        {
            if (!IsServer) return;
            StatusMask mask = StatusHelper.StatusTypeToMask(type);
            if (mask == StatusMask.None) return;

            if (mask == StatusMask.Stunned)
            {
                bool otherStunActive = (type == StatusType.Stunned && _statMgr.HasStatus(StatusType.HitStun))
                                    || (type == StatusType.HitStun && _statMgr.HasStatus(StatusType.Stunned));
                if (otherStunActive || parryStunTimer > 0f) return;
            }

            if (mask == StatusMask.Invulnerable && invulnerabilityTimer > 0f) return;
            if (mask == StatusMask.Rooted && isRopeMoving) return;

            RemoveStatus(mask);
        }

        private void HandleStatManagerStatusBulkCleared()
        {
            if (!IsServer) return;
            networkStatusMask.Value = StatusMask.None;
        }

        private void HandleBuffApplied(BuffType type, float duration)
        {
            if (!IsServer) return;
            BuffMask mask = StatusHelper.BuffTypeToMask(type);
            if (mask != BuffMask.None)
                networkBuffMask.Value |= mask;
        }

        private void HandleBuffRemoved(BuffType type)
        {
            if (!IsServer) return;
            BuffMask mask = StatusHelper.BuffTypeToMask(type);
            if (mask != BuffMask.None)
                networkBuffMask.Value &= ~mask;
        }

        private void HandleDebuffApplied(DebuffType type, float duration)
        {
            if (!IsServer) return;
            DebuffMask mask = StatusHelper.DebuffTypeToMask(type);
            if (mask != DebuffMask.None)
                networkDebuffMask.Value |= mask;
        }

        private void HandleDebuffRemoved(DebuffType type)
        {
            if (!IsServer) return;
            DebuffMask mask = StatusHelper.DebuffTypeToMask(type);
            if (mask != DebuffMask.None)
                networkDebuffMask.Value &= ~mask;
        }

        private void HandleBuffDebuffBulkCleared()
        {
            if (!IsServer) return;
            networkBuffMask.Value = BuffMask.None;
            networkDebuffMask.Value = DebuffMask.None;
        }

        private void SetStateId(CharacterStateId stateId)
        {
            if (!IsServer)
            {
                return;
            }

            if (networkStateId.Value != stateId)
            {
                networkStateId.Value = stateId;
            }
        }

        public void SetTeam(TeamId team)
        {
            if (!IsServer)
            {
                return;
            }

            networkTeamId.Value = team;
        }

        private Vector3 UpdateServerTimers(Vector3 authoritativePos)
        {
            float dt = Time.fixedDeltaTime;

            // No auto-respawn: dead players stay dead until match restart.

            if (invulnerabilityTimer > 0f)
            {
                invulnerabilityTimer -= dt;
                if (invulnerabilityTimer <= 0f)
                {
                    bool statMgrInvulnActive = _statMgr != null && _statMgr.HasStatus(StatusType.Invulnerable);
                    if (!statMgrInvulnActive)
                        RemoveStatus(StatusMask.Invulnerable);
                }
            }

            if (ropeCooldownTimer > 0f)
            {
                ropeCooldownTimer -= dt;
            }

            if (parryWindowTimer > 0f)
            {
                parryWindowTimer -= dt;
            }

            if (parryCooldownTimer > 0f)
            {
                parryCooldownTimer -= dt;
            }

            if (parryStunTimer > 0f)
            {
                parryStunTimer -= dt;
                if (parryStunTimer <= 0f)
                {
                    bool statMgrStunActive = _statMgr != null &&
                        (_statMgr.HasStatus(StatusType.Stunned) || _statMgr.HasStatus(StatusType.HitStun));
                    if (!statMgrStunActive)
                        RemoveStatus(StatusMask.Stunned);
                }
            }

            if (isRopeMoving && networkIsRoping.Value)
            {
                Vector3 current = authoritativePos;
                Vector3 toTarget = ropeTargetPosition - current;
                float distance = toTarget.magnitude;
                float step = ropeSpeed * dt;

                if (distance <= 0.25f || step >= distance)
                {
                    Vector3 arrivalPos = ropeTargetPosition;
                    if (MapBounds3D.Instance != null)
                    {
                        arrivalPos = MapBounds3D.Instance.ResolveServerPosition(arrivalPos, current);
                    }

                    rb.MovePosition(arrivalPos);
                    authoritativePos = arrivalPos;
                    rb.linearVelocity = Vector3.zero;
                    EndRopeAction();
                }
                else
                {
                    Vector3 direction = toTarget / distance;
                    Vector3 next = current + direction * step;
                    if (MapBounds3D.Instance != null)
                    {
                        next = MapBounds3D.Instance.ResolveServerPosition(next, current);
                    }

                    rb.linearVelocity = Vector3.zero;
                    rb.MovePosition(next);
                    authoritativePos = next;
                }
            }

            return authoritativePos;
        }

        #endregion

        #region Client Smoothing

        private void HandlePositionChanged(Vector3 oldPos, Vector3 newPos)
        {
            if (Time.time < suppressInterpolationUntil)
            {
                transform.position = newPos;
            }
        }

        private void HandleYawChanged(float oldYaw, float newYaw)
        {
            // Applied through interpolation in Update.
        }

        private const float PositionInterpSpeed = 30f;
        private const float RotationInterpSpeed = 30f;
        private const float SnapDistance = 8f;

        private void InterpolatePosition()
        {
            if (Time.time < suppressInterpolationUntil)
            {
                return;
            }

            float distance = Vector3.Distance(transform.position, networkPosition.Value);
            if (distance > SnapDistance)
            {
                transform.position = networkPosition.Value;
            }
            else if (distance > 0.001f)
            {
                transform.position = Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * PositionInterpSpeed);
            }
        }

        private void InterpolateRotation()
        {
            Quaternion targetRot = Quaternion.Euler(0f, networkYaw.Value, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * RotationInterpSpeed);
        }

        // ── Owner client-side prediction ────────────────────────
        private const float ReconcileThreshold = 0.5f;
        private const float ReconcileSpeed     = 8f;
        private const float ReconcileSnapDist  = 8f;

        private bool CanPredictMovement =>
            IsOwner && !_isBTControlled
            && networkIsAlive.Value
            && StatusHelper.CanMove(networkStatusMask.Value)
            && !networkIsRoping.Value
            && !IsLocalGameplayBlockedByCardDraft();

        private void PredictPosition()
        {
            if (Time.time < suppressInterpolationUntil) return;
            Vector2 input = cachedMoveInput;
            if (input.sqrMagnitude > 1f) input.Normalize();
            Vector3 velocity = new Vector3(input.x, 0f, input.y) * moveSpeed;
            if (StatusHelper.HasStatus(networkStatusMask.Value, StatusMask.Slowed))
                velocity *= 0.6f;
            Vector3 predicted = transform.position + velocity * Time.deltaTime;
            if (MapBounds3D.Instance != null)
                predicted = MapBounds3D.Instance.ClampToHorizontalBounds(predicted);
            transform.position = predicted;
        }

        private void ReconcileWithServer()
        {
            if (Time.time < suppressInterpolationUntil)
            {
                transform.position = networkPosition.Value;
                return;
            }
            Vector3 serverPos = networkPosition.Value;
            float dist = Vector3.Distance(transform.position, serverPos);
            if (dist > ReconcileSnapDist)
                transform.position = serverPos;
            else if (dist > ReconcileThreshold)
                transform.position = Vector3.Lerp(transform.position, serverPos, Time.deltaTime * ReconcileSpeed);
        }

        #endregion

        #region Camera and Callbacks

        private void SetupTopDownCamera()
        {
            if (!TryResolveOwnerCamera(out Camera cam))
            {
                return;
            }

            ownerCamera = cam;

            TopDownCameraFollow3D follow = cam.GetComponent<TopDownCameraFollow3D>();
            if (follow == null)
            {
                follow = cam.gameObject.AddComponent<TopDownCameraFollow3D>();
            }

            follow.SetTarget(transform);
        }

        private bool TryResolveOwnerCamera(out Camera camera)
        {
            camera = null;

            if (ownerCameraOverride != null && ownerCameraOverride.isActiveAndEnabled)
            {
                camera = ownerCameraOverride;
                return true;
            }

            if (ownerCamera != null && ownerCamera.isActiveAndEnabled)
            {
                camera = ownerCamera;
                return true;
            }

            if (Camera.main != null && Camera.main.isActiveAndEnabled)
            {
                camera = Camera.main;
                return true;
            }

            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera candidate in cameras)
            {
                if (candidate != null && candidate.isActiveAndEnabled)
                {
                    camera = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Allows external local camera manager to bind the owner camera explicitly.
        /// </summary>
        public void SetOwnerCamera(Camera camera)
        {
            if (!IsOwner)
            {
                return;
            }

            ownerCameraOverride = camera;
            ownerCamera = camera;
            SetupTopDownCamera();
        }

        /// <summary>
        /// Returns owner camera for owner-side raycasts (rope, aim, etc.).
        /// </summary>
        public bool TryGetOwnerCamera(out Camera camera)
        {
            if (ownerCamera != null && ownerCamera.isActiveAndEnabled)
            {
                camera = ownerCamera;
                return true;
            }

            if (IsOwner)
            {
                SetupTopDownCamera();
            }

            camera = ownerCamera;
            return camera != null && camera.isActiveAndEnabled;
        }

        private void HandleHPChanged(float oldHP, float newHP)
        {
            OnHPChanged?.Invoke(oldHP, newHP);
        }

        private void HandleStateChanged(CharacterStateId oldState, CharacterStateId newState)
        {
            OnStateChanged?.Invoke(oldState, newState);
        }

        private void HandleStatusChanged(StatusMask oldStatus, StatusMask newStatus)
        {
            OnStatusChanged?.Invoke(newStatus);
        }

        #endregion

        #region Client RPCs

        [Rpc(SendTo.Owner)]
        private void SkillCooldownStartRpc(string skillId)
        {
            if (IsServer) return;
            var exec = GetComponent<SkillExecutor>();
            if (exec != null) exec.MarkUsedNow(skillId);
        }

        public void NotifySkillResetToOwner()
        {
            if (!IsServer) return;
            SkillResetRpc();
        }

        [Rpc(SendTo.Owner)]
        private void SkillResetRpc()
        {
            if (IsServer) return;
            var exec = GetComponent<SkillExecutor>();
            if (exec != null) exec.ResetAll();
            var skillMgr = GetComponent<SkillManager>();
            if (skillMgr != null)
            {
                skillMgr.ClearAll();
                skillMgr.SetAutoCast(true);
                if (_initialSkillSO != null) skillMgr.SetSlot(0, _initialSkillSO);
            }
        }

        public void SetSkillSlotServer(int slotIndex, SkillDefinition skill)
        {
            if (!IsServer) return;
            var skillMgr = GetComponent<SkillManager>();
            if (skillMgr == null) return;
            skillMgr.SetSlot(slotIndex, skill);
            SkillSlotSetRpc(slotIndex, skill != null ? skill.SkillId : "");
        }

        [Rpc(SendTo.Owner)]
        private void SkillSlotSetRpc(int slotIndex, string skillId)
        {
            if (IsServer) return;
            var skillMgr = GetComponent<SkillManager>();
            if (skillMgr == null) return;
            if (string.IsNullOrEmpty(skillId))
            {
                skillMgr.ClearSlot(slotIndex);
                return;
            }
            var registry = GameManager.Instance?.SkillRegistry;
            if (registry == null) return;
            var def = registry.Get(skillId);
            if (def != null) skillMgr.SetSlot(slotIndex, def);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DamageEventRpc(float damage, ulong attackerId, byte damageType)
        {
            // Client-side effects/UI hook point.
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HealEventRpc(float amount)
        {
            // Client-side effects/UI hook point.
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DeathEventRpc(ulong killerId)
        {
            if (!IsOwner || _isBTControlled) return;
            SwitchCameraToAllyOrBoss();
        }

        private void SwitchCameraToAllyOrBoss()
        {
            if (ownerCamera == null) return;
            var follow = ownerCamera.GetComponent<TopDownCameraFollow3D>();
            if (follow == null) return;

            var players = FindObjectsByType<PlayerNetworkController3D>(FindObjectsSortMode.None);
            foreach (var p in players)
            {
                if (p == this || p == null) continue;
                if (p.IsAlive)
                {
                    follow.SetSpectateTarget(p.transform);
                    return;
                }
            }

            if (BossManager.Instance != null && BossManager.Instance.CurrentBoss != null)
                follow.SetSpectateTarget(BossManager.Instance.CurrentBoss.transform);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RespawnEventRpc(Vector3 position)
        {
            transform.position = position;
            if (!IsServer)
            {
                suppressInterpolationUntil = Time.time + Mathf.Max(0f, respawnInterpolationSuppressDuration);
            }

            if (IsOwner && !_isBTControlled)
                SetupTopDownCamera();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RopeResultRpc(bool success, Vector3 target, string reason)
        {
            OnRopeResult?.Invoke(success, reason);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RopeEndRpc()
        {
            OnRopeResult?.Invoke(true, "Ended");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PerkTriggerResultRpc(int triggerId, bool accepted, string detail)
        {
            OnPerkTriggerResult?.Invoke(triggerId, accepted, detail);
        }

        private void UpdateInspectorRuntimeDebug()
        {
            if (!inspectorRuntimeDebug)
            {
                return;
            }

            debugOwnerClientId = OwnerClientId;
            debugIsSpawned = IsSpawned;
            debugIsServer = IsServer;
            debugIsOwner = IsOwner;
            debugIsHost = IsHost;
            debugLocalTick = localTick;
            debugQueuedActionCount = queuedServerActions.Count;
            debugServerMoveInput = serverMoveInput;
            debugServerLookYaw = serverLookYaw;
            debugNetworkPosition = networkPosition.Value;
            debugNetworkYaw = networkYaw.Value;
            debugNetworkHP = networkHP.Value;
            debugNetworkIsAlive = networkIsAlive.Value;
            debugNetworkStateId = networkStateId.Value;
            debugNetworkStatusMask = networkStatusMask.Value;
            debugNetworkTeamId = networkTeamId.Value;
            debugNetworkIsRoping = networkIsRoping.Value;
            debugNetworkRopeTarget = networkRopeTarget.Value;
        }

        private void ClearInspectorRuntimeDebug()
        {
            debugOwnerClientId = 0;
            debugIsSpawned = false;
            debugIsServer = false;
            debugIsOwner = false;
            debugIsHost = false;
            debugLocalTick = 0;
            debugQueuedActionCount = 0;
            debugServerMoveInput = Vector2.zero;
            debugServerLookYaw = 0f;
            debugNetworkPosition = Vector3.zero;
            debugNetworkYaw = 0f;
            debugNetworkHP = 0f;
            debugNetworkIsAlive = false;
            debugNetworkStateId = CharacterStateId.Idle;
            debugNetworkStatusMask = StatusMask.None;
            debugNetworkTeamId = TeamId.None;
            debugNetworkIsRoping = false;
            debugNetworkRopeTarget = Vector3.zero;
        }

        private bool IsLocalGameplayBlockedByCardDraft()
        {
            if (!IsOwner)
            {
                return false;
            }

            if (IsGlobalCardDraftActive()) return true;

            GameStateManager gsm = GameStateManager.Instance;
            if (gsm != null && gsm.CurrentMatchState == MatchState.MatchEnd) return true;

            return false;
        }

        private bool IsServerGameplayBlockedByCardDraft()
        {
            if (!IsServer)
            {
                return false;
            }

            if (IsGlobalCardDraftActive()) return true;

            GameStateManager gsm = GameStateManager.Instance;
            if (gsm != null && gsm.CurrentMatchState == MatchState.MatchEnd) return true;

            return false;
        }

        private bool IsGlobalCardDraftActive()
        {
            GameStateManager gameStateManager = GameStateManager.Instance;
            return gameStateManager != null &&
                   gameStateManager.IsSpawned &&
                   gameStateManager.IsGlobalCardDraftActive;
        }

        private static float NormalizeYaw(float yaw)
        {
            yaw %= 360f;
            if (yaw > 180f) yaw -= 360f;
            else if (yaw < -180f) yaw += 360f;
            return yaw;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        // ICombatant implementation (X3-1, compile bridge — NO behavior change)
        //
        // Phase plan:
        //   X3-1 (this round): interface declaration + read-only properties forward
        //                      to existing PNC3D state. ALL mutating methods are
        //                      warn-once + no-op. SkillManager.Awake's
        //                      GetComponent<ICombatant>() now resolves to PNC3D,
        //                      but no skill execution path mutates PNC3D state
        //                      until X3-2/3.
        //   X3-2: Attach StatManager / StateManager / SkillExecutor / SkillManager
        //         components in PNC3D Awake. Stat tracking begins (parallel to
        //         networkHP, no replace yet).
        //   X3-3: Replace mutation no-ops with StatManager routing
        //         (TakeDamage -> StatManager.ReceiveDamage, RecoverHP -> Heal
        //         wrapper, Shield methods -> StatManager.AddShield etc.).
        //         networkHP becomes server-side mirror of StatManager._currentHP.
        //   X3-4: Position control wiring (Knockback / Pull / MoveBy ->
        //         PNC3D.MovePosition pipeline).
        //   X3-5: SkillProjectile / SkillArea NetworkBehaviour conversion +
        //         IsServer gates.
        //   X3-6: CardManager LEGACY patterns -> GSM RPC/NV routing.
        //   X3-7: SkillManager auto-cast end-to-end smoke test.
        //
        // Per Codex X3-1 critical: this round MUST NOT route TakeDamage /
        // TakeShieldBreakDamage / RecoverHP to existing TakeDamage / Heal
        // implementations. Doing so would let any X2 SkillStep accidentally
        // mutate HP via the new ICombatant path with killerId=0 attribution
        // before X3-3 lands the proper StatManager-routed flow.
        // ══════════════════════════════════════════════════════════════════

        // ── Identity (explicit interface impl — Pascal-case clash with MonoBehaviour) ──
        Transform ICombatant.Transform => transform;
        GameObject ICombatant.GameObject => gameObject;

        // ── State (read-only forwards) ──
        float ICombatant.MaxHP => MaxHP;
        float ICombatant.CurrentHPPercent => _statMgr != null ? _statMgr.GetHPPercent() : (MaxHP > 0f ? CurrentHP / MaxHP : 0f);
        float ICombatant.Shield => networkShield.Value;
        bool  ICombatant.IsAlive => IsAlive;
        bool  ICombatant.IsCasting => _statMgr != null && _statMgr.IsCasting;
        bool  ICombatant.IsParrying => IsParrying;
        float ICombatant.ParryWindow => parryWindowDuration;

        // ── Mutation / query — X3-3 routes through StatManager (single authority).
        // FixedUpdate sync hook mirrors HP/IsAlive changes back to networkHP/networkIsAlive.
        // Skill kill attribution: _lastAttackerId carry-through via attacker ICombatant
        // → PNC3D.OwnerClientId mapping (Codex C-2).

        void ICombatant.TakeDamage(float amount, ICombatant attacker)
        {
            if (!IsServer) return;
            _lastAttackerId = attacker is PlayerNetworkController3D pnc ? pnc.OwnerClientId : 0UL;
            _statMgr?.ReceiveDamage(amount, attacker);
        }

        void ICombatant.TakeShieldBreakDamage(float amount, float multiplier, ICombatant attacker)
        {
            if (!IsServer) return;
            _lastAttackerId = attacker is PlayerNetworkController3D pnc ? pnc.OwnerClientId : 0UL;
            _statMgr?.ReceiveShieldBreakDamage(amount, multiplier, attacker);
        }

        void ICombatant.RecoverHP(float amount) { if (!IsServer) return; _statMgr?.RecoverHP(amount); }
        void ICombatant.AddShield(float amount) { if (!IsServer) return; _statMgr?.AddShield(amount); }

        void ICombatant.ApplyStatus(StatusType type, float duration, float value)
        { if (!IsServer) return; _statMgr?.ApplyStatus(type, duration, value); }

        bool ICombatant.HasStatus(StatusType type) =>
            _statMgr != null && _statMgr.HasStatus(type);

        void ICombatant.ApplyBuff(BuffType type, float duration, float value)
        { if (!IsServer) return; _statMgr?.ApplyBuff(type, duration, value); }

        void ICombatant.ApplyDebuff(DebuffType type, float duration, float value)
        { if (!IsServer) return; _statMgr?.ApplyDebuff(type, duration, value); }

        void ICombatant.RemoveStatuses(CleanseType type, int count)
        { if (!IsServer) return; _statMgr?.RemoveStatuses(type, count); }

        void ICombatant.RemoveBuffs(DispelType type, int count)
        { if (!IsServer) return; _statMgr?.RemoveBuffs(type, count); }

        // X3-4: instant displacement with MapBounds3D clamp + immediate NV mirror.
        // Codex C-1: networkPosition.Value sync inside helper (not deferred to FixedUpdate)
        // to keep position-control contract tick-aligned with movement sync.
        // Duration parameter ignored — coroutine-based smooth motion deferred to X3-N.

        void ICombatant.Knockback(Vector3 direction, float distance)
        {
            if (!IsServer || rb == null) return;
            ApplyPositionOffset(direction, distance);
        }

        void ICombatant.Pull(Vector3 towardPosition, float distance, float duration)
        {
            if (!IsServer || rb == null) return;
            Vector3 dir = (towardPosition - transform.position).normalized;
            ApplyPositionOffset(dir, distance);
            // TODO X3-N: duration-based interpolated pull (coroutine).
        }

        void ICombatant.MoveBy(Vector3 direction, float distance, float duration, MoveType moveType)
        {
            if (!IsServer || rb == null) return;
            ApplyPositionOffset(direction, distance);
            // TODO X3-N: MoveType branching (Dash/Charge/Jump/Rope) + duration motion.
            // MoveType.Rope: route through existing rope queue infrastructure (deferred).
        }

        private void ApplyPositionOffset(Vector3 direction, float distance)
        {
            if (direction.sqrMagnitude < 0.0001f) return;
            Vector3 target = transform.position + direction.normalized * distance;
            if (MapBounds3D.Instance != null)
                target = MapBounds3D.Instance.ResolveServerPosition(target, lastValidatedServerPosition);
            rb.MovePosition(target);
            lastValidatedServerPosition = target;
            networkPosition.Value = target;   // Codex C-1: immediate NV mirror.
        }

        void ICombatant.NotifyParryReward(ParryRewardType rewardType, float value, float duration, ICombatant attacker)
        { if (!IsServer) return; _statMgr?.NotifyParryReward(rewardType, value, duration, attacker); }
    }
}
