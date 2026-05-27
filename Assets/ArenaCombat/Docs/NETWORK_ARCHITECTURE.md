<!--
ARCH MAP (Code Scope)
- ACTIVE_RUNTIME: PlayerNetworkController3D, MapBounds3D, TopDownCameraFollow3D, PlayerSpawnManager, GameStateManager, CombatManager, Relay/Lobby flow
- LEGACY_FALLBACK_2D: PlayerNetworkController, GrapplingHook/GrappleRangePreview, MapBounds, CameraFollow
- PLANNED_NOT_IMPLEMENTED: final 3D hit judgment pipeline, boss FSM/runtime, adaptive boss AI, final perk execution graph
-->

# Arena Combat Network Architecture

## 1. 문서 목적

이 문서는 Arena Combat의 현재 코드 구조와 확정된 기획 방향을 함께 기록하는 아키텍처 기준 문서다.

이 문서를 읽는 AI는 아래를 구분해야 한다.
- 현재 코드에서 실제로 동작하는 것
- 기획은 확정되었지만 아직 구현되지 않은 것
- 과거 2D 경로의 잔재로 남아 있지만 현재 타깃이 아닌 것

핵심 원칙:
- 현재 타깃 구조는 `2인 협동 보스전`, `3D 탑다운`, `호스트 권위 서버`, `퍼크 드래프트`, `패링`, `로프 액션`, `적응형 보스 AI` 방향이다.
- 현재 코드의 실제 런타임 경로는 `3D 우선`이며, 2D 코드는 호환/잔재 수준이다.
- 클라이언트는 입력 의도만 보내고, 최종 판정은 호스트 서버가 확정한다.

## 2. 프로젝트 정체성

### 2.1 게임 정체성
- 프로젝트명: `Arena Combat`
- 장르: `2인 협동 보스전 3D 탑다운 멀티플레이 액션 로그라이트`
- 핵심 플레이: 이동, 기본 공격, 스킬, 패링, 로프 액션, 전투 중 퍼크 드래프트
- 차별 포인트: 플레이어 행동 편향에 따라 보스 대응 패턴이 달라지는 적응형 전투 구조

### 2.2 기술 스택
- Unity `6.3 LTS` (`6000.3.11f1`, 2022.3 LTS에서 마이그레이션)
- NGO `2.11.0` (`Netcode for GameObjects` 2.x — `[Rpc(SendTo.X)]` syntax)
- Unity Transport `2.7.2`
- URP `17.3.0`
- Input System `1.19.0` (Active Input Handling = New only — `Mouse.current` / `Keyboard.current` 직접 접근)
- Unity Relay `1.0.5` + Unity Lobby `1.3.0` + Authentication `3.6.1`
- 호스트 권위 서버 구조 (`Host-authoritative`, dedicated server 아님)
- GitHub 기반 협업

### 2.3 서버 모델
- Host는 `클라이언트 + 서버` 역할을 동시에 수행한다.
- Remote Client는 `입력 의도`만 `[Rpc(SendTo.Server)]`로 보낸다.
- 서버는 `입력 검증 -> 상태 게이트 -> 판정 -> NetworkVariable / [Rpc(SendTo.ClientsAndHost)] 반영` 순서로 처리한다.

## 3. 확정된 기획 방향

### 3.1 채택된 방향
- `2인 협동 보스전`
- `3D + 탑다운`
- `퍼크 드래프트`
- `보스 퍼크 드래프트`
- `패링`
- `스킬`
- `로프 액션`
- `사용자 학습 AI`

### 3.2 보류 또는 제외된 방향
- 난투형 PvP
- 1vs1 / 2vs2 / 4인 개인전
- 캐릭터 셔플/조작권 재할당 기믹
- 2D 메인 타깃 복귀
- Dedicated Server 기반 구조

## 4. 씬 구조와 역할

### 4.1 SampleScene
역할:
- 로비/세션 부트스트랩 씬
- DDOL 매니저 유지

주요 오브젝트:
- `NetworkManager`
- `RelayManager`
- `LobbyManager`
- `PlayerSpawnManager` (DDOL로 함께 운영 가능)
- 로비 UI Canvas
- EventSystem

### 4.2 3DScene
역할:
- 실제 게임플레이 씬
- 2인 전투, 카드 드래프트, 보스전이 진행되는 씬

