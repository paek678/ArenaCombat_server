# ArenaCombat_server — 포트폴리오 분석 세션 프롬프트

이 프롬프트를 Claude / ChatGPT / Codex 등 어떤 AI 세션이든 첫 메시지로 통째로 붙여넣으면, 이 프로젝트를 포트폴리오로 정리하는 데 필요한 분석을 한 번에 수행합니다.

---

## (여기서부터 복사)

당신은 Unity 게임 프로젝트 **ArenaCombat_server**를 개발자 포트폴리오용으로 분석하는 전문 컨설턴트입니다.

이 프로젝트는 단순 Unity 게임 구현이 아닙니다. 아래 한 줄로 정의하세요:
"Host-authoritative 멀티플레이 구조와 서버 권위 전투/스킬/보스 AI 런타임을 가진 2인 협동 3D 액션 게임"

---

### 1. 프로젝트 정체성

- **장르**: 2인 협동 보스전 3D 탑다운 멀티플레이 액션 로그라이트
- **핵심 플레이**: 이동, 기본 공격, 스킬(자동 시전), 패링, 로프 액션, 전투 중 퍼크 드래프트
- **차별점**: 플레이어 행동 편향(9차원 bias vector)에 따라 보스 대응 패턴이 실시간으로 바뀌는 적응형 전투 구조
- **플레이어 수**: 호스트 + 클라이언트 = 2명 고정 (Host-authoritative, Dedicated Server 아님)
- **연관 프로젝트**: BuildUp (ML 학습 환경) — 보스 이동 모델을 ONNX로 export하여 이 프로젝트에 drop-in하는 구조

---

### 2. 기술 스택 (정확한 버전)

| 항목 | 버전 |
|------|------|
| Unity Editor | `6000.3.11f1` (Unity 6.3 LTS) |
| Netcode for GameObjects | `2.11.0` (NGO 2.x) |
| Unity Transport | `2.7.2` |
| URP | `17.3.0` |
| Input System | `1.19.0` (New only) |
| Lobby / Relay / Auth | `1.3.0` / `1.0.5` / `3.6.1` |
| ML-Agents | 3.x (ONNX inference) |

이전에 Unity 2022.3 LTS + NGO 1.x에서 마이그레이션했으므로, 마이그레이션 과정 자체가 포트폴리오 소재입니다.

---

### 3. 아키텍처 구조 (7-Layer Stack)

```
L6 PRESENTATION — UI / VFX / SFX / 카메라 (클라이언트 전용, 권위 없음)
L5 ENTITY LAYER — PlayerNetworkController3D / BossNetworkController3D / SkillProjectile / SkillArea
L4 MANAGER LAYER — GameStateManager / PlayerSpawnManager / CombatManager3D / StatManager / StateManager / SkillManager / SkillExecutor / ProjectilePool / PersistentAreaManager / BossManager
L3 CONTRACT LAYER — ICombatant / IProjectile / IPersistentArea / IPoolable / SkillStep / SkillCondition delegates
L2 DATA LAYER — BaseStatsSO / PlayerStatsSO / BossStatsSO / AttackData3D / SkillDefinition / AbilityCard
L1 NETWORK PRIMITIVES — NetworkVariable<T> / [Rpc(SendTo.X)] / NetworkObject
L0 TRANSPORT — Unity Transport + Relay
```

읽는 방향: 상위 → 하위 호출. 하위 → 상위 접근 금지.
매니저는 판정(judgment), 엔티티는 상태 미러 + 이동(locomotion), SO는 정적 데이터를 소유.

---

### 4. 핵심 구현 영역 — 반드시 분석

#### [A] Host-Authoritative 서버 권위 구조

- 클라이언트는 이동/공격/패링/로프/퍼크 의도만 `[Rpc(SendTo.Server)]`로 전송
- 서버가 검증 → 상태 게이트 → 판정 → `NetworkVariable` / `[Rpc(SendTo.ClientsAndHost)]` 반영
- 영구 상태 = `NetworkVariable` (서버 Write, 모두 Read)
- 사건/연출 = `Rpc(SendTo.ClientsAndHost)`
- 순서 민감 요청만 큐 사용 (rope, perk, attack, parry). 이동은 latest-intent.
- 관련 파일: `PlayerNetworkController3D.cs`, `CombatManager3D.cs`, `GameStateManager.cs`

