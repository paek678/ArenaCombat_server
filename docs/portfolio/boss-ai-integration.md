# Boss AI Integration

## Goal

Boss AI is presented as a hybrid runtime structure.

The ML model should not be described as a full replacement for the boss AI. It is better described as a movement policy component that can be connected to the runtime through ONNX and matching observation/action specs.

## Relationship With BuildUp

```text
BuildUp
  - trains movement policies
  - produces ONNX model assets
  - analyzes matchup behavior

ArenaCombat_server
  - owns multiplayer authority
  - owns combat and skill rules
  - loads/places ONNX model assets
  - provides runtime observation/inference path
```

## Verified Project Evidence

- `Assets/ArenaCombat/Scripts/Core/AI/`
- `Assets/ML-Agents/`
- `Assets/onnx_9matchup/`
- `Assets/ArenaCombat/Docs/BUILDUP_INTEGRATION_PLAN.md`
- `Assets/ArenaCombat/Docs/ML_TRAINING_REFERENCE.md`

## Safe Portfolio Wording

```text
The runtime contains an integration path for ONNX-based boss movement policies. Movement decisions can be supplied by ML models, while skill execution, damage resolution, and multiplayer authority remain in the game runtime.
```

Avoid:

```text
fully autonomous boss AI
perfect ML boss
all combat decisions controlled by ML
```

