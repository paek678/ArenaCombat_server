# Arena Combat — Server-Side Changelog

> Comprehensive record of all server-authoritative system changes, problem resolutions, and architecture decisions.
> Ordered by importance (critical fixes first, then features, then polish).
> Last updated: 2026-05-23

---

## 1. Critical Fixes (Game-Breaking Issues Resolved)

### 1-1. MPPM Editor Freeze — ML-Agents gRPC Block (2026-05-21)

**Problem:** When a second player connected via MPPM, Unity Editor froze indefinitely.

**Root Cause:** `Boss.prefab` Instantiate → `Agent.OnEnable()` → `Academy.Instance` lazy init → `RpcCommunicator.Initialize()` → synchronous gRPC to `localhost:5004`. No Python trainer → **infinite block on main thread**.

**Fix:**
- `BossManager.EnsureAcademyWontBlock()` — Reflection-based safety guard that forces `ConnectTrainer = false` before first boss Instantiate (editor-only)
- ML-Agents Project Settings: "Connect to Trainer" unchecked + settings asset created

**Files:** `BossManager.cs`

---

### 1-2. Network Synchronization Gap — Full SYNC-FIX (2026-05-21)

**Problem:** Multiple client-server desync issues — status effects invisible to client, shield not synced, HP regen not working, skill slots convention-based.

**Phase 1 Fixes:**
| Issue | Solution | File |
|-------|----------|------|
| StatusMask/StatManager desync | Event bridge: `OnStatusApplied/Removed/BulkCleared` → NV bitmask update | `StatManager.cs`, `PNC3D.cs`, `BNC3D.cs` |
| StatManager.Tick() not called | Re-enabled in FixedUpdate of PNC3D and BNC3D | `PNC3D.cs`, `BNC3D.cs` |
| ICombatant missing IsServer guards | All 10 mutating methods guarded | `PNC3D.cs` |
| Shield not synced | Added `networkShield` NV with FixedUpdate sync | `PNC3D.cs` |
| Boss StatusMask missing | Added `networkStatusMask` NV + bridge | `BNC3D.cs` |
| Telegraph cancel not visible | `TelegraphCancelledRpc` clears client VFX | `BNC3D.cs`, `SkillManager.cs` |

**Phase 2 Fixes:**
| Issue | Solution | File |
|-------|----------|------|
| Skill slot convention-based sync | Server-authoritative `SetSkillSlotServer()` + `SkillSlotSetRpc` | `PNC3D.cs` |
| CardManager dual-side slot set | Server-only path via `FindPNC3DForPlayer()` | `CardManager.cs` |
| Buff/Debuff invisible to client | `BuffMask`/`DebuffMask` NV bitmask bridge (same pattern as StatusMask) | `NetworkConstants.cs`, `StatManager.cs`, `PNC3D.cs`, `BNC3D.cs` |
| No bulk effect cleanup | `StatManager.ClearAllEffects()` — stops all coroutines, fires bulk events | `StatManager.cs` |

**Codex Review:** Phase 1: 2 rounds (FAIL→PASS). Phase 2: 2 rounds (FAIL→PASS).

---

### 1-3. Match Start Timing — Premature Match Start (2026-05-20)

**Problem:** `GetConnectedPlayerCount()` used `ConnectedClientsList.Count` which includes clients still loading the scene (not yet spawned). Match started before both players had player objects.

**Fix:** Changed to count `SpawnManager.SpawnedObjects` where `IsPlayerObject == true`.

**File:** `GameStateManager.cs`

---

### 1-4. OOM / Performance Degradation (2026-05-21)

**Problem:** Extended play sessions caused memory pressure from debug logging and unnecessary NV writes.

**Fixes:**
| Change | Detail |
|--------|--------|
| `SkillManager._logAutoCast` | Default → `false`, throttled to every 300 frames |
| `StatManager.RefreshDebugInspector()` | `#if UNITY_EDITOR` + 0.25s throttle + array reuse |
| `BNC3D.networkPosition.Value` | Write guarded with `sqrMagnitude > 0.0001f` change check |

---

## 2. Core Gameplay Systems (Server-Authoritative)

### 2-1. 3D Combat Pipeline — CombatManager3D (2026-05-11)

Complete server-authoritative combat system:

```
Player Input → RequestAttackRpc/RequestParryRpc (queue-based)
    → CombatManager3D.TryProcessAttack3D
        → Physics.OverlapBox (LayerMask filter)
        → Self/team/dead exclusion
        → Parry check (any-parrier-blocks-all)
        → ICombatant.TakeDamage
        → AttackResultRpc (broadcast)
    → K/D/A tracking (kills3D/deaths3D/assists3D/recentDamage3D)
```

