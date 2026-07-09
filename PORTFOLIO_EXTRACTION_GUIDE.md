# ArenaCombat_server 포트폴리오 추출 가이드

## 목적

이 문서는 `ArenaCombat_server` 프로젝트 내부 세션에서 포트폴리오에 필요한 내용을 뽑기 위한 기준 문서다.

이 프로젝트는 단순 Unity 게임 구현이 아니라, 다음 주제로 정리하는 것이 가장 좋다.

```text
Host-authoritative 멀티플레이 구조와 서버 권위 전투/스킬/보스 AI 런타임을 가진 2인 협동 3D 액션 게임
```

## 포트폴리오에서 잡을 핵심 주제

### 1. 메인 프로젝트 카드

```text
Arena Combat Runtime
2인 협동 3D 보스전 게임의 멀티플레이 런타임, 서버 권위 전투 판정, 자동 스킬 시스템, 보스 AI 연동 구조를 구현한 Unity 프로젝트
```

### 2. 케이스스터디 후보

1. Host-authoritative 멀티플레이 구조 설계
2. 클라이언트 입력 의도 전송과 서버 검증 구조
3. 서버 권위 자동 스킬 시스템 설계
4. Unity 2022에서 Unity 6.3 / NGO 2.x로 마이그레이션
5. ML 보스 AI 모델을 실제 게임 런타임에 연동하기 위한 구조

## 반드시 확인할 문서

우선 아래 문서를 읽고 내용을 뽑는다.

```text
Assets/ArenaCombat/Docs/NETWORK_ARCHITECTURE.md
Assets/ArenaCombat/Docs/PROJECT_STRUCTURE.md
Assets/ArenaCombat/Docs/SKILL_SYSTEM_DESIGN.md
Assets/ArenaCombat/Docs/BUILDUP_INTEGRATION_PLAN.md
Assets/ArenaCombat/Docs/ML_TRAINING_REFERENCE.md
Assets/ArenaCombat/Docs/PLAYER_CLASSIFICATION_WEIGHTS.md
Assets/ArenaCombat/Docs/SERVER_CHANGELOG.md
Assets/ArenaCombat/Docs/ROADMAP.md
```

## 반드시 확인할 코드 영역

```text
Assets/ArenaCombat/Scripts/Core/Network/
Assets/ArenaCombat/Scripts/Core/Skill/
Assets/ArenaCombat/Scripts/Core/AI/
Assets/ArenaCombat/Scripts/Core/Combat/
```

주요 클래스:

```text
RelayManager
LobbyManager
PlayerSpawnManager
PlayerNetworkController3D
InputValidator
GameStateManager
CombatManager3D
BossNetworkController3D
BossManager
RoundRecordLogger
BossInferenceAgent
BossObservationCollector
BossAdaptiveWeights
BossAIDefinition
BossDraftManager
SkillDefinition
SkillContext
SkillRegistry
SkillExecutor
SkillManager
SkillLibrary
SkillComponents
SkillProjectile
ProjectilePool
SkillArea
PersistentAreaManager
```

## 뽑아야 할 내용

### 1. 프로젝트 개요

아래 질문에 답한다.

- 이 게임은 어떤 장르인가?
- 핵심 플레이는 무엇인가?
- 왜 2인 협동 보스전인가?
- 일반 보스전과 비교해 차별점은 무엇인가?
- BuildUp ML 학습 프로젝트와 어떤 관계인가?

포트폴리오용 핵심 문장 예시:

```text
2인 협동 3D 탑다운 보스전 게임에서 클라이언트는 입력 의도만 보내고, 호스트 서버가 이동/전투/스킬 판정을 확정하는 서버 권위 멀티플레이 구조를 설계했습니다.
```

### 2. 네트워크 구조

다음 항목을 반드시 뽑는다.

- Host-authoritative 구조를 선택한 이유
- Dedicated Server가 아닌 Host 구조를 선택한 이유
- Unity Relay / Lobby / Authentication 흐름
- SampleScene에서 3DScene으로 넘어가는 흐름
- NetworkManager, RelayManager, LobbyManager, PlayerSpawnManager 역할
- 클라이언트가 직접 판정하지 않는 구조
- NetworkVariable과 RPC의 역할 분리

정리 형식:

```text
문제:
멀티플레이 액션 게임에서 클라이언트가 직접 결과를 확정하면 위치/공격/스킬 판정 불일치와 조작 위험이 생긴다.

해결:
클라이언트는 이동/로프/퍼크 사용 의도만 서버로 보내고, 서버가 검증 후 NetworkVariable과 Client RPC로 결과를 반영하도록 구성했다.

결과:
게임 상태의 최종 권한을 서버에 모아 전투 판정과 동기화 흐름을 일관되게 유지할 수 있게 했다.
```

### 3. 입력 검증 / 서버 판정

`InputValidator`, `PlayerNetworkController3D`, `CombatManager3D`를 중심으로 뽑는다.

확인할 내용:

- rate limit
- monotonic tick validation
- float/vector payload sanitize
- movement는 latest intent wins
- rope/perk는 queue 처리
- actionPriority, clientTick, receivedAt 정렬
- 상태 게이트: dead, stunned, card draft active 등

포트폴리오에서 중요한 이유:

```text
게임 네트워크에서 입력을 그냥 동기화한 것이 아니라, 서버가 검증 가능한 의도 단위로 나누고 요청 종류에 따라 처리 정책을 다르게 설계했다는 점이 강점이다.
```

