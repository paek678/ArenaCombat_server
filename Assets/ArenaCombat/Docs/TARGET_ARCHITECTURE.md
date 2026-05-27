# TARGET ARCHITECTURE — Host-Authoritative Server (North Star)

> **Purpose**: this document is the **destination state** of the codebase, not its current state. Compare against [NETWORK_ARCHITECTURE.md](NETWORK_ARCHITECTURE.md) (current-state) and [ROADMAP.md](ROADMAP.md) (path from current to target). When in doubt about which manager owns a concern, or where new code should live, this file is the tie-breaker.
>
> **Scope discipline**: this document does NOT propose any code change by itself. Every migration step is gated by a ROADMAP item and a Codex Review cycle. The point of this doc is to ensure each step lands in the right layer.
>
> Last revised: 2026-05-12. Author: Claude (paired with user-confirmed architecture).

---

## 0. Foundational Constraints

1. **Host = authority server + client0.** No dedicated server. `IsServer` gates all judgment paths. `IsHost == IsServer && IsClient`.
2. **2-player co-op vs. boss.** Friendly-fire blocked by TeamId; PvP deferred indefinitely.
3. **NGO 2.x.** `[Rpc(SendTo.X)]` only. No legacy `ServerRpc` / `ClientRpc`.
4. **Input System 1.19+ only.** No `UnityEngine.Input.*` anywhere.
5. **Unity 6.3 LTS.** Physics → `Physics.OverlapBox`, position writes → `Rigidbody.MovePosition` only (no `rb.position` / `transform.position` on server path).
6. **Lobby Service is pre-match only.** Once the match scene loads, all state lives in NGO. The two systems never share state.

---

## 1. Layered Stack

```
┌──────────────────────────────────────────────────────────────┐
│ L6  PRESENTATION (client-side only, never authoritative)     │
│     UI / VFX / animation / SFX / camera follow / Yarn dialog │
├──────────────────────────────────────────────────────────────┤
│ L5  ENTITY LAYER (thin NetworkBehaviour, ICombatant impl)    │
│     PlayerNetworkController3D / BossNetworkController3D      │
│     SkillProjectile / SkillArea (pooled NetworkObjects)      │
├──────────────────────────────────────────────────────────────┤
│ L4  MANAGER LAYER (DDOL NetworkBehaviour singletons)         │
│     GameStateManager / PlayerSpawnManager / CombatManager3D  │
│     StateManager / StatManager / SkillManager / SkillExecutor│
│     ProjectilePool / PersistentAreaManager / BossManager     │
│     DraftManager (extract from GSM when GSM > ~800 LOC)      │
│     AIBehaviorLogger (Phase C)                               │
├──────────────────────────────────────────────────────────────┤
│ L3  CONTRACT LAYER (interfaces, enums, delegates)            │
│     ICombatant / IProjectile / IPersistentArea / IPoolable   │
│     SkillStep / SkillCondition delegates                     │
│     CharacterStateId / StatusMask / TeamId / AttackType etc. │
├──────────────────────────────────────────────────────────────┤
│ L2  DATA LAYER (ScriptableObject, immutable at runtime)      │
│     BaseStatsSO / PlayerStatsSO / BossStatsSO                │
│     AttackData3D / SkillDefinition / AbilityCardSO           │
│     BossPatternSO / SkillPoolSO                              │
├──────────────────────────────────────────────────────────────┤
│ L1  NETWORK PRIMITIVES (NGO 2.x)                             │
│     NetworkVariable<T> / [Rpc(SendTo.X)] / NetworkObject     │
│     Tick / FixedUpdate authority loop                        │
├──────────────────────────────────────────────────────────────┤
│ L0  TRANSPORT (Unity Transport + Relay)                      │
└──────────────────────────────────────────────────────────────┘
```

**Read direction**: higher layers know lower layers (L5 calls L4 calls L3). Lower layers MUST NOT reach up. L4 managers may call each other but never the L5 entities directly — always through L3 interfaces.

---

## 2. Authority Partition

Every piece of state has **exactly one owner**. If two managers might both write it, one wins; the other reads.

