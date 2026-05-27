using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Master skill pool. Single source of truth for:
//   - Draft UI (X2-13): GetDraftCandidates(ownedIds, count) -> random offer pool
//   - Boss AI counter-pick (X4): GetCounterCandidates(playerRoleTags, count)
//   - SkillBinder (X2-7): iterate _pool to inject RuntimeStep delegates at game start
//
// Intentionally a single registry (no Player / Boss split) — boss skills coexist
// in the same pool, distinguished by SkillRoleTag values. See SKILL_SYSTEM_DESIGN.md §9.

namespace ArenaCombat.Core.Skill
{
    [CreateAssetMenu(menuName = "ArenaCombat/SkillRegistry", fileName = "SkillRegistry")]
    public class SkillRegistry : ScriptableObject
    {
        [SerializeField] private List<SkillDefinition> _pool = new();

        // ── Single lookup ──────────────────────────────────────
        public SkillDefinition Get(string skillId) =>
            _pool.Find(s => s.SkillId == skillId);

        public List<SkillDefinition> GetAll() => new(_pool);

        // ── Tag-filtered lookup ────────────────────────────────
        public List<SkillDefinition> GetByRoleTag(SkillRoleTag tag) =>
            _pool.Where(s => s.RoleTags != null && System.Array.Exists(s.RoleTags, t => t == tag))
                 .ToList();

        // ── Boss counter-pick: top N skills by CounterTag overlap with playerRoleTags ──
        public List<SkillDefinition> GetCounterCandidates(SkillRoleTag[] playerRoleTags, int count = 3)
        {
            return _pool
                .OrderByDescending(s => ScoreCounter(s, playerRoleTags))
                .Take(count)
                .ToList();
        }

        // ── Draft offer: random N from pool, excluding owned skill IDs ────────────
        // ownedIds remains string (these are SkillId values, not tags).
        public List<SkillDefinition> GetDraftCandidates(List<string> ownedIds, int count = 3)
        {
            var available = _pool.Where(s => !ownedIds.Contains(s.SkillId)).ToList();

            // Fisher-Yates shuffle, take first N.
            for (int i = available.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (available[i], available[j]) = (available[j], available[i]);
            }

            return available.Take(count).ToList();
        }

        public List<SkillDefinition> GetBossDraftCandidates(HashSet<string> ownedIds, int count = 3)
        {
            ownedIds ??= new HashSet<string>();
            var available = _pool.Where(s =>
                s != null &&
                s.IsReady &&
                s.RoleTags != null &&
                System.Array.Exists(s.RoleTags, t => t == SkillRoleTag.Boss) &&
                !ownedIds.Contains(s.SkillId)).ToList();

            for (int i = available.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (available[i], available[j]) = (available[j], available[i]);
            }

            return available.Take(count).ToList();
        }

        private int ScoreCounter(SkillDefinition skill, SkillRoleTag[] playerTags)
        {
            if (skill.CounterTags == null || playerTags == null) return 0;
            return skill.CounterTags.Count(ct => System.Array.Exists(playerTags, pt => pt == ct));
        }
    }
}
