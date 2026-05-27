# BUILDUP PROJECT INTEGRATION PLAN

Migration map for absorbing the **Buildup** project (`C:\Users\paek6\Downloads\Buildup\Buildup`, codename Tenebris) into ArenaCombat6 under our NGO host-authoritative pattern.

**Current status**: Phase X0 (environment prep). NO Buildup data has been imported yet. Buildup is **reference-only**.

---

## 1. Why This Plan Exists

Buildup is a parallel branch of the same game (Tenebris / Arena Combat) — same Unity 6.3, same NGO host-authoritative target, same 9 boss biases, same 3-tier state structure. Each branch advanced different areas:

- **ArenaCombat6 (this project)**: NGO 2.x RPC patterns, Host-authoritative `PlayerNetworkController3D`, `CombatManager3D` with full hit logic + K/D/A (B1 done).
- **Buildup**: 37 SkillComponents, StatManager + StateManager + ICombatant, BossController, ML-Agents training pipeline, Chapter1 boss arena scene.

We chose **Path B (Wrapper Integration)** — port Buildup's gameplay systems into our network layer, replace its single-player PlayerController with our PNC3D delegating to ported StatManager.

---

## 2. Path B High-level Strategy

| Buildup component | Disposition |
|---|---|
| `PlayerController.cs` (228 lines) | **REPLACE** — our `PlayerNetworkController3D` is the network-aware replacement. Port concept of "delegates to StatManager" via composition. |
| `BossController.cs` (317 lines) | **WRAP** — write new `BossNetworkController3D` (NetworkBehaviour) modeled on Buildup BossController behavior. Phase X4. |
| `GameManager.cs` (67 lines) | **PARTIAL ABSORB** — `Players/Bosses` registry duplicates `CombatManager3D.players3D`. Keep `GameManager` as a thin facade for `SkillRegistry` + `ElapsedTime` only; route player/boss queries to CombatManager3D. |
| `StatManager.cs` | **PORT AS-IS** — keep most logic, force Server-authoritative gates (`if (!IsServer) return` on all mutators). PNC3D holds and delegates to it. |
| `StateManager.cs` + `CombatantState.cs` | **PORT AS-IS** — pure state machine logic. |
| `SkillManager.cs` | **PORT WITH MODIFICATION** — auto-cast loop runs server-side only. Owner client may emit suggested-cast intent via RPC; server confirms. |
| `SkillRegistry / SkillExecutor / SkillContext / SkillComponents (37)` | **PORT AS-IS** — execution logic is data-driven; just ensure called from server context only. |
| `ICombatant` interface | **PORT AS-IS** — both PNC3D and BossNetworkController3D implement it. |
| `AbilityCard / CardManager / SelectableUICard` | **PORT WITH MINOR ADAPTATION** — UI; binds to network state via NetworkVariable observers. |
| `ProjectilePool / PersistentAreaManager / SkillProjectile / SkillArea` | **PORT WITH NGO LAYER** — projectiles/areas need to be NetworkObjects spawned by host (per Buildup GAME_DESIGN.md §"투사체 판정"). |
| `RopeAction / RopeAnchor` | **SKIP / RECONCILE** — we have our own rope system. Delete Buildup's. |
| `PlayerCamera` | **SKIP** — we have `TopDownCameraFollow3D`. |
| `Enemy.cs / Follow.cs / test.cs` | **SKIP** — prototype/test scripts. |
| `AI/ folder` (BehaviorGraph, ML-Agents, Player bots) | **SKIP for now** — defer until base Phase X done. Major addition. |
| `BasicMove_config.yaml + skills/ + tools/` | **SKIP** — ML training infra. Not relevant to Chapter1 activation. |

---

## 3. Destination Folder Map

When Phase X1+ starts importing, files land here:

