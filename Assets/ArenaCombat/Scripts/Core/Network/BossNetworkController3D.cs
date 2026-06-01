// ARCH TAG: TARGET_3D
// ARCH SCOPE: X4-6 BossNetworkController3D — phase tracking on top of X4-4 position control.
// ARCH STATUS: HP_AUTHORITY + POSITION_CONTROL + PHASE_TRACKING
//
// X4-4 wires position control routing (Knockback / Pull / MoveBy →
// ApplyPositionOffset → rb.MovePosition + immediate networkPosition mirror).
// Adds Rigidbody / Collider RequireComponent, networkPosition NetworkVariable,
// and client-side snap apply via OnNetworkSpawn subscribe + HandlePositionChanged
// (Codex X4-4 C-1 — explicit NV demands server write + client apply as one set).
// Boss has no FSM yet, so movement only originates from external IC combatant
// position control calls; client interpolation is deferred to X4-N polish.
//
// FSM (boss behavior pattern, auto-cast enable, SkillManager.SetAutoCast(true))
// remains X4-5. Spawn path / NetworkPrefabs registration remain later.
//
// Dormant contract preservation (carries from X4-1..3):
//   - SkillManager.SetAutoCast(false) kept on (Awake) — re-enable in X4-5 FSM.
//   - NetworkVariables default to networkHP=0f / networkIsAlive=false /
//     networkPosition=zero. Only successful server InitializeStatManager flips
//     HP/alive live; networkPosition is primed from transform.position.
//   - InitializeStatManager requires _bossStatsSO assigned; missing → inert.
//   - All mutating ICombatant methods carry IsServer + alive guard. Position
//     control adds _rb != null guard (Codex X4-4 — dead boss does not move).
//   - ICombatant read properties expose replicated state (NV / SO), never
//     client-local StatManager defaults.

using System.Collections.Generic;
using ArenaCombat.Core.AI;
using ArenaCombat.Core.Combat;
using ArenaCombat.Core.Skill;
using ArenaCombat.Core.State;
using ArenaCombat.Core.Stats;
using Unity.Netcode;
using UnityEngine;

