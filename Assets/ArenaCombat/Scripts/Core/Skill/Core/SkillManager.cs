using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using ArenaCombat.Core;
using ArenaCombat.Core.AI;
using ArenaCombat.Core.Combat;
using ArenaCombat.Core.Network;
using ArenaCombat.Core.Stats;
using ArenaCombat.Core.State;

// ══════════════════════════════════════════════════════════════════
// SkillManager — Auto-cast skill manager (per-entity component).
//
// Slot index = priority (0 = highest). Each frame checks CanCast for every
// slot in order and fires the first eligible one. Manual TryExecute API
// retained for ML training mode (auto-cast disabled, ML policy picks slot).
//
// Slot assignment:
//   Inspector Slots array holds SkillDefinition SOs directly.
//   Element 0 = highest priority. Null slots are skipped.
//   Card draft wiring lands at X2-12.
//
// CanCast (7 conditions):
//   1) Executor cooldown elapsed
//   2) IsAlive
//   3) !IsCasting
//   4) !IsParrying
//   5) !HasStatus(Stunned/HitStun/Silence/Rooted)
//   6) Target exists + alive (skipped for Self type)
//   7) Distance <= skill.Range (skipped for Self type)
//
// SERVER AUTHORITY GATE (X2-11):
//   Update() returns early when NetworkManager.Singleton != null AND
//   !IsServer. Allows offline / pre-network editor testing (NM null path).
//   X3 wiring may extract a server-tick AutoCastTick to call from a host
//   FixedUpdate driver; until then Update() runs per-frame on the server.
//
// Buildup origin: Assets/Scripts/Skill/Core/SkillManager.cs (X2-11 import).
// ══════════════════════════════════════════════════════════════════

namespace ArenaCombat.Core.Skill
{
    [RequireComponent(typeof(SkillExecutor))]
    public class SkillManager : MonoBehaviour
    {
        public const int SlotCount = 5;

        [Header("Slots (index 0 = priority, null = empty)")]
        [SerializeField] private SkillDefinition[] _slots = new SkillDefinition[SlotCount];

        // C3a-F: per-slot multiplicative bias applied to BossAdaptiveWeights output in
        // the adaptive auto-cast branch. Defaults to all 1f (no bias). Reset on ClearAll
        // so PopulateBossSkills (which does not push variant weights) starts neutral.
        private readonly float[] _slotWeights = InitDefaultWeights();
        private static float[] InitDefaultWeights()
        {
            var arr = new float[SlotCount];
            for (int i = 0; i < SlotCount; i++) arr[i] = 1f;
            return arr;
        }

        [Header("Settings")]
        [SerializeField] private bool _autoCastEnabled = true;
        [SerializeField] private bool _logAutoCast = false;
        [SerializeField] private bool _roundRobinEnabled = false;

        private int _roundRobinStart = 0;
        private bool _useAdaptiveWeights;
        public bool UseAdaptiveWeights { get => _useAdaptiveWeights; set => _useAdaptiveWeights = value; }

        private bool _aimAtTarget;
        public bool AimAtTarget { get => _aimAtTarget; set => _aimAtTarget = value; }

        [Header("References")]
        [SerializeField] private StatManager _statManager;
        [SerializeField] private StateManager _stateManager;
        [SerializeField] private GameManager _gameManager;

        // Owner = the ICombatant this SkillManager casts on behalf of.
        // M-1 deviation from Buildup: replaced [SerializeField] PlayerController
        // _owner with runtime GetComponent<ICombatant>(). PNC3D / BossNetworkController3D
        // will implement ICombatant in X3+ wiring; auto-detect resolves on Awake.
        private ICombatant _owner;

        private SkillExecutor _executor;

        // ── External queries ────────────────────────────────────
        public int  MaxSlots        => SlotCount;
        public bool AutoCastEnabled => _autoCastEnabled;
        public bool RoundRobinEnabled
        {
            get => _roundRobinEnabled;
            set => _roundRobinEnabled = value;
        }
        public IReadOnlyList<SkillDefinition> Slots => _slots;
        public bool IsTelegraphing => _isTelegraphing;
        public float TelegraphScale { get; set; } = 1f;

        private struct EligibleSkill
        {
            public int Slot;
            public SkillContext Ctx;
            public float Weight;
        }

        // ── Telegraph state (B4-1) ─────────────────────────────
        private bool _isTelegraphing;
        private float _telegraphTimer;
        private int _pendingSlot;
        private SkillDefinition _pendingSkill;

