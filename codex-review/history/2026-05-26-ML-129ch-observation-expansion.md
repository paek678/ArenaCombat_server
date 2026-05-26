# ML 129-Channel Observation Expansion

**Date:** 2026-05-26
**Verdict:** PASS (Round 2)

## Changes

### BossObservationCollector.cs — Full rewrite
- `Phase3Size=35` → `TotalObsSize=129`, `ChannelsPerSlot=7`, `PlayerSkillSlots=5`
- Old `CollectPhase3/Phase1/Phase2/Phase3Stats` → single `CollectFull129(VectorSensor)`
- 129-channel layout matching training ONNX:
  - #0-10: Position/Direction (11ch)
  - #11-14: HP×3 + phase (4ch, phase=(phase+1)/4)
  - #15-49: Boss 5 slots × 7ch (35ch)
  - #50-51: Touch range (2ch)
  - #52-86: P1 5 slots × 7ch (35ch)
  - #87-121: P2 5 slots × 7ch (35ch)
  - #122-128: Extra (7ch: casting×2, speed×2, unlocked_ratio×2, burst_damage)
- Slot 7ch: remaining_cd/30, effective_cd/30, range/55, cone_or_aoe, is_dir, is_aoe, is_proj
- Player SkillManager + SkillExecutor caching added
- `_maxBurstDmg` 120→80 matching training spec

### BossInferenceAgent.cs — Updated
- `TotalObsSize` = `BossObservationCollector.TotalObsSize` (129)
- `CollectExtraObs()` removed — all in collector
- Action branch size 4→5: added backward (case 4)
- Heuristic: DownArrow→4

### Boss.prefab
- VectorObservationSize: 40→129
- BranchSizes: 04000000→05000000

### Chapter1.unity scene override
- VectorObservationSize: 55→129
- BranchSizes: 0400000004000000→05000000

## Round 1 Issues (fixed)
1. Prefab/scene ML config was stale → updated
2. Phase null case emitted 0.25 → fixed to 0f
3. Agent disabled on prefab → by design (runtime enable)