| Domain | Authoritative manager | State surface |
|---|---|---|
| Match lifecycle | `GameStateManager` | `MatchState`, `MatchTimer`, `GameMode`, `RoundNumber` |
| Card draft | `GameStateManager` (or extracted `DraftManager`) | `IsDraftActive`, `DraftRound`, `CardDraftTimer`, draft offer state |
| Spawn / ownership / team | `PlayerSpawnManager` | spawn position, `TeamId` assignment |
| Combat resolution | `CombatManager3D` | hit detection, parry timing, K/D/A, attack cooldowns |
| Stat math | `StatManager` | `MaxHP` / `CurrentHPPercent` / `Shield` / buff / debuff / status timers |
| FSM state | `StateManager` | `CharacterStateId` transitions per combatant |
| Skill lifecycle | `SkillManager` + `SkillExecutor` | skill cooldown, casting state, composite execution |
| Projectile / area | `ProjectilePool` / `PersistentAreaManager` | pooled NetworkObject lifecycle |
| Boss FSM / AI | `BossManager` (BNC3D wraps it) | `BossPhase`, `BossCurrentPatternId`, `BossHP`, `BossAlive`, `BossStatusMask` |
| Behavior logging | `AIBehaviorLogger` (Phase C) | server-internal weight tables |
| Position / yaw / movement | `PlayerNetworkController3D` (entity, not manager) | `Position`, `Yaw`, `IsRoping`, `RopeTarget` |

**Rule of thumb**: managers own *judgment*. Entities own *state mirror + locomotion*. ScriptableObjects own *static data*.

---

## 3. Manager Catalog

> **Pattern correction (X2-4)**: not all "managers" are DDOL singletons. Two distinct shapes exist:
> - **Singleton managers** (`GameStateManager`, `PlayerSpawnManager`, `CombatManager3D`, `BossManager`, `ProjectilePool`, `PersistentAreaManager`, `SkillManager`, `SkillRegistry`, `AIBehaviorLogger`, `MapBounds3D`): one instance per match, NetworkBehaviour or scene-singleton MonoBehaviour. Coordinate across all combatants.
> - **Per-entity component managers** (`StatManager`, `StateManager`, future `SkillExecutor` candidate): one instance per `ICombatant`, sit on the same GameObject as the controller, `MonoBehaviour` (not NetworkBehaviour — host already owns the GameObject as a NetworkObject). Hold per-entity authority surface.
>
> Server-authority enforcement lives at the **call site** (controller / singleton manager) for per-entity components, since they have no `IsServer` themselves.


### 3.1 Existing managers (current state)

| Manager | Lines | Role | Slim down? |
|---|---|---|---|
| `GameStateManager` | medium | match + draft lifecycle, RPC orchestration | extract DraftManager only if GSM exceeds ~800 LOC |
| `PlayerSpawnManager` | small | spawn, team assignment | stable |
| `CombatManager3D` | medium | hit / parry / K-D-A, attack cooldowns | stable — becomes thinner once StatManager owns damage math |
| `MapBounds3D` | small | bounds clamp, rope anchor resolve | stable |

### 3.2 Planned managers (target state)

| Manager | When | Owns | Key APIs (sketch) |
|---|---|---|---|
| `StateManager` | X2-3 | per-ICombatant FSM (Idle/Moving/Casting/Hit/Dead/Roping/Parrying) | `SetState(ICombatant, CharacterStateId)`, `CanTransition(...)`, `GetState(ICombatant)` |
| `StatManager` | X2-4 | HP/Shield/buff/debuff/status timers, damage math | `DealDamage(target, amount, attacker)`, `RecoverHP`, `ApplyBuff`, `Tick(dt)` |
| `SkillRegistry` | X2-5 | static skill DB load | `Get(skillId) -> SkillDefinition` |
| `SkillExecutor` | X2-6 | composite tree execution per active skill | `Begin(ctx)`, `Tick(ctx, dt)` |
| `SkillManager` | X2-11 ✅ | per-player skill slot, cooldown, auto-cast trigger | `TryCast(player, skillId)`, server-only Update gate |
| `ProjectilePool` | X2-9 | NGO-safe pooling for `SkillProjectile` | `Spawn(prefab, pose, ownerId)`, `Despawn(no)` |
| `PersistentAreaManager` | X2-11 | active AoE zones, periodic tick | `Register(area)`, `Tick(dt)` |
| `BossManager` (BNC3D) | X4 | boss FSM, telegraph schedule, AI decision | `Tick(dt)`, `EnterPhase(id)`, RPC entries |
| `AIBehaviorLogger` | Phase C | 9 behavior biases, weight updates | `Log(player, event)`, `EvalWindow(player)` |

