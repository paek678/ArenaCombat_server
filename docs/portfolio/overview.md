# 프로젝트 개요

ArenaCombat_server는 Arena Combat / Tenebris 프로젝트의 멀티플레이 실행 환경입니다. 두 명의 플레이어가 Unity 기반의 3D 탑다운 액션 게임에서 협력하여 보스를 상대합니다.

## 프로젝트 역할

| 프로젝트 | 담당 영역 |
| --- | --- |
| ArenaCombat_server | 멀티플레이 세션, 호스트 권한 기반 전투·스킬 판정, 플레이어 상태, 보스 이동 추론 |
| BuildUp | 플레이어 봇, PPO 이동 정책 학습, 매치업별 분석, ONNX 모델 출력 |

클라이언트는 입력 의도를 호스트에 전달합니다. 호스트는 요청을 검증하고 전투와 스킬 효과를 처리한 뒤, Netcode for GameObjects를 통해 결과 상태를 동기화합니다.

보스 이동 정책은 [BuildUp](https://github.com/paek678/BuildUp)에서 학습합니다. 실행 환경은 관측값 수집과 ONNX 모델 지정을 담당하고, 스킬 선택과 피해 판정은 호스트 권한을 가진 게임 시스템에서 처리합니다.

## 핵심 시스템

- [멀티플레이 실행 구조](multiplayer-runtime.md): 호스트 권한, 요청 검증, 상태 동기화
- [전투 및 스킬 시스템](combat-skill-system.md): 공통 스킬 정의, 실행 문맥, 서버 측 효과 처리
- [보스 AI 연동](boss-ai-integration.md): 이동 추론, 플레이어 성향 분류, 모델 선택

## 시작 씬과 게임 씬

빌드 설정에는 로비 진입 씬인 `Assets/Scenes/SampleScene.unity`와 게임플레이 씬인 `Assets/Scenes/Chapter1.unity`가 등록되어 있습니다. `SessionManager`도 같은 씬 이름을 사용하여 게임을 시작하거나 로비로 돌아갑니다.

## 관련 코드 및 문서

- [빌드 씬 설정](../../ProjectSettings/EditorBuildSettings.asset)
- [세션 매니저](../../Assets/ArenaCombat/Scripts/Core/Network/SessionManager.cs)
- [스킬 시스템 설계](../../Assets/ArenaCombat/Docs/SKILL_SYSTEM_DESIGN.md)
- [네트워크 구조 기록](../../Assets/ArenaCombat/Docs/NETWORK_ARCHITECTURE.md)
- [과거 BuildUp 통합 계획](../../Assets/ArenaCombat/Docs/BUILDUP_INTEGRATION_PLAN.md)
- [ML 학습 참고 자료](../../Assets/ArenaCombat/Docs/ML_TRAINING_REFERENCE.md)

구조 설계와 통합 계획 문서에는 이전 개발 단계의 내용도 포함되어 있습니다. 현재 사용하는 실행 경로는 코드와 빌드 설정을 기준으로 확인해야 합니다.

## 검증 범위와 개발 기록

저장소에는 게임 실행 코드와 ONNX 모델 자산이 포함되어 있습니다. 최종 플레이 동작, 모델 선정, 네트워크 지연, 부하 상황의 동기화, 전투 밸런스는 실행 환경에서 검증해야 합니다.

구현과 검토 절차는 [AI 도구를 활용한 개발 및 검토 기록](ai-development-harness.md), [검토 절차](../../codex-review/CODEX_PROTOCOL.md), [검토 이력](../../codex-review/history/)에 별도로 정리되어 있습니다.