        public event System.Action<SkillDefinition, SkillContext, float> OnTelegraphStarted;
        public event System.Action OnTelegraphCancelled;

        // ══════════════════════════════════════════════════════════
        // Initialization
        // ══════════════════════════════════════════════════════════

        private void Awake()
        {
            _executor = GetComponent<SkillExecutor>();
            if (_statManager  == null) _statManager  = GetComponent<StatManager>();
            if (_stateManager == null) _stateManager = GetComponent<StateManager>();
            if (_owner        == null) _owner        = GetComponent<ICombatant>();
            if (_gameManager  == null) _gameManager  = GameManager.Instance;

            // Force-fix array length (defensive against Inspector size edits).
            if (_slots == null || _slots.Length != SlotCount)
            {
                var fixedSlots = new SkillDefinition[SlotCount];
                if (_slots != null)
                {
                    int copy = Mathf.Min(_slots.Length, SlotCount);
                    for (int i = 0; i < copy; i++) fixedSlots[i] = _slots[i];
                }
                _slots = fixedSlots;
            }
        }

        private void OnValidate()
        {
            // Keep array size consistent across Inspector edits.
            if (_slots == null || _slots.Length != SlotCount)
            {
                System.Array.Resize(ref _slots, SlotCount);
            }
        }

        private void Update()
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
                return;

            if (GameStateManager.Instance != null &&
                (GameStateManager.Instance.IsGlobalCardDraftActive ||
                 GameStateManager.Instance.CurrentMatchState == MatchState.MatchEnd))
                return;

            if (!_autoCastEnabled || _statManager == null || !_statManager.IsAlive)
            {
                if (_isTelegraphing) CancelTelegraph();
                if (_logAutoCast && Time.frameCount % 300 == 0)
                    Debug.LogWarning($"[SkillManager] Update blocked: autoCast={_autoCastEnabled}, statMgr={_statManager != null}, alive={_statManager?.IsAlive}");
                return;
            }

            // ── Telegraph in progress: count down, then execute ──
            if (_isTelegraphing)
            {
                _telegraphTimer -= Time.deltaTime;
                if (_telegraphTimer <= 0f)
                    CompleteTelegraph();
                return;
            }

            // ── Auto-cast skill selection ──
            int start = _roundRobinEnabled ? _roundRobinStart : 0;
            int count = _slots.Length;

            if (_useAdaptiveWeights && BossAdaptiveWeights.Instance != null)
            {
                var eligible = new System.Collections.Generic.List<EligibleSkill>(count);
                float totalWeight = 0f;
                for (int n = 0; n < count; n++)
                {
                    int i = (start + n) % count;
                    if (_slots[i] == null) continue;
                    if (!CanCast(_slots[i], out var ctx)) continue;
                    float w = BossAdaptiveWeights.Instance.ComputeWeight(_slots[i]) * GetSlotWeight(i);
                    eligible.Add(new EligibleSkill { Slot = i, Ctx = ctx, Weight = w });
                    totalWeight += w;
                }
                if (eligible.Count > 0 && totalWeight > 0f)
                {
                    float roll = Random.value * totalWeight;
                    float cumulative = 0f;
                    for (int e = 0; e < eligible.Count; e++)
                    {
                        cumulative += eligible[e].Weight;
                        if (roll <= cumulative)
                        {
                            ExecuteOrTelegraph(eligible[e].Slot, eligible[e].Ctx, $"Adaptive w={eligible[e].Weight:F2}");
                            break;
                        }
                    }
                }
                return;
            }

            for (int n = 0; n < count; n++)
            {
                int i = (start + n) % count;
                if (_slots[i] == null) continue;
                if (!CanCast(_slots[i], out var ctx)) continue;

                ExecuteOrTelegraph(i, ctx, null);
                break;
            }
        }

        // ── Unified execution: telegraph delay or instant fire (B4-1, Codex S-1) ──
        private void ExecuteOrTelegraph(int slot, SkillContext ctx, string logTag)
        {
            var skill = _slots[slot];
            if (skill == null) return;

            if (skill.TelegraphDuration > 0f)
            {
                EnterTelegraph(slot, skill, ctx, skill.TelegraphDuration * TelegraphScale);
                return;
            }

            if (_stateManager != null) _stateManager.NotifyCastStart();
            bool fired = _executor.Execute(skill, ctx);
            if (_stateManager != null) _stateManager.NotifyCastEnd();

            if (fired)
            {
                if (_logAutoCast)
                    Debug.Log($"[AutoCast{(logTag != null ? ":" + logTag : "")}] <b>slot[{slot}]</b> {skill.DisplayName} fired | {name}");
                if (_roundRobinEnabled)
                    _roundRobinStart = (slot + 1) % _slots.Length;
            }
        }

