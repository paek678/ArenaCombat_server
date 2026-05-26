# ML Training Reference — ArenaCombat6 Runtime Weight & Classification System

This document is a complete reference extracted from the live game code.
Use it to align the training environment's reward functions, observation specs,
BT agent personalities, and skill weighting with the runtime game.

---

## 1. Player Archetype Classification

Source: `PlayerArchetypeClassifier.cs`

### 1-1. Archetypes

```
enum PlayerArchetype : byte
    Hybrid = 0   // Default / balanced
    Melee  = 1   // Aggressive close-range
    Ranged = 2   // Long-range / kite style
    CC     = 3   // Crowd-control focus
```

### 1-2. Three Weight Buckets

```
weights[0] = Melee
weights[1] = Ranged
weights[2] = CC
```

### 1-3. Weight Accumulation Events

| Event | Condition | Bucket | Amount |
|-------|-----------|--------|--------|
| Melee hit | dist to boss < 5m | weights[0] | +1.0 |
| Melee hit | dist to boss >= 5m | weights[1] | +0.5 |
| Skill cast | dist < 5m | weights[0] | +1.0 |
| Skill cast | skill.Range > 8m OR dist > 10m | weights[1] | +1.0 |
| Skill cast | CC or Silence tag | weights[2] | +1.5 |
| Parry success | any | weights[0] | +2.0 |
| Passive distance | dist < 5m (sampled every 0.5s) | weights[0] | +0.05 |
| Passive distance | dist > 10m (sampled every 0.5s) | weights[1] | +0.05 |
| Slot CC bonus | per CC/Silence skill in loadout (recomputed, not stored) | weights[2] | +0.5 each |

One skill cast can credit MULTIPLE buckets simultaneously
(e.g., CC + Melee distance = weights[0] +1.0 AND weights[2] +1.5).

### 1-4. Classification Algorithm

```
Eval cycle: every 180 seconds (+ forced on card draft start)
Min total: 5.0 (below → always Hybrid)

total = m + r + c
mPct = m / total * 100
rPct = r / total * 100
cPct = c / total * 100

Deterministic tie-break: M > R > C (topPct uses first-found largest)

IF topPct >= 55% → topType
IF topPct >= 45% AND secondPct < 30% → topType
ELSE → Hybrid
```

### 1-5. Weight Decay After Eval

```
weights[0] *= 0.3
weights[1] *= 0.3
weights[2] *= 0.3
(keeps 30%, decays 70% — recent behavior dominates)
```

### 1-6. Pair Matching (10 Combinations)

Two players → (min, max) normalization → lookup:

```
(Hybrid,Hybrid)  (Hybrid,Melee)  (Hybrid,Ranged)  (Hybrid,CC)
(Melee,Melee)    (Melee,Ranged)  (Melee,CC)
(Ranged,Ranged)  (Ranged,CC)
(CC,CC)
```

Each pair maps to a BossAIDefinition asset containing:
- skillSlots[5]: which 5 skills the boss uses
- slotWeights[5]: per-slot multiplicative bias (default 1.0)
- cooldownScale: global cooldown multiplier (0.1 ~ 2.0)

---

## 2. Player Bias Tracker (9 Indices)

Source: `PlayerBiasTracker.cs`

### 2-1. Counters Collected

| Counter | Trigger |
|---------|---------|
| meleeAttempts | RecordMelee (light attack) |
| skillCasts | RecordSkillCast (any skill) |
| rangedSkillCasts | Skill has Ranged tag OR ctx.TargetDistance > 5m |
| survivalSkillCasts | Skill has Heal / Shield / Survival / Regen tag |
| parryAttempts | RecordParry |
| ropeUses | RecordRope |
| totalActions | Sum of all above |
| teamDistanceSum / teamDistanceSamples | Distance to teammate, sampled every FixedUpdate |

### 2-2. Bias Computation (every 5 seconds, then counters reset)