| Buildup source | ArenaCombat6 destination |
|---|---|
| `Assets/Scripts/Stats/StatManager.cs` | `Assets/ArenaCombat/Scripts/Core/Combat/StatManager.cs` |
| `Assets/Scripts/PlayerStatsSO.cs` | `Assets/ArenaCombat/Scripts/Core/Combat/PlayerStatsSO.cs` |
| `Assets/Scripts/BossStatsSO.cs` | `Assets/ArenaCombat/Scripts/Core/Combat/BossStatsSO.cs` |
| `Assets/Scripts/BaseStatsSO.cs` | `Assets/ArenaCombat/Scripts/Core/Combat/BaseStatsSO.cs` |
| `Assets/Scripts/State/StateManager.cs` | `Assets/ArenaCombat/Scripts/Core/State/StateManager.cs` |
| `Assets/Scripts/State/CombatantState.cs` | `Assets/ArenaCombat/Scripts/Core/State/CombatantState.cs` |
| `Assets/Scripts/Skill/Core/*` | `Assets/ArenaCombat/Scripts/Core/Skill/Core/*` |
| `Assets/Scripts/Skill/Components/SkillComponents.cs` | `Assets/ArenaCombat/Scripts/Core/Skill/Components/SkillComponents.cs` |
| `Assets/Scripts/Skill/Interfaces/ICombatant.cs` | `Assets/ArenaCombat/Scripts/Core/Combat/ICombatant.cs` |
| `Assets/Scripts/Skill/Interfaces/IProjectile.cs` etc. | `Assets/ArenaCombat/Scripts/Core/Skill/Interfaces/*` |
| `Assets/Scripts/Skill/Projectile/*` | `Assets/ArenaCombat/Scripts/Core/Skill/Projectile/*` |
| `Assets/Scripts/Skill/Area/*` | `Assets/ArenaCombat/Scripts/Core/Skill/Area/*` |
| `Assets/Scripts/Skill/Prefab/*` | `Assets/ArenaCombat/Scripts/Core/Skill/Prefab/*` |
| `Assets/Scripts/AbilityCard.cs` | `Assets/ArenaCombat/Scripts/UI/Cards/AbilityCard.cs` |
| `Assets/Scripts/CardManager.cs` | `Assets/ArenaCombat/Scripts/UI/Cards/CardManager.cs` |
| `Assets/Scripts/SelectableUICard.cs` | `Assets/ArenaCombat/Scripts/UI/Cards/SelectableUICard.cs` |
| `Assets/Scripts/CardUI.cs` | `Assets/ArenaCombat/Scripts/UI/Cards/CardUI.cs` |
| `Assets/Scripts/BossController.cs` | reference only — write fresh `BossNetworkController3D.cs` at `Assets/ArenaCombat/Scripts/Core/Network/BossNetworkController3D.cs` |
| `Assets/Scripts/GameManager.cs` | merge into `Assets/ArenaCombat/Scripts/Core/Network/GameManager.cs` (thin facade — SkillRegistry + ElapsedTime; player/boss queries route to CombatManager3D) |
| `Assets/Scenes/Chapter1.unity` + `Chapter1/` (NavMesh) | `Assets/ArenaCombat/Scenes/Chapter1.unity` + `Assets/ArenaCombat/Scenes/Chapter1/` |
| `Assets/Prefabs/*.prefab` | `Assets/ArenaCombat/Prefabs/*.prefab` |
| `Assets/Player&Boss/*.asset` (Stats SOs) | `Assets/ArenaCombat/ScriptableObjects/Stats/*.asset` |
| `Assets/ScriptableObjects/*` | `Assets/ArenaCombat/ScriptableObjects/*` |
| `Assets/Material/`, `Assets/Shader/`, `Assets/UI/` | `Assets/ArenaCombat/Materials/`, `Assets/ArenaCombat/Shaders/`, `Assets/ArenaCombat/UI/` |

---

## 4. Namespace Convention