        private void EnterTelegraph(int slot, SkillDefinition skill, SkillContext ctx, float duration)
        {
            _isTelegraphing = true;
            _telegraphTimer = duration;
            _pendingSlot = slot;
            _pendingSkill = skill;

            if (_logAutoCast)
                Debug.Log($"[Telegraph] <b>slot[{slot}]</b> {skill.DisplayName} windup {duration:F2}s | {name}");

            OnTelegraphStarted?.Invoke(skill, ctx, duration);
        }

        private void CompleteTelegraph()
        {
            _isTelegraphing = false;
            var skill = _pendingSkill;
            int slot = _pendingSlot;
            _pendingSkill = null;

            if (skill == null || slot < 0 || slot >= _slots.Length || _slots[slot] != skill)
                return;

            ICombatant target = skill.TargetType != TargetType.Self ? FindNearestTarget() : null;
            var ctx = BuildSkillContext(target);

            if (_stateManager != null) _stateManager.NotifyCastStart();
            bool fired = _executor.Execute(skill, ctx);
            if (_stateManager != null) _stateManager.NotifyCastEnd();

            if (fired)
            {
                if (_logAutoCast)
                    Debug.Log($"[Telegraph:Complete] <b>slot[{slot}]</b> {skill.DisplayName} fired | {name}");
                if (_roundRobinEnabled)
                    _roundRobinStart = (slot + 1) % _slots.Length;
            }
        }

        public void CancelTelegraph()
        {
            if (!_isTelegraphing) return;
            if (_logAutoCast && _pendingSkill != null)
                Debug.Log($"[Telegraph:Cancel] {_pendingSkill.DisplayName} cancelled | {name}");
            _isTelegraphing = false;
            _telegraphTimer = 0f;
            _pendingSkill = null;
            OnTelegraphCancelled?.Invoke();
        }

        // ══════════════════════════════════════════════════════════
        // Slot management (runtime mutation API — used by card draft, perks)
        // ══════════════════════════════════════════════════════════

        public bool SetSlot(int index, SkillDefinition skill)
        {
            if (index < 0 || index >= SlotCount) return false;
            if (_isTelegraphing && index == _pendingSlot) CancelTelegraph();
            _slots[index] = skill;
            return true;
        }

        public void ClearSlot(int index)
        {
            if (index < 0 || index >= SlotCount) return;
            _slots[index] = null;
        }

        public void ClearAll()
        {
            CancelTelegraph();
            for (int i = 0; i < _slots.Length; i++) _slots[i] = null;
            // C3a-F: reset slot weights so subsequent populations without explicit
            // SetSlotWeights call (e.g. PopulateBossSkills) start at neutral 1.0.
            for (int i = 0; i < _slotWeights.Length; i++) _slotWeights[i] = 1f;
        }

        // C3a-F: variant slot-weight bias. NaN/Infinity/non-positive fall back to 1f.
        // Passing null resets all slots to neutral (1f).
        public void SetSlotWeights(float[] weights)
        {
            if (weights == null)
            {
                for (int i = 0; i < SlotCount; i++) _slotWeights[i] = 1f;
                return;
            }
            int copy = Mathf.Min(weights.Length, SlotCount);
            for (int i = 0; i < copy; i++)
            {
                float w = weights[i];
                if (float.IsNaN(w) || float.IsInfinity(w) || w <= 0f) w = 1f;
                _slotWeights[i] = w;
            }
            for (int i = copy; i < SlotCount; i++) _slotWeights[i] = 1f;
        }

        public float GetSlotWeight(int slot)
            => (slot >= 0 && slot < _slotWeights.Length) ? _slotWeights[slot] : 1f;

        public void SetAutoCast(bool enabled) => _autoCastEnabled = enabled;

        // ══════════════════════════════════════════════════════════
        // Manual fire / query
        // ══════════════════════════════════════════════════════════