주요 오브젝트:
- `GameSceneInitializer`
- `GameStateManager` (`NetworkObject`)
- `CombatManager` (`NetworkObject`)
- `InputValidator`
- `MapBounds3D`
- Main Camera + `TopDownCameraFollow3D`
- Gameplay UI Canvas + EventSystem

## 5. 현재 실제 런타임 경로

현재 코드 기준 실제 런타임 중심 경로:
- `RelayManager` / `LobbyManager` / `NetworkManager`가 세션 연결과 씬 전환을 담당
- `PlayerSpawnManager`가 3D 플레이어 스폰을 담당
- `PlayerNetworkController3D`가 3D 플레이어의 서버 권위 이동/행동 게이트를 담당
- `MapBounds3D`가 이동 가능 영역, 로프 타깃 검증, 세이프 스폰 포인트 계산을 담당
- `GameStateManager`가 전투 상태와 글로벌 카드 드래프트 타이밍을 서버 기준으로 관리
- `CombatManager`가 플레이어 등록, 사망 기록, 3D 퍼크 트리거 게이트를 담당
- `TopDownCameraFollow3D`가 오너 플레이어 기준 탑다운 카메라 추적을 담당

레거시 2D 경로:
- `PlayerNetworkController`
- `GrapplingHook`
- `GrappleRangePreview`
- `MapBounds`
- `CameraFollow`

이들은 현재 메인 타깃이 아니며, 새 작업은 가능하면 3D 경로에만 붙여야 한다.

## 6. 현재 구현된 3D 핵심 컴포넌트

### 6.1 PlayerNetworkController3D
파일:
- `Assets/ArenaCombat/Scripts/Core/Network/PlayerNetworkController3D.cs`

현재 역할:
- 서버 권위 이동 처리
- 로프 요청 검증 및 큐 처리
- 퍼크 트리거 요청 검증 및 큐 처리
- HP, 상태, 위치, 방향, 로프 상태 NetworkVariable 유지
- 외부 3D 클라이언트 코드와 네트워크 경로를 연결하는 진입점 제공

### 6.2 MapBounds3D
파일:
- `Assets/ArenaCombat/Scripts/Core/MapBounds3D.cs`

현재 역할:
- 서버 위치 클램프
- 킬존 체크
- 안전한 리스폰 지점 계산
- 로프 타깃 유효성 검사

### 6.3 TopDownCameraFollow3D
파일:
- `Assets/ArenaCombat/Scripts/Core/TopDownCameraFollow3D.cs`

현재 역할:
- 3D 탑다운 카메라 추적
- 오너 플레이어 추적
- imported `PlayerCamera`와의 충돌 회피 지원

### 6.4 CombatManager
파일:
- `Assets/ArenaCombat/Scripts/Core/Network/CombatManager.cs`

현재 역할:
- 3D 플레이어 등록/해제
- 플레이어 사망 기록 반영
- 3D 퍼크 트리거 서버 게이트
- 레거시 직접 공격 RPC는 기본 비활성화

### 6.5 GameStateManager
파일:
- `Assets/ArenaCombat/Scripts/Core/Network/GameStateManager.cs`

현재 역할:
- `MatchState` 관리
- 서버 기준 글로벌 카드 드래프트 사이클 관리
- 드래프트 활성 시 이동/행동 차단 기준 제공

## 7. 현재 구현된 서버 동기화 모델

### 7.1 권한 원칙
- 클라이언트는 결과를 확정하지 않는다.
- 클라이언트는 `시도` 또는 `의도`만 보낸다.
- 서버가 최종 판정과 상태를 확정한다.
- 상태값은 `NetworkVariable`, 사건/연출은 `[Rpc(SendTo.ClientsAndHost)]`, 계산 중간값은 `서버 내부 변수`로 관리한다.

### 7.2 InputValidator 정책
파일:
- `Assets/ArenaCombat/Scripts/Core/Network/InputValidator.cs`

현재 적용된 정책:
- per-client + per-request-type rate limit
- per-client + per-request-type monotonic tick validation
- float/vector payload sanitize

### 7.3 큐 정책
현재 큐 사용:
- `RequestRopeRpc` (`[Rpc(SendTo.Server)]`, `bool hasAnchorHint` 포함 — 2026-05-11 A4-1 propagation)
- `RequestPerkTriggerRpc` (`[Rpc(SendTo.Server)]`)

