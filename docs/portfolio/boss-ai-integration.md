# Boss AI Integration

## Goal

Boss AI combines an ONNX movement policy with server-side skill and combat systems. The movement policy uses 129 observation channels and one discrete branch with 5 actions: idle, forward, turn left, turn right, and backward.

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

- [AI runtime code](../../Assets/ArenaCombat/Scripts/Core/AI/)
- [ML model assets](../../Assets/ML-Agents/)
- [Matchup model assets](../../Assets/onnx_9matchup/)
- [Historical BuildUp integration plan](../../Assets/ArenaCombat/Docs/BUILDUP_INTEGRATION_PLAN.md)
- [ML training reference](../../Assets/ArenaCombat/Docs/ML_TRAINING_REFERENCE.md)

## Runtime Flow

`PlayerBiasTracker` records combat actions and team distance. `PlayerArchetypeClassifier` classifies players as Melee, Ranged, CC, or Hybrid. `BossAIPoolManager` uses the player pair to select a `BossAIDefinition` and assign its model through `Agent.SetModel`. Model changes are deferred while the boss is busy.

During a network session, `BossInferenceAgent.OnActionReceived` checks server authority before applying movement through `BossNetworkController3D`. Skill selection, auto-cast execution, and damage resolution remain in the server runtime. `BossDraftManager` handles response skill selection after card drafts.

## Runtime Alignment and Validation

The observation layout, normalization, action mapping, and movement behavior must match training. The current BuildUp agent scales backward movement to 70% of forward speed; the runtime agent uses its full configured movement speed for backward movement. This difference requires verification before declaring training and runtime behavior equivalent.

ONNX assets and model-selection code are present. Final model assignments, behavior in a 2-player session, and combat balance require runtime validation. Older integration and handoff documents record historical specifications and should be checked against the current code.
