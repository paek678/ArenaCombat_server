# Arena Combat 프로젝트 구조 (2026-05-11 기준)

> 이 문서는 현재 코드베이스 실제 상태와 Unity 2022.3 LTS → Unity 6.3 LTS 마이그레이션에서 발견된 이슈를 정리한 스냅샷이다.
> 기획/아키텍처 원칙은 [NETWORK_ARCHITECTURE.md](NETWORK_ARCHITECTURE.md)를 따른다.

---

## 1. 기술 스택 (현재 실제)

| 항목 | 버전 | 비고 |
|------|------|------|
| Unity Editor | `6000.3.11f1` (Unity 6.3 LTS) | 2022.3 LTS에서 마이그레이션 완료 |
| Netcode for GameObjects | `2.11.0` | NGO 2.x RPC 패턴 사용 |
| Unity Transport | `2.7.2` | |
| Universal Render Pipeline | `17.3.0` | |
| Input System | `1.19.0` | 신규 시스템으로 마이그레이션 완료 (`Mouse.current` / `Keyboard.current` 직접 접근). Active Input Handling = `1` (New only) |
| Lobby | `1.3.0` | |
| Relay | `1.0.5` | |
| Authentication | `3.6.1` | |
| AI Navigation | `2.0.11` | |
| Visual Scripting | `1.9.10` | |

> ⚠️ `NETWORK_ARCHITECTURE.md` 2.2절에 아직 "Unity 2022.3 LTS"라고 적혀있음 — 갱신 필요.

---

## 2. 폴더/씬 구성

### 2.1 씬 흐름
```
SampleScene (로비)  ──[Host: StartGame()]──>  3DScene (게임플레이)
   │                                              │
   ├─ NetworkManager (DDOL)                       ├─ GameSceneInitializer
   ├─ RelayManager (DDOL)                         ├─ GameStateManager (NetworkObject)
   ├─ LobbyManager (DDOL)                         ├─ CombatManager (NetworkObject)
   ├─ PlayerSpawnManager (DDOL)                   ├─ InputValidator
   └─ LobbyTestUI (Scene only)                    ├─ MapBounds3D
                                                  └─ Main Camera + TopDownCameraFollow3D
```

### 2.2 코드 디렉터리