```
t = max(totalActions, 1)

biases[0] = meleeAttempts / t              // Melee ratio
biases[1] = rangedSkillCasts / t           // Ranged ratio
biases[2] = (meleeAttempts + skillCasts) / t  // Action frequency
biases[3] = survivalSkillCasts / t         // Survival ratio
biases[4] = parryAttempts / t              // Parry ratio
biases[5] = ropeUses / t                   // Rope usage ratio
biases[6] = skillCasts / t                 // Skill cast frequency
biases[7] = avgDist < 8m ? 1 - (avgDist / 8) : 0    // Team closeness
biases[8] = avgDist > 8m ? min((avgDist - 8) / 8, 1) : 0  // Team spread
```

All values are 0.0 ~ 1.0 normalized.

---

## 3. Boss Adaptive Weights — Bias Response Mapping

Source: `BossAdaptiveWeights.cs`

### 3-1. Mapping Table: "Player does X → Boss responds with Y"

```
BiasResponseMap[9]:
  Index | Player Behavior      | Boss Response Tag | Logic
  ------+---------------------+-------------------+---------------------------
  [0]   | High Melee ratio    → Ranged  (13)      | "outrange the brawler"
  [1]   | High Ranged ratio   → Melee   (12)      | "close the gap on kiters"
  [2]   | High Action freq    → Shield  (2)       | "tank the aggression"
  [3]   | High Survival ratio → Burst   (0)       | "burst through healing"
  [4]   | High Parry ratio    → AOE     (9)       | "AOE bypasses parry"
  [5]   | High Rope usage     → Zone    (4)       | "zone restricts mobility"
  [6]   | High Skill freq     → Counter (5)       | "counter-cast punishes"
  [7]   | Team close together → AOE     (9)       | "punish clumping"
  [8]   | Team spread apart   → Mark    (8)       | "debuff isolated targets"
```

### 3-2. Weight Computation Formula

```
baseWeight = 1.0
biasMultiplier = 2.0

ComputeWeight(skill):
    weight = baseWeight
    avgBias = average of both players' biases (per index)
    FOR b in [0..8]:
        IF avgBias[b] > 0 AND skill.RoleTags contains BiasResponseMap[b]:
            weight += avgBias[b] * biasMultiplier
    RETURN weight
```

Example: avgBias[0]=0.5 (players melee a lot), skill has Ranged tag
→ weight = 1.0 + (0.5 × 2.0) = 2.0

Multiple biases can stack on one skill if it has multiple matching tags.

---

## 4. Final Skill Selection (Weighted Random)

Source: `SkillManager.cs`

```
FOR each eligible skill (off cooldown, conditions met):
    adaptiveWeight = BossAdaptiveWeights.ComputeWeight(skill)
    slotWeight     = BossAIDefinition.slotWeights[slotIndex]   (variant-defined, default 1.0)
    finalWeight    = adaptiveWeight × slotWeight

totalWeight = SUM(finalWeights)
roll = Random(0, totalWeight)
cumulative = 0
FOR each eligible skill:
    cumulative += finalWeight
    IF cumulative >= roll → SELECT this skill
```

---

## 5. ML Observation Spec (40 Observations)

Source: `BossObservationCollector.cs` + `BossInferenceAgent.cs`

### 5-1. Observation Layout

