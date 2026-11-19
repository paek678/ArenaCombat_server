# 테네브리스(Tenebris) | 2인 협동 보스전

두 플레이어가 한 팀이 되어 보스를 상대하는 Unity 기반 3D 탑다운 액션 게임입니다. 보스는 두 사람이 얼마나 가까이에서 싸우고 어떤 스킬을 쓰는지 관찰합니다. 두 사람의 전투 성향에 따라 보스의 이동 방식과 다음에 사용할 수 있는 스킬 구성이 달라집니다.

![테네브리스 타이틀 화면과 멀티플레이 메뉴](docs/screenshots/title-screen.png)

## 한 판의 흐름

1. **방 만들기와 참가** — 한 명이 방을 열고 다른 한 명이 참가합니다. 방을 연 플레이어가 호스트를 맡습니다.
2. **협동 전투** — 두 플레이어가 보스를 공격하는 동안 호스트가 공격과 스킬 효과를 판정하고, 그 결과를 두 화면에 동기화합니다.
3. **카드 선택** — 플레이어가 카드를 골라 스킬을 구성합니다. 카드 선택이 끝나면 보스도 두 플레이어의 성향과 최근 전투 행동을 반영해 대응 스킬을 고릅니다.

| 방 생성·참가 | 카드 선택 |
| --- | --- |
| ![방 생성과 참가 코드 입력 메뉴](docs/screenshots/room-menu.png) | ![세 장의 카드 중 하나를 고르는 선택 화면](docs/screenshots/card-selection.png) |
| 호스트가 방을 만들고 다른 플레이어가 참가 코드를 입력하는 메뉴입니다. | 플레이어가 다음 전투에 사용할 스킬을 구성하는 선택 단계입니다. |

카드 선택 사진은 로컬 호스트 1인 테스트에서 해당 단계를 디버그 호출해 촬영했습니다.

## 방 참가에서 전투까지

로비에서는 함께 플레이할 사람을 모으고, 게임을 시작할 때 Relay를 통해 호스트와 참가자의 통신 경로를 연결합니다.

```mermaid
flowchart TD
    Auth["Unity Services 초기화<br/>각 플레이어 익명 인증"] --> HostLobby["호스트가 Lobby 생성"]
    Auth --> GuestLobby["참가자가 방 코드로 Lobby 참가"]
    HostLobby --> Start["호스트가 게임 시작"]
    Start --> Relay["Relay 연결 생성<br/>NGO 호스트 시작"]
    Relay --> Publish["로비에 Relay 코드와 시작 상태 공유"]
    GuestLobby --> Read["참가자가 로비의 시작 상태 확인"]
    Publish --> Read
    Read --> Join["Relay 코드로 접속<br/>NGO 클라이언트 시작"]
    Relay --> Scene["전투 씬 Chapter1 전환·동기화"]
    Join --> Scene
```

플레이어는 방 코드를 입력해 로비에 참가합니다. 게임 시작 시 필요한 Relay 접속 코드는 로비 데이터를 통해 전달합니다.

## 전투 한 번의 처리 흐름

두 플레이어의 공격과 스킬 효과는 호스트에서 확정합니다. 같은 호스트에서 보스의 이동 추론과 전투 로직도 실행합니다.

```mermaid
flowchart TD
    Input["플레이어 이동·공격·스킬 입력"] --> Request["클라이언트가 입력·사용 요청 전달"]
    Request --> Validate["호스트가 요청과 사용 조건 확인"]
    AI["보스 ONNX 이동 추론<br/>규칙·가중치 기반 스킬 선택"] --> Execute["호스트가 이동·스킬 실행"]
    Validate --> Execute
    Execute --> Judge["명중·피해·상태 변화 판정"]
    Judge --> Sync["NetworkVariable · RPC로 결과 동기화"]
    Sync --> Display["각 플레이어 화면에 위치·체력·이펙트 표시"]
```

[멀티플레이 실행 구조](docs/portfolio/multiplayer-runtime.md) · [전투 및 스킬 시스템](docs/portfolio/combat-skill-system.md)

## 보스는 어떻게 대응하나요?

보스는 근접 공격, 보스와의 거리, 사용하거나 장착한 스킬을 바탕으로 각 플레이어를 **근접형·원거리형·군중제어형·혼합형**으로 분류합니다. 두 사람의 성향 조합은 이동 모델을 고를 때와 대응 스킬을 고를 때 모두 사용합니다.

```mermaid
flowchart TD
    A["두 플레이어의 전투 행동과 장착 스킬"] --> B["각 플레이어의 성향 분류"]
    B --> C["두 사람의 성향 조합"]
    C --> M["미리 학습한 이동 모델 선택"]
    M --> N["현재 위치·거리·상태를 보고 다음 이동 결정"]
    C --> S["카드 선택 후 대응 스킬 후보 평가"]
    R["최근 전투 행동과 라운드 진행도"] --> S
    S --> T["점수가 가장 높은 후보를 보스 스킬 슬롯에 장착"]
```

이동 모델은 [BuildUp](https://github.com/paek678/BuildUp)에서 미리 학습합니다. 게임에서는 성향 조합에 맞는 모델을 불러와 다음 이동을 결정하며, 보스가 행동 중이면 교체를 보류합니다. 스킬 선택은 가중치 규칙이, 피해 판정은 호스트의 전투 코드가 담당합니다.

## 팀원 및 역할

| 이름 | 구분 | 담당 역할 |
| --- | --- | --- |
| 백성열 | 팀장 | 게임 기획, 서버 구축, 몬스터 AI 구현 |
| 홍채영 | 팀원 | 클라이언트 개발, 서버 연결, 셰이더 추가 |
| 곽다영 | 팀원 | UI/UX |

## 개발 정보

| 항목 | 내용 |
| --- | --- |
| 개발 | 빌드업 팀 |
| 개발기간 | 2026.03.30 ~ 2026.06.07 |
| 게임 엔진 | Unity 6000.3.11f1 |
| 멀티플레이 | Netcode for GameObjects · Unity Transport · Lobby · Relay · Authentication |
| 보스 이동 AI | Unity ML-Agents · ONNX 모델 추론 |

<a id="related-project-buildup-ml-training"></a>

## 코드와 문서

- [보스 AI 연동](docs/portfolio/boss-ai-integration.md) · [게임에 포함된 이동 모델](Assets/onnx_9matchup/) · [학습 프로젝트 BuildUp](https://github.com/paek678/BuildUp)
- [멀티플레이 실행 구조](docs/portfolio/multiplayer-runtime.md) · [전투와 스킬 시스템](docs/portfolio/combat-skill-system.md)
- [프로젝트 개요](docs/portfolio/overview.md) · [개발·검토 기록](docs/portfolio/ai-development-harness.md)
