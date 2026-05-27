# Network Synchronization Audit

**Date:** 2026-05-21  
**Scope:** Full server-client sync verification across PlayerNetworkController3D, BossNetworkController3D, GameStateManager, CombatManager3D, SkillManager, StatManager

---

## 1. NetworkVariable Inventory

### PlayerNetworkController3D (PNC3D)
| Variable | Type | Purpose | Sync Status |
|---|---|---|---|
| `networkPosition` | `Vector3` | Player world position | OK — server writes FixedUpdate, client interpolates |
| `networkYaw` | `float` | Player facing rotation | OK — server writes, client interpolates |
| `networkHP` | `float` | Current health points | OK — server writes via StatManager, UI reads |
| `networkIsAlive` | `bool` | Alive/dead flag | OK — server toggles on death/respawn |
| `networkStateId` | `CharacterStateId` | Main/Sub state (Idle, Hit, Dead, Roping...) | OK — server sets via SetStateId() |
| `networkStatusMask` | `StatusMask` | Bitflag status effects (Stunned, Rooted, Slowed...) | PARTIAL — see Issue #1 |
| `networkTeamId` | `TeamId` | Team assignment | OK — server sets at spawn |
| `networkIsRoping` | `bool` | Rope active flag | OK — server sets start/end |
| `networkRopeTarget` | `Vector3` | Rope target position | OK — server sets on rope start |

### BossNetworkController3D (BNC3D)
| Variable | Type | Purpose | Sync Status |
|---|---|---|---|
| `networkHP` | `float` | Boss current HP | OK — server writes |
| `networkIsAlive` | `bool` | Boss alive flag | OK — server toggles |
| `networkPosition` | `Vector3` | Boss world position | OK — server writes every FixedUpdate tick |
| `networkYaw` | `float` | Boss facing rotation | OK — added 2026-05-20 session |
| `networkCurrentPhase` | `BossPhase` | Phase transition (None/Phase1/Phase2/Defeated) | OK — server sets, client subscribes for VFX |

### GameStateManager (GSM)
| Variable | Type | Purpose | Sync Status |
|---|---|---|---|
| `networkMatchState` | `MatchState` | Match lifecycle (None/WaitingForPlayers/InProgress/Ended) | OK |
| `networkGameMode` | `GameMode` | Current game mode | OK |
| `networkTimer` | `float` | Match timer | OK |
| `networkRoundNumber` | `int` | Current round | OK |
| `networkCardDraftActive` | `bool` | Card draft UI active | OK |
| `networkCardDraftRound` | `int` | Card draft round counter | OK |
| `networkCardDraftTimer` | `float` | Card draft countdown | OK |
| `networkMatchEndReason` | `MatchEndReason` | Why match ended | OK |

---

## 2. RPC Inventory

### PNC3D RPCs
| Direction | RPC | Purpose |
|---|---|---|
| `SendTo.Server` | `MoveInputRpc` | Client → Server move input |
| `SendTo.Server` | `AttackRpc` | Client → Server attack request |
| `SendTo.Server` | `ParryRpc` | Client → Server parry request |
| `SendTo.Server` | `RopeRpc` | Client → Server rope request |
| `SendTo.Server` | `PerkTriggerRpc` | Client → Server perk use |
| `SendTo.Owner` | `SkillCooldownStartRpc` | Server → Owner cooldown mirror |
| `SendTo.Owner` | `SkillResetRpc` | Server → Owner full skill/cooldown reset |
| `SendTo.Owner` | `AttackRejectedFromOwnerRpc` | Server → Owner reject notification |
| `SendTo.Owner` | `ParryRejectedFromOwnerRpc` | Server → Owner reject notification |
| `SendTo.ClientsAndHost` | `RopeResultRpc` | Rope started/failed broadcast |
| `SendTo.ClientsAndHost` | `RopeEndRpc` | Rope ended broadcast |
| `SendTo.ClientsAndHost` | `RespawnEventRpc` | Respawn visual broadcast |
| `SendTo.ClientsAndHost` | `DeathEventRpc` | Death visual broadcast |
| `SendTo.ClientsAndHost` | `PerkTriggerResultRpc` | Perk trigger result broadcast |
| `SendTo.ClientsAndHost` | `HitEventRpc` | Hit feedback broadcast |
| `SendTo.ClientsAndHost` | `ParryVFXRpc` | Parry visual broadcast |

### BNC3D RPCs
| Direction | RPC | Purpose |
|---|---|---|
| `SendTo.ClientsAndHost` | `TelegraphRpc` | Boss telegraph visual broadcast |

