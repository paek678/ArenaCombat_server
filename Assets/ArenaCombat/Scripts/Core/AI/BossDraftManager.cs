using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using ArenaCombat.Core.Combat;
using ArenaCombat.Core.Network;
using ArenaCombat.Core.Skill;
using ArenaCombat.Core.Stats;

namespace ArenaCombat.Core.AI
{
    [DisallowMultipleComponent]
    public class BossDraftManager : MonoBehaviour
    {
        public static BossDraftManager Instance { get; private set; }

        [SerializeField] BossDraftWeightTable _weightTable;

        const int MinDraftSlot = 1;

        readonly Dictionary<int, SkillDefinition> _draftOverrides = new();
        int _nextOverrideSlot = 4;

        GameStateManager _subscribedGSM;
        BossAIPoolManager _subscribedPool;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(this); return; }
        }

        void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        void OnEnable() => TrySubscribe();
        void OnDisable() => Unsubscribe();

        void Update()
        {
            if (_subscribedGSM == null || _subscribedPool == null)
                TrySubscribe();
        }

        static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        void TrySubscribe()
        {
            if (_subscribedGSM == null && GameStateManager.Instance != null)
            {
                _subscribedGSM = GameStateManager.Instance;
                _subscribedGSM.OnCardDraftEndedServer += HandleDraftEnded;
                _subscribedGSM.OnMatchStateChanged += HandleMatchStateChanged;
            }
            if (_subscribedPool == null && BossAIPoolManager.Instance != null)
            {
                _subscribedPool = BossAIPoolManager.Instance;
                _subscribedPool.OnVariantSlotsApplied += ReapplyOverrides;
            }
        }

        void Unsubscribe()
        {
            if (_subscribedGSM != null)
            {
                _subscribedGSM.OnCardDraftEndedServer -= HandleDraftEnded;
                _subscribedGSM.OnMatchStateChanged -= HandleMatchStateChanged;
                _subscribedGSM = null;
            }
            if (_subscribedPool != null)
            {
                _subscribedPool.OnVariantSlotsApplied -= ReapplyOverrides;
                _subscribedPool = null;
            }
        }

        void HandleMatchStateChanged(MatchState prev, MatchState next)
        {
            if (next == MatchState.WaitingForPlayers)
                ResetState();
        }

        void ResetState()
        {
            _draftOverrides.Clear();
            _nextOverrideSlot = 4;
        }

        void HandleDraftEnded(int round)
        {
            if (!IsServer || _weightTable == null) return;
            if (_nextOverrideSlot < MinDraftSlot)
            {
                Debug.LogWarning("[BossDraft] All override slots filled, skipping.");
                return;
            }

            var bossCtrl = ResolveBossController();
            if (bossCtrl == null) return;

            int pairIndex = ResolvePairIndex();
            var ownedIds = CollectOwnedSkillIds(bossCtrl);

            Debug.Log($"[BossDraft] === Round {round} START === pairIndex={pairIndex}, ownedSkills={ownedIds.Count}, targetSlot={_nextOverrideSlot}");

            var registry = GameManager.Instance != null ? GameManager.Instance.SkillRegistry : null;
            if (registry == null) return;

            var candidates = registry.GetBossDraftCandidates(ownedIds, 3);
            if (candidates.Count == 0)
            {
                Debug.LogWarning("[BossDraft] No candidates available for draft.");
                return;
            }

            SkillDefinition best = null;
            float bestScore = float.NegativeInfinity;
            int totalDraftUnlocks = round * 2;

            foreach (var skill in candidates)
            {
                float matchup = _weightTable.GetMatchupWeight(skill, pairIndex);
                float phase = _weightTable.GetPhaseMultiplier(skill, totalDraftUnlocks);
                float reactive = ComputeReactiveBonus(skill);
                float score = matchup * phase + reactive;

                Debug.Log($"[BossDraft]   candidate: {skill.DisplayName} | matchup={matchup:F2} x phase={phase:F2} + reactive={reactive:F2} = {score:F2}");

                if (score > bestScore)
                {
                    bestScore = score;
                    best = skill;
                }
            }

            if (best == null) return;

            if (bossCtrl.SetBossSkillSlot(_nextOverrideSlot, best))
            {
                _draftOverrides[_nextOverrideSlot] = best;
                Debug.Log($"[BossDraft] >>> PICKED: {best.DisplayName} (score={bestScore:F2}) -> slot {_nextOverrideSlot}");
                _nextOverrideSlot--;
            }
        }

        void ReapplyOverrides()
        {
            if (!IsServer) return;
            var bossCtrl = ResolveBossController();
            if (bossCtrl == null) return;

            foreach (var kvp in _draftOverrides)
                bossCtrl.SetBossSkillSlot(kvp.Key, kvp.Value);

            if (_draftOverrides.Count > 0)
                Debug.Log($"[BossDraft] Re-applied {_draftOverrides.Count} draft overrides after variant recovery.");
        }

        BossNetworkController3D ResolveBossController()
        {
            if (BossManager.Instance == null) return null;
            var nob = BossManager.Instance.CurrentBoss;
            return nob != null ? nob.GetComponent<BossNetworkController3D>() : null;
        }

        int ResolvePairIndex()
        {
            var classifier = PlayerArchetypeClassifier.Instance;
            var nm = NetworkManager.Singleton;
            if (classifier == null || nm == null)
                return 0;

            PlayerArchetype a1 = PlayerArchetype.Hybrid;
            PlayerArchetype a2 = PlayerArchetype.Hybrid;
            int count = 0;
            foreach (var client in nm.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                var arch = classifier.GetArchetype(client.ClientId);
                if (count == 0) a1 = arch;
                else if (count == 1) a2 = arch;
                count++;
                if (count >= 2) break;
            }
            if (count == 1) a2 = a1;
            int idx = BossDraftWeightTable.PairIndexFromArchetypes(a1, a2);
            Debug.Log($"[BossDraft] Player archetypes: P1={a1}, P2={a2} -> pairIndex={idx} (players={count})");
            return idx;
        }

        HashSet<string> CollectOwnedSkillIds(BossNetworkController3D bossCtrl)
        {
            var ids = new HashSet<string>();
            var skillMgr = bossCtrl.GetComponent<SkillManager>();
            if (skillMgr == null) return ids;
            foreach (var slot in skillMgr.Slots)
            {
                if (slot != null && !string.IsNullOrEmpty(slot.SkillId))
                    ids.Add(slot.SkillId);
            }
            return ids;
        }

        float ComputeReactiveBonus(SkillDefinition skill)
        {
            if (_weightTable.reactiveRules == null) return 0f;
            float bonus = 0f;
            foreach (var rule in _weightTable.reactiveRules)
            {
                if (rule.boostedSkill != skill) continue;
                if (EvaluateCondition(rule.trigger))
                {
                    Debug.Log($"[BossDraft]     reactive: {rule.trigger} -> {skill.DisplayName} +{rule.bonus:F2}");
                    bonus += rule.bonus;
                }
            }
            return bonus;
        }

        bool EvaluateCondition(ReactiveCondition condition)
        {
            var biases = PlayerBiasTracker.Instance?.GetAverageBiases();
            switch (condition)
            {
                case ReactiveCondition.SurvivalBiasModerate:
                    return biases != null && biases[3] > 0.15f;
                case ReactiveCondition.SurvivalBiasHigh:
                    return biases != null && biases[3] > 0.2f;
                case ReactiveCondition.AggressionBiasHigh:
                    return biases != null && biases[2] > 0.4f;
                case ReactiveCondition.BossSilenced:
                    return IsBossSilenced();
                case ReactiveCondition.BossStaggered:
                    return IsBossStaggered();
                case ReactiveCondition.PlayerMarked:
                    return false;
                case ReactiveCondition.PlayerLowHP:
                    return IsAnyPlayerLowHP(0.3f);
                case ReactiveCondition.PlayersCloseProximity:
                    return biases != null && biases[7] > 0.4f;
                case ReactiveCondition.BossLowHP:
                    return IsBossLowHP(0.3f);
                default:
                    return false;
            }
        }

        bool IsBossSilenced()
        {
            var bossCtrl = ResolveBossController();
            return bossCtrl != null && (bossCtrl.CurrentStatus & StatusMask.Silenced) != 0;
        }

        bool IsBossStaggered()
        {
            var bossCtrl = ResolveBossController();
            return bossCtrl != null && (bossCtrl.CurrentStatus & StatusMask.Stunned) != 0;
        }

        bool IsAnyPlayerLowHP(float threshold)
        {
            if (GameManager.Instance == null) return false;
            foreach (var p in GameManager.Instance.Players)
            {
                if (p == null) continue;
                var combatant = p.GetComponentInChildren<ICombatant>();
                if (combatant != null && combatant.IsAlive && combatant.CurrentHPPercent < threshold)
                    return true;
            }
            return false;
        }

        bool IsBossLowHP(float threshold)
        {
            var bossCtrl = ResolveBossController();
            if (bossCtrl == null) return false;
            var combatant = (ICombatant)bossCtrl;
            return combatant.IsAlive && combatant.CurrentHPPercent < threshold;
        }
    }
}