- Attack queue: priority-based (Parry=0 > Rope=1 > Attack=2)
- Parry system: window timer + cooldown + stun on successful parry
- Assist tracking: 10s window + 10f damage threshold
- Kill-zone scored death: `Die(OwnerClientId)` → deaths++ (no kill credit)

---

### 2-2. Match End + In-Place Restart (2026-05-17)

**Flow:**
```
[BossDefeated or AllPlayersDead]
    → GameStateManager.EndMatch(reason)
    → networkMatchEndReason NV set
    → MatchEndUI auto-display (Victory/Defeat)
    → Player clicks Restart
    → RequestRestartRpc → RestartMatch()
        → DespawnBoss
        → Player reset (HP, skills, position, cooldowns)
        → SkillResetRpc to clients
        → PlayerBiasTracker.ResetAllCounters()
        → WaitingForPlayers → StartMatchCountdown
```

- Dual NV subscription pattern (solves replication timing race)
- MatchEnd blocks: movement, respawn timer, auto-cast
- In-place state reset (NOT scene reload)

---

### 2-3. Skill System — Buildup Integration (2026-05-12~15)

Full auto-cast skill system imported from Buildup branch:

| Component | Count | Description |
|-----------|-------|-------------|
| SkillComponents | 37 | Factory methods (DealDamage, ApplyStatus, Projectile, Area, etc.) |
| SkillLibrary | 29 | SkillStep definitions (9 boss + 20 player skills) |
| SkillBinder | 1 | Runtime binding: SkillDefinition → RuntimeStep |
| SkillManager | 1 | Server-only auto-cast loop + telegraph state machine |
| SkillExecutor | 1 | Execution pipeline + cooldown tracking |
| SkillRegistry | 1 | ID-based SO lookup for RPC resolution |

**Server Authority:** `SkillManager.Update()` runs server-only. Client gets results via RPCs (`SkillCooldownStartRpc`, `SkillSlotSetRpc`, `TelegraphStartedRpc`).

---

### 2-4. Boss System — BNC3D + Phase Tracking (2026-05-13~16)

```
BossManager.TrySpawnBoss()
    → NetworkObject.Instantiate (server)
    → BossNetworkController3D
        ├─ StatManager (HP, damage, status effects)
        ├─ StateManager (FSM: Dead/Stunned/HitStun/Parrying/Casting/Moving/Idle)
        ├─ SkillManager (auto-cast, 5 slots, adaptive weights)
        ├─ SkillExecutor (cooldowns, telegraph)
        └─ ML-Agents (movement only, skills via auto-cast)

    Phase Tracking:
        Phase1 (100%) → Phase2 (66%) → Phase3 (33%) → Enrage (15%) → Defeated (0%)
        Each phase: CooldownScale / Speed / DamageScale / TelegraphScale adjustments
```

**9 Boss Skills:** ExecutionSpike, CrushingBarrage, FortressArmor, SealChain, BarrierBreaker, RuptureMagazine, ErosionField, CollapseRoar, MarkWave + 2 Self (SurvivalPulse, OverchargeMode)

---

### 2-5. Telegraph + Phase VFX (2026-05-16)

- `SkillManager`: Telegraph state machine (Enter → Timer → Complete/Cancel)
- `BossTelegraphDisplay`: VFX per target type (Area=circle, Direction=slash, Single=sparks, Self=skip)
- Phase transition: ground AOE explosion on phase change
- Telegraph cancel: RPC to clear client VFX on death/ClearAll/SetSlot

---

## 3. AI Systems (Server-Only)

### 3-1. Player Behavior Tracking — PlayerBiasTracker (2026-05-16)

Raw action counter per player:
- 9-element bias array (melee hits, skill casts, parry success, rope uses, distance samples)
- 5s evaluation cycle
- `ResetAllCounters()` on match restart

---

### 3-2. Player Archetype Classification — PlayerArchetypeClassifier (2026-05-16~18)

Per-player weight tracking → archetype classification:

```
Weights: [Melee, Ranged, CC] accumulated from:
    - Melee hit (distance-dependent)
    - Skill cast (CC tag, range, distance)
    - Parry success
    - Passive distance sampling (0.5s interval)
    - Slot CC bias (equipped skill tags)

Classification (every 180s):
    ≥55% dominant → that type
    ≥45% + second <30% → that type
    else → Hybrid

Decay: 0.3x after each eval (prevents stale data)
```

---

### 3-3. Boss Adaptive Weights — BossAdaptiveWeights (2026-05-16~18)

