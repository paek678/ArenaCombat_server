# SKILL SYSTEM DESIGN — Auto-Cast, Host-Authoritative

> **Purpose**: confirmed design decisions for the skill execution pipeline. Reference document for X2-5 through X2-13 ROADMAP items. When in doubt about how a skill should fire / hit / sync, this file is the tie-breaker.
>
> Cross-references:
> - [TARGET_ARCHITECTURE.md](TARGET_ARCHITECTURE.md) — overall layer assignment.
> - [ROADMAP.md](ROADMAP.md) — sub-cycle execution order.
> - [BUILDUP_INTEGRATION_PLAN.md](BUILDUP_INTEGRATION_PLAN.md) — Buildup origin file mapping.
>
> Last revised: 2026-05-12.

---

## 1. Design Principle

**Vampire Survivors style auto-cast**. Skills fire automatically on cooldown when their conditions match. No skill input keys. The player controls movement / parry / rope; the skill system reacts to game state.

Consequences:
- **No `RequestSkillRpc`**. Server has all input it needs (movement / parry / rope already arrive via existing RPCs).
- **No `SkillRejectedRpc`**. Auto-cast doesn't need rejection feedback — the slot just stays not-fired.
- **No skill input slot keys**. Slots are priority-ordered (Buildup pattern), not key-mapped.
- **Deterministic from server state**. Client never predicts; client only renders broadcast cues.

---

## 2. Component Catalog (Buildup → Ours)

| Component | Type | Per-entity / Singleton | Phase | Role |
|---|---|---|---|---|
| `SkillDefinition` | ScriptableObject | data | X2-5 ✅ | Metadata + RuntimeStep composite tree |
| `SkillContext` | C# class | per-cast (allocated) | X2-5 ✅ | Runtime data (Caster, Target, CastPos, CastDir, snapshot) |
| `SkillRegistry` | ScriptableObject | singleton (master) | X2-5 ✅ | All-skills catalog with RoleTag-based filtering |
| `SkillExecutor` | MonoBehaviour | per-entity | X2-6 ✅ | Cooldown tracking + composite tree execution |
| `IProjectile` / `IPoolable` / `SkillProjectile` / `ProjectilePool` | interfaces + MonoBehaviour | per-instance + scene singleton | **X2-7 NEXT** (paired, circular dep) | Pooled projectile, server-side movement + collision + NGO-aware pool |
| `IPersistentArea` / `SkillArea` / `PersistentAreaPool` / `PersistentAreaManager` | interface + MonoBehaviour ×3 | per-instance + scene singletons | X2-8 (paired, circular dep) | Sticky AoE + pool + manager |
| `SkillComponents` | 37 static `SkillStep` impls | code | X2-9 | DealDirectionalHit / ApplyInArea / LaunchProjectile / CheckParry / etc. (calls `ProjectilePool.Instance` + `PersistentAreaManager.Instance` — dependency reason for X2-7/8 first) |
| `SkillLibrary` | static class | code | X2-10 | Composite tree definitions per skill |
| `SkillBinder` | static class | one-shot | X2-10 | Injects RuntimeStep delegates into SkillDefinition SOs at game start |
| `SkillManager` | MonoBehaviour | per-entity | X2-11 | Auto-cast tick: scans 5 slots, picks first ready, calls Executor |

**Player vs Boss**: identical pipeline. Same `SkillExecutor + SkillManager` components. The only difference is **which `SkillDefinition` SOs sit in the slots** — boss-tagged via `RoleTag`. Boss ticks via `BossNetworkController3D` instead of `PNC3D` but calls the same `SkillManager.AutoCastTick(dt)`.

---

## 3. End-to-End Auto-Cast Flow

