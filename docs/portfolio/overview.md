# Portfolio Overview

ArenaCombat_server is the main portfolio repository for the ArenaCombat / BuildUp project pair.

The best public framing is:

```text
ArenaCombat_server = multiplayer boss-fight runtime
BuildUp = related ML-Agents boss movement training project
```

## Project Positioning

ArenaCombat_server demonstrates a Unity 2-player co-op boss fight runtime built around host-authoritative multiplayer, server-side combat/skill resolution, and a runtime integration path for ML-trained boss movement.

The project should not be described as a finished commercial game. It is better described as a systems-heavy portfolio project focused on multiplayer authority, gameplay systems, boss AI integration, and AI-assisted development workflow.

## Public Pitch

```text
I built a Unity 2-player co-op boss fight runtime where clients send gameplay intent and the host resolves combat, skill effects, and synchronized state.

For boss movement AI, I separated training into a related Unity ML-Agents project called BuildUp. BuildUp trains movement policies using 129-channel observations and 5 discrete movement actions, then exports ONNX models that can be placed into the ArenaCombat_server runtime.
```

## Main Strengths

- clear host-authoritative boundary
- server-side combat and skill resolution
- data-driven auto-cast skill system
- ONNX-based boss movement integration path
- traceable AI-assisted development harness

## Evidence To Link

- `Assets/ArenaCombat/Docs/NETWORK_ARCHITECTURE.md`
- `Assets/ArenaCombat/Docs/SKILL_SYSTEM_DESIGN.md`
- `Assets/ArenaCombat/Docs/BUILDUP_INTEGRATION_PLAN.md`
- `Assets/ArenaCombat/Docs/ML_TRAINING_REFERENCE.md`
- `codex-review/CODEX_PROTOCOL.md`
- `codex-review/history/`