#### [B] 입력 검증 시스템 (InputValidator)

- per-client + per-request-type rate limit
- per-client + per-request-type monotonic tick validation
- float/vector payload sanitize (NaN, Infinity, magnitude clamp)
- 요청 큐 정렬: `clientTick` → `actionPriority`(Parry=0 > Rope=1 > Attack=2 > PerkTrigger=3) → `receivedAt`
- 상태 게이트: dead, stunned, card draft active 시 요청 차단
- 관련 파일: `InputValidator.cs`

#### [C] 3D 전투 판정 파이프라인 (CombatManager3D)

- `Physics.OverlapBox` + LayerMask 필터로 서버 측 히트 판정
- Self/team/dead 제외 + 레지스트리 검증 + 게임 쿨다운
- 패링 시스템: any-parrier-blocks-all + attacker stun
- K/D/A 추적: `kills3D` / `deaths3D` / `assists3D` (10초 윈도우, 10f 데미지 임계값)
- Kill-zone 낙사 처리: `Die(OwnerClientId)` → deaths++ (킬 크레딧 없음)
- 관련 파일: `CombatManager3D.cs`, `AttackData3D.cs`

#### [D] 자동 스킬 시스템 (Vampire Survivors 방식)

- 스킬 입력키 없음 — `SkillManager`가 서버 FixedUpdate마다 slot priority + cooldown 기준으로 자동 선택
- `SkillExecutor`가 composite tree(`SkillStep` 델리게이트 체인) 실행
- `SkillComponents`: 37개 factory (DealDirectionalHit, ApplyInArea, LaunchProjectile, SpawnPersistentArea 등)
- `SkillLibrary`: 29 SkillStep + 4 SkillCondition (33 composite tree 정의)
- 12 플레이어 스킬 + 9 보스 스킬 SO 구현
- 플레이어와 보스가 동일한 `SkillExecutor` + `SkillManager` 파이프라인 공유
- 서버만 cooldown, condition, hit, damage 확정 / 클라이언트는 VFX/SFX 렌더링만
- 특수 트리거: 패링 성공 시 / 로프 도착 시 슬롯 스캔
- 관련 파일: `SkillManager.cs`, `SkillExecutor.cs`, `SkillComponents.cs`, `SkillLibrary.cs`, `SkillDefinition.cs`

#### [E] 보스 AI 적응형 전투 구조

- **PlayerArchetypeClassifier**: 3 weight bucket(Melee/Ranged/CC) → 4 archetype(Hybrid/Melee/Ranged/CC)
  - 180초마다 평가, 카드 드래프트 시 강제 재평가, 평가 후 ×0.5 decay
- **PlayerBiasTracker**: 9차원 bias vector (MeleePrefer / RangeKeep / AttackFocus / SurvivalFirst / ParryDepend / RopeManeuver / SkillCentric / TeamCluster / TeamSpread)
  - 5초마다 평가, 각 차원 0.0~1.0 정규화
- **BossAdaptiveWeights**: 9개 BiasResponseMap (플레이어 근접 선호 → 보스 원거리 대응 등)
  - 가중치 = `baseWeight(1.0) + avgBias × biasMultiplier(2.0)`
- **BossAIPoolManager**: 2인 archetype pair(10 조합) → `BossAIDefinition`(skillSlots[5] + slotWeights[5] + cooldownScale) swap
- **BossDraftManager**: pair index × phaseMultiplier + reactiveBonus 기반 보스 스킬 선택
- **BossObservationCollector**: 40-obs ML observation spec (양 플레이어 위치/HP/방향/거리/속도 + 보스 HP/phase + 5 skill CD% + cast state + burst damage)
- **BossInferenceAgent**: ML-Agents ONNX 추론 (4-action discrete: idle/forward/left/right) — 이동만 ML, 스킬 선택은 auto-cast
- **BossPhase**: None → Phase1(100~70%) → Phase2(70~40%) → Phase3(40~10%) → Enrage(<10%) → Defeated
- 관련 파일: `Core/AI/` 전체 (12개 파일)

#### [F] NGO 동기화 모델

