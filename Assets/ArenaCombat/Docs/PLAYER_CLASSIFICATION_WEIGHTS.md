# Player Classification Weights

Two systems classify player behavior at runtime: **PlayerArchetypeClassifier** (broad archetype) and **PlayerBiasTracker** (granular bias vector). Both feed into the boss adaptive AI pipeline.

---

## Revision Log

**2026-05-27** — Tuned based on BT bot engagement distance analysis (9,134 match dataset).

Key problems fixed:
1. Melee threshold 5m was below BT Melee bot engagement range (5-10m) → raised to **12m**
2. 5-10m dead zone (neither Melee nor Ranged) → filled with mid-range Melee signal
3. CC skills with range > 8f leaked Ranged credit → **CC-tagged skills now credit CC only**
4. Ranged skill threshold 8f too low (CC mid-range skills counted) → raised to **15f**
5. Decay ×0.3 too aggressive → raised to **×0.5**

---

## 1. PlayerArchetypeClassifier

**Source:** `Scripts/Core/AI/PlayerArchetypeClassifier.cs`  
**Eval interval:** 180 seconds  
**Decay per eval:** ×0.5  
**Minimum total weight:** 5.0 (below this → Hybrid)

### 1.1 Weight Buckets

Three accumulator buckets: **Melee [0]**, **Ranged [1]**, **CC [2]**

| Event | Condition | Bucket | Weight |
|-------|-----------|--------|--------|
| Melee hit landed | Distance < 12m | Melee | +1.0 |
| Melee hit landed | Distance 12–20m | Melee | +0.3 |
| Melee hit landed | Distance > 20m | Ranged | +0.5 |
| Skill cast | CC or Silence tagged | CC | +1.5 (exclusive — no Ranged/Melee credit) |
| Skill cast (non-CC) | Range > 15f OR distance > 20m | Ranged | +1.0 |
| Skill cast (non-CC) | Range 8–15f AND distance > 12m | Ranged | +0.5 |
| Skill cast (non-CC) | Distance < 12m | Melee | +1.0 |
| Parry success | — | Melee | +1.5 |
| Passive distance (every 0.5s) | Distance < 12m | Melee | +0.05 |
| Passive distance (every 0.5s) | Distance 12–20m | *(neutral)* | 0 |
| Passive distance (every 0.5s) | Distance > 20m | Ranged | +0.05 |
| Slot CC bonus (per eval) | Per CC/Silence tagged skill in loadout | CC | +0.8 each |

**Key design change:** CC-tagged skill casts are now **exclusive** — they credit only the CC bucket. This prevents CC↔Ranged confusion from mid-range CC skills (ErosionField 10m, SealChain, etc.).

### 1.2 Classification Thresholds

```
total = Melee + Ranged + CC + slotCCBonus
ratio[i] = bucket[i] / total

dominant  = max(ratio)
secondary = second highest ratio
```

| Condition | Result |
|-----------|--------|
| total < 5.0 | **Hybrid** (insufficient data) |
| dominant ≥ 50% | Assign dominant archetype |
| dominant ≥ 40% AND secondary < 30% | Assign dominant archetype (semi-dominant) |
| Otherwise | **Hybrid** |

### 1.3 Archetype Enum

| Value | Name | Meaning |
|-------|------|---------|
| 0 | Hybrid | No clear specialization |
| 1 | Melee | Close-range focused |
| 2 | Ranged | Long-range focused |
| 3 | CC | Crowd-control focused |

### 1.4 Log Output

```
[Archetype] client={id} M={melee:F1} R={ranged:F1} C={cc:F1} (slotCC={bonus:F1}, total={sum:F1}) → {type}
```

---

## 2. PlayerBiasTracker

**Source:** `Scripts/Core/AI/PlayerBiasTracker.cs`  
**Eval interval:** 5 seconds  
**Counters:** reset to zero after each eval  
**Team distance sampling:** every FixedUpdate

### 2.1 Bias Indices (9 dimensions)

| Index | Name | Computation | Range |
|-------|------|-------------|-------|
| 0 | MeleePrefer | meleeAttempts / totalActions | 0–1 |
| 1 | RangeKeep | rangedSkillCasts / totalActions | 0–1 |
| 2 | AttackFocus | (meleeAttempts + skillCasts) / totalActions | 0–1 |
| 3 | SurvivalFirst | survivalSkillCasts / totalActions | 0–1 |
| 4 | ParryDepend | parryAttempts / totalActions | 0–1 |
| 5 | RopeManeuver | ropeUses / totalActions | 0–1 |
| 6 | SkillCentric | skillCasts / totalActions | 0–1 |
| 7 | TeamCluster | `1 - avgTeamDist/threshold` when avgDist < 10m, else 0 | 0–1 |
| 8 | TeamSpread | `(avgTeamDist - threshold) / threshold` when avgDist > 10m, clamped | 0–1 |