```
[Server FixedUpdate Tick — per Combatant]
     │
     ▼
SkillManager.AutoCastTick(dt)
     │
     ├─ for slot in slots[0..N]:        // priority order, first-ready wins
     │     ├─ skill ?= slot
     │     ├─ if skill == null → skip
     │     ├─ if !SkillExecutor.CanUse(skill) → skip   (cooldown not ready)
     │     ├─ if !StateManager.CanCast → break         (Silence / Stunned / Dead etc.)
     │     ├─ ctx = BuildSkillContext(skill, this)
     │     ├─ if !skill.RuntimeCondition?(ctx) → skip  (HP% / distance / target check)
     │     └─ FOUND → break with (skill, ctx)
     │
     ├─ if no slot found → end tick
     │
     ▼
StateManager.NotifyCastStart()           ← state -> Casting
     │
     ▼
SkillExecutor.Execute(skill, ctx)         ← composite tree runs server-only
     │
     ├─ ctx.RefreshSnapshot()             ← TargetHpPercent / TargetDistance / TargetCasting
     ├─ skill.RuntimeStep(ctx)            ← composite tree mutates ctx + calls components
     │     │
     │     ├─ Common shapes (most skills):
     │     │     ├─ LaunchProjectile(prefab, speed, range)
     │     │     │     → ProjectilePool.Spawn(prefab, ctx.CastPosition, ctx.CastDirection, ctx.Caster)
     │     │     │     → SkillProjectile NetworkObject.Spawn (server)
     │     │     │     → projectile FixedUpdate move (server) → impact at arrival point
     │     │     │     → DealDirectionalHit / ApplyInArea / SpawnPersistentArea at impact
     │     │     │
     │     │     ├─ ApplyInArea(radius)   ← immediate AoE at CastPosition
     │     │     │     → Physics.OverlapBox + TeamId filter + StatManager.DealDamage
     │     │     │
     │     │     └─ SpawnPersistentArea(prefab, duration)
     │     │           → SkillArea NetworkObject.Spawn → PersistentAreaManager.Register
     │     │           → ticked at intervals, periodic StatManager.DealDamage / ApplyStatus
     │     │
     │     └─ Effect-only shapes:
     │           ApplyStatus / ApplyBuff / Knockback / RecoverHP — direct StatManager calls
     │
     ▼
SkillExecutor.RecordCooldown(skill)       ← server-internal Dictionary<SkillId, lastUseTime>
     │
     ▼
StateManager.NotifyCastEnd()              ← (or auto-end based on cast duration)
     │
     ▼
SkillStartedRpc(combatantId, skillId, castPos, castDir)   ── SendTo.ClientsAndHost
     │   (VFX / animation / SFX cue only — no result data)
     │
     ▼
[All Clients]
     ├─ Trigger animation, particle, sound
     ├─ Spawned NetworkObject (projectile / area) auto-syncs via NGO
     └─ Damage / status results arrive via existing StatManager NV / DamageEventRpc
```

**One skill per tick rule**: if multiple slots become ready in the same FixedUpdate tick, fire the highest-priority one only. Remaining ready slots wait for the next tick. Designer controls burst pacing via `Cooldown` values.

---

## 4. Special Trigger Types (Beyond Cooldown Auto-Cast)

Some skill slots are tagged to fire on specific game events instead of (or in addition to) cooldown ticks. Server-resolved inline at the relevant event handler. **No new RPC required** — the events already arrive on server.

### 4.1 Parry-Triggered Skill

```
[Server] CombatManager3D.TryProcessAttack3D
   parry detected
       │
       ├─ Existing: ParrySuccessRpc broadcast, parry-stun routing
       │
       └─ NEW (X3 wiring): SkillManager.OnParrySuccess(defender, attacker, parryPos)
              └─ scan slots tagged ParryTrigger
              └─ Build ctx with CastPosition = parryPos, CastDirection = attacker - defender
              └─ Execute (same auto-cast path)
```

### 4.2 Rope-Arrival-Triggered Skill

```
[Server] PNC3D.ExecuteQueuedRopeAction → arrival
       │
       ├─ Existing: RopeEndRpc broadcast, networkIsRoping = false
       │
       └─ NEW (X3 wiring): SkillManager.OnRopeArrival(player, arrivalPos)
              └─ scan slots tagged RopeArrivalTrigger
              └─ Build ctx with CastPosition = arrivalPos, CastDirection = movement direction
              └─ Execute
```

### 4.3 Other potential triggers (later)

- `OnKillTrigger` — fire when caster lands killing blow
- `OnHurtTrigger` — fire when caster takes damage
- `OnAllyDownTrigger` — fire when teammate is downed (co-op support skill)