- **NetworkVariable 인벤토리**:
  - PNC3D: `networkPosition`, `networkYaw`, `networkHP`, `networkIsAlive`, `networkStateId`, `networkStatusMask`, `networkTeamId`, `networkIsRoping`, `networkRopeTarget`, `networkShield`
  - BNC3D: `networkHP`, `networkIsAlive`, `networkPosition`, `networkYaw`, `networkCurrentPhase`, `networkStatusMask`
  - GSM: `networkMatchState`, `networkGameMode`, `networkTimer`, `networkRoundNumber`, `networkCardDraftActive/Round/Timer`, `networkMatchEndReason`
- **RPC 방향 규칙**: 의도 = `SendTo.Server` / 결과 브로드캐스트 = `SendTo.ClientsAndHost` / 개인 피드백 = `SendTo.Owner`
- **StatusMask/BuffMask/DebuffMask**: StatManager 이벤트 → NV bitmask 브리지로 실시간 동기화
- **StatManager.Tick()**: PNC3D/BNC3D FixedUpdate에서 호출, status/buff/debuff 타이머 + HP regen 처리
- 관련 파일: `SYNC_AUDIT.md`, `NetworkConstants.cs`

#### [G] 씬 구조 + 세션 흐름

- SampleScene(로비, index 0) → Chapter1(게임플레이, index 1)
- DDOL 매니저 체인: `NetworkManager` / `RelayManager` / `LobbyManager` / `PlayerSpawnManager`
- Chapter1: `GameStateManager`(NB) / `CombatManager3D`(NB) / `InputValidator` / `MapBounds3D` / `BossManager`
- Relay/Lobby/Authentication으로 NAT 통과 + 세션 관리
- 관련 파일: `RelayManager.cs`, `LobbyManager.cs`, `PlayerSpawnManager.cs`

---

### 5. 코드 통계

핵심 코드 디렉토리: `Assets/ArenaCombat/Scripts/Core/`

| 디렉토리 | 파일 수 | 역할 |
|----------|---------|------|
| Network/ | 14 | 서버 권위 매니저 + 엔티티 컨트롤러 |
| Skill/ | 16 | 자동 스킬 파이프라인 전체 |
| AI/ | 12 | 적응형 보스 AI + ML 추론 + 플레이어 분류 |
| Combat/ | 5 | 스탯 SO + ICombatant 인터페이스 |
| State/ | 2 | StateManager + CombatantState FSM |
| Stats/ | 1 | StatManager (HP/Shield/buff/debuff/status 권위 계산) |
| Card/ | 4 | 카드 드래프트 UI |
| Gameplay/ | 5 | MapBounds3D, 카메라, 씬 초기화 |
| UI/ | 2 | HUD, MatchEnd UI |

총 약 **61개 핵심 스크립트**

주요 파일 규모:

| 파일 | LOC 규모 | 포트폴리오 포인트 |
|------|----------|------------------|
| PlayerNetworkController3D.cs | ~2000+ | 서버 권위 이동/로프/퍼크/공격/패링 + ICombatant 구현 |
| CombatManager3D.cs | ~600 | Physics.OverlapBox 히트 판정 + K/D/A + 패링 시스템 |
| GameStateManager.cs | ~1000 | MatchState + 카드 드래프트 사이클 + Match End/Restart |
| InputValidator.cs | ~200 | Rate limit + tick validation + payload sanitize |
| SkillManager.cs | ~300 | Auto-cast tick + telegraph 상태머신 |
| SkillComponents.cs | ~550 | 37개 SkillStep factory |
| SkillLibrary.cs | ~400 | 29 SkillStep + 4 SkillCondition composite tree |
| StatManager.cs | ~700 | HP/Shield/buff/debuff/status 권위 계산 + coroutine 타이머 |
| BossNetworkController3D.cs | ~500 | Phase tracking + auto-cast + ML inference 호출 |
| BossAdaptiveWeights.cs | ~150 | 9 BiasResponseMap + 가중치 공식 |
| BossObservationCollector.cs | ~200 | 40-obs ML observation vector |
| PlayerArchetypeClassifier.cs | ~200 | 3-bucket 분류 + 180초 평가 + decay |
| PlayerBiasTracker.cs | ~150 | 9차원 bias vector + 5초 평가 |
| RelayManager.cs | ~300 | Unity Relay/Lobby/Auth 세션 흐름 |

---

### 6. 마이그레이션 / 문제 해결 사례 (반드시 포함)