        public bool TryExecute(int slotIndex, SkillContext ctx)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Length || _slots[slotIndex] == null) return false;
            return _executor.Execute(_slots[slotIndex], ctx);
        }

        public bool CanUse(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Length || _slots[slotIndex] == null) return false;
            return _executor.CanUse(_slots[slotIndex]);
        }

        public float GetRemainingCooldown(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Length || _slots[slotIndex] == null) return 0f;
            return _executor.GetRemainingCooldown(_slots[slotIndex]);
        }

        public void ResetCooldown(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Length || _slots[slotIndex] == null) return;
            _executor.ResetCooldown(_slots[slotIndex].SkillId);
        }

        // ══════════════════════════════════════════════════════════
        // CanCast — full 7-condition check + ctx build
        // ══════════════════════════════════════════════════════════

        private bool CanCast(SkillDefinition skill, out SkillContext ctx)
        {
            ctx = null;
            bool logThisFrame = _logAutoCast && Time.frameCount % 300 == 0;
            if (skill == null || !skill.IsReady)
            {
                if (logThisFrame) Debug.LogWarning($"[CanCast] {skill?.SkillId ?? "null"} failed: IsReady={skill?.IsReady}");
                return false;
            }

            if (!_executor.CanUse(skill))
                return false;

            if (_stateManager != null && !_stateManager.CanCast)
            {
                if (logThisFrame) Debug.LogWarning($"[CanCast] {skill.SkillId} failed: CanCast=false (State={_stateManager.CurrentState})");
                return false;
            }

            ICombatant target = null;
            if (skill.TargetType != TargetType.Self)
            {
                target = FindNearestTarget();
                if (target == null || !target.IsAlive)
                {
                    if (logThisFrame) Debug.LogWarning($"[CanCast] {skill.SkillId} failed: no target (candidates={(_gameManager != null ? (_statManager != null && _statManager.Kind == CombatantKind.Boss ? _gameManager.Players.Count : _gameManager.Bosses.Count) : -1)})");
                    return false;
                }

                float dist = Vector3.Distance(transform.position, target.Transform.position);
                if (dist > skill.Range)
                {
                    if (logThisFrame) Debug.LogWarning($"[CanCast] {skill.SkillId} failed: out of range ({dist:F1}m > {skill.Range}m)");
                    return false;
                }
            }

            ctx = BuildSkillContext(target);

            // RuntimeCondition check — failure skips slot without consuming cooldown.
            if (skill.RuntimeCondition != null && !skill.RuntimeCondition(ctx))
                return false;

            return true;
        }

        // ══════════════════════════════════════════════════════════
        // SkillContext build
        // ══════════════════════════════════════════════════════════

        public SkillContext BuildSkillContext(ICombatant target)
        {
            var ctx = new SkillContext
            {
                Caster         = _owner,
                PrimaryTarget  = target,
                CastPosition   = transform.position,
                CastDirection  = (_aimAtTarget && target?.Transform != null)
                    ? AimDirection(target.Transform.position)
                    : transform.forward,
                ParryInputTime = (_statManager != null && _statManager.IsParrying) ? Time.time : 0f,
                DamageScale    = _statManager != null
                    ? _statManager.GetPhaseDamageScale() * _statManager.GetDamageUpMultiplier()
                    : 1f,
            };
            ctx.RefreshSnapshot();
            return ctx;
        }

        Vector3 AimDirection(Vector3 targetPos)
        {
            Vector3 dir = targetPos - transform.position;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
        }

        // ══════════════════════════════════════════════════════════
        // Nearest boss as target.
        // X3 / X4 will route through CombatManager3D / boss registry per
        // BUILDUP_INTEGRATION_PLAN.md; this Bosses iteration is the temporary
        // Buildup-compatible path.
        // ══════════════════════════════════════════════════════════

        public ICombatant FindNearestTarget()
        {
            if (_gameManager == null) return null;

            var candidates = (_statManager != null && _statManager.Kind == CombatantKind.Boss)
                ? _gameManager.Players
                : _gameManager.Bosses;

            ICombatant nearest  = null;
            float      bestDist = float.MaxValue;

            foreach (var obj in candidates)
            {
                if (obj == null) continue;
                var combatant = obj.GetComponentInChildren<ICombatant>();
                if (combatant == null || !combatant.IsAlive) continue;

                float dist = Vector3.Distance(transform.position, combatant.Transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest  = combatant;
                }
            }
            return nearest;
        }
    }
}