- Per-skill weight adjustment based on hit/miss/parry results
- Feeds into SkillManager auto-cast weight selection
- Per-slot weight bias from BossAIDefinition SO

---

### 3-4. Adaptive Boss AI Selection — NEW (2026-05-22)

**Complete system for team-based AI variant selection with win rate learning:**

```
                    ┌─────────────────────────────┐
  Player Actions ──►│ PlayerArchetypeClassifier    │ (per-player weights)
                    └──────────┬──────────────────┘
                               │
                    ┌──────────▼──────────────────┐
                    │ TeamArchetypeResolver (NEW)  │ (sum weights → team type)
                    │  30s eval cycle              │
                    │  Own slot CC bonus calc      │
                    │  Classify → Hybrid/M/R/CC    │
                    └──────────┬──────────────────┘
                               │
                    ┌──────────▼──────────────────┐
                    │ BossAIPoolManager (MODIFIED) │
                    │  4×10 variant pool           │
                    │  ForceEvaluate before read   │
                    │  Deferred swap (boss busy)   │
                    └──────────┬──────────────────┘
                               │
                    ┌──────────▼──────────────────┐
                    │ BossAIWinRateTracker (NEW)   │
                    │  Wilson Score Lower Bound    │
                    │  Epsilon-Greedy (15%)        │
                    │  Session-only memory         │
                    └──────────┬──────────────────┘
                               │
               ┌───────────────▼───────────────────┐
               │  BNC3D.ApplyAIVariant(def)         │
               │  (skill slots, weights, cooldown)  │
               └───────────────┬───────────────────┘
                               │
                         [Match End]
                               │
                    RecordResult(teamArch, variantIdx, bossWon)
                    → _appliedFromPool guard (default AI excluded)
                    → Stored metadata from apply-time (not current)
```

**Selection Algorithm:**
| Phase | Condition | Action |
|-------|-----------|--------|
| Cold Start | All variants < 5 matches | Uniform random |
| Exploration | 15% roll | Pick least-sampled variant (random tie-break) |
| Exploitation | 85% roll | Pick highest Wilson lower bound (random tie-break) |

**Asset Pool:** 40 variant SOs (4 archetypes × 10 each) + 1 default. Empty skill slots — ready to fill.

**Debug Tools:**
- Inspector fields: current variant, team archetype, win/loss counts, last selection mode
- Editor menu: `ArenaCombat > Boss AI > [Runtime]` — simulate matches, dump tables, force evaluate, clear data
- Setup menu: `Full Setup` — auto-wire 40 assets + add components

**Codex Review:** 3 rounds (REVISE → REVISE → PASS WITH NOTES). All 12 issues resolved.

---

## 4. Balance Tuning (BAL-1, 2026-05-18~19)

### 4-1. Speed / Cooldown / ML Normalization (T1A)

| Param | Phase1 | Phase2 | Phase3 | Enrage |
|-------|--------|--------|--------|--------|
| Boss Speed | 8.4 | 8.4 | 9.2 | 10.1 |
| Cooldown Scale | 1.0 | 0.85 | 0.7 | 0.5 |
| Player Speed | 7.0 | — | — | — |
| ML Reward Norm | playerHP/150, bossHP/6000 | — | — | — |

### 4-2. Damage / HP Scaling (T2)

- Boss HP: 1000 → **6000**
- Player HP: 100 → **150**
- Player CrushingBarrage: 140→112 (detection hit 28→0)
- SkillProjectile detection radius: 0.5→1.0
- 7 boss skill damage adjustments

### 4-3. Phase Scaling (T3)

| Axis | Phase1 | Phase2 | Phase3 | Enrage |
|------|--------|--------|--------|--------|
| Damage | 1.00 | 1.08 | 1.16 | 1.25 |
| Telegraph | 1.00 | 0.90 | 0.78 | 0.70 |
| Cooldown | 1.00 | 0.85 | 0.70 | 0.50 |
| Speed | 8.4 | 8.4 | 9.2 | 10.1 |

Phase damage flows: `BNC3D → StatManager._phaseDamageScale → SkillManager.BuildSkillContext → ctx.DamageScale → SkillComponents`

---

## 5. UI / Client Sync (Server-Driven)

### 5-1. HUD System (2026-05-20)

- `HUDManager.cs`: auto-discovers UI by name (FindDeep recursive)
- Player HP bars: reads `networkHP` / `MaxHP`
- Boss HP bar: reads `ICombatant.CurrentHPPercent`
- Skill slot UI: dynamic build per `SkillManager.Slots` — cooldown overlay, ready glow, text
- Cooldown sync: `SkillCooldownStartRpc` (server OnExecuted → client `MarkUsedNow`)
- Polling-based (0.05s) — simpler than event subscription lifecycle