```
INDEX  | NAME                | FORMULA                          | RANGE
-------+--------------------+----------------------------------+--------
[0]    | P1 dir X           | (P1.pos - Boss.pos).normalized.x | -1~1
[1]    | P1 dir Z           | (P1.pos - Boss.pos).normalized.z | -1~1
[2]    | P1 distance         | dist / 55m                      | 0~1
[3]    | Boss forward X     | Boss.forward.x                   | -1~1
[4]    | Boss forward Z     | Boss.forward.z                   | -1~1
[5]    | Boss-P1 dot        | dot(forward, dirToP1)            | -1~1
[6]    | P2 dir X           | (P2.pos - Boss.pos).normalized.x | -1~1
[7]    | P2 dir Z           | (P2.pos - Boss.pos).normalized.z | -1~1
[8]    | P2 distance         | dist / 55m                      | 0~1
[9]    | P1-P2 distance     | dist(P1,P2) / 55m               | 0~1
[10]   | Boss-P2 dot        | dot(forward, dirToP2)            | -1~1
[11]   | Boss HP%           | currentHP / maxHP                | 0~1
[12]   | P1 HP%             | P1 currentHP / maxHP             | 0~1
[13]   | P2 HP%             | P2 currentHP / maxHP             | 0~1
[14-18]| Skill CD% (5 slots)| remaining / total cooldown       | 0~1
[19]   | Phase%             | currentPhase / 4                 | 0~1
[20-24]| Skill range (5)    | skill.Range / 55m                | 0~1
[25]   | Slot0 CD max       | skill.Cooldown / 30s             | 0~1
[26]   | Slot0 target type  | (int)targetType / (N-1)          | 0~1
[27]   | Slot1 CD max       | (same)                           | 0~1
[28]   | Slot1 target type  | (same)                           | 0~1
[29]   | Slot2 CD max       | (same)                           | 0~1
[30]   | Slot2 target type  | (same)                           | 0~1
[31]   | Slot3 CD max       | (same)                           | 0~1
[32]   | Slot3 target type  | (same)                           | 0~1
[33]   | Slot4 CD max       | (same)                           | 0~1
[34]   | Slot4 target type  | (same)                           | 0~1
[35]   | P1 casting?        | 0 or 1                           | 0~1
[36]   | P2 casting?        | 0 or 1                           | 0~1
[37]   | P1 avg speed       | speed / 16 m/s                   | 0~1
[38]   | P2 avg speed       | speed / 16 m/s                   | 0~1
[39]   | Burst damage       | recentDmg(1s window) / 80        | 0~1
```

### 5-2. TargetType Enum

```
Single    = 0   // Single target
Area      = 1   // Area of effect
Self      = 2   // Self-cast
Direction = 3   // Directional skillshot
```

### 5-3. ML Action Space (Movement Only)

```
1 Discrete branch, 4 options:
  0 = Idle
  1 = Move forward
  2 = Rotate left
  3 = Rotate right
```

Skill selection is NOT handled by ML — it uses server-side auto-cast
with BossAdaptiveWeights + variant slotWeights.

---

## 6. Boss Phase System

Source: `BossNetworkController3D.cs`, `BossStatsSO.cs`

### 6-1. Phase Enum

```
enum BossPhase : byte
    None     = 0
    Phase1   = 1   // 100% ~ 70% HP
    Phase2   = 2   // 70% ~ 40% HP
    Phase3   = 3   // 40% ~ 10% HP
    Enrage   = 4   // Below 10% HP
    Defeated = 5
```

### 6-2. Phase Transition Thresholds

BossStatsSO.BossPhaseThresholds (descending HP ratio array):
```
[0] = 0.7  → Phase2 when HP drops below 70%
[1] = 0.4  → Phase3 when HP drops below 40%
[2] = 0.1  → Enrage when HP drops below 10%
```

### 6-3. Boss Speed Per Phase

```
Phase1:  8.4 m/s  (Player 14 × 60%)
Phase2:  8.4 m/s
Phase3:  9.2 m/s  (Player 14 × 66%)
Enrage: 10.1 m/s  (Player 14 × 72%)
```

### 6-4. Boss Damage Scale Per Phase

```
Phase1: ×1.00
Phase2: ×1.08
Phase3: ×1.16
Enrage: (not shown, presumably higher)
```

---

## 7. Boss Stats (BossStatsSO)

```
BossMaxHP        = 1000
BossBaseDamage   = 50
BossBaseDefense  = 20
TelegraphTimeMul = 1.0
AggroSensitivity = 1.0
```

---

## 8. Player Stats & Constants

```
Player Move Speed  = 14 m/s
Player Slow Factor = ×0.6 (when Slowed status active)
Tick Rate          = 60 Hz
Map Bounds         = ±75m XZ, 0~20m Y
Kill Zone Y        = -10m
```

---

## 9. Status Effects (StatusMask)