### 3.3 Inter-manager call graph (target)

```
Owner Input
    │
    ▼
PNC3D ─── RequestXxxRpc(intent) ──▶ CombatManager3D / SkillManager
                                         │
                                         ▼ (validation)
                                    StateManager.CanTransition
                                         │
                                         ▼
                                    StatManager.DealDamage / RecoverHP
                                         │ (applies stat math)
                                         ▼
                                    StateManager.SetState
                                         │ (FSM transition)
                                         ▼
                                    ICombatant NV write (HP, StateId, ...)
                                         │
                                         ▼
                                    ResultRpc(broadcast) — VFX/UI cue

Boss tick
    │
    ▼
BossManager ── (same chain: StatManager / StateManager / SkillManager)
```

Forbidden edges:
- Manager → entity (`PNC3D.networkHP.Value = ...` from outside) — always via `ICombatant`.
- Entity → entity direct call — always via manager.
- Skill component → entity direct — via `SkillContext.target` of `ICombatant`.
- Lower layer → higher layer (data SO → manager) — SOs are pure data.

---

## 4. Entity Layer (Thin Wrappers)

### 4.1 PlayerNetworkController3D target shape

After X3 migration, PNC3D should be ~600 LOC (currently ~1700). It owns:

1. **NetworkVariables (state mirror)**: Position, Yaw, HP, Alive, StateId, StatusMask, Team, IsRoping, RopeTarget. ← These are *mirrors* of authoritative truth (StatManager/StateManager).
2. **Owner input collection**: subscribes to PlayerInputHandler, sends `RequestXxxRpc(clientTick)`.
3. **Server queue dispatch**: `QueuedServerAction` ingestion + FixedUpdate dispatch to managers.
4. **Locomotion application**: `Rigidbody.MovePosition` only, position result computed by `CombatManager3D` + `MapBounds3D`.
5. **ICombatant implementation**: 23 members, all delegated to managers (HP/Shield → StatManager, IsCasting → StateManager, Knockback → StatManager+locomotion, etc.).

What PNC3D should **NOT** own after migration:
- Damage math (`TakeDamage` body) → delegate to `StatManager.DealDamage`.
- Parry window timer → `StateManager.ApplyStatus(Parrying, duration)`.
- Attack cooldown / hit dedup / parry routing → already in `CombatManager3D` ✓.

### 4.2 BossNetworkController3D target shape (X4)

Mirror of PNC3D but **no owner input**:

1. NetworkVariables: BossHP, BossAlive, BossPhase, BossCurrentPatternId, BossStatusMask, Position, Yaw.
2. Server-only `Tick()` driven by `BossManager` (BossManager *is* the tick driver; BNC3D is the state mirror).
3. ICombatant implementation, same delegation pattern as PNC3D.

### 4.3 Skill entity types

| Type | Base | Role |
|---|---|---|
| `SkillProjectile` | NetworkBehaviour + IProjectile + IPoolable | flying hit-volume, despawned via `ProjectilePool` |
| `SkillArea` | NetworkBehaviour + IPersistentArea | sticky AoE, ticked by `PersistentAreaManager` |

Both spawned by host only. Pool keeps prefab variants; `Spawn()` calls `NetworkObject.Spawn()` then activates.

---

## 5. Data Layer (ScriptableObject)

