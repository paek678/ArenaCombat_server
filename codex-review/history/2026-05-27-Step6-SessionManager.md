# Step 6: SessionManager Extraction — Disconnect Ownership Unification

## Date
2026-05-27

## Topic
Extract scene transition, disconnect, and network callback ownership from RelayManager + GameSceneInitializer into SessionManager DDOL singleton.

## Files Changed

| File | Action |
|------|--------|
| `Core/Network/SessionManager.cs` | CREATE |
| `Core/Network/RelayManager.cs` | MODIFY (423→~170 lines) |
| `Core/GameSceneInitializer.cs` | MODIFY (166→~60 lines) |
| `UI/TestUI/LobbyTestUI.cs` | MODIFY (reference changes) |

## Review Rounds

### Round 1: REVISE
1 critical + 3 suggestions:
- Critical: SessionManager not wired into scene (requires Editor setup post-review)
- Suggestion: OnDisconnectClicked duplicate UI reset — fixed by removing redundant calls
- Suggestion: LoadScene return check missing — added SceneEventProgressStatus check
- Suggestion: OnClientStopped guard coupled to RelayManager — acceptable for Relay-only architecture

### Round 2: PASS WITH NOTES
All code issues resolved. Notes:
- Scene wiring (add SessionManager to SampleScene DDOL GO) required before runtime
- Added SetGameSessionActive(false) rollback when LoadScene fails
- OnDisconnectClicked now single-owned by Disconnect → ForceCleanup → OnLobbyLeft chain
- Callback ownership clean: SessionManager sole registrant for all 4 NGO callbacks