```
[Flags] enum StatusMask : ushort
    None         = 0
    Stunned      = 1<<0   // Cannot act
    Rooted       = 1<<1   // Cannot move
    Silenced     = 1<<2   // Cannot use skills
    Slowed       = 1<<3   // Movement speed ×0.6
    Invulnerable = 1<<4   // Cannot take damage
    SuperArmor   = 1<<5   // Cannot be interrupted
    Burning      = 1<<6   // DoT
    Poisoned     = 1<<7   // DoT
    Frozen       = 1<<8   // Cannot act + visual
    Invisible    = 1<<9   // Hidden
```

---

## 10. SkillRoleTag — Full Enum (30 tags)

```
// X2-5 originals (0..8)
Burst       = 0    // High single-hit damage
DOT         = 1    // Damage over time
Shield      = 2    // Defensive / shield grant
Parry       = 3    // Parry-related
Zone        = 4    // Persistent area / lingering AoE
Counter     = 5    // Anti-casting
Heal        = 6    // HP restoration
Mobility    = 7    // Knockback / pull / displacement
Mark        = 8    // Vulnerability mark / debuff stack

// X1-6b-1 append (9..28)
AOE         = 9    // Instant area-of-effect
MultiHit    = 10   // Multi-strike sequence
Pierce      = 11   // Armor pierce
Melee       = 12   // Short range
Ranged      = 13   // Long range
DamageUp    = 14   // Damage increase buff
DefUp       = 15   // Defense increase
DefDown     = 16   // Defense decrease debuff
SelfBuff    = 17   // Self-buff
Vulnerable  = 18   // Damage taken increase
ShieldBreak = 19   // Anti-shield
Cleanse     = 20   // Status removal
Regen       = 21   // Sustained HP regen
AntiHeal    = 22   // Healing reduction
CC          = 23   // Crowd control (stun/root/etc)
Silence     = 24   // Skill prevention
Buff        = 25   // Beneficial effect on target
Execute     = 26   // High damage vs low HP
Stealth     = 27   // Invisibility
Survival    = 28   // Low-HP survival trigger

// X4-7 boss tags (29+)
Boss        = 29   // Boss-only skill filter key
```

---

## 11. BT Player Agent (Training Opponent)

Source: `BTPlayerAgent.cs`

### 11-1. Personality Parameters

```
meleeAggression  = 0.5  [0~1]  // Probability of melee attack when in range
parryTendency    = 0.3  [0~1]  // Probability of parry when boss is close
survivalCaution  = 0.3  [0~1]  // (unused in current tree)
meleeRange       = 3m          // Distance for melee attacks
fleeHPThreshold  = 0.3         // Flee when HP < 30%
tickInterval     = 0.2s        // Decision frequency
```

### 11-2. Behavior Priority (Selector)

```
Priority 1: IF HP < 30% → move AWAY from boss (flee)
Priority 2: IF dist < 4.5m AND random < parryTendency → PARRY
Priority 3: IF dist < 3m AND random < meleeAggression → LIGHT ATTACK + stop
Priority 4: DEFAULT → move TOWARD boss
```

### 11-3. Training Considerations

- BT is deterministic per personality params → risk of overfitting if boss trains only vs fixed BT
- Consider: randomize personality params per episode, or use self-play / scripted variety
- BT currently has NO skill casting — only melee + parry + movement
- For richer training, BT should use skills to generate diverse bias profiles

---

## 12. Card Draft Timing — AI Swap Trigger (CONFIRMED DESIGN)

```
175s combat → Card Draft Start (8s) → Card Draft End → 175s combat
                                                       (max 4 rounds)

OnCardDraftStarted:
  ① PlayerArchetypeClassifier.ForceEvaluate()
     → immediate re-classify using accumulated weights
     → no waiting for 180s cycle
  ② IF archetype changed → OnPlayerArchetypeChanged event
  ③ BossAIPoolManager.EvaluateAndSwap()
     → pair matching → variant swap (skills + slotWeights + cooldownScale)
     → (future) ONNX model hot-swap per pair
  ④ Draft 8s buffer allows smooth transition before combat resumes
```

