# Boss Card Draft System

**Date:** 2026-05-27
**Topic:** Boss participates in card draft — matchup-weighted skill selection after player draft rounds

## Files Changed

| File | Action |
|------|--------|
| `Scripts/Core/AI/BossDraftWeightTable.cs` | NEW — SO definition (matchup weights, phase rules, reactive rules) |
| `Scripts/Core/AI/BossDraftManager.cs` | NEW — Server-only orchestrator |
| `Scripts/Core/AI/BossAIPoolManager.cs` | MODIFY — Added `OnVariantSlotsApplied` event |
| `Scripts/Core/Network/BossNetworkController3D.cs` | MODIFY — Added `SetBossSkillSlot()` returning bool |
| `Scripts/Core/Skill/Core/SkillRegistry.cs` | MODIFY — Added `GetBossDraftCandidates()` |
| `Data/BossAI/BossDraftWeightTable.asset` | NEW — Weight data (11 skills × 10 pairs) |

## Codex Feedback Summary

- **High**: Weight array authoring order mismatch (analysis doc vs code index order) → Added OnValidate + Tooltip labels
- **Medium**: Missing `using ArenaCombat.Core.Combat;` → Fixed
- **Medium**: `round * 2` renamed to `totalDraftUnlocks` for clarity
- **Medium**: Reactive conditions renamed to match available telemetry (SurvivalBiasModerate, AggressionBiasHigh, etc.)
- **Low**: Event renamed `OnVariantSlotsRecovered` → `OnVariantSlotsApplied`
- **Low**: MinDraftSlot = 1 guard (protects slot 0)
- **Low**: `SetBossSkillSlot` returns bool
- **Low**: Null defense in `GetBossDraftCandidates`

All feedback items applied.

## Outcome

Implemented. Awaiting Unity compile check and runtime verification.