### 5-2. Lobby Error Recovery (2026-05-20)

- Wrong lobby code → button disabled during attempt → re-enabled on failure with error message

### 5-3. Match End UI (2026-05-17)

- `MatchEndUI.cs`: code-generated, dual NV subscription, "Victory!"/"Defeat" display

---

## 6. Infrastructure

### 6-1. Player Position Authority (2026-05-11)

All server-path position writes converted to collision-respecting:
- `rb.position = x` → `rb.MovePosition(x)` (bounds clamp, rope step, rope arrival)
- Single `authoritativePos` local cache for NV publish

### 6-2. Map Bounds Expansion (2026-05-22)

- Code defaults: (-50,0,-50)/(50,20,50) → **(-75,0,-75)/(75,20,75)** (1.5x)
- Note: `SerializeField` — Inspector values in scene may override

### 6-3. Legacy 2D Removal (2026-05-16)

- Complete removal of legacy 2D combat manager, 2D player controller references
- `CombatManager.cs` deleted, all references point to `CombatManager3D`

### 6-4. Scene Organization (2026-05-17~18)

- Chapter1: 30+ objects renamed (separators, arena, walls, boundaries, UI, HP bars, cards)
- SampleScene: typo fixes (`CreatLobbyButton` → `CreateLobbyButton`)
- Scene structure: SampleScene (lobby, index 0) → Chapter1 (gameplay, index 1)

---

## 7. Known Issues (Outstanding)

| # | Issue | Severity | Status |
|---|-------|----------|--------|
| 1 | Boss may not take damage after restart | Medium | Diagnostic log added to `BNC3D.TakeDamage`, needs runtime test |
| 2 | `rb.position` collision bypass | Low | A2-followup: `lastValidatedServerPosition` update order, needs runtime verification |
| 3 | Player spawn offset | Low | `PlayerSpawnManager.GetSpawnPosition` + bounds edge case |
| 4 | MapBounds Inspector override | Low | Code defaults changed but scene may have old SerializeField values |
| 5 | `_logAutoCast` prefab Inspector | Low | May still be `true` in serialized prefab — uncheck manually |
| 6 | 40 AI variant assets empty | Intended | Skills to be filled by designer — framework works as-is |

---

## 8. Network Variable Map (Server-Authoritative)

### PlayerNetworkController3D
| NV | Type | Purpose |
|----|------|---------|
| `networkHP` | float | Current HP |
| `networkShield` | float | Current shield |
| `networkIsAlive` | bool | Alive state |
| `networkPosition` | Vector3 | Position sync |
| `networkRotationY` | float | Y rotation sync |
| `networkIsRoping` | bool | Rope movement state |
| `networkStatusMask` | StatusMask (byte) | Status effects bitmask |
| `networkBuffMask` | BuffMask (byte) | Active buffs bitmask |
| `networkDebuffMask` | DebuffMask (byte) | Active debuffs bitmask |

### BossNetworkController3D
| NV | Type | Purpose |
|----|------|---------|
| `networkHP` | float | Current HP |
| `networkIsAlive` | bool | Alive state |
| `networkPosition` | Vector3 | Position sync |
| `networkRotationY` | float | Y rotation sync |
| `networkCurrentPhase` | BossPhase (byte) | Current boss phase |
| `networkStatusMask` | StatusMask (byte) | Status effects bitmask |
| `networkBuffMask` | BuffMask (byte) | Active buffs bitmask |
| `networkDebuffMask` | DebuffMask (byte) | Active debuffs bitmask |

### GameStateManager
| NV | Type | Purpose |
|----|------|---------|
| `networkMatchState` | MatchState (byte) | Current match state |
| `networkMatchEndReason` | MatchEndReason (byte) | Why match ended |

---

## 9. Codex Review History

| Date | Topic | Rounds | Result |
|------|-------|--------|--------|
| 05-11 | A2 rb.position cleanup | 3 | PASS |
| 05-11 | A4 MapBounds cleanup | 2 | PASS |
| 05-11 | B1-1~B1-5 combat pipeline | 11 total | All PASS |
| 05-12 | X2-1~X2-12 skill system import | 12 | All PASS |
| 05-13 | X3-1~X3-7 PNC3D wiring | 7 | All PASS |
| 05-13~14 | X4-1~X4-8 boss system | 8+ | All PASS |
| 05-16 | B4-1~B4-4 telegraph+VFX | 4 | All PASS |
| 05-16 | C1-1~C3-1 AI systems | 3 | All PASS |
| 05-16 | D1-1 legacy removal | 1 | PASS |
| 05-17 | B5-1 mouse look | 1 | PASS |
| 05-17 | B6-1 match end/restart | 3 | PASS |
| 05-18 | BAL-1 T1A~T3 balance | 6 | All PASS |
| 05-18 | C3a-A~F archetype+pool | 6 | All PASS |
| 05-20 | SYNC-1-2 projectile/area sync | 1 | PASS |
| 05-21 | SYNC-FIX Phase 1 | 2 | PASS |
| 05-21 | SYNC-FIX Phase 2 | 2 | PASS |
| 05-22 | C3b adaptive AI selection | 3 | PASS WITH NOTES |