These are slot tags, not separate types. Each `SkillDefinition` declares which triggers it responds to. Default = `CooldownAuto`.

---

## 5. CastPosition / CastDirection / Target Resolution

Driven by `SkillDefinition.TargetType` enum (X2-2: `Single` / `Area` / `Self` / `Direction`).

| TargetType | CastPosition | CastDirection | PrimaryTarget |
|---|---|---|---|
| `Single` | `caster.position` | toward auto-target | nearest alive enemy (by default) |
| `Area` | `caster.position` (or auto-target position) | N/A | optional auto-target for AoE center |
| `Self` | `caster.position` | N/A | `caster` itself |
| `Direction` | `caster.position` | `caster.lookYaw` (server already has this from `RequestMoveRpc`) | none |

**Parry/Rope triggers override `CastPosition`** to the event location (parry impact / rope arrival). Server resolves at trigger time.

**Auto-target default policy**: nearest alive enemy with valid LOS. Override per-skill via `RoleTag`:
- `RoleTag = "Burst"` → lowest-HP-percent enemy
- `RoleTag = "Counter"` → enemy currently casting (`ICombatant.IsCasting == true`)
- `RoleTag = "AntiTank"` → highest-HP enemy
- (designers extend as needed in `SkillLibrary`)

---

## 6. Server Authority Contract

**All decisions on server. Always.**

| Concern | Server-only | Notes |
|---|---|---|
| Cooldown timer | yes | `Dictionary<SkillId, float>` in SkillExecutor (server-internal, NOT NV) |
| Auto-cast condition evaluation | yes | `RuntimeCondition` delegate runs in server tick |
| Composite tree execution | yes | `SkillStep` delegates run in server context |
| Projectile movement | yes | `SkillProjectile.FixedUpdate` server-only (clients mirror via NV/transform sync) |
| Projectile collision | yes | `Physics.OverlapBox` on server, same as B1-4 attack pattern |
| Persistent area tick | yes | `PersistentAreaManager.Tick` server-only |
| Damage / status application | yes | via `StatManager` (already server-only contract from X2-4) |
| VFX / animation / SFX | client | triggered by `SkillStartedRpc` broadcast |
| Cooldown UI | client | derived locally from `last SkillStartedRpc time + skill.Cooldown` (no extra RPC) |
| Projectile mesh + particle | client | NetworkObject auto-syncs transform; client renders only |

**Forbidden**: client-side cooldown reservation, client-side hit prediction, client-side projectile spawn.

---

## 7. Projectile Collision Layer / TeamId Filtering

Caster's team → opposite team only. Friendly fire blocked.

```csharp
SkillProjectile.Initialize(SkillContext ctx) {
    casterTeam = ctx.Caster.TeamId;
    hitMask    = casterTeam switch {
        TeamId.Team1 => bossLayerMask,    // player projectile -> boss only
        TeamId.Team2 => playerLayerMask,  // boss projectile -> players only
        _            => allCombatLayer,   // teamless (e.g., environmental)
    };
}

SkillProjectile.OnTriggerEnter(Collider other) {
    if (!IsServer) return;
    if (((1 << other.gameObject.layer) & hitMask) == 0) return;
    if (!CombatantRegistry.TryResolve(other, out ICombatant hit)) return;
    if (hit.TeamId == casterTeam) return;  // double-check
    if (alreadyHit.Contains(hit)) return;  // dedup per projectile
    
    casterStatManager.DealDamage(hit, projectileDamage);
    alreadyHit.Add(hit);
    // optional: trigger impact event (DealDirectionalHit / ApplyInArea / SpawnPersistentArea)
}
```

Same pattern as `CombatManager3D` B1-4 (`playerLayer + TeamId` filter). Validated path.

---

## 8. Slot Count + Extensibility

```csharp
// SkillManager.cs
public const int SlotCount = 5;
[SerializeField] private SkillDefinition[] _slots = new SkillDefinition[SlotCount];
```

**To add a slot**: change `5` → `N` in one line. Card draft (X2-12) reads `SkillManager.SlotCount` for offer count alignment. Inspector array auto-resizes on next reload.

