using System;
using UnityEngine;
using ArenaCombat.Core.Skill;

namespace ArenaCombat.Core.AI
{
    public enum ReactiveCondition : byte
    {
        SurvivalBiasModerate,
        SurvivalBiasHigh,
        AggressionBiasHigh,
        BossSilenced,
        BossStaggered,
        PlayerMarked,
        PlayerLowHP,
        PlayersCloseProximity,
        BossLowHP,
    }

    [Serializable]
    public class SkillMatchupEntry
    {
        public SkillDefinition skill;
        [Tooltip("Index order: 0=H+H 1=H+M 2=H+R 3=H+CC 4=M+M 5=M+R 6=M+CC 7=R+R 8=R+CC 9=CC+CC")]
        public float[] weights = new float[10];
    }

    [Serializable]
    public class PhaseRule
    {
        public SkillRoleTag[] targetTags;
        public int minDraftUnlocks;
        public int maxDraftUnlocks;
        public float multiplier = 1f;
    }

    [Serializable]
    public class ReactiveRule
    {
        public ReactiveCondition trigger;
        public SkillDefinition boostedSkill;
        public float bonus;
    }

    [CreateAssetMenu(menuName = "ArenaCombat/AI/BossDraftWeightTable")]
    public class BossDraftWeightTable : ScriptableObject
    {
        [Header("Section 4 — Matchup Weights (skills x 10 pairs)")]
        public SkillMatchupEntry[] matchupEntries;

        [Header("Section 5 — Phase Multipliers")]
        public PhaseRule[] phaseRules;

        [Header("Section 6 — Reactive Bonuses")]
        public ReactiveRule[] reactiveRules;

        static readonly string[] PairLabels =
        {
            "H+H", "H+M", "H+R", "H+CC",
            "M+M", "M+R", "M+CC",
            "R+R", "R+CC",
            "CC+CC"
        };

        public float GetMatchupWeight(SkillDefinition skill, int pairIndex)
        {
            if (skill == null || matchupEntries == null) return 1f;
            if (pairIndex < 0 || pairIndex >= 10) return 1f;
            foreach (var entry in matchupEntries)
            {
                if (entry.skill == skill)
                    return (entry.weights != null && pairIndex < entry.weights.Length)
                        ? entry.weights[pairIndex]
                        : 1f;
            }
            return 1f;
        }

        public float GetPhaseMultiplier(SkillDefinition skill, int totalDraftUnlocks)
        {
            if (skill == null || phaseRules == null || skill.RoleTags == null) return 1f;
            float best = 1f;
            foreach (var rule in phaseRules)
            {
                if (totalDraftUnlocks < rule.minDraftUnlocks || totalDraftUnlocks > rule.maxDraftUnlocks)
                    continue;
                if (rule.targetTags == null) continue;
                foreach (var tag in rule.targetTags)
                {
                    if (Array.Exists(skill.RoleTags, t => t == tag))
                    {
                        if (rule.multiplier > best) best = rule.multiplier;
                        break;
                    }
                }
            }
            return best;
        }

        // Normalized pair -> 0-9 index. Smaller enum value first.
        // (0,0)=0 (0,1)=1 (0,2)=2 (0,3)=3 (1,1)=4 (1,2)=5 (1,3)=6 (2,2)=7 (2,3)=8 (3,3)=9
        public static int PairIndexFromArchetypes(PlayerArchetype a, PlayerArchetype b)
        {
            int lo = Mathf.Min((int)a, (int)b);
            int hi = Mathf.Max((int)a, (int)b);
            int rowStart = lo * 4 - lo * (lo - 1) / 2;
            return rowStart + (hi - lo);
        }

        void OnValidate()
        {
            if (matchupEntries == null) return;
            foreach (var entry in matchupEntries)
            {
                if (entry.weights == null || entry.weights.Length != 10)
                {
                    var old = entry.weights;
                    entry.weights = new float[10];
                    if (old != null)
                    {
                        int copy = Mathf.Min(old.Length, 10);
                        Array.Copy(old, entry.weights, copy);
                    }
                    for (int i = 0; i < 10; i++)
                        if (entry.weights[i] <= 0f) entry.weights[i] = 1f;

                    string skillName = entry.skill != null ? entry.skill.name : "(null)";
                    Debug.LogWarning($"[BossDraftWeightTable] {skillName} weights resized to 10. " +
                                     $"Expected order: {string.Join(", ", PairLabels)}");
                }
            }
        }
    }
}