---

## 10. Design Decisions & Rationale

### 10-1. Team Archetype (4 values) vs Pair Combination (10 values)

**Decision:** Sum all player weights → single team archetype classification (4 values).

**Rejected alternative:** Track exact player pair combinations (HH, HM, HR, HC, MM, MR, MC, RR, RC, CC = 10 values).

**Why:**
- With pair-based approach, win rate data is spread across 10 buckets — each bucket takes 5+ matches to reach confidence threshold. At ~3 min/match, that's 150+ minutes of play to build useful statistics for all combos
- Team-sum approach: 4 buckets → data concentrates faster, reaches exploitation phase ~2.5× sooner
- Player archetypes shift mid-match as behavior changes → pair combos create fragile keys that change frequently. Team-level classification is more stable (gradual weight shift)
- 2-player co-op is the fixed mode — pair combos have high correlation (both players fighting same boss), making team-sum a better signal

### 10-2. Variants Per Archetype (10): Multiple Boss Strategies vs Single Counter

**Decision:** 10 different boss AI configurations per team archetype (40 total).

**Purpose:** The 10 variants are NOT 10 player combinations. They are 10 different boss strategies that can be tried against the same team type, e.g. vs Melee teams:
- Variant 0: Ranged kiting build
- Variant 1: CC chain build
- Variant 2: Hit-and-run build
- etc.

The win rate tracker learns which boss build works best against each team type. `VariantsPerArchetype` is a single constant — adjustable at any time (e.g., 5×4=20 or 3×4=12 if fewer builds are needed).

### 10-3. Session-Only Win Rate (No File Persistence)

**Decision:** Win rate data resets every app restart. No JSON file I/O.

**Why:**
- Boss AI definitions (skill loadouts) are still empty — persisted data from empty builds would be noise
- Avoids file I/O complexity and deserialization bugs during rapid iteration
- Session-length learning window (30-60 min) is sufficient: cold start → exploration → exploitation converges in ~15-20 matches with 10 variants
- Can add JSON persistence later without changing any API (RecordResult/SelectVariant interface is stable)

### 10-4. ForceEvaluate Before Read (Stale Weight Prevention)

**Decision:** BossAIPoolManager calls `TeamArchetypeResolver.ForceEvaluate()` before reading `CurrentTeamArchetype`.

**Why:** TeamArchetypeResolver runs on its own 30s eval cycle. If BossAIPoolManager reads the cached value without forcing a refresh, it may use stale data from up to 30 seconds ago — especially problematic at match start when the first eval hasn't fired yet.

### 10-5. Applied Metadata Stored at Defer Time (Attribution Fix)

**Decision:** When a swap is deferred (boss busy), store `_pendingTeamArchetype` and `_pendingFromPool` at queue time, not apply time.

**Why:** Between queuing and applying, the team archetype may change. If metadata is recomputed at apply time, the match result would be attributed to the wrong archetype — polluting win rate data. Storing at defer-time ensures correct attribution even with delayed application.

### 10-6. Wilson Score Lower Bound (Not Raw Win Rate)

**Decision:** Use Wilson score lower bound (95% CI) instead of raw `wins/total` for variant ranking.

**Why:**
- Raw win rate with 1 match: 1/1 = 100% — would always pick this variant
- Wilson lower bound with 1 match: ~0.025 — properly accounts for small sample uncertainty
- With more data (e.g. 10W/2L), Wilson score (~0.59) converges toward raw rate (0.83) but with principled pessimism
- Standard statistical approach for explore/exploit problems (same as Reddit comment ranking)

### 10-7. Default AI Excluded from Win Rate Recording

**Decision:** `RecordMatchResult()` only fires when `_appliedFromPool == true`.

**Why:** The default AI is a fallback used when no pool variant is available. Recording its results against team archetypes would mix default-AI performance with variant-specific performance, producing misleading data.

---

## 11. Editor Debug Infrastructure