아래 각각을 **"문제 → 원인 → 해결 → 결과 → 관련 파일"** 형식으로 정리하세요:

**(1) Unity 2022.3 → Unity 6.3 LTS 전체 마이그레이션**
- `Rigidbody.velocity` → `linearVelocity`
- `FindObjectsOfType` → `FindObjectsByType<T>(FindObjectsSortMode.None)`
- `rb.position` 직접 대입 → `rb.MovePosition` (충돌 보존)

**(2) NGO 1.x → NGO 2.x RPC 패턴 전환**
- `[ServerRpc]`/`[ClientRpc]` → `[Rpc(SendTo.Server)]`/`[Rpc(SendTo.ClientsAndHost)]`
- `RpcParams` 통합, `ServerRpcParams`/`ClientRpcParams` 폐기

**(3) Legacy Input System → New Input System 전환**
- `UnityEngine.Input.GetKey/GetAxisRaw` → `Keyboard.current`/`Mouse.current` 직접 접근
- InputAction 에셋 없이 최소 침습 마이그레이션 (기존 이벤트 아키텍처 보존)
- 4개 파일 일괄 전환: `PlayerInputHandler.cs`, `Player.cs`, `FollowMouseInstant.cs`, `RopeAction.cs`

**(4) Buildup 프로젝트 통합 (Path B Wrapper Integration)**
- 별도 브랜치(Buildup/Tenebris)의 37 SkillComponents + StatManager + BossController를 NGO host-authoritative 패턴으로 포팅
- 30+ Codex 검증 라운드를 거친 안전한 단계별 통합
- `.meta` GUID 보존으로 SO/씬 참조 무결성 유지

**(5) MPPM Editor Freeze 해결**
- ML-Agents gRPC 동기 연결이 메인 스레드 블록 → Reflection 기반 안전 가드 (`BossManager.EnsureAcademyWontBlock()`)

**(6) Network Sync 전면 감사 (SYNC-FIX)**
- StatusMask/Shield/Buff/Debuff NV 동기화 누락 발견
- StatManager 이벤트 브리지 패턴(`OnStatusApplied/Removed/BulkCleared` → NV bitmask update)으로 전면 수정
- ICombatant 10개 mutating method에 `IsServer` 가드 추가

---

### 7. 산출물 요청

위 내용을 기반으로 다음 산출물을 **한국어**로 작성하세요.
코드 식별자와 기술 용어는 영어 그대로 유지합니다.

```
[1] 프로젝트 한 줄 요약 (1문장)

[2] 포트폴리오용 프로젝트 설명 (300자 내외)

[3] 내 역할로 정리 가능한 내용 (bullet 5~7개)

[4] 기술 스택 테이블

[5] 핵심 구현 5~7개 (각각 제목 + 2~3문장 설명)

[6] 아키텍처 흐름도 (텍스트 다이어그램)
    - 클라이언트 입력 → 서버 검증 → 판정 → 동기화 전체 흐름
    - 자동 스킬 시전 흐름
    - 보스 AI 적응 흐름 (플레이어 행동 → 분류 → 가중치 → 스킬 선택)

[7] 문제 해결 사례 6개 (각각: 문제 / 원인 / 해결 / 결과 / 관련 파일)

[8] GitHub README용 요약 (영문, 200단어 내외)

[9] Notion 포트폴리오 카드용 요약 (한국어, 150자 내외)

[10] 과장 위험 문장 체크리스트
     - "~완성", "~상용 수준", "ML AI가 완벽히 적용" 같은 과장 표현 대신 사용할 정확한 표현 제시

[11] 스크린샷 / 영상으로 남기면 좋은 장면 5~7개
```

---

### 8. 과장 금지 규칙

아래 표현은 구현 상태를 확인하지 않고 사용하지 마세요:

| ✗ 과장 | ✓ 정확한 표현 |
|--------|--------------|
| 완성된 상용 게임 | 멀티플레이 전투 런타임 구현 |
| 전용 서버 구현 | 호스트 권위 서버 구조 |
| ML 보스 AI가 완벽히 적용됨 | ML 보스 이동 모델을 연동할 수 있는 구조를 설계하고 ONNX 추론 파이프라인을 구현 |
| 모든 스킬 31종 완성 | 12 플레이어 스킬 + 9 보스 스킬 SO를 구현하고 auto-cast 파이프라인으로 실행 |
| 상용 수준의 치트 방지 | per-client rate limit, monotonic tick validation, payload sanitize로 입력 검증 계층 구현 |
| 대규모 멀티플레이 지원 | 2인 협동 호스트 권위 멀티플레이 |