#### 활성 3D 런타임 (`Assets/ArenaCombat/Scripts/`)
- **Core/** — `GameSceneInitializer`, `MapBounds3D`, `TopDownCameraFollow3D` + 레거시 2D (`MapBounds`, `CameraFollow`)
- **Core/Network/** — `NetworkConstants`, `LobbyManager`, `RelayManager`, `PlayerSpawnManager`, `PlayerSpawnPoint3D`, `PlayerNetworkController3D`, `GameStateManager`, `CombatManager`, `InputValidator`, `PlayerInfoDisplay` + 레거시 `PlayerNetworkController`
- **Character/Movement/** — `PlayerInputHandler`, `FollowMouseInstant`
- **Perk/Effects/** — 퍼크 효과 (부분 구현)
- **UI/TestUI/** — 디버그/테스트 UI

#### 3DScene 클라이언트 비주얼 (`Assets/ArenaCombat/3DSceneScript/Scripts/`)
- `Player.cs` — 클라이언트 측 로컬 표현
- `PlayerCamera.cs` — 임포트된 카메라 (Top-down과 충돌 회피 처리)
- `RopeAction.cs`, `RopeAnchor.cs` — 로프 시각/입력 브리지
- `CardManager.cs`, `CardUI.cs`, `AbilityCard.cs` — 카드 드래프트 UI
- `ArrowIndicator.cs`, `IconHover.cs`, `test.cs`

#### 기타 에셋
- `Player/`, `Props/`, `Resources/`, `SpeedTutor - Tutorial Scene - FREE/`

---

## 3. NGO 2.x 네트워크 구조 (현재 실제 동작)

### 3.1 권한 모델
- **Host-authoritative** — 호스트가 client + server 동시 수행 (dedicated server 아님)
- 클라이언트는 **입력 의도**만 `Rpc(SendTo.Server)`로 전송
- 서버가 검증 → 상태 게이트 → 판정 → `NetworkVariable` / `Rpc(SendTo.ClientsAndHost)` 반영

### 3.2 RPC 패턴 (NGO 2.x로 정상 마이그레이션됨)
- ✅ `[Rpc(SendTo.Server)]`, `[Rpc(SendTo.ClientsAndHost)]` 사용
- ✅ `[ServerRpc]`, `[ClientRpc]` 미사용 확인됨
- ✅ `NetworkManager.SceneManager.LoadScene()` 사용 (`RelayManager.cs:295`)

### 3.3 NetworkVariable 맵
| 컴포넌트 | 변수 |
|---------|------|
| `PlayerNetworkController3D` | `networkPosition`, `networkYaw`, `networkHP`, `networkIsAlive`, `networkStateId`, `networkStatusMask`, `networkTeamId`, `networkIsRoping`, `networkRopeTarget` |
| `GameStateManager` | `networkMatchState`, `networkGameMode`, `networkTimer`, `networkRoundNumber`, `networkCardDraftActive`, `networkCardDraftRound`, `networkCardDraftTimer` |

### 3.4 큐 정책
- 큐 사용: `RequestRopeServerRpc`, `RequestPerkTriggerServerRpc` (정렬: `clientTick` → `actionPriority` → `receivedAt`)
- 큐 미사용 (latest-intent): 이동 입력

---

## 4. Unity 2022 → 6.3 마이그레이션 이슈

### 4.1 ✅ 정상 마이그레이션된 항목
- `Rigidbody.linearVelocity` 사용 (구 `velocity` 미사용)
- `FindObjectsByType<T>(FindObjectsSortMode.None)` 사용 (`PlayerNetworkController3D.cs:1259` 등)
- NGO 2.x RPC attribute 일괄 적용
- `NetworkManager.SceneManager` 사용
- `#if UNITY_2022` 등 버전 게이트 잔재 없음
- **Input System (2026-05-11 완료)** — 4개 파일에서 `UnityEngine.Input.*` 제거, `UnityEngine.InputSystem.Mouse.current` / `Keyboard.current` 직접 접근으로 전환

### 4.2 ~~Legacy Input System 잔재~~ → 해결됨 (2026-05-11)

Active Input Handling이 `1` (New only)이라 기존 `UnityEngine.Input.*` 호출은 런타임에 `InvalidOperationException`을 던지는 상태였음. 다음 파일을 모두 `UnityEngine.InputSystem` 직접 접근으로 마이그레이션:

| 파일 | 변경 내용 |
|------|---------|
| [Scripts/Character/Movement/PlayerInputHandler.cs](../../Character/Movement/PlayerInputHandler.cs) | `Input.GetAxisRaw` → `ReadMoveAxis()` 헬퍼 (WASD + 화살표), `Input.GetKey/Down` → `Keyboard.current.<key>.isPressed/wasPressedThisFrame`, `Input.GetMouseButton*` → `Mouse.current.leftButton.*`, `Input.mousePosition` → `Mouse.current.position.ReadValue()` |
| [3DSceneScript/Scripts/Player.cs](../../../3DSceneScript/Scripts/Player.cs) | 동일 헬퍼 + Mouse 처리 |
| [Scripts/Character/Movement/FollowMouseInstant.cs](../../Character/Movement/FollowMouseInstant.cs) | `Input.mousePosition` → `Mouse.current.position.ReadValue()` |
| [3DSceneScript/Scripts/RopeAction.cs](../../../3DSceneScript/Scripts/RopeAction.cs) | `Input.GetMouseButtonDown(0)` → `Mouse.current.leftButton.wasPressedThisFrame`, `Input.mousePosition` 동일 처리 |

마이그레이션 정책: **InputAction 에셋 신규 생성 없이 직접 디바이스 접근** — 기존 이벤트 기반 아키텍처(`PlayerInputHandler` 이벤트들) 보존, `using UnityEngine.InputSystem;` 추가만으로 최소 침습.

### 4.3 🟡 점검 권장 — `Camera.main` 사용처

대부분 `Start()`/초기화에서 캐시되어 있어 6.3에서 큰 문제는 없으나, 일부 매 프레임 접근 패턴 존재:

- [PlayerNetworkController3D.cs:1253-1255](PlayerNetworkController3D.cs#L1253-L1255) — 한 메서드 내 다중 접근. 캐시 권장.
- 기타: `FollowMouseInstant.cs:40`, `PlayerInfoDisplay.cs:42,55`, `RopeAction.cs:48,237`, `Player.cs:26,33` — 캐시 패턴 확인됨

### 4.4 📝 문서 정합성 이슈

[NETWORK_ARCHITECTURE.md:33](NETWORK_ARCHITECTURE.md#L33)에 아직 `Unity 2022.3 LTS`로 표기되어 있음. 6.3으로 갱신 필요.
또한 2.3절에서 `ServerRpc`라는 구 NGO 1.x 용어를 사용 — 실제 코드는 NGO 2.x `[Rpc(SendTo.Server)]`로 동작 중이므로 문구 수정 필요.

---

## 5. 알려진 코드 버그 (마이그레이션 무관)

이전 점검(2026-04-08) 기준 미해결 이슈 — 코드 변경 시 검증 필요.

### 5.1 High Priority
1. ✅ **RESOLVED 2026-05-11** (ROADMAP A2) — `PlayerNetworkController3D`의 server-path 직접 위치 쓰기 (`rb.position = X`, `transform.position = X`)는 모두 로컬 `authoritativePos` 기반 `rb.MovePosition` 경로로 교체됨. Rope arrival도 `ResolveServerPosition` 거침. Codex 검증 3 라운드 통과.
2. **DEFERRED to D1** — `PlayerNetworkController.GetSpawnPosition()` Vector3.up*5 버그는 레거시 2D 파일에 존재. 활성 3D는 `PlayerSpawnManager` 사용으로 dormant. Phase D1 (legacy 2D removal)에서 정리.
3. **NEXT (A2-followup)** — `lastValidatedServerPosition` 갱신 순서가 `UpdateServerTimers` 호출 전이라 rope step의 `ResolveServerPosition` 두 번째 인자가 한-fixed-step 옛 위치. memory의 "rope이 bounds 밖으로 다시 밀어냄" 노트가 실제 버그인지 런타임 검증 필요.

### 5.2 Medium Priority
4. ✅ **RESOLVED 2026-05-11** (A4-3) — `if (!networkIsRoping.Value)` redundant wrapper 제거. line 622 early return 덕분에 condition이 항상 true였음.
5. ✅ **RESOLVED 2026-05-11** (A4-2) — `ASSIST_WINDOW` 상수 삭제. assist tracking 시스템 구현 시 (Phase B) 재도입 예정.
6. ✅ **RESOLVED 2026-05-11** (A4-1) — `MapBounds3D.TryResolveRopeTarget` `Vector3.zero` sentinel 제거. 전체 rope chain에 `bool hasAnchorHint` 전파됨.

---

## 6. 구현 진행 상태

### 6.1 동작 중
- SampleScene → 3DScene NGO 씬 전환
- DDOL 매니저 체인
- 3D 플레이어 스폰 + 오너십
- 서버 권위 3D 이동 동기화
- 로프 요청 서버 검증 + 큐 처리
- 퍼크 트리거 서버 검증 + 큐 처리
- 글로벌 카드 드래프트 시작/종료 동기화
- 카드 선택 서버 검증 + 2P 슬롯 (Host=Left, Guest=Right)
- 연결 끊김 감지 → SampleScene 복귀
- 3DSceneScript 임포트 코드 ↔ 네트워크 브리지

### 6.2 미구현 (사실처럼 말하지 말 것)
- 최종 3D 히트 판정 / 데미지 처리 (Physics overlap/cast)
- `ISkillAction` composite tree 스킬 시스템 (설계 확정, 코드 0)
- 보스 상태머신 / 페이즈 / 텔레그래프 런타임
- 적응형 AI 통계 수집 + 가중치 반영
- 레거시 2D 코드 제거

### 6.3 다음 우선순위
1. 최종 3D 전투 판정 (Physics overlap/cast 데미지)
2. `ISkillAction` composite tree 실행
3. 보스 #1 상태머신 + 기본 패턴 세트
4. 보스 텔레그래프 + 페이즈 전환
5. 플레이어 행동 편향 로그 수집
6. BT 기반 플레이어 에이전트
7. 보스 적응형 가중치 적용
8. 레거시 2D 완전 제거

---

## 7. 절대 규칙 (이 프로젝트에서 지켜야 함)

- Unity 6.3 API만 사용 (`FindObjectsByType`, `[Rpc(SendTo.X)]`, `Rigidbody.linearVelocity`)
- NGO 2.x RPC 패턴만 사용 (`[ServerRpc]`/`[ClientRpc]` 금지)
- 서버 권위 로직은 `if (IsServer)` 가드 내부
- `NetworkVariable` = Server Write, Everyone Read
- 클라이언트는 `Rpc(SendTo.Server)`로 의도만 전송
- 새 기능은 **3D 경로에만** 추가 (레거시 2D 코드 수정 금지)
- 변경 시 [NETWORK_ARCHITECTURE.md](NETWORK_ARCHITECTURE.md)와 본 문서 동기화