| SO | Owner | Mutability | Notes |
|---|---|---|---|
| `BaseStatsSO` / `PlayerStatsSO` / `BossStatsSO` | StatManager (inflate to runtime instance) | **immutable at runtime** | SO = template; runtime stat = StatManager-owned struct |
| `AttackData3D` | CombatManager3D `attackTable` | immutable | already ✓ |
| `SkillDefinition` | SkillRegistry | immutable | composite tree |
| `AbilityCardSO` | DraftManager pool | immutable | offer pool source |
| `BossPatternSO` | BossManager | immutable | telegraph + resolve definition |

**Rule**: never mutate a `SerializeField` SO at runtime. Perks/buffs modify the *runtime stat instance* held by StatManager. SOs are read-only data templates.

---

## 6. Communication Discipline (Hold The Line)

This section is **already enforced** in the codebase. Listing here so any new code is checked against it.

| Mechanism | Use for | Don't use for |
|---|---|---|
| `NetworkVariable<T>` (Server-write, Everyone-read) | authoritative state (HP, Alive, Position, MatchState, BossPhase) | events, intermediate calculations, AI weights |
| `[Rpc(SendTo.Server)]` | client → server input intent (with `clientTick`) | result, judgment, state-changing payloads |
| `[Rpc(SendTo.ClientsAndHost)]` | events / VFX / UI cues that all clients need to see | authoritative state (use NV) |
| `[Rpc(SendTo.Owner)]` | per-player rejection reasons, private card offers | broadcasts |
| `[Rpc(SendTo.SpecifiedInParams)]` | targeted private RPC (e.g., draft offer to one player) | when SendTo.Owner suffices |
| Server-internal fields | queues, cooldowns, AI raw weights, behavior log buffers | anything a client needs to react to |

**Five non-negotiable principles** (already practiced):
1. **Client sends intent only.** Never sends a result.
2. **Server confirms HP / status / win-loss.** Client never decides.
3. **Input queue / AI weights / cooldown timers live in server-internal vars.** Never NV.
4. **Single judgment path per concern.** No duplicate detection paths.
5. **Conflicting requests are tick + actionSeq + server-validated.** No client-side ordering trust.

---

## 7. Server Tick Authority Loop

```
[FixedUpdate on Host]
  │
  ├─ 1. Ingest queued requests
  │     Sort by (clientTick → actionPriority → receivedAt)
  │     actionPriority (within-player only):
  │       Parry=0, Rope=1, Attack=2, PerkTrigger=3
  │       (Skill removed — auto-cast model has no client skill RPC, see SKILL_SYSTEM_DESIGN.md)
  │
  ├─ 2. Per combatant: StateManager.Tick(dt)
  │     - decrement status / buff / debuff timers
  │     - auto-clear expired states
  │
  ├─ 3. Dispatch dequeued actions via managers
  │     CombatManager3D (Attack/Parry) / SkillManager (Skill)
  │     PNC3D (Move/Rope locomotion)
  │
  ├─ 4. StatManager.Tick(dt) — DoT / regen / shield decay
  │
  ├─ 5. BossManager.Tick(dt) (when present)
  │     - phase check / pattern schedule / telegraph
  │
  ├─ 6. PersistentAreaManager.Tick(dt) — AoE periodic effects
  │
  ├─ 7. Resolve final positions / clamps / publish NVs
  │     PNC3D.UpdateServerTimers → MovePosition → networkPosition.Value = clamped
  │
  └─ 8. Broadcast result RPCs (VFX / UI cues)
```