### GSM RPCs
| Direction | RPC | Purpose |
|---|---|---|
| `SendTo.Server` | `RequestRestartRpc` | Client → Server restart request |
| `SendTo.Server` | `ClientSpawnReadyRpc` | Client → Server spawn ack |
| `SendTo.Server` | `SubmitCardChoiceRpc` | Client → Server card draft choice |
| `SendTo.ClientsAndHost` | `ShowMatchEndUIRpc` | Match end screen broadcast |
| `SendTo.ClientsAndHost` | `HideMatchEndUIRpc` | Hide end screen broadcast |
| `SendTo.ClientsAndHost` | `ForceCloseCardDraftRpc` | Close draft UI broadcast |
| `SendTo.ClientsAndHost` | `ShowCardDraftRpc` | Show draft options broadcast |
| `SendTo.SpecifiedInParams` | `CardChoiceResultRpc` | Draft result to specific client |

### CombatManager3D RPCs
| Direction | RPC | Purpose |
|---|---|---|
| `SendTo.ClientsAndHost` | `AttackResultRpc` | Attack outcome broadcast (log only) |
| `SendTo.ClientsAndHost` | `ParryStartedRpc` | Parry window opened broadcast (log only) |
| `SendTo.ClientsAndHost` | `ParrySuccessRpc` | Parry success broadcast (log only) |
| `SendTo.ClientsAndHost` | `PlayerKilled3DRpc` | Kill event broadcast (log only) |
| `SendTo.ClientsAndHost` | `PerkTriggerAccepted3DRpc` | Perk trigger broadcast (log only) |

---

## 3. Issues Found — By Severity

### HIGH: StatusMask / StatManager Stun Desync

**Location:** `PNC3D` lines 2150-2151, `StatManager.ApplyStatus()`

**Problem:** `ICombatant.ApplyStatus()` delegates directly to `StatManager.ApplyStatus()`, which manages status effects via coroutines and internal `_runtimeStats` fields. However, it does NOT update `networkStatusMask`. The movement check `StatusHelper.CanMove(networkStatusMask.Value)` at lines 512, 797, 894 reads from the NV — so skill-applied stuns (e.g. CC skills with `StatusType.Stunned`) won't actually block movement.

**Currently working:** Only `AddStatus(StatusMask.Stunned)` / `RemoveStatus(StatusMask.Stunned)` called directly by PNC3D (parry stun at line 232, rope root at line 1082, invuln at line 1618) correctly update the NV. All StatManager-routed status effects are invisible to the NV.

**Impact:** CC skills that apply stun/root/silence via `ICombatant.ApplyStatus()` will not prevent movement or actions on the network level. The stun only affects `StatManager._runtimeStats` (server-local struct) but never propagates to `networkStatusMask` which all action gates check.

**Fix:** Bridge `StatManager.ApplyStatus()` calls to `PNC3D.AddStatus()` / `PNC3D.RemoveStatus()` so the NV stays in sync. Either:
- (A) Have `StatManager` callback to PNC3D when status changes, or
- (B) Have `ICombatant.ApplyStatus()` in PNC3D also call `AddStatus()` with the equivalent `StatusMask` flag.

---

### MEDIUM: PNC3D ICombatant Methods Missing IsServer Guard

**Location:** `PNC3D` lines 2135-2167

**Problem:** `ICombatant.TakeDamage()`, `ICombatant.RecoverHP()`, `ICombatant.AddShield()`, `ICombatant.ApplyStatus()`, `ICombatant.ApplyBuff()`, `ICombatant.ApplyDebuff()` all forward directly to `_statMgr` without checking `IsServer`. If a client somehow calls these (e.g. a SkillStep executes on client), they silently mutate client-local `StatManager` state with no NV sync.

**Current mitigation:** SkillManager/SkillExecutor are gated with `if (!IsServer) return;` in Update, so skill steps should only run server-side. But the interface itself lacks protection.

**Risk:** Low probability but undefined behavior if any code path calls ICombatant on client.

**Fix:** Add `if (!IsServer) return;` guards to all mutating `ICombatant` implementations, or add a single early-out in `StatManager` constructor/init.

---

### MEDIUM: StatManager.Tick() Never Called

**Location:** `StatManager.Tick()` line 174, `PNC3D` — no call site found

**Problem:** `StatManager.Tick(float dt)` handles HP regeneration (`_hpRegenRate`), but no code in PNC3D or any controller calls it. HP regen from stats is silently broken.

**Impact:** `PlayerStatsSO.hpRegenRate` has no runtime effect. Any buff or stat that sets HP regen rate does nothing.

**Fix:** Call `_statMgr?.Tick(Time.fixedDeltaTime)` from PNC3D's server-side `FixedUpdate` or `UpdateServerTimers()`.

---

### MEDIUM: Shield Value Not Synced to Client

**Location:** `StatManager._currentShield` — no corresponding NetworkVariable in PNC3D

**Problem:** `StatManager.AddShield()` and shield absorption in `ReceiveDamage()` work server-side, but shield amount is never exposed via NV. Client HUD cannot display shield bar.

**Impact:** Shield-granting skills work mechanically (damage absorbed server-side) but are invisible to client UI.

**Fix:** Add `networkShield` NV to PNC3D, sync from StatManager when shield changes.

---