큐 비사용:
- 이동 입력 (`latest intent wins`)

정렬 기준:
- `clientTick`
- `actionPriority`
- `receivedAt`

## 8. 현재 실제 3D NetworkVariable 기준

`PlayerNetworkController3D`에 구현된 주요 NetworkVariable:
- `networkPosition` (`Vector3`)
- `networkYaw` (`float`)
- `networkHP` (`float`)
- `networkIsAlive` (`bool`)
- `networkStateId` (`CharacterStateId`)
- `networkStatusMask` (`StatusMask`)
- `networkTeamId` (`TeamId`)
- `networkIsRoping` (`bool`)
- `networkRopeTarget` (`Vector3`)

`GameStateManager`에 구현되었거나 직접적으로 연결된 글로벌 상태:
- `MatchState`
- `networkTimer`
- `networkCardDraftActive`
- `networkCardDraftRound`
- `networkCardDraftTimer`

아직 기획만 있고 본격 구현되지 않은 글로벌 보스 상태:
- `BossPhase`
- `BossCurrentPatternId`
- `BossHP`
- `BossAlive`
- `BossStatusMask`

## 9. 현재 확정된 상태 모델 (기획 확정, 코드 완전 반영 전)

### 9.1 Main State
- `Idle`
- `Move`
- `Attack`
- `Skill`
- `Parry`
- `Rope`
- `Hit`
- `Down`
- `Dead`
- `Respawn`
- `Locked`

### 9.2 Sub State
- `None`
- `Startup`
- `Active`
- `Recovery`
- `Interrupted`
- `Channeling`
- `Attach`
- `Pull`
- `Release`
- `Counter`
- `Knockback`
- `GetUp`
- `Drafting`
- `PhaseTransition`

### 9.3 Status
- `Stunned`
- `HitStunned`
- `Rooted`
- `Slowed`
- `Vulnerable`
- `Invulnerable`
- `Shielded`
- `Reflecting`
- `HPRegen`
- `DamageOverTime`
- `DamageUp`
- `DamageDown`
- `DefenseUp`
- `DefenseDown`
- `Buffed`
- `Debuffed`
- `Marked`
- `AggroFocused`

중요:
- `Main State`는 큰 행동 하나만 유지
- `Sub State`는 그 행동의 세부 단계 하나만 유지
- `Status`는 다중 중첩 가능

## 10. 현재 확정된 스탯 모델 (기획 확정, 코드 부분 반영)

### 10.1 플레이어 기준 스탯
- `MaxHP`, `CurrentHP`
- `BaseDamage`, `BaseDefense`
- `CritChance`, `CritMultiplier`
- `MoveSpeed`, `TurnSpeed`, `ActionSpeed`
- `MoveAcceleration`, `MoveDeceleration`
- `RopeRange`, `RopeSpeed`, `RopeCooldown`, `RopeAttachTime`, `RopeReleaseRecovery`
- `AttackAreaScale`, `SkillPower`, `SkillCooldownMultiplier`, `ChannelDurationMultiplier`
- `ParryWindow`, `ParryCooldown`, `CounterWindow`
- `ShieldMax`, `CurrentShield`, `HPRegenRate`, `ReviveTime`
- `AggroWeight`

### 10.2 공용 전투 스탯
- `DamageTakenMultiplier`
- `HealingReceivedMultiplier`
- `MoveControlMultiplier`
- `RopeCancelResistance`
- `SpawnInvulnerableDuration`
- `StunDurationMultiplier`
- `CrowdControlPower`
- `CrowdControlResistance`
- `HitStunResistance`
- `DebuffDurationResistance`
- `DamageUpMultiplier`
- `DefenseUpMultiplier`
- `VulnerabilityBonus`
- `ReflectRatio`

### 10.3 보스 전용 스탯
- `BossMaxHP`, `BossCurrentHP`
- `BossBaseDamage`, `BossBaseDefense`
- `BossPhaseThresholds`
- `BossTelegraphTimeMultiplier`
- `BossAggroSensitivity`

## 11. 적응형 보스 AI 설계 방향 (기획 확정, 런타임 미구현)

### 11.1 플레이어 행동 편향
- `근접 선호`
- `원거리 유지`
- `공격 집중`
- `생존 우선`
- `패링 의존`
- `로프 기동`
- `스킬 중심`
- `팀 밀착`
- `팀 분산`

