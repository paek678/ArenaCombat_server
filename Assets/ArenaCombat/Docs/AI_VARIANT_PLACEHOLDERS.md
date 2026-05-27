# Boss AI Variant Placeholder SOs

## Context
C3a Boss AI Pool Selection landed the selection framework (PlayerArchetypeClassifier + BossAIPoolManager + BossNetworkController3D.ApplyAIVariant + SkillManager slot weights). The 11 BossAIDefinition ScriptableObjects below are the **content** that the framework selects from.

This phase ships the framework with **empty / placeholder SOs**. The designer populates `skillSlots`, `slotWeights`, and `cooldownScale` per variant through play tuning.

## SO asset paths to create

Place in `Assets/ArenaCombat/Data/BossAI/` (create the folder if missing).

| File | playerType1 | playerType2 | isDefault | Notes |
|------|-------------|-------------|-----------|-------|
| `BossAI_Default.asset` | Hybrid | Hybrid | **true** | Cold-start variant. Used 0~3min before first archetype eval, and as lookup-miss fallback. |
| `BossAI_HH.asset` | Hybrid | Hybrid | false | Both players classified Hybrid (mixed play-style). |
| `BossAI_MH.asset` | Hybrid | Melee | false | One Melee + one Hybrid. Order normalized so (M,H) and (H,M) both map here. |
| `BossAI_RH.asset` | Hybrid | Ranged | false | One Ranged + one Hybrid. |
| `BossAI_CH.asset` | Hybrid | CC | false | One CC + one Hybrid. |
| `BossAI_MM.asset` | Melee | Melee | false | Both Melee. |
| `BossAI_MR.asset` | Melee | Ranged | false | Mixed Melee/Ranged. |
| `BossAI_MC.asset` | Melee | CC | false | Melee + CC. |
| `BossAI_RR.asset` | Ranged | Ranged | false | Both Ranged. |
| `BossAI_RC.asset` | Ranged | CC | false | Ranged + CC. |
| `BossAI_CC.asset` | CC | CC | false | Both CC. |

**Order convention**: `playerType1` should hold the lower-byte enum value (`Hybrid=0 < Melee=1 < Ranged=2 < CC=3`). The pool manager's lookup key normalization expects this, but it does not enforce it — the lookup only uses normalized keys, so the field order on the SO is mostly a readability convention.

## Creating the SOs in Unity

1. Right-click in the Project window inside `Assets/ArenaCombat/Data/BossAI/`.
2. `Create` → `ArenaCombat` → `AI` → `BossAIDefinition`.
3. Rename the asset to one of the names in the table above.
4. Inspector fields:
   - `Variant Name` — display name (e.g. "Default", "MM Boss", "Melee+CC Boss")
   - `Player Type 1` / `Player Type 2` — set per table
   - `Is Default` — set `true` ONLY on `BossAI_Default.asset`
   - `Skill Slots` — drag in up to 5 SkillDefinition SOs (placeholders OK; can leave null)
   - `Slot Weights` — leave at default `1.0` for all 5 entries unless biasing a specific slot
   - `Cooldown Scale` — leave at `1.0` unless variant needs slower/faster boss

## Wiring in Chapter1.unity

1. Add a `BossAIPoolManager` MonoBehaviour to the `--- Managers ---` separator (or its own GameObject under it).
2. Inspector:
   - `Default AI` — drag `BossAI_Default.asset`
   - `Combos` (array of 10) — drag the 10 non-default SOs in any order; the pool builds an order-invariant lookup at Awake
   - `Verbose Log` — leave `true` for initial tuning; flip `false` once stable

## Verification (manual)

With placeholders assigned (empty skill arrays allowed):

1. Start a 2P match. Console should log:
   - `[BossAI] swap applied: Default (...)` shortly after `MatchState.InProgress`
2. Wait 3 minutes. Console should log:
   - `[Archetype] client={id} M=… R=… C=… → {Type}` per player
   - `[BossAI] swap applied: BossAI_XX (...)` if archetypes resolved to a non-default combo
3. Try force-changing archetype via classifier debug (if `ContextMenu` helpers added) and observe swap.

## Tuning notes (for the designer)

- Variants should encode a "what the boss does against this play-style" intent. Examples:
  - `BossAI_MM` (both melee) → boss favors ranged poke + zoning skills
  - `BossAI_RR` (both ranged) → boss favors gap-close + kit-disruption
  - `BossAI_CC` (both CC-heavy) → boss favors stun-immune burst windows
  - `BossAI_Default` → balanced fallback, no strong counter-pick
- `slotWeights` biases adaptive picker but does not disable any skill. Use `[3,1,1,1,1]` to make slot 0 ~3× more likely while others still fire occasionally.
- `cooldownScale < 1` → boss is faster; `> 1` → slower. Default phase cooldowns from `PopulateBossSkills` already cover this for phases (1.0 → 0.5 in Enrage); use variant cooldown to layer on top per archetype matchup.

## Out of scope here
- ML-based archetype inference (currently rule-based with 3-min eval)
- Per-phase variants (Phase 1 vs Enrage MM may differ — currently one variant per combo regardless of phase)
- Client-side variant visibility (server-only for now)