**사실로 취급 가능:**
- 메인 타깃은 3D 탑다운
- 호스트 권위 서버 구조로 전투 판정 서버 확정
- 카드 드래프트는 서버 기준 동기화
- 로프/퍼크/공격/패링 트리거는 서버 큐 기반 처리
- 자동 스킬 시전 파이프라인 서버 권위 실행
- 적응형 보스 AI가 플레이어 행동에 따라 스킬 가중치를 실시간 조정

---

### 9. 참조한 프로젝트 문서

| 문서 | 역할 |
|------|------|
| `Assets/ArenaCombat/Docs/NETWORK_ARCHITECTURE.md` | 현재 상태 아키텍처 — 서버 모델, 씬 구조, NV 맵, 큐 정책, 상태/스탯 모델 |
| `Assets/ArenaCombat/Docs/PROJECT_STRUCTURE.md` | 코드베이스 스냅샷 — 기술 스택 버전, 폴더/씬 구성, 마이그레이션 이슈 |
| `Assets/ArenaCombat/Docs/SKILL_SYSTEM_DESIGN.md` | 스킬 시스템 기획 — Auto-cast 흐름, 컴포넌트 카탈로그, 서버 권위 계약 |
| `Assets/ArenaCombat/Docs/BUILDUP_INTEGRATION_PLAN.md` | 통합 계획 — Path B Wrapper, disposition 결정, 위험 완화 |
| `Assets/ArenaCombat/Docs/ML_TRAINING_REFERENCE.md` | ML/AI 수치 레퍼런스 — 40-obs spec, bias index, 분류 알고리즘, 보스 phase |
| `Assets/ArenaCombat/Docs/PLAYER_CLASSIFICATION_WEIGHTS.md` | 분류 가중치 튜닝 — 9,134 매치 기반 임계값 조정 |
| `Assets/ArenaCombat/Docs/SERVER_CHANGELOG.md` | 서버 측 변경 기록 — MPPM freeze, SYNC-FIX, OOM 대응 |
| `Assets/ArenaCombat/Docs/TARGET_ARCHITECTURE.md` | 목표 아키텍처 (North Star) — 7-Layer Stack, Authority Partition |
| `Assets/ArenaCombat/Docs/SYNC_AUDIT.md` | 동기화 감사 — NV/RPC 인벤토리 전수, 이슈 severity 분류 |
| `Assets/ArenaCombat/Docs/ROADMAP.md` | 통합 로드맵 — Phase A~X 전체 작업 이력, 50+ Codex 검증 기록 |

---

### 10. 코드를 직접 읽을 수 있는 경우

이 프롬프트의 컨텍스트만으로 분석이 가능하지만, 파일 접근이 가능한 AI 환경이라면 다음을 추가로 읽어서 분석에 반영하세요:

```
Assets/ArenaCombat/Scripts/Core/Network/
Assets/ArenaCombat/Scripts/Core/Skill/
Assets/ArenaCombat/Scripts/Core/AI/
Assets/ArenaCombat/Scripts/Core/Combat/
Assets/ArenaCombat/Scripts/Core/Stats/
Assets/ArenaCombat/Scripts/Core/State/
```

## (여기까지 복사)

---

## 사용 팁

- **Codex 세션**: `codex-review/codex-session-prompt.md`와 동일한 방식으로, 새 세션의 첫 메시지에 "여기서부터 복사" ~ "여기까지 복사" 구간을 통째로 붙여넣기
- **Claude Code 세션**: 프롬프트 전송 후 "위 문서 목록의 파일들도 직접 읽어서 분석에 반영해줘" 추가
- **산출물 형식 변경**: 섹션 7의 `[1]`~`[11]` 항목을 원하는 대로 수정/추가/삭제. 예: `[12] 기술 면접 예상 질문 10개`
- **이 프롬프트가 outdated 됐다면**: 프로젝트 문서(`Docs/` 폴더)가 갱신될 때 이 파일도 같이 갱신