### 11.2 보스 행동 분류
- `근접 압박형`
- `원거리 견제형`
- `패링 견제형`
- `로프 대응형`
- `폭딜 대응형`
- `생존 압박형`
- `분산 대응형`
- `밀집 대응형`
- `적응형 변칙형`

### 11.3 가중치 운영 기준
계획 기준:
- 편향 점수 범위: `0.0 ~ 1.0`
- 평가 주기: `5초`
- 관측 구간: `최근 15~20초`
- 일반 상승: `+0.02`
- 강한 상승: `+0.05`
- 일반 하락: `-0.02`
- 자연 감쇠: `-0.01 / 평가주기`

### 11.4 구현 전략
현재 가장 현실적인 구현 계획:
- 플레이어 쪽은 처음부터 ML-Agent로 만들지 않는다.
- 먼저 `Behavior Tree` 기반 플레이어 에이전트를 여러 타입으로 만든다.
- 보스는 그 플레이어 편향에 대응하도록 가중치 기반 또는 ML-Agent 방식으로 발전시킨다.
- 즉, `Player BT -> Boss 대응 로직 -> 필요 시 Boss ML-Agent` 순서가 우선이다.

## 12. 현재 코드 기준 구현 완료 / 부분 완료 / 미구현

### 12.1 구현 완료 또는 동작 중
- SampleScene -> 3DScene 세션 흐름
- 3D 플레이어 스폰과 오너십 연결
- 3D 이동 동기화
- 로프 요청 검증과 서버 큐 처리
- 글로벌 카드 드래프트 시작/종료 동기화
- 카드 선택 서버 검증과 2인 슬롯 레이아웃 동기화
- owner camera binding
- 3DSceneScript imported 코드와 네트워크 브리지 연동

### 12.2 부분 완료
- 3D 전투 판정 구조
- 3D 퍼크 실행 구조
- 디버그/로그 가시성
- 스폰 정책의 최종 맵 종속 결정

### 12.3 아직 미구현 또는 설계 단계
- 최종 3D 히트 판정 파이프라인
- 보스 상태머신
- 보스 페이즈 전환 런타임
- 보스 텔레그래프/패턴 시스템
- 적응형 AI 통계 수집과 가중치 반영
- 최종 퍼크 효과 그래프/테이블
- 레거시 2D 완전 제거

## 13. 다음 구현 우선순위

권장 순서:
1. 최종 3D 전투 판정 파이프라인 정리
2. 스킬/퍼크 효과 실행 구조 고정
3. 보스 1종의 상태머신과 기본 패턴 세트 구축
4. 보스 페이즈와 텔레그래프 연결
5. 플레이어 행동 편향 수집 로그 추가
6. BT 기반 플레이어 에이전트 초안 제작
7. 보스 대응 가중치 로직 연결
8. 레거시 2D 제거 또는 격리

## 14. 절대 오해하면 안 되는 것

새 AI는 아래를 사실처럼 말하면 안 된다.
- 최종 3D 히트 판정이 이미 완성되었다
- 보스 AI 런타임이 이미 구현되었다
- 적응형 AI가 실제 게임에서 동작 중이다
- 프로젝트가 dedicated server 기반이다
- 2D가 아직도 메인 타깃 경로다

새 AI는 아래를 사실로 취급해도 된다.
- 프로젝트의 메인 타깃은 3D 탑다운이다
- 현재 네트워크 구조는 host-authoritative다
- 카드 드래프트는 서버 기준으로 동기화된다
- 로프/퍼크 트리거는 서버 큐 기반 처리 경로가 있다
- 앞으로의 핵심은 보스/AI/최종 전투 판정 구현이다

## 15. 문서 동기화 규칙

아키텍처, 상태, 기획 방향, 구현 우선순위가 바뀌면 아래 문서를 같이 갱신한다.
- `Assets/ArenaCombat/Scripts/Core/Network/NETWORK_ARCHITECTURE.md`
- `C:/Users/paek6/.claude/projects/c--Users-paek6-Arena-Combat/memory/MEMORY.md`
- `C:/Users/paek6/.claude/projects/c--Users-paek6-Arena-Combat/memory/SESSION_PROGRESS.md`
- `C:/Users/paek6/.claude/projects/c--Users-paek6-Arena-Combat/memory/AI_PROMPT.md`