**Ranged skill detection:** A skill cast counts as "ranged" if it has `SkillRoleTag.Ranged` OR `ctx.TargetDistance > 15m`.

**Survival detection:** Counts casts of skills tagged `Heal`, `Shield`, `Survival`, or `Regen`.

### 2.2 Thresholds

| Parameter | Value | Purpose |
|-----------|-------|---------|
| `_rangedDistanceThreshold` | 15m | Context distance above which a skill cast is classified as ranged |
| `_teamCloseThreshold` | 10m | Pivot point for TeamCluster (biases[7]) vs TeamSpread (biases[8]) |

### 2.3 Log Output

```
[Bias] client={id} M={[0]:F2} R={[1]:F2} AF={[2]:F2} S={[3]:F2} P={[4]:F2} Rp={[5]:F2} SF={[6]:F2} TC={[7]:F2} TS={[8]:F2}
```

---

## 3. How Classification Feeds Into Boss AI

### 3.1 Archetype → Boss Variant Selection (BossAdaptiveWeights)

Two player archetypes form a **normalized pair** (smaller enum value first), mapped to one of 10 pair indices:

| Pair | Index | ONNX variant |
|------|-------|-------------|
| Hybrid + Hybrid | 0 | H×H |
| Hybrid + Melee | 1 | M×H |
| Hybrid + Ranged | 2 | R×H |
| Hybrid + CC | 3 | CC×H |
| Melee + Melee | 4 | M×M |
| Melee + Ranged | 5 | M×R |
| Melee + CC | 6 | M×CC |
| Ranged + Ranged | 7 | R×R |
| Ranged + CC | 8 | R×CC |
| CC + CC | 9 | CC×CC (fallback to H×H) |

**Pair index formula:** `lo × 4 − lo × (lo − 1) / 2 + (hi − lo)`

### 3.2 Archetype → Boss Draft Skill Selection (BossDraftManager)

`BossDraftManager` uses the same pair index to look up **matchup weights** from `BossDraftWeightTable`.

**Scoring formula:**
```
score = matchupWeight[pairIndex] × phaseMultiplier + reactiveBonus
```

### 3.3 Biases → Reactive Bonus (BossDraftManager)

| Reactive Condition | Bias Check |
|-------------------|------------|
| SurvivalBiasModerate | biases[3] > 0.15 |
| SurvivalBiasHigh | biases[3] > 0.20 |
| AggressionBiasHigh | biases[2] > 0.40 |
| PlayersCloseProximity | biases[7] > 0.40 |

Other conditions (BossSilenced, BossStaggered, PlayerLowHP, BossLowHP) check entity status directly.

### 3.4 Biases → Adaptive Weights (BossAdaptiveWeights)

`BossAdaptiveWeights` reads the full 9-dimension bias vector from `PlayerBiasTracker.GetAverageBiases()` to tune ML observation channels in real-time.

---

## 4. Timing Summary

| System | Interval | Scope |
|--------|----------|-------|
| PlayerArchetypeClassifier | 180s eval + 0.5s distance sample | Per-client, server-only |
| PlayerBiasTracker | 5s eval + every FixedUpdate team distance | Per-client, server-only |
| BossDraftManager | On draft round end (event-driven) | Server-only |
| BossAIPoolManager variant swap | On archetype change (event-driven) | Server-only |

---

## 5. Expected Classification by BT Bot Type

| BT Bot | Melee bucket | Ranged bucket | CC bucket | Expected result |
|--------|-------------|--------------|-----------|-----------------|
| **Melee** (5-10m) | High — hits+passive all < 12m | Low — nothing > 20m | None | Melee |
| **Ranged** (20-30m) | None | High — skills > 15f, distance > 20m | Some — if using CC-tagged skills | Ranged |
| **CC** (8-14m) | Some — distance < 12m occasionally | Low — skills < 15f now excluded | High — CC tags + slotCC ×0.8 | CC |
| **Hybrid** (variable) | Moderate | Moderate | Low | Hybrid |