### 11-1. BossAISetupTool — Editor Menu System

**Location:** `Assets/ArenaCombat/Scripts/Editor/BossAISetupTool.cs`

**Menu: `ArenaCombat > Boss AI > ...`**

| Menu Item | Mode | Description |
|-----------|------|-------------|
| Full Setup | Edit | Add components + wire all 40 variants to pool manager |
| Wire All Variants To Pool Manager | Edit | SerializedObject-based asset discovery and slot assignment |
| Add Resolver + Tracker To Pool Manager GO | Edit | Auto-add TeamArchetypeResolver + BossAIWinRateTracker |
| Validate Pool Manager | Edit | Detailed status dump — wired counts, null slots, component presence |
| [Runtime] Dump Win Rate Table | Play | Full record dump with Wilson scores per variant |
| [Runtime] Simulate: Melee v0 Wins 5 | Play | Inject 5 wins for Melee/v0 |
| [Runtime] Simulate: Melee v1 Loses 5 | Play | Inject 5 losses for Melee/v1 |
| [Runtime] Simulate: Mixed Data | Play | Multi-archetype injection + auto dump |
| [Runtime] Force Evaluate + Swap Now | Play | Trigger immediate team eval + variant selection |
| [Runtime] Clear All Win Rate Data | Play | Reset all records |

### 11-2. Inspector Debug Fields

**TeamArchetypeResolver:**
- `_debugCurrentArchetype` — current team classification
- `_debugTotalM / R / C` — raw weight sums
- `_debugPlayerCount` — connected player count
- `_debugEvalCount` — total evaluation cycles run

**BossAIWinRateTracker:**
- `_debugTotalRecords` — unique (archetype, variant) keys tracked
- `_debugTotalWins / Losses` — aggregate counts
- `_debugLastSelection` — e.g. "EXPLOIT BossAI_Melee_03 (w=0.591)"
- `_debugLastRecord` — e.g. "Melee/v3 W (4W/1L)"

**BossAIPoolManager:**
- `_debugCurrentVariant` — active AI definition name
- `_debugTeamArchetype` — current team type
- `_debugFromPool` — true if from variant pool (false = default AI)
- `_debugPending` — deferred swap target name (empty if none)

### 11-3. Testing With Empty Skill Data

All 40 variant assets ship with empty skill slots. The system is designed to work in this state:
- `SkillManager` auto-cast loop finds no skills → boss idles (no crash)
- `BossAIPoolManager.ApplyAIVariant()` applies empty slots cleanly
- Win rate tracking works regardless of skill content — it tracks selection and outcome, not skill behavior
- When skills are filled in later, the framework activates immediately with no code changes

---

## 12. Skill System Architecture (Server-Authoritative)

### 12-1. Execution Pipeline

```
SkillManager.Update() [server-only]
    │
    ├─ Auto-cast loop: iterate slots → check cooldown → select by weight
    │
    ├─ SkillManager.ExecuteOrTelegraph(slot)
    │   ├─ Telegraph path: fire TelegraphStartedRpc → wait duration → CompleteTelegraph
    │   └─ Direct path: immediate execution
    │
    ├─ SkillExecutor.ExecuteSkill(def, ctx)
    │   ├─ SkillBinder.BindAll() → resolve SkillDefinition → RuntimeStep[]
    │   ├─ Execute each step sequentially
    │   ├─ Track cooldown, hit count, use count
    │   └─ Fire OnExecuted → SkillCooldownStartRpc to clients
    │
    └─ SkillComponents (37 factory methods)
        ├─ DealDamage / DealDamagePercent / TakeShieldBreakDamage
        ├─ ApplyStatus / ApplyBuff / ApplyDebuff / Cleanse / Dispel
        ├─ SpawnProjectile / SpawnArea
        ├─ Knockback / Pull / MoveBy
        ├─ Heal / HealPercent / AddShield
        ├─ ModifyStat / ResetCooldown
        └─ Conditional (IfHPBelow, IfTargetHasStatus, etc.)
```

### 12-2. Skill Definition Structure (ScriptableObject)

```
SkillDefinition SO
    ├─ ID (int) — unique, used for RPC resolution via SkillRegistry
    ├─ DisplayName / Description (Korean)
    ├─ TargetType: Single / Area / Self / Direction
    ├─ CooldownTime (float)
    ├─ TelegraphDuration (float) — 0 = no telegraph
    ├─ RoleTags: SkillRoleTag[] — classification for archetype resolver
    ├─ CounterTags: SkillRoleTag[] — what this skill counters
    └─ Steps: SkillStep[] — composite execution tree
```

### 12-3. Skill Inventory