### ~~MEDIUM: SkillManager Slot Sync is Convention-Based~~ — FIXED (SYNC-FIX Phase 2, 2026-05-21)

Server-authoritative `SetSkillSlotServer()` + `SkillSlotSetRpc(SendTo.Owner)` replaces convention-based sync. `ApplyInitialLoadoutServer` and `CardManager.HandleSelectionResolved` now use the RPC path.

---

### LOW: Boss Telegraph Cancel Not RPC'd

**Location:** `BNC3D` — `TelegraphRpc()` exists but no cancel RPC

**Problem:** If boss telegraph is interrupted server-side, client may still show the telegraph indicator until timeout.

**Impact:** Visual-only; no gameplay impact since damage is server-authoritative.

---

### LOW: K/D/A Tracking Server-Only

**Location:** `CombatManager3D.PlayerKilled3DRpc()` — broadcast is log-only

**Problem:** Kill/death/assist counts are not stored in any NV. Client cannot display scoreboard.

**Impact:** No scoreboard feature exists yet, so no functional impact.

---

### ~~LOW: Buff/Debuff Durations Not Visible to Client~~ — FIXED (SYNC-FIX Phase 2, 2026-05-21)

`networkBuffMask`/`networkDebuffMask` NVs added to PNC3D and BNC3D. StatManager fires OnBuffApplied/OnBuffRemoved/OnDebuffApplied/OnDebuffRemoved/OnBuffDebuffBulkCleared events. Bridge handlers update NVs. Remaining: buff duration timers (not just presence) for client UI display — deferred to buff UI implementation.

---

## 4. What's Working Correctly

- Player position/rotation: NV + interpolation (both host and client)
- Boss position/rotation: NV + interpolation (fixed 2026-05-20)
- HP sync: NV for both player and boss, HUD reads correctly
- Match state lifecycle: Full NV coverage (MatchState, Timer, Round, etc.)
- Skill cooldown UI: `SkillCooldownStartRpc` mirrors cooldown start to owner client (fixed 2026-05-20)
- Death/Respawn: Full NV + RPC + visual broadcast chain
- Rope system: NV flags + RPC start/end + visual broadcast
- Card draft: Full RPC pipeline (show → choose → result)
- Boss phase transitions: NV + client VFX subscription
- Parry stun: Directly updates `networkStatusMask` via `AddStatus()` — works correctly
- Invulnerability: Directly updates `networkStatusMask` via `AddStatus()` — works correctly
- **Skill-applied CC effects (stun/root/silence/slow)**: Bridged via StatManager events → StatusMask NV (SYNC-FIX 2026-05-21)
- **Shield**: networkShield NV added, synced from StatManager (SYNC-FIX 2026-05-21)
- **Boss StatusMask**: networkStatusMask NV added to BNC3D (SYNC-FIX 2026-05-21)
- **HP Regen**: StatManager.Tick() now called in FixedUpdate (SYNC-FIX 2026-05-21)
- **Telegraph cancel**: TelegraphCancelledRpc broadcast on cancel (SYNC-FIX 2026-05-21)
- **Skill slot sync**: Server-authoritative SetSkillSlotServer + SkillSlotSetRpc (SYNC-FIX Phase 2, 2026-05-21)
- **Buff/debuff visibility**: networkBuffMask/networkDebuffMask NVs on PNC3D + BNC3D (SYNC-FIX Phase 2, 2026-05-21)

---

## 5. Fix Status (2026-05-21 SYNC-FIX)

1. **StatusMask/StatManager bridge** (HIGH) — FIXED: OnStatusApplied/OnStatusRemoved/OnStatusBulkCleared events + bridge handlers in PNC3D/BNC3D
2. **StatManager.Tick() integration** (MEDIUM) — FIXED: called in FixedUpdate of both PNC3D and BNC3D
3. **ICombatant IsServer guards** (MEDIUM) — FIXED: all 10 mutating methods guarded (including NotifyParryReward)
4. **Shield NV** (MEDIUM) — FIXED: networkShield NV added to PNC3D with FixedUpdate sync
5. **BNC3D StatusMask NV** (MEDIUM) — FIXED: networkStatusMask NV added with same bridge pattern
6. **Telegraph cancel RPC** (LOW) — FIXED: OnTelegraphCancelled event + TelegraphCancelledRpc

## 6. Fix Status (2026-05-21 SYNC-FIX Phase 2)

7. **Skill slot sync** (MEDIUM) — FIXED: SetSkillSlotServer + SkillSlotSetRpc(SendTo.Owner). ApplyInitialLoadoutServer and CardManager.HandleSelectionResolved use server-authoritative path. Convention-based client SetSlot removed.
8. **Buff/debuff visibility** (LOW) — FIXED: BuffMask/DebuffMask byte enums, StatManager events, networkBuffMask/networkDebuffMask NVs on PNC3D + BNC3D, bridge handlers, Die/Respawn/Defeat cleanup via ClearAllEffects()