Why not `List<SkillDefinition>`: dynamic add/remove not needed (slots fixed per match). Why not `SerializeField int`: const enables compile-time array size = clearer Inspector layout, no runtime resize edge cases.

---

## 9. Confirmed Design Parameters

| Parameter | Value | Where decided |
|---|---|---|
| Slot count | `5` (const, easy to bump) | Q-by-Q sign-off, 2026-05-12 |
| Tick rate | **Current (post X3-6): `SkillManager.Update` per-frame, server-only gate + `IsGlobalCardDraftActive` gate**. AutoCastTick extraction to host FixedUpdate driver = future polish, not committed. | Q4 + X2-11 + X3-6 |
| Skills per tick | `1` (first ready wins, others wait) | Q5 lean accepted |
| Auto-target default | nearest alive enemy | Q6 lean accepted |
| Auto-target override mechanism | `SkillDefinition.RoleTag` | Q6 follow-up |
| Registry shape | single master `SkillRegistry` SO | Q1 lean accepted |
| `SkillContext` lifetime | per-cast `new` allocation | Q2 lean accepted |
| `SkillId` type | `string` (Buildup parity) | Q3 lean accepted |
| Player-vs-boss skill pipeline | identical (same components, different definition pool) | confirmed |
| Projectile collision | caster team → opposite team only (TeamId filter) | confirmed |
| Hit detection | server-only `Physics.OverlapBox` (B1-4 pattern) | confirmed |
| Cooldown storage | server-internal `Dictionary<SkillId, float>` (NOT NetworkVariable) | spec §6 principle |
| Cooldown UI sync | client-derived from `SkillStartedRpc` time + `skill.Cooldown` | confirmed |

---

## 10. Spec Table Diff (vs original NV / RPC plan)

Original spec had skill-related RPCs assuming key-input model. Auto-cast pivot removes these:

| Spec entry | Original status | Decision | Reason |
|---|---|---|---|
| C→S #4 `RequestSkillServerRpc` | planned | **REMOVE** | Auto-cast — no client intent |
| S→Target #2 `SkillRejectedClientRpc` | planned | **REMOVE** | No request → no rejection |
| S→All #5 `SkillStartedClientRpc` | planned | **KEEP** | VFX/UI cue still required |
| Server authority #5 (skill cooldown / use check) | planned | **KEEP** | Server-internal state, gates auto-cast tick |

Basic attack RPC (`RequestBasicAttackServerRpc`) status: **deferred** — basic attack input model not yet decided. Current `PNC3D.RequestAttackRpc` stays as-is until basic attack design is reopened.

---

## 10a. ML-Agents Transfer Preservation Policy (locked 2026-05-12)

User decision: Buildup has 12 trained `.onnx` models + curriculum chain (BasicMove → DualTarget → SkillIntro → SkillIntro_Comprehensive → 4 Vs* specialized). To keep these inference-ready in our project:

**STRUCTURE PRESERVED (locked):**
- All `StatManager` / `StateManager` / `SkillExecutor` / `SkillDefinition` / `SkillRegistry` / `SkillContext` public method + field names byte-identical to Buildup. No renames during X2 import rounds.
- `BossObservationCollector` + 5 Agent `.cs` files (X4-N round): import verbatim with English comments only. Field names, sensor.AddObservation call order, normalization variable names all preserved.
- `BehaviorParameters` Inspector values (Behavior Name, Vector Observation Size, Stacked Vectors, Action Spec): copy verbatim from Buildup `.prefab` at X4-N integration time.
- Component composition pattern: `StatManager + StateManager + SkillExecutor + SkillManager + BossObservationCollector + Agent` on the same GameObject (Buildup pattern). NetworkBehaviour wrap (BNC3D) sits as outer layer holding NetworkVariables — does not split or move these per-entity components.