- **Existing**: `ArenaCombat.Core.Network` (PNC3D, CombatManager3D, GameStateManager, etc.)
- **New for Buildup-derived**:
  - `ArenaCombat.Core.Combat` — StatManager, ICombatant, Stats SOs
  - `ArenaCombat.Core.State` — StateManager, CombatantState
  - `ArenaCombat.Core.Skill` — SkillRegistry, SkillExecutor, SkillContext, SkillManager
  - `ArenaCombat.Core.Skill.Components` — SkillComponents (static class with 37 parts)
  - `ArenaCombat.Core.Skill.Projectile` — SkillProjectile, ProjectilePool
  - `ArenaCombat.Core.Skill.Area` — SkillArea, PersistentAreaManager
  - `ArenaCombat.UI.Cards` — AbilityCard, CardManager, etc.

This avoids cross-namespace pollution and lets Phase D1 (legacy 2D removal) leave Combat/State/Skill untouched.

---

## 5. Pre-Import Prerequisites (must finish in Phase X0 before X1 starts)

These exist already in our codebase but need adjustment so Buildup data slots in cleanly:

### X0-A: Team assignment (= Phase B Followup #1)
- Already on Phase B Followups list with HIGH priority before B3.
- Buildup Chapter1 has player + boss, both with implicit teams. Without `SetTeam` wiring, player attacks would hit boss correctly (different `TeamId.None` values? — actually both default `None`, so fall through to "different team" check which returns true... wait no, `attacker.Team != TeamId.None && attacker.Team == target.Team` returns false when both are None, so attacks land on anyone). Boss attacking player and player attacking boss BOTH work in current code.
- BUT: 2-player co-op friendly fire is currently active (B1-4 smoke test showed players damage each other).
- For Chapter1 minimum: player vs boss needs to be different teams so future "boss skill hits all players" works correctly. Player vs player on same team should NOT damage.
- **Action**: implement `SetTeam` wiring from PlayerSpawnManager (assign both players → `TeamId.Team1`) and a future BossNetworkController3D spawn (assign → `TeamId.Team2`).

### X0-B: ICombatant interface stub (lightweight, no Buildup import)
- Define `ICombatant` interface in `ArenaCombat.Core.Combat` namespace at `Assets/ArenaCombat/Scripts/Core/Combat/ICombatant.cs`.
- Methods aligned with Buildup's contract (TakeDamage, RecoverHP, ApplyStatus, etc.) but our PNC3D implements with current `networkHP` etc. — StatManager delegation comes in X3.
- Why now: X1 imports prefabs and SOs that may reference `ICombatant`-typed fields. Having interface present means import won't break refs.

### X0-C: Folder skeleton (optional — can defer to first import)
- Empty folders for destinations above. Unity will create .meta when first file lands.
- Or just let Edit/Write tool create on first file.

---

## 6. Sub-step Order (Phase X0 — current cycle)

| Sub-step | Scope | Codex review? |
|---|---|---|
| **X0-1** Update ROADMAP with Phase X structure | Doc only | No |
| **X0-2** Write this BUILDUP_INTEGRATION_PLAN.md | Doc only | No |
| **X0-3** Update memory (project_buildup_reference.md) | Memory only | No |
| **X0-4** Phase B Followup #1 (Team assignment) — wire `SetTeam` from PlayerSpawnManager | Code change | YES (cycle) |
| **X0-5** Define `ICombatant` interface stub | Code change | YES (cycle) |

X0-1 / X0-2 / X0-3 are this current response. X0-4 / X0-5 are separate Codex cycles to execute later.

---

## 7. Sub-step Order (Phase X1 onward — deferred)

