# Project Overview

ArenaCombat_server is the multiplayer runtime for the Arena Combat / Tenebris project. Two players cooperate against a boss in a Unity 3D top-down action game.

## Project Roles

| Project | Responsibility |
| --- | --- |
| ArenaCombat_server | Multiplayer sessions, authoritative combat and skills, player state, and boss movement inference |
| BuildUp | Player bots, PPO movement training, matchup analysis, and ONNX model outputs |

Clients send gameplay intent to the host. The host validates requests, resolves combat and skill effects, and synchronizes resulting state through Netcode for GameObjects.

Boss movement policies are trained in [BuildUp](https://github.com/paek678/BuildUp). The runtime provides observation collection and ONNX model assignment, while skill selection and damage resolution remain in the authoritative game systems.

## Core Systems

- [Multiplayer runtime](multiplayer-runtime.md): host authority, request validation, and state synchronization.
- [Combat and skill system](combat-skill-system.md): shared skill definitions, execution context, and server-side effects.
- [Boss AI integration](boss-ai-integration.md): movement inference, player classification, and model selection.

## Scene Entry Points

The build settings enable `Assets/Scenes/SampleScene.unity` as the lobby entry scene and `Assets/Scenes/Chapter1.unity` as the gameplay scene. `SessionManager` uses the same scene names for match start and return to the lobby.

## Implementation References

- [Build scene settings](../../ProjectSettings/EditorBuildSettings.asset)
- [Session manager](../../Assets/ArenaCombat/Scripts/Core/Network/SessionManager.cs)
- [Skill system design](../../Assets/ArenaCombat/Docs/SKILL_SYSTEM_DESIGN.md)
- [Network architecture notes](../../Assets/ArenaCombat/Docs/NETWORK_ARCHITECTURE.md)
- [Historical BuildUp integration plan](../../Assets/ArenaCombat/Docs/BUILDUP_INTEGRATION_PLAN.md)
- [ML training reference](../../Assets/ArenaCombat/Docs/ML_TRAINING_REFERENCE.md)

Architecture and integration notes contain earlier implementation stages. Current code and build settings determine the active runtime paths.

## Validation and Development Records

The repository contains runtime code and ONNX assets. Final playable behavior, model selection, latency, synchronization under load, and balance require runtime validation.

The implementation and review workflow is recorded separately in [AI development workflow](ai-development-harness.md), [review protocol](../../codex-review/CODEX_PROTOCOL.md), and [review history](../../codex-review/history/).