Within-player ordering matters; cross-player ordering does not (each player's queue is independent — no cross-player tick comparison to avoid latency drift bias).

---

## 8. Match Lifecycle (Target)

```
Lobby (Unity Services, no NGO)
   │ host StartMatch
   ▼
Loading (NGO scene load)
   │ all clients ready
   ▼
WaitingForPlayers ─▶ Countdown ─▶ InProgress ◀─┐
                                        │       │
                                        │       │
                                  Draft trigger │
                                  (HP %, time)  │
                                        │       │
                                        ▼       │
                                  CardDraft ────┘
                                  (InProgress + IsDraftActive=true)
                                        │
                                        ▼
                                  RoundEnd (optional, multi-round only)
                                        │
                                        ▼
                                  MatchEnd ─▶ result screen ─▶ Lobby
```

End conditions:
- Boss death → MatchEnd(win)
- All players permadead → MatchEnd(lose)
- Host disconnect → graceful shutdown (out of scope until production)

---

## 9. Open Design Decisions (Defer Until Needed)

These are gaps where the architecture has **multiple valid shapes** and we pick when the relevant phase starts. None block current work.

1. **PlayerMaxHP NV promotion** — when does perk/buff first change MaxHP? At X3 StatManager wiring, decide between (a) NV mirror always, (b) NV only after first mutation. **Lean**: (a) for simplicity.
2. **RoundEnd vs MatchEnd separation** — single-match (current) vs multi-round (best-of-3). **Lean**: keep single-match until design demands otherwise.
3. **Rope event RPC granularity** — current `RopeResult(success)` collapsed vs spec's `RopeStarted` / `RopeRejected` split. Split gives all clients a teammate-rope cue. **Lean**: split at X3 if rope VFX needs broadcast.
4. **Aimed attack `targetHint`** — current PNC3D `RequestAttackRpc(AttackType)` has no targetHint (hitbox auto). Basic attack input model is **deferred** (skill system uses auto-cast, no lock-on need). Decision reopened only when basic attack is reworked.
5. **RequestPerkTrigger lifecycle** — current ad-hoc RPC predates SkillManager. With auto-cast model, `RequestSkillRpc` does NOT exist; perk trigger gets absorbed differently. **Lean**: at X2-12, fold perk-trigger logic into a server-resolved auto-trigger (parry / rope / kill event hook per [SKILL_SYSTEM_DESIGN.md §4](SKILL_SYSTEM_DESIGN.md)) and delete the RPC.
6. **DraftManager extraction** — GSM currently owns draft + match. **Lean**: extract only if GSM > ~800 LOC.

**Closed decisions (moved to [SKILL_SYSTEM_DESIGN.md](SKILL_SYSTEM_DESIGN.md))**: skill input model (auto-cast), slot count (5, const-extensible), tick rate (FixedUpdate), one-skill-per-tick rule, auto-target policy, registry shape (single master), SkillId type (string), SkillContext lifetime (per-cast), projectile collision (TeamId opposite-only), hit detection (server `Physics.OverlapBox`), cooldown UI sync (client-derived).

---

## 10. Migration Order (Mapped to ROADMAP)

The architecture is reached **by following the existing ROADMAP**, not by a separate refactor pass. Each step lands in the right layer.

| ROADMAP step | Architecture impact |
|---|---|
| X2-1 ✅ | Stats SO classes → L2 data layer foundation |
| X2-2 ✅ | ICombatant full surface + skill enums → L3 contract layer |
| X2-3 ✅ | CombatantState enum (scope-down) → L3 contract layer. StateManager moved to X2-4 due to hard `RequireComponent(StatManager)` dep. |
| X2-4 ✅ | StatManager + StateManager paired → L4 manager layer (1st + 2nd new managers). **Per-entity `MonoBehaviour` components** (correction to §3 framing — see note below). Server authority enforced at call sites, not inside managers. |
| X2-5 ✅ | SkillContext + SkillDefinition + SkillRegistry + SkillRoleTag enum + delegate activation → L3 contract layer foundation. Auto-cast model confirmed; `string[]` tags → `SkillRoleTag[]` enum (compile-safe). X1-6b-1에서 enum 9→29 append-only 확장. |
| X2-6 ✅ | SkillExecutor → L4 per-entity component. Composite tree runtime + cooldown dict + ML observation surface preserved (GetRemainingCooldown/GetHitRate/GetUseCount/GetLastNSkillIds). |
| X2-7 ✅ | IProjectile + IPoolable + SkillProjectile + ProjectilePool paired (~207 LOC) → L3 + L4 + L5. Codex caught Buildup origin double-enqueue bug in Pool.Get(); patched (Get always dequeues, CreateInstance always enqueues — symmetric). MonoBehaviour, X3 wiring round flips ShouldRunHitDetection to IsServer. |
| X2-8 ✅ | IPersistentArea + SkillArea + PersistentAreaPool + PersistentAreaManager paired (~247 LOC) → L3 + L4 + L5. Same Codex Pool.Get() preemptive fix applied. MonoBehaviour, X3 wiring adds TickArea server gate. |
| X2-9 ✅ | SkillComponents (37 factories) + SkillRangeDisplay (debug viz, paired ~837 LOC) → L3-L4. All Projectile/Area deps compileable. |
| X2-10 ✅ | SkillLibrary (29 SkillStep + 4 SkillCondition = 33 public methods) + SkillBinder (delegate injection bootstrap) paired (~475 LOC) → L3-L4. 22 implemented + 7 null UNIMPLEMENTED (input system / wrapper deps absent). |
| X2-11 ✅ | SkillManager + GameManager paired (~370 LOC) → L4 per-entity (SkillManager `ArenaCombat.Core.Skill`) + L4 facade (GameManager `ArenaCombat.Core`). Server-only gate at Update top (`NetworkManager.Singleton != null && !IsServer` return). Players/Bosses lists TEMPORARY (route through CombatManager3D in X3/X4). `Update` (not FixedUpdate) preserved per Buildup verbatim — X3 may extract AutoCastTick for server FixedUpdate driver. `_owner` Inspector field replaced with `GetComponent<ICombatant>()` auto-detect (M-1 deviation). |
| X2-12 ✅ | AbilityCard + CardManager + CardUI + SelectableUICard paired (~325 LOC) → L2 + L4 + L6 in `ArenaCombat.Core.Card`. **LEGACY LOCAL DRAFT MODE** header warning — X3 wiring required before production scene activation (replaces Time.timeScale / FindGameObjectWithTag / direct SetSlot / Invoke timer with GSM RPC/NV). Buildup CardManager.cs had mojibake quote breakage; full clean rewrite mandatory. M-1 PlayerSkillSlot branch removed. |
| **PHASE X2 COMPLETE ✅** | **12 sub-cycles done. ~5,700 LOC, 32+ Buildup files imported.** |
| X3-1 ✅ | PNC3D `: ICombatant` + 23 explicit interface impl (X3-1 compile bridge: read-only property forwards + warn-once no-op mutations) |
| X3-2 ✅ | 4 per-entity manager components RequireComponent + BindOwner in PNC3D Awake (StatManager / StateManager / SkillExecutor / SkillManager) |
| X3-3 ✅ | Stat authority swap (merged with NV sync): StatManager owns HP/IsAlive; networkHP/networkIsAlive sync hook in FixedUpdate; Die transition; skill kill attackerId carry |
| X3-4 ✅ | Position control routing (Knockback/Pull/MoveBy → ApplyPositionOffset → MovePosition + immediate networkPosition mirror) |
| X3-5a ✅ | SkillProjectile / SkillArea: MonoBehaviour → NetworkBehaviour + IsServer hit/tick gates |
| X3-5b ✅ | ProjectilePool / PersistentAreaPool NGO Spawn/Despawn(false) lifecycle + IsServerContext guards + PersistentAreaManager.Spawn server-only |
| X3-6 ✅ | CardManager 4 LEGACY patterns removed → GSM event subscription + SubmitLocalCardSelection RPC + RegisterCardCatalogSize + SkillManager draft gate |
| X3-7 ✅ | Phase X3 wiring closure + smoke test preflight + checklist (doc-only round) |
| **PHASE X3 COMPLETE pending runtime smoke** | **All wiring done. User Play-mode host + 2P verification required before COMPLETE declaration.** |
| X4-1 ✅ | BossNetworkController3D shell (`NetworkBehaviour, ICombatant` + 23 explicit interface impl — inert defaults `IsAlive=false`/`CurrentHPPercent=0`/`MaxHP=0` + warn-once no-op mutations via static `_x4StubWarned`). `[DisallowMultipleComponent]` + `[RequireComponent(typeof(NetworkObject))]`. Shell only — not registered as NetworkPrefab; X3 smoke과 파일 0겹침. |
| X4-2 ✅ | 4 per-entity manager components RequireComponent (StatManager / StateManager / SkillExecutor / SkillManager) + `_bossStatsSO` SerializeField + Awake BindOwner. `SkillManager.SetAutoCast(false)` 호출로 dormant 계약 강화 (Codex C-1). Initialize + live ICombatant routing은 X4-3. |
| X4-3 ✅ | HP/alive authority via StatManager + NV sync + ICombatant routing (merged round, PNC3D X3-3 미러). NetworkVariable 2개 (`networkHP` / `networkIsAlive`, default 0/false). `InitializeStatManager` (BossStatsSO null → skip + warn + inert). `OnNetworkSpawn`(server) + `FixedUpdate` sync hook (IsServer+IsSpawned+_statMgr+alive 가드). 8 read property → NV/SO 기반 (Codex C-2). 11 mutation/query → IsServer 가드 + StatManager forward (Codex C-3). Position control 3 stub 유지 → X4-4. Match-end broadcast → X4-5/6. |
| X4-4 ✅ | Position control routing (PNC3D X3-4 미러). Rigidbody + Collider RequireComponent. `networkPosition` NV (server-write everyone-read). **Codex C-1**: `OnNetworkSpawn` 비서버 분기에서 snap + OnValueChanged 구독 + `OnNetworkDespawn` unsubscribe. `HandlePositionChanged` immediate snap. `ApplyPositionOffset` helper (MapBounds3D.ResolveServerPosition + rb.MovePosition + 즉시 NV mirror). 3 stub → ApplyPositionOffset (IsServer + alive + `_rb` 가드). FixedUpdate networkPosition 매-tick 갱신 안 함 (Codex S-2). `WarnX4Stub` 제거 → `_warnedOnce` 리네임. FSM → X4-5. |
| X4-5a ✅ | BossManager 셸 (scene-local singleton, MonoBehaviour, [DisallowMultipleComponent]). Codex C-1로 DDOL 제거 — serialized scene Transform `_bossSpawnPoint`와 DDOL 조합이 dangling ref 위험. Inspector 슬롯 (`_bossPrefab` / `_bossSpawnPoint`) + `CurrentBoss` getter + `TrySpawnBoss()` stub. 호출자 부재. |
| X4-6 ✅ | Phase tracking (Buildup BossController.HandlePhase 포팅). `NetworkVariable<BossPhase>` (Codex C-1 — 공용 enum 재사용). InitializeStatManager → Phase1 (Codex C-2). OnBossDefeated → Defeated (Codex C-3). HandlePhase는 thresholds[i] cross 시 Phase2/Phase3/Enrage 매핑, 4번째 이상 무시 + warn-once. OnPhaseChanged 현재 log-only — behavior wiring은 X4-7. |
| **X3** | PNC3D ↔ StatManager wiring; PNC3D slims from ~1700 → ~600 LOC |
| **X4** | BossNetworkController3D + BossManager → L4 + L5; fills 5 boss NVs. X4-1/2/3/4/5a/6 DONE. X4-5b NEXT: designer 프리팹/NetworkPrefabs/씬 배치. X4-5c: spawn 활성화 + dormant 해제. X4-7 FSM (phase별 skill 풀). X4-8 ML-Agents. |
| **X5** | Chapter1 scene activation = user goal |
| Phase C | AIBehaviorLogger → L4 |
| Phase D1 | Legacy 2D removal — final cleanup |

After X5 + Phase D1, the codebase matches §1 stack exactly.

---

## 11. How to Use This Document

- **Before writing any new code**, identify the layer (L0–L6) and the manager / entity / SO it belongs to.
- **If a feature seems to span 2 managers**, one of them is wrong — re-read §2 Authority Partition.
- **If you're tempted to put logic in an entity**, ask whether a manager should own it (default yes).
- **If you need a new manager not in §3**, propose it via ROADMAP first; don't invent ad-hoc.
- **When a `pending.md` is drafted**, cross-check that the proposal lands in the layer this doc says.

This document evolves only via explicit user request or a ROADMAP-driven architecture shift. Cosmetic edits OK; layer reassignments require user sign-off.