| Category | Count | Examples |
|----------|-------|---------|
| Player Skills | 12 | ExecutionSpike, FortressArmor, ErosionField, SurvivalPulse, CrushingBarrage, HuntingMark, SealChain, CollapseRoar, BarrierBreaker, OverchargeMode, PiercingShot, RuptureMagazine |
| Boss Skills | 9+2 | Same pool (minus some) + SurvivalPulse(Self) + OverchargeMode(Self) |
| Total Definitions | 29 | In SkillLibrary |

### 12-4. Card System — Skill Acquisition

```
Match Start → CardManager offers 3 random cards from catalog (12 cards)
    → Player picks → CardManager.PickCardRpc (server validation)
    → Server: PNC3D.SetSkillSlotServer(slotIndex, skillDef)
    → Server: SkillSlotSetRpc → client UI update
    → SkillManager adds to auto-cast rotation
```

Level-up triggers new card offer. Max 5 skill slots per player.

---

## 13. Buildup Integration Path Summary

### 13-1. Integration Strategy

**Source:** `C:\Users\paek6\Downloads\Buildup\Buildup` — same game, different branch with more advanced systems.

**Approach:** Phase X in the roadmap — step-by-step import with Codex review at each stage.

### 13-2. Import Phases

| Phase | Content | Status |
|-------|---------|--------|
| X0 | Environment prep (folders, namespace, team assignment, ICombatant stub) | DONE |
| X1 | Visual/data assets (VFX packs, meshes, textures, Chapter1 scene, SOs, prefabs) | DONE |
| X2 | Code import (StatManager, StateManager, SkillManager, 37 SkillComponents, SkillLibrary, SkillBinder, SkillExecutor, pools) | DONE |
| X3 | PNC3D wiring (ICombatant implementation, skill slot sync, stat bridging) | DONE |
| X4 | Boss system (BNC3D, BossManager, BossPhase, ML-Agents integration) | DONE |

### 13-3. Key Decisions During Integration

- **GUID Preservation:** All Buildup `.meta` GUIDs preserved so that imported `.asset` files resolve correctly without manual rebinding
- **English Rewrite:** All `.cs` files rewritten in clean English (Buildup originals had Korean comments that caused mojibake in UTF-8 pipeline)
- **Namespace Wrap:** All code placed under `ArenaCombat.Core.*` namespaces (Buildup used default namespace)
- **ProjectilePool Bug Fix:** Double-enqueue bug in Buildup's `ProjectilePool.Get()` fixed during import (Codex-discovered, documented as DIVERGENCE FROM BUILDUP)
- **ML Observation Surface:** All public API names kept byte-identical to Buildup for ML-Agents observation compatibility

---

## 14. C3b Codex Review — Detailed Issue Log

### Round 1 (REVISE) — 12 Issues Found

| # | Severity | Issue | Fix |
|---|----------|-------|-----|
| 1 | Critical | Wilson score integer division: `wins/total` → always 0 for int operands | Cast to `(float)rec.wins / n` |
| 2 | Critical | TeamArchetypeResolver reading stale post-decay weights from classifier | Own 30s eval cycle with independent weight summation |
| 3 | High | `OnPlayerArchetypeChanged` fires per-player, not sufficient for team changes | Resolver does own eval, doesn't subscribe to classifier events |
| 4 | High | Match-start: applied default AI before trying pool | Removed early `_currentDef == null` branch, always tries pool first |
| 5 | High | Wrong archetype attribution: match end reads current (not applied) archetype | Stored `_appliedTeamArchetype`, `_appliedVariantIndex`, `_appliedFromPool` |
| 6 | Medium | Default AI results polluting variant stats | `_appliedFromPool` guard in `RecordMatchResult()` |
| 7 | Medium | SelectVariant didn't handle null/empty candidates | Explicit null checks + `isDefault` filter |
| 8 | Medium | Tie-breaking not randomized — always first candidate | `Random.Range()` among ties in both explore and exploit |
| 9 | Medium | WinRecord struct copy: `rec.wins++` mutates local copy | `_records[key] = rec` write-back pattern |
| 10 | Low | Singleton retry pattern missing (race condition at scene load) | `Update()` poll for null instances |
| 11 | Low | `isDefault` variants in pool array corrupt selection | `BuildVariantPool()` skips `def.isDefault` entries with warning |
| 12 | Low | BNC3D log referenced removed `playerType1`/`playerType2` | Updated to `teamArchetype`/`variantIndex` |

### Round 2 (REVISE) — 2 Remaining Blockers