### 4. 자동 스킬 시스템

`SKILL_SYSTEM_DESIGN.md`와 `Assets/ArenaCombat/Scripts/Core/Skill/`을 기준으로 뽑는다.

반드시 포함:

- Vampire Survivors식 auto-cast 구조
- 스킬 입력키 없음
- SkillManager가 slot priority와 cooldown을 기준으로 스킬 선택
- SkillExecutor가 composite tree 실행
- SkillDefinition은 데이터와 RuntimeStep을 가진다
- projectile / area / persistent area / status / buff 처리
- 서버만 cooldown, condition, hit, damage를 확정
- 클라이언트는 VFX/SFX 렌더링만 담당

정리 형식:

```text
문제:
멀티플레이에서 플레이어/보스 스킬을 클라이언트 입력 기반으로 처리하면 쿨타임, 타격, 투사체 충돌이 불일치할 수 있다.

해결:
서버가 조건/쿨타임/대상 판단을 수행하는 auto-cast 스킬 파이프라인을 만들고, 클라이언트는 스킬 시작 연출만 받아 렌더링하게 했다.

결과:
플레이어와 보스가 동일한 스킬 실행 구조를 공유하면서도, 서버 권위 전투 판정을 유지할 수 있게 했다.
```

### 5. 보스 AI / ML 연동 구조

`Core/AI`와 BuildUp 문서 연결을 기준으로 뽑는다.

확인할 내용:

- BossInferenceAgent
- BossObservationCollector
- BossAdaptiveWeights
- BossAIDefinition
- BossDraftManager
- BossAIPoolManager
- RoundRecordLogger
- ONNX 모델을 넣기 위한 구조
- ML은 boss movement 중심, skill은 SkillManager가 처리한다는 분리

포트폴리오 핵심:

```text
학습 프로젝트(BuildUp)에서 생성한 보스 이동 모델을 실제 게임 프로젝트(ArenaCombat_server)에 drop-in할 수 있도록, observation/action spec과 런타임 구조를 분리해 설계했다.
```

### 6. 마이그레이션 / 문제 해결

반드시 뽑을 문제 해결 사례:

- Unity 2022.3에서 Unity 6.3 LTS로 마이그레이션
- NGO 1.x 방식에서 NGO 2.x `[Rpc(SendTo.X)]` 방식으로 전환
- Legacy Input System 제거 후 New Input System으로 전환
- 2D 레거시 코드와 3D 런타임 경로 분리
- `Rigidbody.velocity` 계열 API 변경 대응
- `FindObjectsByType` 등 Unity 6 API 대응

각 문제는 아래 형식으로 정리한다.

```text
문제:

원인:

해결:

결과:

관련 파일:
```

## 클로드/코덱스 세션에 넣을 요청 프롬프트

```text
이 프로젝트의 PORTFOLIO_EXTRACTION_GUIDE.md를 기준으로 ArenaCombat_server를 개발자 포트폴리오용으로 분석해줘.

목표:
이 프로젝트를 "Host-authoritative 멀티플레이 전투 런타임과 서버 권위 스킬/보스 AI 구조를 가진 Unity 게임 프로젝트"로 정리하고 싶다.

반드시 확인할 문서:
- Assets/ArenaCombat/Docs/NETWORK_ARCHITECTURE.md
- Assets/ArenaCombat/Docs/PROJECT_STRUCTURE.md
- Assets/ArenaCombat/Docs/SKILL_SYSTEM_DESIGN.md
- Assets/ArenaCombat/Docs/BUILDUP_INTEGRATION_PLAN.md
- Assets/ArenaCombat/Docs/ML_TRAINING_REFERENCE.md

반드시 확인할 코드:
- Assets/ArenaCombat/Scripts/Core/Network/
- Assets/ArenaCombat/Scripts/Core/Skill/
- Assets/ArenaCombat/Scripts/Core/AI/

산출물 형식:

[프로젝트 한 줄 요약]

[포트폴리오용 프로젝트 설명]

[내 역할로 정리 가능한 내용]

[기술 스택]

[핵심 구현 5개]

[아키텍처 흐름]

[문제 해결 사례 5개]
- 문제:
- 원인:
- 해결:
- 결과:
- 관련 파일:

[GitHub README용 요약]

[Notion 포트폴리오 카드용 요약]

[확인 필요 / 과장 위험 문장]

[스크린샷 또는 영상으로 남기면 좋은 장면]
```

## 외부 포트폴리오에 넣을 때의 우선순위

가장 중요:

1. Host-authoritative 서버 구조
2. 서버 권위 전투 판정
3. 자동 스킬 시스템
4. ML 보스 AI 연동 가능 구조
5. Unity/NGO/InputSystem 마이그레이션 문제 해결

덜 중요:

- 단순 에셋 import
- 단순 UI 버튼
- 임시 테스트 코드
- 레거시 2D 경로
- 아직 구현되지 않은 기획만 있는 기능

## 과장 금지

아래는 구현 상태를 확인하지 않고 단정하지 않는다.

- "완성된 상용 게임"
- "전용 서버 구현"
- "ML 보스 AI가 완벽히 적용됨"
- "모든 스킬 31종 완성"
- "상용 수준의 치트 방지"
- "대규모 멀티플레이 지원"

대신 이렇게 쓴다.

```text
호스트 권위 구조 기반의 2인 협동 멀티플레이 전투 런타임을 구현하고, ML 기반 보스 이동 모델을 연동할 수 있는 구조를 설계했습니다.
```