---

## 13. Complete Data Flow Diagram

```
[PLAYER ACTIONS]
    │
    ├─ RecordMelee / RecordSkillCast / RecordParry / RecordRope
    │     │
    │     ├──→ PlayerBiasTracker
    │     │       counters++ → every 5s → biases[9] compute → reset
    │     │
    │     └──→ PlayerArchetypeClassifier
    │             weights[3] accumulate → every 180s (+ draft start)
    │             → Classify(M,R,C) → decay ×0.3
    │
    ├─ SamplePassiveDistances (every 0.5s)
    │     → weights[0] or [1] += 0.05 based on distance to boss
    │
    └─ SampleTeamDistance (every FixedUpdate)
          → biases[7] team close, biases[8] team spread

[ARCHETYPE CHANGE EVENT]
    │
    └──→ BossAIPoolManager.EvaluateAndSwap()
           → (P1, P2) pair → BossAIDefinition lookup
           → Apply: skillSlots[5] + slotWeights[5] + cooldownScale
           → (future) ONNX model swap

[EVERY SKILL SELECTION (auto-cast tick)]
    │
    └──→ SkillManager.Update()
           → FOR each eligible skill:
           │    adaptiveWeight = BossAdaptiveWeights.ComputeWeight(skill)
           │      → GetAverageBiases() → BiasResponseMap match → weight += bias × 2.0
           │    finalWeight = adaptiveWeight × slotWeight
           → Weighted random selection → fire skill

[ML MOVEMENT (ONNX)]
    │
    └──→ BossInferenceAgent.OnActionReceived()
           → 40 observations → idle/forward/left/right
           → Completely independent from skill selection
```

---

## 14. Key Numbers Summary

| Parameter | Value | Used In |
|-----------|-------|---------|
| Player move speed | 14 m/s | NetworkConstants |
| Boss move speed (P1/P2) | 8.4 m/s | BossInferenceAgent |
| Boss move speed (P3) | 9.2 m/s | BNC3D OnPhaseChanged |
| Boss move speed (Enrage) | 10.1 m/s | BNC3D OnPhaseChanged |
| Slow factor | ×0.6 | StatusMask.Slowed |
| Melee distance threshold | 5m | Archetype Classifier |
| Ranged distance threshold | 10m | Archetype Classifier |
| Team close threshold | 8m | Bias Tracker |
| Ranged skill distance | 5m | Bias Tracker |
| Eval interval (archetype) | 180s | Classifier |
| Eval interval (bias) | 5s | Bias Tracker |
| Passive sample interval | 0.5s | Classifier |
| Weight decay | ×0.3 | Classifier |
| Adaptive base weight | 1.0 | BossAdaptiveWeights |
| Adaptive bias multiplier | 2.0 | BossAdaptiveWeights |
| Min total for classify | 5.0 | Classifier |
| Dominant threshold | 55% | Classifier |
| Semi-dominant threshold | 45% | Classifier |
| Secondary guard | 30% | Classifier |
| Boss max HP | 1000 | BossStatsSO |
| Boss base damage | 50 | BossStatsSO |
| Boss base defense | 20 | BossStatsSO |
| Phase2 threshold | HP < 70% | BossStatsSO |
| Phase3 threshold | HP < 40% | BossStatsSO |
| Enrage threshold | HP < 10% | BossStatsSO |
| Card draft interval | 175s | GameStateManager |
| Card draft duration | 8s | GameStateManager |
| Max draft rounds | 4 | GameStateManager |
| Observation size | 40 | BossInferenceAgent |
| Max observation distance | 55m | ObservationCollector |
| Max cooldown normalize | 30s | ObservationCollector |
| Speed normalize | 16 m/s | ObservationCollector |
| Burst damage normalize | 80 | BossInferenceAgent |
| Map bounds XZ | ±75m | MapBounds3D |
| Map bounds Y | 0~20m | MapBounds3D |
| Tick rate | 60 Hz | NetworkManager |
