# Combat & Skill System

## Goal

The combat and skill system is designed to keep gameplay resolution on the authoritative runtime side.

The portfolio value is not only the number of skills. The stronger point is that combat and skills are organized through reusable definitions, context, and execution flow.

## Key Ideas

- skill data and execution logic are separated
- combat effects are resolved by the authoritative runtime
- client-side presentation remains separate from gameplay authority
- auto-cast behavior can reuse a shared execution path

## Verified Project Evidence

- `Assets/ArenaCombat/Scripts/Core/Combat/`
- `Assets/ArenaCombat/Scripts/Core/Skill/`
- `Assets/ArenaCombat/Docs/SKILL_SYSTEM_DESIGN.md`

## Suggested README Wording

```text
Skills are represented through shared definitions and executed through a common runtime context. This keeps skill behavior easier to extend while allowing the host-authoritative runtime to resolve gameplay effects consistently.
```

## Interview Talking Points

- Why separate skill definition from execution?
- What must be resolved by the host/server?
- Which parts can stay client-side?
- How does auto-cast interact with cooldown, targeting, and combat state?