| # | Severity | Issue | Fix |
|---|----------|-------|-----|
| 1 | High | Cold-start still defaulted before pool selection attempt | Removed `if (_currentDef == null)` early branch entirely |
| 2 | High | Deferred swap recomputed metadata at apply-time instead of using stored values | Added `_pendingTeamArchetype`, `_pendingFromPool` fields stored at defer time |

### Round 3 (PASS WITH NOTES) — 2 Non-Blocking Notes

| # | Note | Action |
|---|------|--------|
| 1 | `_pendingVariantIndex` field was unused | Removed |
| 2 | `ForceEvaluate()` can cause duplicate log when called from both `EvaluateAndSwap()` and `HandleTeamArchetypeChanged()` | Accepted — benign duplicate log, no logic impact |

---

## 15. Server Authority Enforcement Map

All game state mutations are server-gated. Client sends intent via RPC → server validates → server mutates → server broadcasts result.

```
                    ┌─────────────────────────────────────────────┐
                    │              SERVER AUTHORITY                │
                    │                                             │
  Client Inputs     │   Server Processing         → Client Sync   │
  ─────────────     │   ─────────────────         ──────────────  │
  WASD movement ──► │   ProcessServerMovement     → networkPos NV │
  Mouse look    ──► │   ProcessServerRotation     → networkRotY   │
  Attack button ──► │   RequestAttackRpc          → AttackResult  │
  Parry button  ──► │   RequestParryRpc           → ParryStarted  │
  Rope click    ──► │   RequestRopeRpc            → rope NV       │
  Card pick     ──► │   PickCardRpc               → SkillSlotSet  │
  Restart click ──► │   RequestRestartRpc         → match state   │
                    │                                             │
  (no input)        │   SkillManager auto-cast    → CooldownStart │
  (no input)        │   StatManager.Tick()        → HP/status NV  │
  (no input)        │   BossAIPoolManager eval    → AI swap       │
  (no input)        │   TeamArchetypeResolver     → (internal)    │
  (no input)        │   BossAdaptiveWeights       → (internal)    │
                    └─────────────────────────────────────────────┘
```

### Server-Only Systems (No Client Counterpart)

| System | Purpose | Runs On |
|--------|---------|---------|
| CombatManager3D | Hit detection, K/D/A, damage routing | Server |
| GameStateManager | Match state FSM, player count, card offers | Server |
| BossManager | Boss spawn/despawn lifecycle | Server |
| BossAIPoolManager | AI variant selection and swap | Server |
| TeamArchetypeResolver | Team archetype classification | Server |
| BossAIWinRateTracker | Win rate tracking and variant selection | Server |
| PlayerArchetypeClassifier | Per-player behavior classification | Server |
| PlayerBiasTracker | Raw action counting | Server |
| BossAdaptiveWeights | Per-skill weight adjustment | Server |

---

## 16. Data Flow — Match Lifecycle

```
[SampleScene: Lobby]
    Player creates/joins lobby (Relay + Lobby SDK)
    Host starts → scene load Chapter1

[Chapter1: WaitingForPlayers]
    Players spawn (PlayerSpawnManager → PNC3D)
    Both spawned → StartMatchCountdown (3s)

[Chapter1: InProgress]
    BossManager.TrySpawnBoss()
    GameStateManager starts card offer cycle
    PlayerArchetypeClassifier begins tracking
    TeamArchetypeResolver evaluates every 30s
    BossAIPoolManager selects initial variant (cold start → random)
    SkillManager auto-cast loop active
    BossAdaptiveWeights adjusts per hit/miss/parry

    ┌─ Player kills boss (HP → 0) ──────────────────┐
    │   BossManager.HandleBossDefeated               │
    │   GSM.EndMatch(BossDefeated)                   │
    │   BossAIPoolManager.RecordMatchResult (loss)   │
    └────────────────────────────────────────────────┘

    ┌─ Boss kills all players ──────────────────────┐
    │   CombatManager3D.AreAllPlayersDead()          │
    │   GSM.EndMatch(AllPlayersDead)                 │
    │   BossAIPoolManager.RecordMatchResult (win)    │
    └────────────────────────────────────────────────┘

[Chapter1: MatchEnd]
    MatchEndUI displays Victory/Defeat
    Movement + respawn + auto-cast blocked
    Player clicks Restart

[Chapter1: WaitingForPlayers (restart)]
    DespawnBoss
    Reset: HP, skills, position, cooldowns, bias counters
    TeamArchetypeResolver → Hybrid
    BossAIPoolManager → clear current/pending
    StartMatchCountdown → InProgress (new cycle)
    Next variant selection uses updated win rate data
```

---

> Last updated: 2026-05-23