**NUMBERS DEFERRED (training will re-tune):**
- Stats SO values (HP, damage, cooldown) — import Buildup `.asset` as-is at X1-6, then re-train in our environment if balance changes.
- Normalization constants (`_maxDistance`, `_maxCooldown`, `_maxBurstDmg`, `_maxSpeed`) — preserve Inspector values from Buildup at X4-N. If gameplay balance shifts later, re-train with new constants.
- ML-Agents package version: train-time package can produce ONNX compatible with our Unity 6 + Sentis runtime. If incompatibility surfaces at X4-N, re-export ONNX from training env.

**Why this split**: structure changes break the contract between trained policy and runtime; numbers can be re-learned. Lock structure now (free), defer numerical tuning to the training loop (where it belongs).

---

## 10b. Implementation Notes (post X2-5)

- **Tag type closed: `SkillRoleTag` enum** (NOT Buildup `string[]`). Compile-time safety + Inspector dropdowns. **Append-only** — adding values at the end is `.asset`-safe; reordering or removing breaks serialized references. Defined in `Assets/ArenaCombat/Scripts/Core/Skill/Core/SkillRoleTag.cs` (separate file from `SkillTypes.cs` for independent growth). Reserved index ranges:
  - `0..8` X2-5 originals: `Burst, DOT, Shield, Parry, Zone, Counter, Heal, Mobility, Mark`
  - `9..28` X1-6b-1 append (Buildup 12 PlayerSkill audit, 2026-05-13): `AOE, MultiHit, Pierce, Melee, Ranged, DamageUp, DefUp, DefDown, SelfBuff, Vulnerable, ShieldBreak, Cleanse, Regen, AntiHeal, CC, Silence, Buff, Execute, Stealth, Survival`
  - `29+` Future rounds (Boss skill audit / new skills) — append only.
- **Menu path closed: `"ArenaCombat/SkillDefinition"` and `"ArenaCombat/SkillRegistry"`** (project-branded; deliberate divergence from X2-1 SOs' generic `"Scriptable Objects/"` path).
- **`SkillContext` field surface closed: full Buildup verbatim** (incl. `ParryInputTime`, `_log`/`AddLog`/`GetLog`). Forward-compat with X3 parry-trigger and debug.
- **Delegates `SkillStep` / `SkillCondition`** activated in `SkillTypes.cs` (X2-5) — moved INSIDE `namespace ArenaCombat.Core.Skill` (Buildup origin had them at global, but our convention nests). Both reference `SkillContext` from same namespace, no extra `using`.

---

## 11. Deferred / Open Decisions

These can wait for the relevant phase; they don't block X2-5.

1. **Slot count bump trigger**: when does `SlotCount` go from 5 → 6 / 7 / etc? Likely tied to card draft round count (X2-12).
2. **Trigger tag enum**: formal enum (`SkillTrigger { CooldownAuto, ParrySuccess, RopeArrival, OnKill, OnHurt, ... }`) vs string tag? Lean: enum at X2-11.
3. **Cast duration / channeling**: some skills may need wind-up. Buildup uses `IsCasting` flag + duration. Defer until a wind-up skill is designed.
4. **Skill interruption**: if caster is stunned mid-cast, what happens to in-flight projectile? Continue (already spawned, server-owned) or despawn? Lean: continue (Buildup pattern + simplest).
5. **Multi-projectile skills (e.g., 5-shot fan)**: composite of `LaunchProjectile` × N inside `RuntimeStep`. Already supported by SkillLibrary pattern; no design change needed.
6. **Boss skill telegraph**: if boss skills need a wind-up warning, route via `BossPatternStartedRpc` (already in NV/RPC spec, B3 scope) before `SkillStartedRpc`. Decision deferred to X4.

---

## 12. How to Use This Document

- **Before adding a skill component** (X2-9 SkillComponents, ~37 parts): check §5 / §6 / §7 to ensure it routes through StatManager / honors TeamId / runs server-only.
- **Before defining a new SkillDefinition SO**: check §11 deferred items — if your skill needs a wind-up, decision #3 must be reopened.
- **SkillManager wiring** (X3 complete): per-frame `Update` with server-only gate + card-draft gate. Boss / PNC3D wiring identical via `GetComponent<ICombatant>()` auto-detect. Parry/rope trigger hooks future polish.
- **Before changing slot count, registry shape, or cooldown storage**: this doc must be updated first; design changes are not silent.
