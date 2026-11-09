# 보스 AI 연동

## 설계 목적

보스 AI는 ONNX 이동 정책과 서버 측 스킬·전투 시스템을 결합합니다. 이동 정책은 129개 관측값과 단일 이산 행동 분기를 사용하며, 행동은 대기·전진·좌회전·우회전·후진의 5개로 구성합니다.

## BuildUp과의 역할 분담

```text
BuildUp
  - 이동 정책 학습
  - ONNX 모델 출력
  - 매치업별 행동 분석

ArenaCombat_server
  - 멀티플레이 판정 권한 관리
  - 전투와 스킬 규칙 처리
  - ONNX 모델 자산 배치 및 지정
  - 실행 환경의 관측값 수집과 추론 경로 제공
```

## 관련 코드 및 자산

- [AI 실행 코드](../../Assets/ArenaCombat/Scripts/Core/AI/)
- [ML 모델 자산](../../Assets/ML-Agents/)
- [매치업별 모델 자산](../../Assets/onnx_9matchup/)
- [과거 BuildUp 통합 계획](../../Assets/ArenaCombat/Docs/BUILDUP_INTEGRATION_PLAN.md)
- [ML 학습 참고 자료](../../Assets/ArenaCombat/Docs/ML_TRAINING_REFERENCE.md)

## 실행 흐름

`PlayerBiasTracker`가 전투 행동과 팀 간 거리를 기록합니다. `PlayerArchetypeClassifier`는 플레이어를 근접형(Melee), 원거리형(Ranged), 군중제어형(CC), 혼합형(Hybrid)으로 분류합니다. `BossAIPoolManager`는 두 플레이어의 성향 조합에 맞는 `BossAIDefinition`을 선택하고, `Agent.SetModel`로 해당 모델을 지정합니다. 보스가 행동 중이면 모델 교체를 보류합니다.

네트워크 세션에서 `BossInferenceAgent.OnActionReceived`는 서버 권한을 확인한 뒤 `BossNetworkController3D`를 통해 이동을 적용합니다. 스킬 선택, 자동 시전, 피해 판정은 서버의 실행 로직에서 처리합니다. `BossDraftManager`는 카드 드래프트가 끝난 뒤 대응 스킬을 선택합니다.

## 학습 환경과의 정합성 및 검증

관측값 구성, 정규화 방식, 행동 매핑, 이동 동작은 학습 환경과 일치해야 합니다. 현재 BuildUp 에이전트는 후진 속도에 전진 대비 70%를 적용하지만, 실행 환경 에이전트는 설정된 이동 속도를 그대로 사용합니다. 두 환경의 동작이 같다고 판단하기 전에 이 차이를 검증해야 합니다.

저장소에는 ONNX 자산과 모델 선택 코드가 포함되어 있습니다. 최종 모델 지정, 실제 2인 세션에서의 동작, 전투 밸런스는 실행 환경에서 검증해야 합니다. 과거 통합 계획과 인수인계 문서에는 이전 규격이 기록되어 있으므로 현재 코드와 대조하여 확인해야 합니다.