namespace ArenaCombat.Core.Network
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody))]      // X4-4 — server-authoritative position control via MovePosition.
    [RequireComponent(typeof(Collider))]       // X4-4
    [RequireComponent(typeof(StatManager))]    // X4-2
    [RequireComponent(typeof(StateManager))]   // X4-2
    [RequireComponent(typeof(SkillExecutor))]  // X4-2
    [RequireComponent(typeof(SkillManager))]   // X4-2
    public class BossNetworkController3D : NetworkBehaviour, ICombatant
    {
        [Header("=== Stat Authority (X4-3) ===")]
        // Designer-assigned BossStatsSO. Required for InitializeStatManager to
        // produce a live boss; if null the boss stays inert (Codex X4-3 C-1).
        [SerializeField] private BossStatsSO _bossStatsSO;
        [SerializeField] private Transform _visualRoot;

        private StatManager _statMgr;
        private SkillManager _skillMgr;
        private SkillExecutor _skillExec;
        private BossVisualFollower _visualFollower;
        private BossTelegraphDisplay _telegraphDisplay;
        private Rigidbody _rb;

        // X4-8 ML inference flag. (BAL-1 T1B: now diagnostic-only — ML controls movement
        // only; skill selection stays on server auto-cast regardless of this flag.)
        private bool _mlInferenceActive;

        // BAL-1 T1A: cached BossInferenceAgent ref for per-phase SetMoveSpeed calls.
        private BossInferenceAgent _inferenceAgent;

        // X4-3 skill kill attribution carry-through (PNC3D X3-3 mirror).
        private ulong _lastAttackerId;

        // X4-4 last validated server position for MapBounds3D.ResolveServerPosition input.
        private Vector3 _lastValidatedServerPosition;

        // ── C3a-D: Boss AI Pool integration ─────────────────────
        // IsBusy = true while a telegraph is windup. SkillManager.Execute itself is
        // synchronous so does not extend the busy window. Pool manager (Phase E)
        // checks IsBusy before applying a variant; if busy, queues via OnIdleAfterAction.
        public bool IsBusy => _skillMgr != null && _skillMgr.IsTelegraphing;
        public event System.Action OnIdleAfterAction;
        public event System.Action OnPhaseSkillsPopulated;
        private bool _wasBusy;

        // ══════════════════════════════════════════════════════
        // NetworkVariables (X4-3 / X4-4)
        // ══════════════════════════════════════════════════════
        // Defaults are 0 / false / zero; server-side InitializeStatManager flips
        // HP/alive live, OnNetworkSpawn primes networkPosition from transform.

        private readonly NetworkVariable<float> networkHP = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> networkIsAlive = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // X4-4 server-authoritative position. PNC3D-style explicit NV (NetworkTransform
        // intentionally not used — keeps authority pattern consistent with PNC3D and
        // avoids extra component dependency before FSM design lands in X4-5).
        // Codex X4-4 C-1: explicit NV requires explicit client apply path.
        private readonly NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> networkYaw = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // X4-6 phase tracking. Uses the project-wide BossPhase enum (NetworkConstants.cs)
        // — not raw int — to avoid X4-7 FSM interpretation conflicts (Codex X4-6 C-1).
        // Defaults to None; InitializeStatManager flips to Phase1 on successful spawn,
        // HandlePhase advances through Phase2/Phase3/Enrage on HP%, OnBossDefeated → Defeated.
        private readonly NetworkVariable<BossPhase> networkCurrentPhase = new NetworkVariable<BossPhase>(
            BossPhase.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<StatusMask> networkStatusMask = new NetworkVariable<StatusMask>(
            StatusMask.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<BuffMask> networkBuffMask = new NetworkVariable<BuffMask>(
            BuffMask.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<DebuffMask> networkDebuffMask = new NetworkVariable<DebuffMask>(
            DebuffMask.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public BossPhase CurrentPhase => networkCurrentPhase.Value;
        public StatusMask CurrentStatus => networkStatusMask.Value;
        public BuffMask CurrentBuffs => networkBuffMask.Value;
        public DebuffMask CurrentDebuffs => networkDebuffMask.Value;

        public event System.Action<ulong> BossDefeated;

        // Process-wide warn-once gate. Used by OnBossDefeated; X4-4 removes position
        // stub paths so WarnX4Stub helper is gone (Codex X4-4 S-4).
        private static readonly HashSet<string> _warnedOnce = new HashSet<string>();

        // ══════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (TryGetComponent<StatManager>(out var statMgr))
            {
                _statMgr = statMgr;
                statMgr.BindOwner(this);
            }
            if (TryGetComponent<StateManager>(out var stateMgr))
                stateMgr.BindOwner(this);
            // SkillExecutor: no BindOwner API (cooldown dict self-contained).
            if (TryGetComponent<SkillManager>(out var skillManager))
            {
                _skillMgr = skillManager;
                skillManager.SetAutoCast(false);
                skillManager.OnTelegraphStarted += HandleTelegraphStarted;
                skillManager.OnTelegraphCancelled += HandleTelegraphCancelled;
            }
            if (TryGetComponent<SkillExecutor>(out var skillExec))
                _skillExec = skillExec;
            TryGetComponent(out _telegraphDisplay);

            // X4-8 detect ML inference agent on this GameObject. BAL-1 T1A: cache the
            // reference so OnPhaseChanged can call SetMoveSpeed regardless of enabled state.
            TryGetComponent<BossInferenceAgent>(out _inferenceAgent);
            _mlInferenceActive = _inferenceAgent != null && _inferenceAgent.enabled;

            if (TryGetComponent<Rigidbody>(out var rb))
            {
                _rb = rb;
                // X4-4 server-authoritative kinematic-ish setup (PNC3D pattern).
                // isKinematic policy deferred to X4-5 FSM movement decision.
                _rb.useGravity = false;
                _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        private new void OnDestroy()
        {
            base.OnDestroy();
            if (_skillMgr != null)
            {
                _skillMgr.OnTelegraphStarted -= HandleTelegraphStarted;
                _skillMgr.OnTelegraphCancelled -= HandleTelegraphCancelled;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

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

                _lastValidatedServerPosition = transform.position;
                networkPosition.Value = transform.position;
                networkYaw.Value = transform.eulerAngles.y;
                InitializeStatManager();
            }
            else
            {
                transform.position = networkPosition.Value;
                transform.rotation = Quaternion.Euler(0f, networkYaw.Value, 0f);
                networkPosition.OnValueChanged += HandlePositionChanged;
                networkYaw.OnValueChanged += HandleYawChanged;
            }

            if (IsClient)
                networkCurrentPhase.OnValueChanged += HandlePhaseChangedClient;

            if (_skillMgr != null)
                _skillMgr.AimAtTarget = true;

            SetupVisualFollower();
        }

        void SetupVisualFollower()
        {
            if (_visualRoot == null)
            {
                var renderer = GetComponentInChildren<Renderer>();
                if (renderer != null)
                    _visualRoot = renderer.transform.root == transform
                        ? renderer.transform : renderer.transform;
            }
            if (_visualRoot == null || _visualRoot == transform) return;

            _visualFollower = _visualRoot.gameObject.AddComponent<BossVisualFollower>();
            _visualFollower.Initialize(transform);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

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

            if (!IsServer)
            {
                networkPosition.OnValueChanged -= HandlePositionChanged;
                networkYaw.OnValueChanged -= HandleYawChanged;
            }

            if (IsClient)
                networkCurrentPhase.OnValueChanged -= HandlePhaseChangedClient;

            if (_visualFollower != null)
                Destroy(_visualFollower.gameObject);

            // C3a-D: defensive cleanup for pooled boss objects.
            _wasBusy = false;
        }

        private const float InterpSpeed = 30f;
        private Vector3 _interpTarget;
        private bool _interpActive;

        private void HandlePositionChanged(Vector3 oldPosition, Vector3 newPosition)
        {
            _interpTarget = newPosition;
            _interpActive = true;
        }

        private float _interpYaw;
        private bool _yawInterpActive;

        private void HandleYawChanged(float oldYaw, float newYaw)
        {
            _interpYaw = newYaw;
            _yawInterpActive = true;
        }

        private void Update()
        {
            if (IsServer) return;
            if (_interpActive)
                transform.position = Vector3.Lerp(transform.position, _interpTarget, Time.deltaTime * InterpSpeed);
            if (_yawInterpActive)
            {
                float current = transform.eulerAngles.y;
                float lerped = Mathf.LerpAngle(current, _interpYaw, Time.deltaTime * InterpSpeed);
                transform.rotation = Quaternion.Euler(0f, lerped, 0f);
            }
        }

        // X4-3 initialize StatManager authority and prime HP/alive NVs.
        // Returns false (inert) when BossStatsSO unassigned — boss stays at
        // HP=0 / IsAlive=false so stray instances cannot be hit / targeted.
        private bool InitializeStatManager()
        {
            if (_statMgr == null || _bossStatsSO == null)
            {
                Debug.LogWarning(
                    "[BossNetworkController3D] BossStatsSO missing on " +
                    $"{name}; boss remains inert (HP=0, IsAlive=false).", this);
                networkHP.Value = 0f;
                networkIsAlive.Value = false;
                return false;
            }

            _statMgr.Initialize(
                _bossStatsSO,
                _bossStatsSO.BossMaxHP,
                shieldMax:   0f,
                hpRegenRate: 0f,
                parryWindow: 0f,
                CombatantKind.Boss);

            networkHP.Value = _statMgr.GetHP();
            networkIsAlive.Value = _statMgr.IsAlive;
            _statMgr.SetPhaseDamageScale(1f);
            networkCurrentPhase.Value = BossPhase.Phase1;
            PopulateBossSkills(BossPhase.Phase1);
            Debug.Log($"[BNC3D] InitializeStatManager OK - id={NetworkObjectId}, HP={networkHP.Value}, alive={networkIsAlive.Value}, phase={networkCurrentPhase.Value}", this);
            return true;
        }

        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || _statMgr == null || !networkIsAlive.Value)
                return;

            _statMgr.Tick(Time.fixedDeltaTime);

            float statHP = _statMgr.GetHP();
            if (!Mathf.Approximately(networkHP.Value, statHP))
                networkHP.Value = statHP;

            Vector3 pos = transform.position;
            if ((pos - networkPosition.Value).sqrMagnitude > 0.0001f)
                networkPosition.Value = pos;
            float yaw = transform.eulerAngles.y;
            if (!Mathf.Approximately(networkYaw.Value, yaw))
                networkYaw.Value = yaw;

            HandlePhase();  // X4-6 — phase tracking based on HP% (must run before defeat check
                            //        so OnBossDefeated can overwrite phase to Defeated).

            if (networkIsAlive.Value && !_statMgr.IsAlive)
            {
                // C3a-D: clear busy state so the death-cancel of telegraph does not
                // surface as a spurious OnIdleAfterAction event next tick.
                _wasBusy = false;
                OnBossDefeated(_lastAttackerId);
                return;
            }

            // C3a-D: busy→idle edge detection for Phase E deferred swap queue.
            bool busyNow = IsBusy;
            if (_wasBusy && !busyNow) OnIdleAfterAction?.Invoke();
            _wasBusy = busyNow;
        }

        // X4-6 phase tracking (Buildup BossController.HandlePhase / OnPhaseChanged port).
        // Designer-set BossStatsSO.BossPhaseThresholds is a descending HP-ratio array
        // (e.g. [0.7, 0.4, 0.1]). enum mapping (Codex X4-6 S-2):
        //   index 0 crossed → Phase2
        //   index 1 crossed → Phase3
        //   index 2 crossed → Enrage
        // thresholds beyond index 2 cannot be represented by BossPhase enum
        // (Phase1..Enrage = 4 combat phases). Extras are ignored with warn-once
        // (Codex X4-6 S-3).
        private const int MaxPhaseThresholds = 3;
        private static readonly BossPhase[] _thresholdPhases =
            { BossPhase.Phase2, BossPhase.Phase3, BossPhase.Enrage };

        private void HandlePhase()
        {
            if (_statMgr == null || _bossStatsSO == null) return;
            var thresholds = _bossStatsSO.BossPhaseThresholds;
            if (thresholds == null || thresholds.Length == 0) return;

            if (thresholds.Length > MaxPhaseThresholds && _warnedOnce.Add("PhaseThresholdsOverflow"))
            {
                Debug.LogWarning(
                    $"[BossNetworkController3D] BossPhaseThresholds has {thresholds.Length} entries " +
                    $"but BossPhase enum only covers {MaxPhaseThresholds} combat transitions " +
                    "(Phase2 / Phase3 / Enrage). Extra entries ignored.", this);
            }

            float hpRatio = _statMgr.GetHPPercent();
            BossPhase oldPhase = networkCurrentPhase.Value;
            BossPhase newPhase = oldPhase;

            int max = Mathf.Min(thresholds.Length, MaxPhaseThresholds);
            for (int i = 0; i < max; i++)
            {
                BossPhase candidate = _thresholdPhases[i];
                if (hpRatio <= thresholds[i] && (int)newPhase < (int)candidate)
                    newPhase = candidate;
            }

            if (newPhase != oldPhase)
            {
                networkCurrentPhase.Value = newPhase;
                OnPhaseChanged(oldPhase, newPhase);
            }
        }

        private void OnPhaseChanged(BossPhase oldPhase, BossPhase newPhase)
        {
            Debug.Log($"[BossNetworkController3D] Phase {oldPhase} → {newPhase} (HP {_statMgr.GetHPPercent() * 100f:F1}%)", this);
            PopulateBossSkills(newPhase);
            OnPhaseSkillsPopulated?.Invoke();

            if (_inferenceAgent != null)
            {
                float phaseSpeed = newPhase switch
                {
                    BossPhase.Phase1 => 8.4f,
                    BossPhase.Phase2 => 8.4f,
                    BossPhase.Phase3 => 9.2f,
                    BossPhase.Enrage => 10.1f,
                    _ => 8.4f,
                };
                _inferenceAgent.SetMoveSpeed(phaseSpeed);
            }

            if (_statMgr != null)
            {
                float dmgScale = newPhase switch
                {
                    BossPhase.Phase1 => 1.0f,
                    BossPhase.Phase2 => 1.08f,
                    BossPhase.Phase3 => 1.16f,
                    BossPhase.Enrage => 1.25f,
                    _ => 1.0f,
                };
                _statMgr.SetPhaseDamageScale(dmgScale);
            }
        }

        private void HandlePhaseChangedClient(BossPhase oldPhase, BossPhase newPhase)
        {
            if (oldPhase == BossPhase.None) return;
            if (newPhase == BossPhase.None || newPhase == BossPhase.Defeated) return;
            _telegraphDisplay?.ShowPhaseTransition(newPhase);
        }

        private void PopulateBossSkills(BossPhase phase)
        {
            if (_skillMgr == null) return;

            var registry = GameManager.Instance != null ? GameManager.Instance.SkillRegistry : null;
            if (registry == null)
            {
                Debug.LogWarning("[BossNetworkController3D] SkillRegistry unavailable — boss has no skills.", this);
                return;
            }

            var bossSkills = registry.GetByRoleTag(SkillRoleTag.Boss);
            if (bossSkills.Count == 0)
            {
                Debug.LogWarning("[BossNetworkController3D] No Boss-tagged skills in registry.", this);
                return;
            }

            _skillMgr.ClearAll();
            int slotCount = Mathf.Min(bossSkills.Count, _skillMgr.MaxSlots);
            for (int i = 0; i < slotCount; i++)
                _skillMgr.SetSlot(i, bossSkills[i]);

            _skillMgr.RoundRobinEnabled = (phase == BossPhase.Enrage);

            if (_skillExec != null)
            {
                _skillExec.CooldownScale = phase switch
                {
                    BossPhase.Phase1 => 1.0f,
                    BossPhase.Phase2 => 0.85f,
                    BossPhase.Phase3 => 0.7f,
                    BossPhase.Enrage => 0.5f,
                    _ => 1.0f,
                };
            }

            _skillMgr.TelegraphScale = phase switch
            {
                BossPhase.Phase1 => 1.0f,
                BossPhase.Phase2 => 0.9f,
                BossPhase.Phase3 => 0.78f,
                BossPhase.Enrage => 0.7f,
                _ => 1.0f,
            };

            _skillMgr.SetAutoCast(true);

            _skillMgr.UseAdaptiveWeights = (BossAdaptiveWeights.Instance != null);
        }

        // C3a-D: Boss AI variant application — called by BossAIPoolManager (Phase E)
        // on archetype-pair lookup result. Swaps slot pool, slot weights (Phase F),
        // and cooldown scale. Coexists with PopulateBossSkills(phase) — both call
        // ClearAll + SetSlot; last writer wins. Pool manager re-applies after phase
        // transitions to recover from PopulateBossSkills clobbering.
        public void ApplyAIVariant(BossAIDefinition def)
        {
            if (!IsServer || !IsSpawned || !networkIsAlive.Value || _skillMgr == null || def == null)
                return;

            _skillMgr.ClearAll();
            if (def.skillSlots != null)
            {
                int slotCount = Mathf.Min(def.skillSlots.Length, _skillMgr.MaxSlots);
                for (int i = 0; i < slotCount; i++)
                    _skillMgr.SetSlot(i, def.skillSlots[i]);
            }

            // C3a-F: push variant's per-slot weight bias to SkillManager (multiplied
            // into BossAdaptiveWeights.ComputeWeight during adaptive auto-cast).
            _skillMgr.SetSlotWeights(def.slotWeights);

            // Variant SOs do not carry round-robin state; reset deterministically so a
            // later variant does not inherit Enrage's round-robin from PopulateBossSkills.
            _skillMgr.RoundRobinEnabled = false;

            if (_skillExec != null)
                _skillExec.CooldownScale = def.cooldownScale;

            // BAL-1 T1B: auto-cast 항상 ON (ML은 이동만 담당).
            _skillMgr.SetAutoCast(true);

            _skillMgr.UseAdaptiveWeights = (BossAdaptiveWeights.Instance != null);

            string label = string.IsNullOrEmpty(def.variantName) ? def.name : def.variantName;
            Debug.Log($"[BossAI] Variant applied: {label} (pair={def.playerType1}+{def.playerType2})", this);
        }

        // Re-apply variant skill slots + weights only, preserving phase-controlled
        // settings (CooldownScale, TelegraphScale, RoundRobinEnabled). Called by
        // BossAIPoolManager after PopulateBossSkills clobbers variant during phase transition.
        public void ApplyAIVariantSlots(BossAIDefinition def)
        {
            if (!IsServer || !IsSpawned || !networkIsAlive.Value || _skillMgr == null || def == null)
                return;

            _skillMgr.ClearAll();
            if (def.skillSlots != null)
            {
                int slotCount = Mathf.Min(def.skillSlots.Length, _skillMgr.MaxSlots);
                for (int i = 0; i < slotCount; i++)
                    _skillMgr.SetSlot(i, def.skillSlots[i]);
            }

            _skillMgr.SetSlotWeights(def.slotWeights);
            _skillMgr.SetAutoCast(true);
            _skillMgr.UseAdaptiveWeights = (BossAdaptiveWeights.Instance != null);

            string label = string.IsNullOrEmpty(def.variantName) ? def.name : def.variantName;
            Debug.Log($"[BossAI] Variant slots re-applied after phase change: {label}", this);
        }

        public bool SetBossSkillSlot(int slotIndex, SkillDefinition skill)
        {
            if (!IsServer || !IsSpawned || !networkIsAlive.Value || _skillMgr == null)
                return false;
            return _skillMgr.SetSlot(slotIndex, skill);
        }

        // X4-8 ML Agent movement entry point. Server-only: MovePosition + NV mirror.
        public void ApplyMLPosition(Vector3 targetPos)
        {
            if (!IsServer || !networkIsAlive.Value || _rb == null) return;
            if (MapBounds3D.Instance != null)
                targetPos = MapBounds3D.Instance.ResolveServerPosition(targetPos, _lastValidatedServerPosition);
            _rb.MovePosition(targetPos);
            _lastValidatedServerPosition = targetPos;
            networkPosition.Value = targetPos;
        }

        // X4-4 position control helper (PNC3D X3-4 mirror). Server-authoritative:
        // apply MapBounds3D resolve → MovePosition → immediate NV mirror.
        private void ApplyPositionOffset(Vector3 direction, float distance)
        {
            if (direction.sqrMagnitude < 0.0001f) return;
            Vector3 target = transform.position + direction.normalized * distance;
            if (MapBounds3D.Instance != null)
                target = MapBounds3D.Instance.ResolveServerPosition(target, _lastValidatedServerPosition);
            _rb.MovePosition(target);
            _lastValidatedServerPosition = target;
            networkPosition.Value = target;
        }

        // ══════════════════════════════════════════════════════
        // Telegraph RPC (B4-1)
        // ══════════════════════════════════════════════════════

        private void HandleTelegraphStarted(SkillDefinition skill, SkillContext ctx, float duration)
        {
            if (!IsServer || !IsSpawned) return;
            TelegraphStartedRpc(
                skill.SkillId ?? string.Empty,
                ctx.CastPosition,
                ctx.CastDirection,
                duration,
                (int)skill.TargetType);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TelegraphStartedRpc(string skillId, Vector3 position, Vector3 direction, float duration, int targetType)
        {
            if (_telegraphDisplay != null)
                _telegraphDisplay.Show(position, direction, duration, (TargetType)targetType);
        }

        private void HandleTelegraphCancelled()
        {
            if (!IsServer || !IsSpawned) return;
            TelegraphCancelledRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TelegraphCancelledRpc()
        {
            if (_telegraphDisplay != null)
                _telegraphDisplay.Hide();
        }

        public void AddStatus(StatusMask status)
        {
            if (!IsServer) return;
            networkStatusMask.Value = StatusHelper.AddStatus(networkStatusMask.Value, status);
        }

        public void RemoveStatus(StatusMask status)
        {
            if (!IsServer) return;
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
                if (otherStunActive) return;
            }

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

        private void OnBossDefeated(ulong attackerId)
        {
            networkIsAlive.Value = false;
            if (_statMgr != null) _statMgr.ClearAllEffects();
            networkStatusMask.Value = StatusMask.None;
            networkBuffMask.Value = BuffMask.None;
            networkDebuffMask.Value = DebuffMask.None;
            networkCurrentPhase.Value = BossPhase.Defeated;
            if (_skillMgr != null) _skillMgr.CancelTelegraph();
            Debug.Log($"[BossNetworkController3D] Boss defeated by clientId={attackerId}.", this);
            BossDefeated?.Invoke(attackerId);
        }

        // ══════════════════════════════════════════════════════
        // ICombatant: Identity
        // ══════════════════════════════════════════════════════
        Transform  ICombatant.Transform  => transform;
        GameObject ICombatant.GameObject => gameObject;

        // ══════════════════════════════════════════════════════
        // ICombatant: State (read-only) — replicated reads (Codex X4-3 C-2)
        // ══════════════════════════════════════════════════════
        float ICombatant.MaxHP =>
            _bossStatsSO != null
                ? _bossStatsSO.BossMaxHP
                : (_statMgr != null ? _statMgr.GetMaxHP() : 0f);

        float ICombatant.CurrentHPPercent
        {
            get
            {
                float max = ((ICombatant)this).MaxHP;
                return max > 0f ? networkHP.Value / max : 0f;
            }
        }

        float ICombatant.Shield => 0f;

        bool ICombatant.IsAlive => networkIsAlive.Value;

        bool ICombatant.IsCasting =>
            IsServer && _statMgr != null && networkIsAlive.Value && _statMgr.IsCasting;

        bool  ICombatant.IsParrying  => false;
        float ICombatant.ParryWindow => 0f;

        // ══════════════════════════════════════════════════════
        // ICombatant: Damage / heal — IsServer guarded (Codex X4-3 C-3)
        // ══════════════════════════════════════════════════════

        void ICombatant.TakeDamage(float amount, ICombatant attacker)
        {
            if (!IsServer || _statMgr == null || !networkIsAlive.Value)
            {
                Debug.LogWarning($"[BNC3D:TakeDamage] REJECTED: IsServer={IsServer}, statMgr={(_statMgr != null)}, alive={networkIsAlive.Value}, amount={amount}");
                return;
            }
            _lastAttackerId = attacker is PlayerNetworkController3D pnc
                ? pnc.OwnerClientId
                : 0UL;
            _statMgr.ReceiveDamage(amount, attacker);
        }

        void ICombatant.TakeShieldBreakDamage(float amount, float multiplier, ICombatant attacker)
        {
            if (!IsServer || _statMgr == null || !networkIsAlive.Value) return;
            _lastAttackerId = attacker is PlayerNetworkController3D pnc
                ? pnc.OwnerClientId
                : 0UL;
            _statMgr.ReceiveShieldBreakDamage(amount, multiplier, attacker);
        }

        void ICombatant.RecoverHP(float amount)
        {
            if (!IsServer || _statMgr == null || !networkIsAlive.Value) return;
            _statMgr.RecoverHP(amount);
        }

        void ICombatant.AddShield(float amount)
        {
            if (!IsServer || _statMgr == null || !networkIsAlive.Value) return;
            _statMgr.AddShield(amount);
        }

        // ══════════════════════════════════════════════════════
        // ICombatant: Status / Buff / Debuff — IsServer guarded
        // ══════════════════════════════════════════════════════

        void ICombatant.ApplyStatus(StatusType type, float duration, float value)
        {
            if (!IsServer || _statMgr == null || !networkIsAlive.Value) return;
            _statMgr.ApplyStatus(type, duration, value);
        }

        bool ICombatant.HasStatus(StatusType type)
            => _statMgr != null && _statMgr.HasStatus(type);

        void ICombatant.ApplyBuff(BuffType type, float duration, float value)
        {
            if (!IsServer || _statMgr == null || !networkIsAlive.Value) return;
            _statMgr.ApplyBuff(type, duration, value);
        }

        void ICombatant.ApplyDebuff(DebuffType type, float duration, float value)
        {
            if (!IsServer || _statMgr == null || !networkIsAlive.Value) return;
            _statMgr.ApplyDebuff(type, duration, value);
        }

        void ICombatant.RemoveStatuses(CleanseType type, int count)
        {
            if (!IsServer || _statMgr == null || !networkIsAlive.Value) return;
            _statMgr.RemoveStatuses(type, count);
        }

        void ICombatant.RemoveBuffs(DispelType type, int count)
        {
            if (!IsServer || _statMgr == null || !networkIsAlive.Value) return;
            _statMgr.RemoveBuffs(type, count);
        }

        // ══════════════════════════════════════════════════════
        // ICombatant: Positional control — X4-4 (server-authoritative routing)
        // ══════════════════════════════════════════════════════
        // Codex X4-4 S-5: dead boss does not move; _rb null guard covers prefab
        // misconfiguration even though RequireComponent should prevent it.

        void ICombatant.Knockback(Vector3 direction, float distance)
        {
            if (!IsServer || !networkIsAlive.Value || _rb == null) return;
            ApplyPositionOffset(direction, distance);
        }

        void ICombatant.Pull(Vector3 towardPosition, float distance, float duration)
        {
            if (!IsServer || !networkIsAlive.Value || _rb == null) return;
            Vector3 dir = (towardPosition - transform.position).normalized;
            ApplyPositionOffset(dir, distance);
            // TODO X4-N: duration-based interpolated pull (coroutine).
        }

        void ICombatant.MoveBy(Vector3 direction, float distance, float duration, MoveType moveType)
        {
            if (!IsServer || !networkIsAlive.Value || _rb == null) return;
            ApplyPositionOffset(direction, distance);
            // TODO X4-N: MoveType branching (Dash/Charge/Jump/Rope) + duration motion.
        }

        // ══════════════════════════════════════════════════════
        // ICombatant: Parry reward delivery — IsServer guarded
        // ══════════════════════════════════════════════════════

        void ICombatant.NotifyParryReward(ParryRewardType rewardType, float value, float duration, ICombatant attacker)
        {
            if (!IsServer || _statMgr == null || !networkIsAlive.Value) return;
            _statMgr.NotifyParryReward(rewardType, value, duration, attacker);
        }
    }
}
