# Codex Review — Pending

## Player Classification Threshold Tuning (2026-05-27)

### Changed Files
1. `Assets/ArenaCombat/Scripts/Core/AI/PlayerArchetypeClassifier.cs`
2. `Assets/ArenaCombat/Scripts/Core/AI/PlayerBiasTracker.cs`
3. `Assets/ArenaCombat/Scripts/Core/AI/BossDraftManager.cs`

### Summary of Changes

**PlayerArchetypeClassifier.cs — 7 threshold changes + 1 logic change:**

| Parameter | Old | New | Reason |
|-----------|-----|-----|--------|
| `_meleeDistance` | 5.0f | 12.0f | BT Melee bot engagement range is 5-10m; old threshold missed most melee actions |
| `_rangedDistance` | 10.0f | 20.0f | True ranged behavior starts beyond 20m; old 10m caught mid-range CC/hybrid actions |
| `_weightDecayOnEval` | 0.3f | 0.5f | 70% decay per 180s eval was too aggressive, causing classification instability |
| `_dominantPercent` | 55f | 50f | Compensates for wider thresholds making clean dominance harder |
| `_semiDominantPercent` | 45f | 40f | Same rationale |
| `_parryWeight` | 2.0f | 1.5f | Single parry event shouldn't flip classification |
| `_slotCCBias` | 0.5f | 0.8f | Stronger CC loadout signal for 3-4 CC skill builds |

**Logic change in RecordSkillCast:** CC-tagged skills now credit ONLY the CC bucket (previously also credited Ranged if range > 8f). Non-CC skills use tiered Ranged thresholds: range > 15f → full Ranged (+1.0), range 8-15f at distance > 12m → weak Ranged (+0.5).

**Logic change in RecordMeleeHit:** Added mid-zone 12-20m → Melee +0.3 (previously ≥5m → Ranged +0.5).

**PlayerBiasTracker.cs — 2 threshold changes:**

| Parameter | Old | New | Reason |
|-----------|-----|-----|--------|
| `_rangedDistanceThreshold` | 5f | 15f | Match Classifier's new Ranged skill threshold; 5f was counting close-range casts as ranged |
| `_teamCloseThreshold` | 8f | 10f | Wider team proximity detection for M×M scenarios |

**BossDraftManager.cs — 2 reactive condition threshold changes:**

| Condition | Old | New | Reason |
|-----------|-----|-----|--------|
| AggressionBiasHigh | biases[2] > 0.5 | > 0.4 | biases[2] uses 5s window ratio; 0.5 was rarely achievable |
| PlayersCloseProximity | biases[7] > 0.5 | > 0.4 | With teamCloseThreshold=10m, avgDist<5m needed for 0.5 was too strict |

### Risk Assessment
- All changes are to SerializeField defaults (overridable in Inspector) except RecordSkillCast/RecordMeleeHit logic
- RecordSkillCast logic change: CC-exclusive is a behavioral change that eliminates CC↔Ranged cross-contamination
- RecordMeleeHit mid-zone: additive signal, no existing behavior removed
- No network/RPC changes, no API surface changes