| Sub-step | Scope | Estimated Codex rounds |
|---|---|---|
| X1-1 | Copy non-code assets from Buildup (scenes, prefabs, materials, textures, NavMesh, animations, SOs) | 1-2 |
| X1-2 | Move Stats SOs to destination + verify Inspector references | 1 |
| X2-1 | Port StatManager.cs (server-only gates added) | 2-3 |
| X2-2 | Port StateManager + CombatantState | 1-2 |
| X2-3 | Port SkillRegistry + SkillExecutor + SkillContext + SkillComponents | 3-5 (37 components is a lot) |
| X2-4 | Port SkillProjectile + ProjectilePool (with NGO NetworkObject conversion) | 3-4 |
| X2-5 | Port PersistentAreaManager + SkillArea | 2-3 |
| X2-6 | Port SkillManager (auto-cast, server-only) | 2-3 |
| X2-7 | Port AbilityCard + CardManager + SelectableUICard | 2-3 |
| X3-1 | PNC3D adds StatManager component + ICombatant impl (delegation) | 3-4 |
| X3-2 | Reconcile PNC3D networkHP with StatManager (sync mirror) | 2 |
| X3-3 | CombatManager3D.TryProcessAttack3D routes through StatManager.DealDamage | 2 |
| X4-1 | New BossNetworkController3D (modeled on Buildup BossController) | 4-6 |
| X4-2 | Boss FSM + 1 basic pattern | 3-5 |
| X5-1 | Wire Chapter1 spawn (player → PlayerSpawnManager, boss → new spawner) | 2-3 |
| X5-2 | Smoke test + bug fixes | 1-3 |

**Estimated total**: 30-50 Codex rounds across Phase X1-X5. Spread over many cycles.

---

## 8. Risks & Mitigations

1. **Stat ownership double-write** (PNC3D networkHP vs StatManager.GetHP). Mitigation: PNC3D networkHP becomes a sync mirror of StatManager's HP. StatManager is server-only authority. Only StatManager mutators write; PNC3D updates networkHP after StatManager change.
2. **Buildup uses `Input.GetAxisRaw` (Old Input System)**. We're on New Input System (Active Input Handling = 1). Any Buildup script with `Input.X` will throw at runtime. Mitigation: each ported script greps for Input.X usage and converts to `UnityEngine.InputSystem.Mouse/Keyboard.current` per `feedback_coding_rules.md`.
3. **Buildup uses `ServerRpc` terminology in design docs** (NGO 1.x). Actual Buildup code has no NGO. Mitigation: when porting to NGO, use our `[Rpc(SendTo.X)]` patterns.
4. **GameObject pool patterns conflict with NGO NetworkObject lifecycle**. Buildup's `ProjectilePool` instantiates pooled GameObjects; NGO requires NetworkObject.Spawn() for network sync. Mitigation: pool stores NetworkObject prefab; on spawn, host calls `Spawn()`; on despawn, host calls `Despawn()` and returns to pool.
5. **`SkillManager` auto-cast** runs `Update()` every frame. In NGO, server-only auto-cast prevents client desync. Mitigation: `if (!IsServer) return` at top of Update.
6. **ScriptableObject GUID conflicts** — copying assets keeps original GUIDs which is good for reference integrity. Watch for GUID collision with our own existing SOs (unlikely but verify).
7. **Codex review burden** — 30-50 rounds is a lot. Mitigation: each Codex cycle keeps scope tight (one file or one tightly-related group). User pastes pending → feedback. Migration is 30-50 paste pairs spread over weeks/months.
8. **Buildup project drift** — Buildup may continue to evolve. We're snapshotting reference now. If Buildup changes after we port, we manually re-merge. Mitigation: archive a snapshot of Buildup source in our repo for reference, or document the snapshot date.

---

## 9. Reference Snapshot Note

Buildup code we're referencing was last read on 2026-05-11. Key Buildup docs reviewed:
- `CLAUDE.md` (overall workflow)
- `GAME_DESIGN.md` (full game design)
- `Assets/CHANGES.md` (recent change log up to 2026-05-03)

If Buildup changes substantially between now and Phase X1, re-survey before porting.

---

## 10. What This Plan Does NOT Do

- Does NOT copy any Buildup file (zero file movement in Phase X0).
- Does NOT modify Buildup project (reference-only — we never touch `C:\Users\paek6\Downloads\Buildup\`).
- Does NOT lock implementation details (sub-step diffs come at each X-N cycle's pending.md).
- Does NOT commit to AI/ML-Agents porting (out of Phase X scope; possible Phase Y later).
