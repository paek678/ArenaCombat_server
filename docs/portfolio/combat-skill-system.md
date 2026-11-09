# Combat & Skill System

## Goal

The combat and skill system is designed to keep gameplay resolution on the authoritative runtime side.

Combat and skills share reusable definitions, execution context, and a common execution flow.

## Key Ideas

- skill data and execution logic are separated
- combat effects are resolved by the authoritative runtime
- client-side presentation remains separate from gameplay authority
- auto-cast behavior can reuse a shared execution path

## Verified Project Evidence

- [Combat code](../../Assets/ArenaCombat/Scripts/Core/Combat/)
- [Skill code](../../Assets/ArenaCombat/Scripts/Core/Skill/)
- [Skill system design](../../Assets/ArenaCombat/Docs/SKILL_SYSTEM_DESIGN.md)

## Execution Flow

| Component | Responsibility |
| --- | --- |
| `SkillDefinition` | Skill identity, cooldown, range, targeting, and AI hints |
| `SkillContext` | Caster, targets, cast position and direction, and per-cast results |
| `SkillExecutor` | Execute the skill's runtime behavior |
| `SkillManager` | Manage slots, cooldowns, targeting, and auto-cast decisions |

`SkillLibrary` builds runtime skill behavior and `SkillBinder` attaches it to definitions. These runtime delegates are not serialized, so they must be bound again after a domain reload or scene initialization.

Skill effects are resolved through the authoritative runtime. Client-side presentation remains separate from decisions that change gameplay state.
