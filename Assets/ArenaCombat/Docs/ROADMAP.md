# Arena Combat 통합 로드맵

> 이 문서는 **총괄 계획(roadmap)** 이다. 세부 작업은 이 로드맵 항목에 맞춰 즉시 진행한다 (매번 plan을 다시 세우지 않음). 방향을 바꾸려면 이 파일을 직접 수정하거나, AI에게 변경을 지시한다.
>
> - **DONE** = 완료, 회귀 방지만 신경
> - **IN PROGRESS** = 현재 작업 중 (한 번에 하나만)
> - **NEXT** = 합의된 다음 1~2 항목
> - **LATER** = 우선순위 정해졌지만 시점 미정 (순서 변경 가능)
> - **DEFERRED** = 의도적으로 보류
>
> **북극성 (north star)**: [TARGET_ARCHITECTURE.md](TARGET_ARCHITECTURE.md) — 도달 후 코드 형태. 작업이 어느 레이어에 들어가야 할지 헷갈리면 이 문서가 결정권.
>
> **스킬 시스템 기획**: [SKILL_SYSTEM_DESIGN.md](SKILL_SYSTEM_DESIGN.md) — 자동 시전 + 호스트 권위. X2-5~X2-13 작업 시 컴포넌트 역할/플로우/파라미터 단일 진실 공급원. **2026-05-12 reorder**: SkillComponents가 ProjectilePool/PersistentAreaManager 직접 호출 → Projectile/Area subsystem이 먼저 import되도록 순서 재배치 (X2-7 SkillComponents → X2-11로 후순위 이동).
>
> 마지막 갱신: 2026-05-16
>
> **씬 구조 (2026-05-16)**: SampleScene (lobby, index 0) → **Chapter1 (gameplay, index 1)**. 3DScene 삭제됨 — 모든 매니저/GO Chapter1으로 이관 완료.

---

## Phase A — 안정화 (Unity 2022→6.3 마이그레이션 + 알려진 버그)

이 페이즈가 끝나야 신규 기능 작업을 안정적으로 쌓을 수 있다.

### A1. Legacy Input System 마이그레이션 — **DONE** (2026-05-11)
- [PlayerInputHandler.cs](PlayerInputHandler.cs.meta 참조), [Player.cs](../../../3DSceneScript/Scripts/Player.cs), [FollowMouseInstant.cs](../../Character/Movement/FollowMouseInstant.cs), [RopeAction.cs](../../../3DSceneScript/Scripts/RopeAction.cs)
- `UnityEngine.Input.*` → `UnityEngine.InputSystem.Mouse.current` / `Keyboard.current` 직접 접근
- 자세한 내용은 [PROJECT_STRUCTURE.md §4.2](PROJECT_STRUCTURE.md) 참조

### A2. PlayerNetworkController3D 직접 위치 쓰기 제거 — **DONE** (2026-05-11)
- **A2-1** ✅ FixedUpdate bounds clamp (line 377) `rb.position = resolvedPos` → `rb.MovePosition(resolvedPos)` + 로컬 `authoritativePos` 도입
- **A2-2** ✅ Rope step (line 1164) `rb.position = next` → `rb.MovePosition(next)`
- **A2-3** ✅ Rope arrival (line 1150) `transform.position = ropeTargetPosition` → `ResolveServerPosition` 거친 후 `rb.MovePosition`
- **A2-4** ✅ `UpdateServerTimers` 시그니처 `void → Vector3 (return-style)`, respawn timer 경로에서 `authoritativePos = transform.position` 동기화
- 변경 결과: server-path 직접 위치 쓰기는 모두 collision-respecting `MovePosition` 경유. publish는 단일 로컬 `authoritativePos` 기반.
- Codex 검증 3 라운드 완료 ([history/2026-05-11-A2-rb-position-cleanup.md](../../../../codex-review/history/2026-05-11-A2-rb-position-cleanup.md))

### A3 (formerly memory item). 레거시 `PlayerNetworkController.GetSpawnPosition` Vector3.up*5 — **DEFERRED to D1**
- 코드 위치: `PlayerNetworkController.cs:850` (legacy 2D file)
- 활성 3D 경로는 `PlayerSpawnManager.GetSpawnPosition`을 사용하므로 dormant
- 코딩 규칙상 레거시 2D 수정 금지 → Phase D1 (legacy 2D removal)에서 함께 제거

### A2-followup. `lastValidatedServerPosition` 갱신 순서 + rope/bounds 재검증 — **BLOCKED (runtime test required)**
- A2 작업 중 Codex S-1이 발견: `lastValidatedServerPosition`가 line 381 (UpdateServerTimers 호출 전)에서 갱신되어, rope step 경로가 한-fixed-step 옛 "previous" 위치를 `ResolveServerPosition`의 두 번째 인자로 사용
- memory의 "rope이 bounds 밖으로 다시 밀어냄" note가 실제 버그인지 런타임 검증 필요
- 가능한 해결: bounds resolution을 FixedUpdate 끝에 한 번 더 (post-UpdateServerTimers), 또는 publish 직전에 final clamp

### A4. MapBounds3D + 잡일 정리 — **DONE** (2026-05-11)
- **A4-1** ✅ `MapBounds3D.TryResolveRopeTarget` `Vector3.zero` sentinel 제거. 전체 rope chain (RopeAction → SubmitRopeIntent → RequestRopeRpc → QueuedServerAction → ExecuteQueuedRopeAction → TryResolveRopeCandidateTarget → MapBounds3D)에 `bool hasAnchorHint` 전파. world-origin anchor도 정상 처리.
- **A4-2** ✅ `ASSIST_WINDOW` 미사용 상수 삭제. 설계 의도 (10s assist window)는 **Phase B1/B 작업 시 재도입 예정** — assist tracking 구현 시 `recentDamage` dict 옆에 `private const float ASSIST_WINDOW = 10f;` 다시 추가.
- **A4-3** ✅ `PlayerNetworkController3D.cs:647` redundant `if (!networkIsRoping.Value)` 래퍼 제거. 라인 622 early return 덕분에 항상 true였음.
- Codex 검증 2 라운드 완료 ([history/2026-05-11-A4-mapbounds-cleanup.md](../../../../codex-review/history/2026-05-11-A4-mapbounds-cleanup.md))

### A5. 문서 정합성 — **DONE** (2026-05-11)
- ✅ §2.2 Unity 버전 + 전체 패키지 버전 표 갱신 (`6000.3.11f1`, NGO 2.11, Transport 2.7.2, URP 17.3, Input System 1.19, Lobby/Relay/Auth)
- ✅ §2.3 RPC 용어 NGO 2.x로 일괄 갱신 (`ServerRpc` → `[Rpc(SendTo.Server)]`, `ClientRpc` → `[Rpc(SendTo.ClientsAndHost)]`)
- ✅ §7.1 사건/연출 RPC 표기 갱신
- ✅ §7.3 큐 정책의 메서드 이름 갱신 (`RequestRopeServerRpc` → `RequestRopeRpc`, `bool hasAnchorHint` 추가 명시)
- 코드 변경 없음 — Codex 검증 면제 (workflow 규칙)

---

## Phase B — 핵심 게임플레이 구현 (안정화 후)

### B1. 최종 3D 전투 판정 파이프라인 — **DONE** (2026-05-11)

Sub-items locked after B1-1 architecture round (Codex 3 라운드 통과):

- **B1-1** ✅ Architecture decisions DONE (2026-05-11). 코드 변경 0. ([history/2026-05-11-B1-1-architecture.md](../../../../codex-review/history/2026-05-11-B1-1-architecture.md))
- **B1-2** ✅ **DONE** (2026-05-11). `CombatManager3D.cs` skeleton + `AttackData3D.cs` ScriptableObject + `[SerializeField] List<AttackData3D> attackTable` + `Lookup(AttackType)` helper + `[RequireComponent(typeof(NetworkObject))]`. Scene requirement: `CombatManager3D`는 활성 3D 씬에 NetworkBehaviour로 존재해야 함. NO hit logic / NO RPCs / NO registration wiring (B1-3에서). Codex 2 라운드 통과 ([history/2026-05-11-B1-2-skeleton.md](../../../../codex-review/history/2026-05-11-B1-2-skeleton.md)).
- **B1-3** ✅ **DONE** (2026-05-11). Input subscription (`OnLightAttack`/`OnHeavyAttack`/`OnParry`) + `SubmitAttackIntent` + `SubmitParryIntent` + `RequestAttackRpc` (with `Enum.IsDefined`) + `RequestParryRpc` + reject RPCs (`SendTo.Owner`) + queue extension (`Attack`=2, `Parry`=3, `AttackKind` field) + actionPriority (Parry=0, Rope=1, Attack=2, PerkTrigger=3, **within-player only**) + `parryWindowDuration`/`parryCooldown` config + `parryWindowTimer`/`parryCooldownTimer` + `IsParrying` accessor + ExecuteQueued handlers (with resolver-failure rollback) + `parryWindowTimer` decrement in `UpdateServerTimers` + clear in Respawn/Die + CombatManager3D registration in `OnNetworkSpawn`/`OnNetworkDespawn` (with null guards) + CombatManager3D resolver stubs (`TryProcessAttack3D`/`TryProcessParry3D`) + result RPCs (`AttackResultRpc` with `ulong[] hitTargets` placeholder, `ParryStartedRpc`). Codex 2 라운드 통과 ([history/2026-05-11-B1-3-input-rpc-chain.md](../../../../codex-review/history/2026-05-11-B1-3-input-rpc-chain.md)).
- **B1-4** ✅ **DONE** (2026-05-11). `CombatManager3D.TryProcessAttack3D` 본 구현: `Physics.OverlapBox` + LayerMask filter + self/team/dead exclusion + registry validation (CI-B1-4-R2-1) + dedup + 게임 쿨다운 (`AttackData3D.Cooldown`) + parry handling (any-parrier-blocks-all + attacker stun via new `parryStunTimer`) + damage via `TakeDamage` + `ParrySuccessRpc` broadcast. Hit detection config (playerLayer + attackVerticalOffset + maxHitTargets cap on RPC payload only). `appliedStatus != None`은 무시 + warning. Owner-only reject(early) / broadcast(resolved hit/miss/parry) 경계 명확. `attackCooldowns3D` 라이프사이클 정리 (despawn + unregister). `SetStateId(Attacking)` recovery는 defer (animation 설계 대기). Codex 3 라운드 통과 ([history/2026-05-11-B1-4-hit-logic.md](../../../../codex-review/history/2026-05-11-B1-4-hit-logic.md)).
- **B1-5** ✅ **DONE** (2026-05-11). K/D/A 신규 dict (`kills3D`/`deaths3D`/`assists3D`/`recentDamage3D` with `DamageAttribution` struct: cumulative damage + lastDamageTime) in CombatManager3D + `OnPlayerDeath3D(victimId, killerId)` API + `ComputeAssisters3D` (assistWindow3D=10s + assistDamageThreshold=10f + 킬러/미등록 attacker 제외) + `TrackDamageForAssist3D` + `GetKills3D/GetDeaths3D/GetAssists3D/GetKDAString3D` + `[Rpc(SendTo.ClientsAndHost)] PlayerKilled3DRpc`. `RegisterPlayer3D`에서 `EnsurePlayerSessionBuckets3D` init. `OnNetworkDespawn`에서 K/D/A 4개 dict + recentDamage3D clear (UnregisterPlayer3D는 K/D/A 유지 — legacy "keep stats for session" 미러). **Atomic flip**: `PlayerNetworkController3D.Die()`이 `CombatManager.Instance.OnPlayerDeath` → `CombatManager3D.Instance.OnPlayerDeath3D`로 **완전 교체**. **TakeDamage `void → bool`**: 호출자(CM3D resolver) 1곳만 영향, "actually damaged" 시맨틱으로 hitTargets refinement (Codex S-B1-4-R3-1). 데미지 루프에 `if (t.IsAlive) TrackDamageForAssist3D(...)` 가드 (Codex CI-B1-5-R1-1: fatal hit 후 OnPlayerDeath3D가 이미 처리하므로 stale 엔트리 corruption 방지). Codex 2 라운드 통과 ([history/2026-05-11-B1-5-kda-die-flip.md](../../../../codex-review/history/2026-05-11-B1-5-kda-die-flip.md)).

### B1 PHASE COMPLETE ✅ (2026-05-11)

5 sub-cycles 완료. 약 600줄 net 추가, 3 파일 수정/신규. 종합 회고: [B1_PHASE_SUMMARY.md](../../../../codex-review/B1_PHASE_SUMMARY.md).

**B1 closure 요약 contract**:
- 3D 전투의 모든 권위 결정은 `CombatManager3D` 소유 (registry / hit detection / cooldown / K/D/A / result RPCs).
- ~~`CombatManager.cs` 무수정~~ → **D1에서 삭제 완료** (2026-05-16).
- **Death contract**: K/D/A counter는 **HP-zero combat death에만** 변동. Kill-zone 떨어짐도 `Die()` 경유 scored death (BF-1a).

### Phase B Followups (B2/B3 시작 전 처리 권장)

- ~~**Team assignment for friendly fire**~~ ✅ **DONE 2026-05-11** (Phase X0-4). `PlayerSpawnManager`에 `defaultPlayerTeam = TeamId.Team1` 필드 추가 + `SpawnAsPlayerObject` 직후 `controller.SetTeam(defaultPlayerTeam)` 호출. 친선전 차단됨. **B1-4/B1-5 smoke test 시 친선전 비활성 — 데미지 테스트 필요하면 Inspector에서 `defaultPlayerTeam = TeamId.None`으로 임시 변경하거나 한 플레이어를 Team2로 수동 재할당.** 보스 team(Team2) 할당은 Phase X4에서.
- ✅ **Kill-zone scored death** (BF-1a) — `Die(OwnerClientId)` 호출로 변경. deaths++ (self-kill, kills 크레딧 없음). StatManager.SetHP(0f) 동기화 추가.
- ✅ **`appliedStatus` warning 1회 제한** (BF-1b) — `HashSet<AttackType>` 게이트로 AttackType당 1회만 경고.
- ✅ **AttackData3D SO 생성** (BF-2) — Light(dmg=10, range=2, cd=0.3s) + Heavy(dmg=25, range=2.5, cd=0.8s). CombatManager3D.attackTable 와이어 + playerLayer=Default.
- **DEFERRED: Source-tagged status duration model** — 설계 결정 필요 (지속시간/스택/클렌즈 규칙). B2/B3에서 처리.

원칙:
- `CombatManager3D.cs` 단일 전투 매니저 (legacy `CombatManager.cs` D1에서 삭제 완료)
- Attack/Parry는 queue (movement만 latest-intent)
- Hurtbox는 player root collider 1개 + LayerMask 필터 (per-body-part 안 씀)

### B2. ISkillAction Composite Tree 스킬 시스템 — **DONE (via X2)** (2026-05-12)
- ~~39개 파트의 composite 트리~~ → Phase X2에서 Buildup 37 SkillComponents + SkillLibrary (33 methods) + SkillBinder 통합 완료
- SkillComponents 37 factory + SkillLibrary 29 SkillStep + 4 SkillCondition = 전체 실행 그래프 포팅됨
- 상세: [BUILDUP_INTEGRATION_PLAN.md](BUILDUP_INTEGRATION_PLAN.md)

### B3. 보스 #1 상태머신 + 기본 패턴 세트 — **DONE (via X4)** (2026-05-15)
- ~~BossPhase, BossCurrentPatternId, BossHP, BossAlive NetworkVariable~~ → X4-3 (HP/alive NV + StatManager authority) + X4-6 (BossPhase NV + phase tracking) 완료
- ~~기본 패턴 4~6개~~ → X4-7 (9 boss skills pool, phase-driven CooldownScale) + X4-8 (ML-Agents inference agent) 완료
- BNC3D: phase tracking (Phase1→Phase2→Phase3→Enrage→Defeated) + auto-cast + adaptive weights (C3)

### B4. 보스 텔레그래프 + 페이즈 전환 — **DONE** (2026-05-16)
- 페이즈 전환: X4-6에서 NV + callback 완료. 비주얼 연출만 필요
- VFX 자산 활용: LiteFireEffect (X1-1) / Hovl Studio (X1-4) / ShaderGraph_Dissolve (X1-2)

#### Sub-cycles
- ✅ **B4-1 Telegraph foundation** (2026-05-16) — SkillDefinition.TelegraphDuration 필드 + SkillManager telegraph 상태머신 (EnterTelegraph → timer → CompleteTelegraph/CancelTelegraph) + BNC3D TelegraphStartedRpc. ExecuteOrTelegraph 통합 헬퍼. CancelTelegraph: death/ClearAll/SetSlot 호출. Codex 1 라운드 PASS. ([history/2026-05-16-B4-1-telegraph-system.md](../../../../codex-review/history/2026-05-16-B4-1-telegraph-system.md))
- ✅ **B4-2 BossTelegraphDisplay** (2026-05-16) — NEW `BossTelegraphDisplay.cs` MonoBehaviour (Boss prefab root). `Show(pos, dir, dur, targetType)` → VFX Instantiate + `Destroy(obj, dur)`. TargetType switch: Area→Freeze circle, Direction→Charge slash blue, Single→Sparks flashing blue, Self→skip. Boss.prefab에 컴포넌트 + 3 VFX prefab 와이어. BNC3D `TelegraphStartedRpc` → `_telegraphDisplay.Show()`. Codex 1 라운드 PASS. ([history/2026-05-16-B4-2-telegraph-display.md](../../../../codex-review/history/2026-05-16-B4-2-telegraph-display.md))
- ✅ **B4-3 Boss skill SO TelegraphDuration** (2026-05-16) — 11 boss skill .asset 수정. Direction: 0.6~1.0s (ExecutionSpike 0.8, CrushingBarrage 0.6, FortressArmor 0.7, SealChain 0.8, BarrierBreaker 1.0, RuptureMagazine 0.8). Area: 1.0~1.2s (ErosionField 1.2, CollapseRoar 1.0, MarkWave 1.0). Self: 0 (SurvivalPulse, OverchargeMode). SO data 수정 — Codex 면제.
- ✅ **B4-4 Phase transition VFX** (2026-05-16) — `BossTelegraphDisplay.ShowPhaseTransition()` + BNC3D `networkCurrentPhase.OnValueChanged` IsClient 구독. Ground AOE explosion (Hovl Studio). None→Phase1 초기 스폰 + Defeated 스킵. Codex 1 라운드 PASS. ([history/2026-05-16-B4-4-phase-transition-vfx.md](../../../../codex-review/history/2026-05-16-B4-4-phase-transition-vfx.md))
- ✅ **B5-1 Mouse look rotation fix** (2026-05-17) — `ProcessServerMovement`: always use serverLookYaw. Owner immediate rotation. NormalizeYaw helper. RpcParams sender check. ([history/2026-05-17-B5-1-mouse-look-rotation.md](../../../../codex-review/history/2026-05-17-B5-1-mouse-look-rotation.md))
- ✅ **B6-1 Match End UI + In-Place Restart** (2026-05-17) — `MatchEndReason` NV (BossDefeated/AllPlayersDead). `EndMatch(reason)` 통합 진입점. `CombatManager3D.AreAllPlayersDead()` (Count<2 guard). `MatchEndUI.cs` dual NV subscription. `RequestRestartRpc` → `RestartMatch()` (DespawnBoss + player reset + StartMatchCountdown). MatchEnd 중 movement/respawn 차단. Codex 3 라운드. ([history/2026-05-17-B6-1-match-end-restart.md](../../../../codex-review/history/2026-05-17-B6-1-match-end-restart.md))

---

## Phase X — Buildup 프로젝트 통합 (Path B Wrapper, 단계별)

레퍼런스: `C:\Users\paek6\Downloads\Buildup\Buildup` (테네브리스 프로젝트, 같은 게임의 다른 브랜치).
세부 계획: [BUILDUP_INTEGRATION_PLAN.md](BUILDUP_INTEGRATION_PLAN.md).

목표: Buildup의 Chapter1 씬 + 발전된 시스템(StatManager, 37 SkillComponents, BossController 등)을 우리 NGO host-authoritative 패턴(PNC3D, CombatManager3D)에 통합. 단계별 Codex 검증 거치며 안전하게 가져옴.

### X0. 환경 준비 (NO Buildup data import) — ✅ **DONE** (2026-05-11)
- ✅ 통합 destination 폴더 구조 + namespace 규약 확정 ([BUILDUP_INTEGRATION_PLAN.md](BUILDUP_INTEGRATION_PLAN.md))
- ✅ **X0-4 Team assignment** (Phase B Followup #1 닫음). PlayerSpawnManager에서 `defaultPlayerTeam = TeamId.Team1` 자동 할당. ([history/2026-05-11-X0-4-team-assignment.md](../../../../codex-review/history/2026-05-11-X0-4-team-assignment.md))
- ✅ **X0-5 ICombatant interface stub** — minimum surface (Transform/GameObject/MaxHP/CurrentHP/IsAlive/TakeDamage/RecoverHP). 새 namespace `ArenaCombat.Core.Combat` 시작점. ([history/2026-05-11-X0-5-icombatant-stub.md](../../../../codex-review/history/2026-05-11-X0-5-icombatant-stub.md))

**Phase X0 closed. X1+ deferred — 사용자 결정 시점 시작.**

### X1. 비주얼/데이터 자산 import — **DONE** (2026-05-11~15)
- Chapter1.unity (씬 자체) + NavMesh + 프리팹(시각만) + Materials + Textures + Animations + ScriptableObjects(Stats SO 등)
- 코드 의존 0건 (.cs 파일 import 안 함)
- Unity import 후 컴파일 에러 0 — 시각 자산만

#### Sub-cycles
- ✅ **X1-1 LiteFireEffect** (2026-05-11) — 8.2MB VFX 팩, 45 non-meta + 53 meta 파일, 0 .cs/.asmdef. PowerShell `Copy-Item -Recurse` + `.meta` GUID 보존. ([history/2026-05-11-X1-1-litefireeffect-import.md](../../../../codex-review/history/2026-05-11-X1-1-litefireeffect-import.md))
- ✅ **X1-2 ShaderGraph_Dissolve + Dark Ghosts FREE** (2026-05-11) — 24.1MB 합산. SGD 35/43, DG 36/59 파일. C# 6개 모두 namespaced (`DissolveExample`, `namespace_animclip_offset`) — conflict 0. **`DissolveOffest.cs` Old Input API → New Input System 패치** (Codex CI-X1-2-R1-1). ([history/2026-05-11-X1-2-shadergraph-darkghosts.md](../../../../codex-review/history/2026-05-11-X1-2-shadergraph-darkghosts.md))
- ✅ **X1-3 QuarterView 3D Action BE5** (2026-05-12) — 4.1MB 플레이어 메시 팩, 130/141 파일. C# 2개 (`ReadmeBE5`/`ReadmeEditorBE5`, default namespace지만 BE5 suffix unique). **`ReadmeEditorBE5.cs` `LoadLayout()` 자동 호출 비활성** (Codex CI-X1-3-R1-1: Unity 6 reflection 호환 문제 방지). 마커는 유지하여 import-time 호출 1회만. ([history/2026-05-12-X1-3-quarterview.md](../../../../codex-review/history/2026-05-12-X1-3-quarterview.md))
- ✅ **X1-4 Hovl Studio Magic effects pack** (2026-05-12) — 71MB VFX, 164/181 파일. **C# 0, asmdef 0, custom shader 0** (zero-script 패턴, X1-1과 동일 위험 프로파일). URP 호환 핑크 particle은 visual-only로 defer (실제 사용 시 swap). ([history/2026-05-12-X1-4-hovl-studio.md](../../../../codex-review/history/2026-05-12-X1-4-hovl-studio.md))
- ✅ **X1-5 Symphonie / Ruins environment pack** (2026-05-12) — **분류 정정: 오디오 아니라 보스 아레나 dressing용 Ruins 3D 환경 팩.** 108MB (4K PBR 텍스처 4장: Normal 52.6MB + Diffuse 33MB + Metallic 18.25MB + AO 3.4MB). 14/29 파일. **C# 0, asmdef 0, custom shader 0, .shadergraph 1** (`Ruins_URP.shadergraph` URP 전용). Multi-RP variant (URP/HDRP/Build_IN) — **사용 시 `Assets/Symphonie/Ruins/URP/Prefabs/`만 사용**, 다른 variant는 URP에서 핑크 (안 쓰면 무해). 4K texture downsize / Streaming Mipmaps는 Chapter1 배치 시점 결정. ([history/2026-05-12-X1-5-symphonie-ruins.md](../../../../codex-review/history/2026-05-12-X1-5-symphonie-ruins.md))
- **X1-6 DONE** (2026-05-13~16): Buildup `.asset` 인스턴스 import. X3 smoke의 사전 조건. 분할:
  - ✅ **X1-6a Stat .asset import** (2026-05-13) — Buildup `Player&Boss/PlayerStatsSO.asset` + `BossStatsSO.asset` + `BaseStatsSO.asset` (3개) 그대로 복사하여 `Assets/ArenaCombat/Resources/Stats/` 배치. Buildup .meta GUID 보존 (script GUID 양 프로젝트 동일 — `fad02e47…` / `ccbce52a…` / `9155adbe…`, X2 import 시 보존된 결과). enum 충돌 없음 (primitive 필드만). NEW empty `SkillRegistry.asset` (`_pool: []`, ArenaCombat script GUID `f315d276…`, fresh asset GUID `de01f1de…`) at `Resources/Skills/`. **Codex C-1**: 본 라운드는 "registry null 제거 + PlayerStatsSO 할당" preflight unblock만 — full skill behavior (AbilityCard.skillDefinition resolve, auto-cast, projectile spawn)은 X1-6b/c 후. **Codex C-2**: SkillRegistry.asset 수동 YAML 작성 시 m_Script GUID 정확성 + `_pool: []` 검증. Codex S-1~S-5 모두 반영. ([history/2026-05-13-X1-6a-stat-asset-import.md](../../../../codex-review/history/2026-05-13-X1-6a-stat-asset-import.md))
  - **X1-6b DONE** — 12 PlayerSkill .asset import 작업. 4단계 분할:
    - ✅ **X1-6b-1 SkillRoleTag enum append** (2026-05-13) — `SkillRoleTag.cs` EDIT (실제 경로 `/Skill/Core/`, **Codex C-1** path 정정). Buildup 12 PlayerSkill audit (RoleTags+CounterTags grep) 결과 unique tag 26개 식별 → 기존 9 사용 + 누락 20개 append-only 추가 (인덱스 9..28). 카테고리: Damage type 3 / Range 2 / Stat mod 5 / Defense 1 / Heal+Regen 3 / CC 3 / Misc 3. 헤더에 "Reserved index ranges" 명시 (Codex S-4). Cleanse/Heal, AOE/Zone, Survival/Heal, SelfBuff/Buff, DamageUp 별개 결정 (Codex S-3 — counter-pick 정보 손실 회피). [SKILL_SYSTEM_DESIGN.md §10b](SKILL_SYSTEM_DESIGN.md) 동시 갱신 (Codex S-1). X1-6b-2 .asset 변환은 string name 기반 매핑 사용 권장 (Codex S-2). ([history/2026-05-13-X1-6b-1-skillroletag-append.md](../../../../codex-review/history/2026-05-13-X1-6b-1-skillroletag-append.md))
    - ✅ **X1-6b-2 12 PlayerSkill .asset import** (2026-05-14) — 12 NEW `.asset` + 12 NEW `.meta` + 1 폴더 `.meta` (`Resources/Skills/PlayerSkills/`). RoleTags / CounterTags Buildup string → ArenaCombat enum ordinal 변환 (X1-6b-1 enum 0..28 매핑). 모든 .meta GUID Buildup에서 보존 (12 SkillDefinition GUID, X1-6a 패턴 미러). DisplayName / Description 한국어 그대로 유지 (Unity YAML double-quote escape decode). **Codex APPROVED via MCP** — **첫 자동 워크플로우 라운드** (X1-6b-1 reload 후 Codex MCP 0.130.0 활성화). Codex 확인사항: enum array YAML 형식 = `- 19` int 한 줄당 (SkillRoleTag.cs:8-9 ordinal 직렬화 명시), TargetType ordering = `Single=0/Area=1/Self=2/Direction=3` (SkillTypes.cs:20-25). 12 변환표 ordinal 모두 정확. ([history/2026-05-14-X1-6b-2-skilldefinition-import.md](../../../../codex-review/history/2026-05-14-X1-6b-2-skilldefinition-import.md))
    - ✅ **X1-6b-3 SkillRegistry._pool 12 GUID 등록** (2026-05-14) — `SkillRegistry.asset` EDIT (`_pool: []` → 12 entry list, 14 lines added). 모든 12 GUID가 X1-6b-2 .meta 정확히 매치 (Codex 모든 GUID line-by-line 검증). **Codex APPROVED via MCP** — 핵심 변경사항: (1) `# SkillName` 인라인 코멘트 제거 (Unity reserialize 시 strip 위험, Codex C-2), (2) `_pool` 들여쓰기 MonoBehaviour 필드 위치 유지, (3) trailing newline EOF. SkillBinder.BindAll 런타임 안전성 확인됨 (12 player skill 모두 SkillLibrary 매핑 성공, null skill은 IsReady=false 가드). ([history/2026-05-14-X1-6b-3-skillregistry-pool.md](../../../../codex-review/history/2026-05-14-X1-6b-3-skillregistry-pool.md))
    - ✅ **X1-6b-4a 4 AbilityCard NEW class GUID 변환 + skillDefinition 매핑** (2026-05-14) — 4 `Resources/AbilityCard/*.asset` EDIT. 발견: 3 종류 AbilityCard 자산 — Resources/ (Missing Script, GUID `0d939ffd…`) / 3DSceneScript/ (legacy class `c923b417…`) / NEW X2-12 (`0606cc02…` `ArenaCombat.Core.Card.AbilityCard`). 4 Resources/ 파일 NEW class GUID로 변환 + `skillDefinition` 필드 매핑. 매핑 (4 distinct role): AbilityCard→ExecutionSpike (Burst), AbilityCard 1→FortressArmor (Shield), AbilityCard 2→ErosionField (DOT), AbilityCard 3→SurvivalPulse (Heal). **Codex APPROVED via MCP** (no critical) — m_EditorClassIdentifier format / 필드 순서 / Missing Script reimport 안전성 / GUID 매치 / 매핑 합리성 모두 검증. ([history/2026-05-14-X1-6b-4a-abilitycard-rebind.md](../../../../codex-review/history/2026-05-14-X1-6b-4a-abilitycard-rebind.md))
    - ✅ **X1-6b-4b 3DScene CardManager.allCards 재바인딩** (2026-05-14) — `Assets/Scenes/3DScene.unity:573-579` EDIT. 6 LEGACY entries (3 GUID × 2 중복: `8c06a47a` / `09242b3d` / `b6b942f9` — 모두 3DSceneScript/AbilityCard/) → 4 NEW Resources/ entries (`11fee131` / `a5e093a9` / `b10fb927` / `4ff7c729`). allCards.Length 6→4. **Codex APPROVED via MCP** (no critical) — CardManager.allCards 동적 길이 처리 확인 (CardManager.cs:59 RegisterCardCatalogSize, 116/138 bounds check, 184 IndexOf), GSM.RegisterCardCatalogSize N=4 처리 확인 (GameStateManager.cs:588 Mathf.Max + 1079-1102 BuildOfferFromCatalog), 4 Resources GUID 모두 .meta 일치. 인라인 코멘트 제거 (X1-6b-3 패턴). ([history/2026-05-14-X1-6b-4b-cardmanager-rebind.md](../../../../codex-review/history/2026-05-14-X1-6b-4b-cardmanager-rebind.md))
    - ✅ **X1-6b-4c DONE** (2026-05-16) — 3DSceneScript/AbilityCard/ legacy .asset 삭제 (GUID 중복 해소) + 8 NEW AbilityCard 작성 (Resources/AbilityCard/ AbilityCard 4~11). 12 SkillDefinition 전체 풀 활용. Chapter1 CardManager.allCards 4→12 확장. SkillCard1.png 스프라이트 시트 12 서브스프라이트 각 카드에 할당. 카드 이름 한국어 (처형 쐐기/요새 갑주/침식 필드/생존 파동/분쇄 탄막/사냥 표식/봉인 사슬/붕괴 포효/방벽 파괴/과충전 모드/관통 사격/파열 탄창).
  - **X1-6c DONE** — Pool/Prefab/NetworkPrefab 일체. 4단계 분할:
    - ✅ **X1-6c-1 SkillProjectile + SkillArea 프리팹 생성** (2026-05-14) — 2 NEW prefab + 1 폴더 .meta. 위치 `Assets/ArenaCombat/Resources/Skills/Prefabs/`. **Codex APPROVED WITH CHANGES** — MCP create_prefab은 RequireComponent 구체 타입 (SphereCollider) 보장 못 함 + Rigidbody isKinematic=false 필요 (SkillProjectile.Launch가 `_rb.linearVelocity` 사용) → YAML-direct 방식 채택. SkillProjectile: Transform + Rigidbody (useGravity=0, isKinematic=0, interpolate=1, FreezeRotation X+Z) + SphereCollider (isTrigger=1, radius=0.5) + NetworkObject + SkillProjectile (color=red, _detectionRadius=0.5, _targetMask=all). SkillArea: Transform + NetworkObject + SkillArea (_areaColor semi-trans red). NetworkObject script GUID `d5a57f767e5e46a458fc5d3c628d0cbb` (Player A.prefab 검증). Visual mesh 미포함 (SkillArea.cs:32 _renderer null 안전 처리됨, X1-6c-N polish 가능). ([history/2026-05-14-X1-6c-1-prefab-create.md](../../../../codex-review/history/2026-05-14-X1-6c-1-prefab-create.md))
    - ✅ **X1-6c-2 3DScene Pool/Manager 3 GameObject 추가** (2026-05-14) — `Assets/Scenes/3DScene.unity` EDIT (line 4754 직전 + SceneRoots m_Roots). 3 NEW GameObject + 3 Transform + 3 MonoBehaviour: ProjectilePool (`_prefab → SkillProjectile component fileID 7000006`, `_initialSize=10`) / PersistentAreaPool (`_prefab → SkillArea component fileID 8000004`, `_initialSize=5`) / PersistentAreaManager (`_pool → PersistentAreaPool MonoBehaviour scene-local fileID 9100003`). FileID range 9000001..9200003. **Codex APPROVED WITH CHANGES** — `_prefab` / `_pool` component reference (GameObject ref 아님), 외부 prefab은 type 3 + GUID, scene-local은 fileID만. SceneRoots m_Roots에 3 Transform fileID 추가. 검증: ProjectilePool MCP get_gameobject `_prefab: "SkillProjectile"` 표시, _initialSize=10. 콘솔 0 에러/경고. ([history/2026-05-14-X1-6c-2-pool-scene.md](../../../../codex-review/history/2026-05-14-X1-6c-2-pool-scene.md))
    - ✅ **X1-6c-3 NetworkPrefabs 등록** (2026-05-14) — `Assets/DefaultNetworkPrefabs.asset` (GUID `2497bd5b…`, NGO 2.x NetworkPrefabsList SO, SampleScene NetworkManager.NetworkConfig.Prefabs.NetworkPrefabsLists 참조). **자동 등록됨** — Unity Editor가 NetworkBehaviour prefab 추가 인식 시 자동 추가. SkillProjectile (line 27-31, GameObject fileID 7000001 + GUID 5ca17d1e…) + SkillArea (line 32-36, 8000001 + 1b8d63f8…) 두 entry 모두 List에 존재. NGO Spawn() 활성화. **smoke verification 5 (Pool Spawn → Despawn(false) → re-Spawn cycle) 차단 해소**. ([history/2026-05-14-X1-6c-3-networkprefabs.md](../../../../codex-review/history/2026-05-14-X1-6c-3-networkprefabs.md))
  - **X1-6c COMPLETE ✅** — 4단계 모두 완료. X3 smoke test preflight 1~6 모두 unblock.
    - ✅ **X1-6c-N Visual mesh polish** (2026-05-14) — 2 prefab EDIT. SkillProjectile에 MeshFilter (Sphere mesh built-in 10207) + MeshRenderer (Default-Material 10303) 추가 (component fileID 7000007 / 7000008). SkillArea에 MeshFilter (Cylinder mesh 10206) + MeshRenderer (Default-Material) 추가 (8000005 / 8000006). **Codex APPROVED WITH CHANGES via MCP** — Cylinder 유지 권장 (SkillArea.cs:76 y=0.02 scale로 thin puck 효과), Unity 6000.3 full MeshRenderer 블록 사용 (m_StaticShadowCaster / m_RayTraceProcedural / m_AdditionalVertexStreams 등 normalization churn 회피). Default-Material 사용 (불투명 → SkillArea 빨강 표시되지만 _areaColor.a=0.35 transparency는 적용 안 됨). 콘솔 0 신규 에러/경고. **smoke test 디버깅 가시성 향상** — projectile/area spawn cycle 눈으로 확인 가능. ([history/2026-05-14-X1-6c-N-visual-mesh.md](../../../../codex-review/history/2026-05-14-X1-6c-N-visual-mesh.md))
- ✅ **X1-7 DONE** (2026-05-15) — Chapter1.unity (282KB, 9255 lines) Buildup에서 복사. 40 unique script GUID 중 28 FOUND / 12 MISS (training-only). Missing script 경고만 발생, 컴파일 에러 없음.

#### 알려진 이슈 (X1-1)
- 없음 (이번 사이클 컴파일/runtime 에러 0). URP 셰이더 호환은 실제 사용 시 확인.

### X2. 스킬/스탯/상태 시스템 import — **DONE** (2026-05-12)
- StatManager, StateManager, SkillManager, SkillRegistry, SkillExecutor, SkillContext
- SkillComponents (37 파트), ICombatant 확장, AbilityCard, CardManager
- ProjectilePool, PersistentAreaManager, IProjectile, IPersistentArea, IPoolable
- 우리 namespace로 옮기되 NGO 통합은 X3에서. 이 단계에선 컴파일 통과 + 단독 동작만.

**Pivot 이유**: X1-6 (game data SOs) / X1-7 (Chapter1 scene)이 Buildup .cs를 GUID로 참조. 스크립트 없이 import 시 "missing script" warning 다수 발생. 사용자가 "안전한" 선택 → X2 먼저 진행해서 SO/씬 import 시 clean state 보장.

#### Sub-cycles (제안)
- ✅ **X2-1 Stats SO classes** (2026-05-12) — BaseStatsSO (16 float), PlayerStatsSO (24 추가 float, BaseStatsSO 상속), BossStatsSO (6 float + array, BaseStatsSO 상속) 3개 파일 import + `namespace ArenaCombat.Core.Combat` wrap. `.meta` GUID 보존 (X1-6 .asset 참조용). 의존성 0, 메서드 0, 컴파일 위험 0. ([history/2026-05-12-X2-1-stats-sos.md](../../../../codex-review/history/2026-05-12-X2-1-stats-sos.md))
- ✅ **X2-2 SkillTypes enums + ICombatant full surface** (2026-05-12) — NEW `Core/Skill/Core/SkillTypes.cs` (9 enums: AreaShape/TargetType/MoveType/ParryRewardType/StatusType×12/BuffType/DebuffType/CleanseType/DispelType; 2 delegates commented out pending SkillContext in X2-5) wrapped in `namespace ArenaCombat.Core.Skill`, Buildup .meta GUID preserved. REPLACE `Core/Combat/ICombatant.cs` X0-5 7-member stub → full 23-member surface (`CurrentHPPercent`/`Shield`/`IsCasting`/`IsParrying`/`ParryWindow` props + `TakeShieldBreakDamage`/`AddShield`/`ApplyStatus`/`HasStatus`/`ApplyBuff`/`ApplyDebuff`/`RemoveStatuses`/`RemoveBuffs`/`Knockback`/`Pull`/`MoveBy`/`NotifyParryReward` methods), existing .meta preserved, `using ArenaCombat.Core.Skill;` added. **Codex Round 1 APPROVED WITH CHANGES** — Korean-comment mojibake corrupts enum values + ICombatant members in verbatim copy; switched to clean ASCII rewrite (English comments) for both files. Zero implementers; PNC3D adapter lands in X3. ([history/2026-05-12-X2-2-skilltypes-icombatant.md](../../../../codex-review/history/2026-05-12-X2-2-skilltypes-icombatant.md))
- ✅ **X2-3 CombatantState enum (scope-down)** (2026-05-12) — NEW `Core/State/CombatantState.cs` 7-value enum (Idle/Moving/Casting/Parrying/HitStun/Stunned/Dead, Buildup numbering 0..6 preserved) wrapped in `namespace ArenaCombat.Core.State`, Buildup .meta GUID `04b911cd…` preserved. Folder `Core/State/` created. **Scope down**: original "StateManager + CombatantState" split because Buildup `StateManager.cs` has `[RequireComponent(typeof(StatManager))]` + 4 direct StatManager method calls — compile-time hard dep. StateManager defer to X2-4 paired with StatManager. State resolution priority documented as comment (strongest→weakest, separate from numeric value). **Codex Round 1 APPROVED WITH CHANGES** — priority comment wording fix only; applied. ([history/2026-05-12-X2-3-combatant-state-enum.md](../../../../codex-review/history/2026-05-12-X2-3-combatant-state-enum.md))
- ✅ **X2-4 StatManager + StateManager paired** (2026-05-12) — NEW `Core/Stats/StatManager.cs` (~700 LOC, English rewrite, namespace `ArenaCombat.Core.Stats`, Buildup GUID `cc3c21c8…` preserved) with `CombatantKind` top-level enum (Player/Boss) inside namespace (NOT nested in StatManager class per Codex S-1). NEW `Core/State/StateManager.cs` (178 LOC, namespace `ArenaCombat.Core.State`, Buildup GUID `17b9658f…` preserved). Both `MonoBehaviour` per-entity components (Buildup pattern). Server authority NOT enforced inside managers — header contract states "callers must gate mutations" (Codex S-4 applied). Public API + field names byte-identical to Buildup; only comments + Debug.Log strings translated to English (X2-2 lesson). Coroutine pattern preserved (StartCoroutine + Coroutine handle dicts for status/buff/debuff/parry). NEW folder `Core/Stats/` (fresh GUID `f3ab8a0d…`). **Codex Round 1 APPROVED** with 5 non-blocking suggestions all adopted. ([history/2026-05-12-X2-4-statmanager-statemanager.md](../../../../codex-review/history/2026-05-12-X2-4-statmanager-statemanager.md))
- ✅ **X2-5 Skill foundation contract** (2026-05-12) — NEW `Core/Skill/Core/SkillContext.cs` (per-cast runtime data, ICombatant 의존, namespace `ArenaCombat.Core.Skill`, Buildup GUID `d90ef223…` 보존), NEW `SkillDefinition.cs` (SO + composite tree slot, menu `ArenaCombat/SkillDefinition`, Buildup GUID `a193f29c…` 보존), NEW `SkillRegistry.cs` (단일 마스터 풀 + 카운터 매칭, menu `ArenaCombat/SkillRegistry`, Buildup GUID `f315d276…` 보존), NEW `SkillRoleTag.cs` (9-value enum: Burst/DOT/Shield/Parry/Zone/Counter/Heal/Mobility/Mark, fresh GUID). EDIT `SkillTypes.cs` X2-2 commented delegate → namespace 내부로 이동 + 활성화. DELETE 구 prototype `Perk/Effects/SkillActionTest` (.cs 확장자 없어 미컴파일). **Plan-Mode 사용자 승인 통과** (Codex 게이트 동등 처리). `RoleTags`/`CounterTags` Buildup `string[]` → `SkillRoleTag[]` enum 전환 (compile-safe, designer typo 방지). 모든 .cs 깨끗한 영문 재작성. ([history/2026-05-12-X2-5-skill-foundation.md](../../../../codex-review/history/2026-05-12-X2-5-skill-foundation.md))
- ✅ **X2-6 SkillExecutor** (2026-05-12) — NEW `Core/Skill/Core/SkillExecutor.cs` (~155 LOC, MonoBehaviour, namespace `ArenaCombat.Core.Skill`, Buildup GUID `f847c085…` 보존). Public API + private dict 필드명 byte-identical to Buildup (ML observation surface 보존 — `GetRemainingCooldown`/`GetHitRate`/`GetUseCount`/`GetLastNSkillIds`/`TotalHitCount`/`TotalUseCount`/`_attemptCounts`/`_hitCounts`/`_skillHistory`). 헤더에 SERVER AUTHORITY CONTRACT + ML OBSERVATION SURFACE guard 주석. Korean Debug.Log → 영문 (`대상:` → `target:`, `없음` → `none`, `실행 로그:` → `trace:`). **워크플로우 위반**: pending.md 작성 후 Codex 응답 대기 없이 적용 (사용자 지적). **Codex retroactive APPROVED** (사용자 송부 후) — public surface 19/19 byte-identical 확인, GUID 일치 확인, ML observation 보존 확인. Non-blocking suggestion: Unicode box-drawing 구분선 (`══`/`─`/`—`/`§`)이 "English translation only" 엄격 해석에 안 맞음 — cleanup batch로 미룸. [SKILL_SYSTEM_DESIGN.md §10a](SKILL_SYSTEM_DESIGN.md) ML transfer 정책 확정 — 구조 보존 / 수치는 학습 단계에서 조정. **X2-7부터 정상 gate 흐름 복귀**. ([history/2026-05-12-X2-6-skill-executor.md](../../../../codex-review/history/2026-05-12-X2-6-skill-executor.md))
- ✅ **X2-7 Projectile subsystem paired** (2026-05-12) — NEW `Core/Skill/Interfaces/IProjectile.cs` + `IPoolable.cs` + `Core/Skill/Projectile/SkillProjectile.cs` + `ProjectilePool.cs`. 4 파일 paired (~207 LOC). 모든 Buildup .meta GUID 보존 (`89b4713f…`/`89c42833…`/`d5c6fbf5…`/`f3ee6d1f…`). 2 NEW 폴더 (Interfaces fresh GUID `b90152db…`, Projectile fresh `7463dc0c…`). `MonoBehaviour` 유지 (NetworkBehaviour 전환은 X3 wiring에서 — Buildup 작성자가 이미 `ShouldRunHitDetection()` forward-compat 게이트 박아둠). **Codex Round 1 APPROVED WITH CHANGES** — critical: `ProjectilePool.Get()` Buildup 원본 double-enqueue 버그 발견 (empty path에서 CreateInstance가 enqueue + Get이 같은 active projectile 반환 → 다음 Get에서 재dequeue). Codex 권장 패치 적용 (Get이 항상 dequeue, CreateInstance가 항상 enqueue, 대칭). DIVERGENCE FROM BUILDUP — ProjectilePool.cs 헤더에 명시. 5개 suggestion 모두 반영. ML preservation 정책 준수 (public surface byte-identical). ([history/2026-05-12-X2-7-projectile-paired.md](../../../../codex-review/history/2026-05-12-X2-7-projectile-paired.md))
- ✅ **X2-8 Area subsystem paired** (2026-05-12) — NEW `Core/Skill/Interfaces/IPersistentArea.cs` (8 LOC) + `Core/Skill/Area/SkillArea.cs` (175 LOC) + `PersistentAreaPool.cs` (75 LOC) + `PersistentAreaManager.cs` (45 LOC). 4 파일 paired. 모든 Buildup .meta GUID 보존 (`4119d693…`/`a01c7231…`/`6b9e09b4…`/`eed28664…`). 1 NEW 폴더 `Core/Skill/Area/` (fresh GUID `60d77af4…`). **PersistentAreaPool.Get() 동일한 double-enqueue 버그 선제 패치 적용** (X2-7 Codex 패턴). PersistentAreaManager warning string `미연결` → `not assigned` (Codex S-2 mojibake 회피). 10개 SkillArea 필드 (`_areaColor`/`_renderer`/`_pool`/`_routine`/`_radius`/`_angleDeg`/`_shape`/`_tickEffect`/`_ctx`/`_forward`) 모두 실제 코드로 존재 확인 (Codex S-3 verbatim 손상 점검). `MonoBehaviour` 유지. SkillArea의 server gate hook은 X3에서 추가 (Codex S-4). `Physics.OverlapSphere` 그대로 (NonAlloc 전환은 X3+ perf pass — Codex S-5). PersistentAreaPool singleton 없음 (Inspector-managed — Codex S-6). **Codex Round 1 APPROVED, critical 0**. ([history/2026-05-12-X2-8-area-paired.md](../../../../codex-review/history/2026-05-12-X2-8-area-paired.md))
- ✅ **X2-9 SkillComponents + SkillRangeDisplay paired** (2026-05-12) — **최대 라운드 ~837 LOC**. NEW `Core/Skill/Core/SkillComponents.cs` (~550 LOC English rewrite, namespace `ArenaCombat.Core.Skill`, Buildup GUID `03ccc585…` 보존). 37개 factory (36 SkillStep + 1 SkillCondition) 검증 완료 (grep). 카테고리 분류: Combat (#1-3,#35) / Survival (#4-8) / Status (#9-13,#25,#27) / Buff-Debuff (#14-17,#21-22) / Position (#18-19,#36) / Defense-Execute-Cleanse (#26,#28-30) / Parry (#23-24) / Area (#20,#31) / Projectile (#32) / Control flow (#33-34) / Condition (#37). NEW `Core/Skill/Core/SkillRangeDisplay.cs` (~290 LOC clean ASCII rewrite per Codex S-3, Buildup GUID `eceb2777…` 보존). SkillComponents가 `SkillRangeDisplay.Instance?` 7곳 참조 → 페어 import 강제 (shim 대안은 X2-3 stub-trap). `using ArenaCombat.Core.Combat;` 추가 (Codex S-2 — ICombatant). SkillRangeDisplay.SpawnAt() pool 패턴은 X2-7/8과 다름 (CreateInstance가 enqueue 안 함, 빈 path는 new instance 직접 반환 — 버그 없음, Codex S-5). `Physics.OverlapSphere` GC + `HashSet<ICombatant>` 할당 그대로 (X3+ perf pass — Codex S-4). **Codex Round 1 APPROVED, critical 0**. ([history/2026-05-12-X2-9-skillcomponents-paired.md](../../../../codex-review/history/2026-05-12-X2-9-skillcomponents-paired.md))
- ✅ **X2-10 SkillLibrary + SkillBinder paired** (2026-05-12) — NEW `Core/Skill/Core/SkillLibrary.cs` (~400 LOC clean rewrite, namespace `ArenaCombat.Core.Skill`, `using static ArenaCombat.Core.Skill.SkillComponents;`, Buildup GUID `11b22318…` 보존). **33 public methods 검증** (29 SkillStep + 4 SkillCondition, grep 통과). NEW `Core/Skill/Core/SkillBinder.cs` (~75 LOC, Buildup GUID `ff73184f…` 보존). 22개 implemented skills (player common 12 + ParryEnhance + boss common 9) + 7개 null UNIMPLEMENTED (player only 5 + boss SealChain/RuptureMagazine — 패링 입력 시스템 / 로프 랜딩 이벤트 / 멀티 디렉션 wrapper 부재 사유 명시). **Codex Round 1 APPROVED WITH CHANGES** — critical: verbatim copy 금지 (mojibake로 메서드 선언 일부가 주석 줄에 붙음, clean rewrite 필수, ExecutionSpike/HuntingMark/CollapseRoar/OverchargeModeCondition/PiercingShot/RuptureMagazine/CounterSlash 등 영향). 모든 메서드 선언 줄 분리 + namespace wrap + BarrierBreaker XML doc 영문 one-liner로 교체. 5 non-blocking suggestion 모두 반영. ([history/2026-05-12-X2-10-skilllibrary-binder.md](../../../../codex-review/history/2026-05-12-X2-10-skilllibrary-binder.md))
- ✅ **X2-11 SkillManager + GameManager paired** (2026-05-12) — **재배치: GameManager X2-13 → X2-11 페어** (SkillManager가 `_gameManager.Bosses` 참조). NEW `Core/Skill/Core/SkillManager.cs` (~290 LOC clean rewrite, namespace `ArenaCombat.Core.Skill`, Buildup GUID `024d2968…` 보존, 14개 public surface). NEW `Core/GameManager.cs` (~80 LOC, namespace `ArenaCombat.Core`, Buildup GUID `9f0167c7…` 보존). **3 Codex critical 모두 적용**: (1) `Update()` 첫 줄에 `NetworkManager.Singleton`-based server-only gate (`!= null && !IsServer` return), (2) GameManager namespace `ArenaCombat.Core`, (3) **TEMPORARY Buildup-compatible registry 명시** — `_players/_bosses` lists는 X3 (PNC3D ICombatant wiring) + X4 (BNC3D + boss registry)에서 CombatManager3D 라우팅으로 교체 예정 (BUILDUP_INTEGRATION_PLAN.md 일치). **M-1 deviation**: `[SerializeField] PlayerController _owner` 제거 → `private ICombatant _owner; Awake: GetComponent<ICombatant>()`. PNC3D X3 wiring 후 자동 resolve. **DEVIATION 기록**: SkillManager.Update vs SKILL_SYSTEM_DESIGN.md §9 "FixedUpdate" — Buildup verbatim Update 유지 + server gate. X3에서 AutoCastTick 추출 가능성 노트. GameManager.Start의 SkillBinder.BindAll은 server-only X (로컬 delegate injection — 클라도 IsReady 체크에 필요). Codex 5 suggestion 모두 반영. **X2-13 삭제, sub-cycle 13→12 축소**. ([history/2026-05-12-X2-11-skillmanager-gamemanager.md](../../../../codex-review/history/2026-05-12-X2-11-skillmanager-gamemanager.md))
- ✅ **X2-12 Card draft system (final X2 round)** (2026-05-12) — NEW `Core/Card/` 폴더 (fresh GUID `8e31ffa7…`) + 4 파일 (~325 LOC). NEW `AbilityCard.cs` (15 LOC SO, menu `ArenaCombat/AbilityCard`, Buildup GUID `c923b417…`) + `CardManager.cs` (~155 LOC, Buildup GUID `ea987e74…`) + `CardUI.cs` (88 LOC, Buildup GUID `194612b6…`) + `SelectableUICard.cs` (73 LOC, Buildup GUID `af784ace…`). 모든 4 파일 namespace `ArenaCombat.Core.Card`. **Codex Round 1 APPROVED WITH CHANGES** — critical: (1) **CardManager Buildup 원본 mojibake 발견 — 문자열 quote 깨짐 (`"[CardManager] ?щ’ 媛??李?`)으로 컴파일 깨짐 → clean ASCII rewrite 강제** (X2-10 패턴 동일), (2) `using ArenaCombat.Core.Skill;` AbilityCard + CardManager 모두 추가, (3) **CardManager 헤더에 "LEGACY LOCAL DRAFT MODE ONLY" 위험 경고** (`Time.timeScale=0f` / `FindGameObjectWithTag("Player")` / 직접 `SetSlot` / `Invoke` 타이머 — X3 wiring 전 production scene 활성화 금지). **M-1**: `PlayerSkillSlot` fallback branch clean removal (auto-cast only 모델). **M-2**: Korean → 영문 ASCII 전체 번역. **M-3**: `Cards/AbilityCard` → `ArenaCombat/AbilityCard` 메뉴. **M-4**: namespace wrap. **OnCardSelected X3 routing 주석 추가**: 최종 형태는 `GameStateManager.SubmitLocalCardSelection(round, cardIndex)` RPC 체인. 5 non-blocking suggestion 모두 반영. ([history/2026-05-12-X2-12-card-draft.md](../../../../codex-review/history/2026-05-12-X2-12-card-draft.md))

### Phase X2 COMPLETE ✅ (2026-05-12)

**12 sub-cycle 모두 완료**. Buildup 스크립트 import 100% 종료 (총 ~5,700 LOC, 32+ 파일). 스킬 시스템 + 카드 드래프트 + 매치 facade + per-entity 매니저 + Stats SO 모두 우리 namespace로 통합 완료.

**X2 closure 요약**:
- L3 contract: ICombatant (23 멤버) + 9 enum + SkillContext/Definition/Registry/RoleTag + IProjectile/IPoolable/IPersistentArea + CombatantState
- L4 per-entity: StatManager (700 LOC) + StateManager (178 LOC) + SkillExecutor (155 LOC) + SkillManager (290 LOC)
- L4 scene singletons: ProjectilePool + PersistentAreaPool + PersistentAreaManager + GameManager + CardManager + SkillRangeDisplay
- L3-L4 code: SkillComponents (37 factory, 550 LOC) + SkillLibrary (29 SkillStep + 4 SkillCondition = 33, 400 LOC) + SkillBinder (75 LOC)
- L5 entity: SkillProjectile + SkillArea + (4 card UI)
- L2 data: BaseStatsSO + PlayerStatsSO + BossStatsSO (X2-1) + AbilityCard SO

**Codex 검증 12 라운드** — critical 발견 4건 (ProjectilePool Get / PersistentAreaPool Get / SkillLibrary mojibake / CardManager mojibake) 모두 패치, ML observation surface 보존, namespace 정리 완료.

**Phase X2 후속 deviations 일괄 처리 대상**:
1. SkillProjectile `ShouldRunHitDetection()` MonoBehaviour → NetworkBehaviour 전환 (X3)
2. SkillArea TickArea server gate (X3)
3. SkillManager Update server gate은 X2-11에 박았으나 NetworkBehaviour 전환 검토 가능 (X3)
4. GameManager `_players/_bosses` 직접 list → CombatManager3D 라우팅 (X3/X4)
5. CardManager 4개 LEGACY local-only 패턴 → GSM RPC/NV 라우팅 (X3)
6. Buildup origin `_logCombat=true` / `_logExecution=true` / `_logAutoCast=true` defaults — perf-sensitive 시점에 false로 (X3+)

### X3. PNC3D ↔ StatManager 통합 — **DONE** (2026-05-13~14)

7 sub-cycle 코드 작업 모두 완료. 단일 권위 모델 구축됨 (basic attack + skill 모두 StatManager.ReceiveDamage 수렴, networkHP/networkIsAlive sync hook, NGO projectile/area pool lifecycle, GSM-driven card draft). Phase X3 COMPLETE 선언은 사용자 Play-mode host + 2P smoke test 통과 후 (Codex X3-7 C-1).

**X3 sub-cycle 분할** (Codex X3-1 round에서 승인):
- ✅ **X3-1 PNC3D ICombatant interface stub** (2026-05-12) — PNC3D 클래스 선언에 `: ICombatant` 추가 + 23 explicit interface impl 멤버 (9 properties read-only forward + 14 mutation methods **warn-once + no-op**). `using ArenaCombat.Core.Combat;` + `using ArenaCombat.Core.Skill;` 추가. ICombatant.cs 헤더 "22 → 23 members" 정정 (Codex S-6). **Codex critical 적용**: TakeDamage / TakeShieldBreakDamage / RecoverHP 모두 warn-once + no-op (실제 routing은 X3-3에서 — SkillComponents detected ICombatant 경유 조기 mutation 회피). Static `_x3StubWarned` HashSet으로 process-lifetime warn 억제. **Behavior change 0**, compile-clean. SkillManager.Awake `GetComponent<ICombatant>()`가 PNC3D 검출 시작 (실제 호출은 IsServer + statManager null 체크로 차단). ([history/2026-05-12-X3-1-pnc3d-icombatant-stub.md](../../../../codex-review/history/2026-05-12-X3-1-pnc3d-icombatant-stub.md))
- ✅ **X3-2 PNC3D 4 manager component attach** (2026-05-12) — `[RequireComponent]` 4개 추가 (StatManager / StateManager / SkillExecutor / SkillManager) + Awake BindOwner + Player A.prefab 마이그레이션. zero gameplay mutation (Initialize 미호출 → 매니저 dormant).
- ✅ **X3-3 Stat authority swap (merged with NV sync)** (2026-05-13) — PNC3D에 `[SerializeField] PlayerStatsSO _playerStatsSO` + `_statMgr` cache + `_lastAttackerId` 추가. Helper `InitializeStatManager()`. OnNetworkSpawn 서버측 + Respawn 모두 Initialize 재호출 (Codex C-1). FixedUpdate sync hook: `_statMgr.GetHP()` → networkHP mirror + alive→dead transition 감지 시 `Die(_lastAttackerId)`. PNC3D.TakeDamage 리팩: networkHP 직접 변경 제거 → `_statMgr.ReceiveDamage`, Die 호출 제거 (sync hook 처리), Hit 인터럽트는 `_statMgr.IsAlive` 기반. Heal도 `_statMgr.RecoverHP` 라우팅 (Codex S-3). ICombatant 11 mutation/query (TakeDamage/TakeShieldBreakDamage/RecoverHP/AddShield/ApplyStatus/HasStatus/ApplyBuff/ApplyDebuff/RemoveStatuses/RemoveBuffs/NotifyParryReward) → StatManager 실제 routing. attacker is PNC3D면 OwnerClientId 추출하여 `_lastAttackerId` 갱신 (Codex C-2 — skill kill attribution). Read accessor 3개 (CurrentHPPercent/Shield/IsCasting) StatManager forward. **Knockback/Pull/MoveBy stub은 warn-once helper 유지** (X3-4 대상). **StatusType vs StatusMask 계층 분리**: skill/stat 계층 = StatManager.HasStatus(StatusType), legacy movement gate = networkStatusMask (StatusMask bitflag) — bridge 미구현 (Codex S-1, 후속 라운드). ([history/2026-05-13-X3-3-stat-authority-swap.md](../../../../codex-review/history/2026-05-13-X3-3-stat-authority-swap.md))
- ✅ **X3-4 Position control routing** (2026-05-13) — 3 ICombatant stub (Knockback / Pull / MoveBy) → `ApplyPositionOffset(direction, distance)` private helper. 즉시 displacement + MapBounds3D.ResolveServerPosition clamp + `rb.MovePosition(target)` + `lastValidatedServerPosition = target` + **`networkPosition.Value = target` 즉시 NV mirror** (Codex C-1 — position control 계약이 FixedUpdate sync와 같은 tick에 일관되도록). Duration 파라미터 무시 (TODO X3-N coroutine 보간). `MoveType` 4값 동일 처리 (TODO X3-N — Rope는 기존 rope queue routing). `WarnX3Stub` helper + `_x3StubWarned` HashSet **완전 제거** (남은 stub 0, Codex S-3). ([history/2026-05-13-X3-4-position-control.md](../../../../codex-review/history/2026-05-13-X3-4-position-control.md))
- ✅ **X3-5a SkillProjectile / SkillArea class conversion + IsServer gates** (2026-05-13) — SkillProjectile + SkillArea `MonoBehaviour` → `NetworkBehaviour` + `[RequireComponent(typeof(NetworkObject))]`. `using Unity.Netcode;` 추가. SkillProjectile.`ShouldRunHitDetection()` X2-7 forward-compat stub `return true` → `=> IsServer` (활성화). SkillArea.`TickArea()` 첫 줄에 `if (!IsServer) return;` gate. **Pool spawn/despawn lifecycle은 X3-5b 별도 라운드** (Codex C-1 — pool 재사용 계약 + prefab registration까지 한 번에 가면 검증 공백). 디자이너 설정 (NetworkObject 컴포넌트 + NetworkPrefabs 등록 + 선택적 NetworkTransform) 소스 헤더에 노트. ([history/2026-05-13-X3-5a-class-conversion.md](../../../../codex-review/history/2026-05-13-X3-5a-class-conversion.md))
- ✅ **X3-5b Pool NGO spawn/despawn lifecycle** (2026-05-13) — ProjectilePool + PersistentAreaPool에 `IsServerContext` helper (NetworkManager.Singleton null + IsServer 체크) 추가. Get/Return/ReturnAll 모두 server-only guard (warn + early return on client). Get: `NetworkObject.Spawn()` (null check + IsSpawned check). Return: `NetworkObject.Despawn(destroy: false)` (재사용 위해 destroy=false, NGO 2.x re-Spawn 지원). PersistentAreaManager.Spawn server-only caller contract guard 추가 + Pool.Get null 반환 처리 (Codex S-6). SkillComponents.LaunchProjectile에 null check 추가 (Codex critical — proj null NRE 방지). `using Unity.Netcode;` 3 파일 추가. 디자이너 설정: NetworkPrefabs 등록 필수 (소스 헤더 명시). Smoke test는 Play 모드 host 매치 + projectile/area spawn → return → re-spawn 사이클 확인 필요 (사용자 검증). ([history/2026-05-13-X3-5b-pool-ngo-lifecycle.md](../../../../codex-review/history/2026-05-13-X3-5b-pool-ngo-lifecycle.md))
- ✅ **X3-6 CardManager LEGACY → GSM RPC routing** (2026-05-13) — CardManager 전체 리팩. 4 LEGACY 패턴 제거 (Invoke timer / Time.timeScale / FindGameObjectWithTag / 직접 SetSlot). GSM 4개 C# 이벤트 구독 (OnCardDraftStarted / Ended / SelectionResolved / Rejected). `GSM.SubmitLocalCardSelection` 사용. 플레이어 lookup `SpawnManager.SpawnedObjects` 순회 + `IsPlayerObject && OwnerClientId == playerId` 필터 (Codex C-2 — ConnectedClients non-host 신뢰 안 됨). `allCards[cardIndex]` 모든 접근에 bounds/null guard (Codex C-3). **Codex C-1**: `Start()`에서 `GSM.RegisterCardCatalogSize(allCards.Length)` (없으면 server offer 전부 -1). **Codex C-4**: SkillManager.Update에 `if (GSM.IsGlobalCardDraftActive) return;` gate 추가 (draft 중 auto-cast 차단). HideAllCards는 resolve broadcast 시점만, reject는 UI 유지 + isSelecting 해제. `using ArenaCombat.Core.Network` SkillManager + CardManager 추가. 5 suggestion 모두 반영. ([history/2026-05-13-X3-6-cardmanager-gsm-routing.md](../../../../codex-review/history/2026-05-13-X3-6-cardmanager-gsm-routing.md))
- ✅ **X3-7 Phase X3 wiring closure + smoke test preflight** (2026-05-13) — 코드 변경 없음 (X3-6까지 모든 wiring 완료, PNC3D 11곳 `IsServerGameplayBlockedByCardDraft` + `IsOwnerInputBlockedByCardDraft` 이미 적용됨, SkillManager.Update card draft gate X3-6 추가됨). doc-only round: ROADMAP / TARGET_ARCHITECTURE / SKILL_SYSTEM_DESIGN의 X3 stale entries 정리, smoke test preflight 6개 + verification 5개 체크리스트 명시. **Phase X3 COMPLETE 선언은 사용자 Play-mode smoke test 통과 후** (Codex C-1). ([history/2026-05-13-X3-7-phase-x3-closure.md](../../../../codex-review/history/2026-05-13-X3-7-phase-x3-closure.md))

**X3 smoke test preflight** (Unity Play 모드 host 매치 전 designer setup 확인):
1. PlayerStatsSO가 Player A.prefab의 PNC3D Inspector에 할당됨
2. CardManager.allCards Inspector 배열에 AbilityCard SO 들이 채워짐
3. 각 AbilityCard.skillDefinition이 유효한 SkillDefinition SO 참조
4. SkillBinder.BindAll(SkillRegistry)이 게임 시작 시 호출되어 RuntimeStep 주입됨 (GameManager.Start에서 자동)
5. SkillProjectile + SkillArea 프리팹이 NetworkManager.NetworkConfig.NetworkPrefabs 목록에 등록됨
6. ProjectilePool / PersistentAreaPool / PersistentAreaManager 매치 씬에 배치되고 prefab 참조 연결됨

**X3 smoke test verification** (Play 모드 host + 2P 세션에서 확인):
1. 새 컴파일 에러 0
2. CardManager 이벤트 구독/해제 NRE 없음 (특히 GSM.Instance null fallback 경고가 떠도 draft UI 영구 idle 안 됨 — Codex S-2; 떠면 X3-6.1 패치)
3. Draft 중 PNC3D 기본 입력 + SkillManager auto-cast 양쪽 모두 차단
4. CardSelectionResolved 후 양쪽 클라이언트의 동일 player SkillManager slot 상태 일치
5. SkillProjectile/SkillArea Spawn → Despawn(false) → re-Spawn 사이클 정상

**Phase X3 COMPLETE 후 → X4 NEXT** (BossNetworkController3D + 보스 FSM + ML-Agents 통합).

**X3-S1 Smoke test patch — EventSystem Input System 전환** (2026-05-14) — Smoke test 첫 시도에서 발견. SampleScene/3DScene/Title.unity의 EventSystem GameObject가 `StandaloneInputModule` (옛 Input API) 사용 → 프로젝트 Input System Package 전환 후 `InvalidOperationException: You are trying to read Input using the UnityEngine.Input class` 발생. **수정**: 3 씬 EventSystem의 MonoBehaviour script GUID `4f231c4fb786f3946a6b90b886c48677` (StandaloneInputModule) → `01614664b831546d2ae94a42149d80ac` (InputSystemUIInputModule). 옛 fields (HorizontalAxis/VerticalAxis/SubmitButton 등) 제거, Unity가 default fields 자동 채움. Demo scenes (UI pack / Dark Ghosts / MasterStylizedProjectiles) 미수정 — 사용 안 함. ([history/2026-05-14-X3-S1-eventsystem-inputsystem.md](../../../../codex-review/history/2026-05-14-X3-S1-eventsystem-inputsystem.md))

**X3-S2 Smoke test patch — Choice Canvas RectTransform/Sort Order 수정** (2026-05-14) — 두 번째 smoke 시도에서 발견. CardManager.HandleDraftStarted 정상 호출되고 `[CardManager] Draft started` 로그 + `cardUIPanel.SetActive(true)` 실행됨에도 카드 UI 화면에 안 보임. **원인**: 3DScene `Choice Canvas` GameObject RectTransform `m_LocalScale: (0, 0, 0)` ← UI 0 크기로 그려짐. 추가로 `m_AnchorMax: (0, 0)` (stretch 안 됨), `m_Pivot: (0, 0)` (center 아님), Canvas SortingOrder=0 (Main Canvas와 같은 layer). **수정**: 3DScene Choice Canvas (fileID 60875990) RectTransform `LocalScale: 1,1,1` + `AnchorMax: 1,1` (fullscreen stretch) + `Pivot: 0.5, 0.5`. Canvas SortingOrder 0→10 + OverrideSorting 0→1 (Main Canvas 위에 표시). Choice1/2/3 cardSlots RectTransforms는 정상 (LocalScale=1, 위치 (-550/0/550, -110), Size 500x700) — 본 라운드 무수정. ([history/2026-05-14-X3-S2-choice-canvas.md](../../../../codex-review/history/2026-05-14-X3-S2-choice-canvas.md))

**X3-S3 Smoke test patch — CardManager + CardUI script GUID LEGACY → NEW swap** (2026-05-14) — 세 번째 smoke 시도에서 발견. X3-S2 적용 후에도 카드 UI 안 표시 + Project window에서 4 Resources/AbilityCard 자산이 깨진 아이콘 ({}). **원인**: 3DScene의 CardManager component가 LEGACY script GUID `ea987e742f42384479606957e5c252f8` (`3DSceneScript/Scripts/CardManager.cs`, **global namespace** `CardManager`) 가리킴. NEW Resources/AbilityCard 4개 자산은 `ArenaCombat.Core.Card.AbilityCard` (namespaced). LEGACY CardManager의 `public AbilityCard[] allCards`는 global `AbilityCard[]` 기대 → **타입 mismatch** → Unity가 4 자산을 모두 null로 deserialize → CardManager.HandleDraftStarted에서 line 116 `allCards[cardIdx] == null` 체크에 모든 slot 걸려 deactivate → UI 안 보임. **수정**: 3DScene.unity script GUID swap: (1) CardManager (fileID 158453617) `ea987e74…` → `180fb7e4ac69a93438f987bcd9f4ac31` (NEW `Scripts/Core/Card/CardManager.cs`), (2) 3 CardUI 인스턴스 (fileIDs 569462507/1102588391/1243347760) `194612b6667ab5d42b230decd6852912` → `82e3ae51f498c4e4b9384232973258a5` (NEW `Scripts/Core/Card/CardUI.cs`). 검증: MCP get_gameobject CardManager → `allCards: ["AbilityCard", "AbilityCard 1", "AbilityCard 2", "AbilityCard 3"]` 정상 표시 (이전 [null,null,null,null]). 부수 효과: LEGACY 필드 일부 (hostUI/clientUI/bigCardPreview/standaloneXXX/debugXXX) NEW에 없어 discarded — host/guest persistent slot UI 손실, 카드 draft 자체는 정상. ([history/2026-05-14-X3-S3-cardmanager-cardui-swap.md](../../../../codex-review/history/2026-05-14-X3-S3-cardmanager-cardui-swap.md))

**X3-S4 Smoke test patch — AbilityCard cardIcon Sprite 참조 정리** (2026-05-14) — 네 번째 smoke 시도. X3-S3 후 카드 draft 트리거 + `[GameStateManager] Card catalog size registered: 4` + `Match started` 정상. 하지만 Round=1 직전 `MissingReferenceException: The variable cardIcon of AbilityCard doesn't exist anymore` 발생 — 4 Resources/AbilityCard.asset의 `cardIcon: {fileID: 21300000, guid: …, type: 3}` Sprite 참조가 깨짐. PNG 파일 (pngegg.png GUID `f8229e5c…` 외 3개) + .meta 모두 존재 + `textureType: 8 (Sprite) / spriteMode: 1 (Single)` 정확하지만 Unity가 fileID 21300000 sub-asset을 찾지 못함. **임시 fix**: 4 .asset의 `cardIcon: {fileID: 0}` (null) 처리. ([history/2026-05-14-X3-S4-cardicon-null.md](../../../../codex-review/history/2026-05-14-X3-S4-cardicon-null.md))

**X3-S5 Smoke test patch — CardUI cardIcon null guard** (2026-05-14) — 다섯 번째 smoke (X3-S4 후 새 에러). `UnassignedReferenceException: The variable cardIcon of AbilityCard has not been assigned`. **원인**: `Scripts/Core/Card/CardUI.cs:40` `cardMaterial.SetTexture("_MainTex", card.cardIcon.texture);` — null sprite의 `.texture` 접근 시 Unity UnassignedReferenceException. line 32 `icon.sprite = card.cardIcon` (null sprite slot OK) 통과하지만 line 40 `.texture` 호출은 NRE. **수정**: null guard 1줄 — `card.cardIcon != null ? card.cardIcon.texture : null`. ([history/2026-05-14-X3-S5-cardui-null-guard.md](../../../../codex-review/history/2026-05-14-X3-S5-cardui-null-guard.md))

**X3-S6 Smoke test patch — CardUI cardName 해시 색상 tint** (2026-05-14) — X3-S5 후 카드 UI 표시되지만 4 카드 모두 흰색 → 구별 불가. cardName 텍스트 표시 UI 필드 부재. **수정**: CardUI.Setup에서 `card.cardIcon == null && cardName != ""` 일 때 cardName 해시 기반 HSV 색상 (saturation 0.55, value 1) tint 적용. 4 카드 distinct hue (블랙매지션걸 / 푸른눈의 백룡 / 오시리스의 천공룡 / 오드아이즈 각각 다른 색). 정확한 sprite 복원은 후속 polish 또는 디자이너 라운드. **재컴파일 1회 필요**. ([history/2026-05-14-X3-S6-cardicon-color-tint.md](../../../../codex-review/history/2026-05-14-X3-S6-cardicon-color-tint.md))

### X4. BossNetworkController3D 신규 — **DONE** (2026-05-13~15)

X3 wiring 클로저 직후 X3 smoke test와 병렬로 시작. PNC3D X3 분할 패턴 그대로 미러링 — 셸 → 매니저 부착 → 스탯 권위 → 위치 제어 → FSM → ML 순.

**X4 sub-cycle 분할**:
- ✅ **X4-1 BossNetworkController3D ICombatant compile-bridge shell** (2026-05-13) — 1개 NEW 파일 (`Assets/ArenaCombat/Scripts/Core/Network/BossNetworkController3D.cs`, ~110 LOC). `NetworkBehaviour, ICombatant` + 23 explicit interface impl (9 properties **inert defaults** — Codex C-2: `IsAlive=false`, `CurrentHPPercent=0`, `MaxHP=0` — 실수로 씬에 들어가도 live combatant로 취급되지 않도록 / 14 mutations warn-once + no-op). **static readonly** `_x4StubWarned` HashSet (Codex S-1, process-wide 1회). `[DisallowMultipleComponent]` + `[RequireComponent(typeof(NetworkObject))]` (Codex S-2). 파일 헤더에 "shell only — do not place in production scene / do not register as NetworkPrefab until X4 spawn path lands" 명시 (Codex S-3). `_bossStatsSO` SerializeField는 **포함 안 함** (Codex C-1 — X4-2에서 StatManager.Initialize 실제 wiring 시 추가). NetworkPrefab 미등록. **X3 smoke test와 파일 0겹침**.
- ✅ **X4-2 4 manager component attach + `_bossStatsSO` SerializeField** (2026-05-13) — `BossNetworkController3D.cs` EDIT. `[RequireComponent]` 4개 추가 (StatManager / StateManager / SkillExecutor / SkillManager). `[SerializeField] BossStatsSO _bossStatsSO` 슬롯 추가 (X4-3 Initialize 입력). `_statMgr` 캐시 필드 + `Awake()` BindOwner 호출 (StatManager + StateManager). **Codex C-1**: `skillManager.SetAutoCast(false)` Awake에서 호출 — Initialize 미호출이어도 `_isAlive=true` default + slot이 실수로 채워지면 self skill 실행될 수 있어 명시적 차단. X4-3/4에서 실제 boss tick 붙일 때 명시적 재활성화. `using ArenaCombat.Core.Stats;` + `using ArenaCombat.Core.State;` 추가. 헤더 X4-2 문구 갱신 ("X4-2 attaches managers and binds owner. StatManager.Initialize and live ICombatant routing remain X4-3"). Boss 프리팹은 ArenaCombat에 부재 → 프리팹 생성은 X4-5 spawn path 라운드로 분리. **X3 smoke test와 파일 0겹침** 유지. ([history/2026-05-13-X4-2-boss-manager-attach.md](../../../../codex-review/history/2026-05-13-X4-2-boss-manager-attach.md))
- ✅ **X4-3 HP/alive authority via StatManager + NV sync + ICombatant routing** (2026-05-13) — `BossNetworkController3D.cs` EDIT (merged round, PNC3D X3-3 패턴 미러). NetworkVariable 2개 추가 (`networkHP` float / `networkIsAlive` bool, server-write everyone-read, default 0/false). `InitializeStatManager()` 헬퍼 — **Codex C-1**: `_bossStatsSO == null` 시 skip + warn + inert 유지 (BossMaxHP fallback 없음 — stray shell이 live target이 되면 안 됨). `OnNetworkSpawn` IsServer 분기에서 Initialize 호출 + NV prime. `FixedUpdate` server-only sync hook (**Codex S-4**: `IsServer + IsSpawned + _statMgr != null + networkIsAlive.Value` 가드) — `_statMgr.GetHP()` mirror + alive→dead transition 시 `OnBossDefeated(_lastAttackerId)`. **Death handling**: networkIsAlive=false + warn-once log까지 (match-end broadcast은 X4-5/6 BossManager+GSM). ICombatant 17 멤버 교체: **Codex C-2** — 8 read property는 NV / SO 기반 (`MaxHP→BossStatsSO`, `CurrentHPPercent→networkHP/MaxHP`, `IsAlive→networkIsAlive.Value`, `IsCasting→IsServer + alive + _statMgr.IsCasting`, `Shield→0f`, parry 2개 false/0); **Codex C-3** — 11 mutation/query 모두 `if (!IsServer || _statMgr == null || !networkIsAlive.Value) return;` 가드 후 StatManager forward (`ReceiveDamage / ReceiveShieldBreakDamage / RecoverHP / AddShield / ApplyStatus / HasStatus / ApplyBuff / ApplyDebuff / RemoveStatuses / RemoveBuffs / NotifyParryReward`). 3 position-control stub 유지 (Knockback/Pull/MoveBy → X4-4). `_lastAttackerId` 캐리 (PlayerNetworkController3D OwnerClientId 추출, PNC3D X3-3 미러). `SetAutoCast(false)` X4-2 유지 — X4-4 FSM 라운드에서 명시적 활성화. `CombatantKind.Boss` 사용 확인됨. `BossBaseDefense` 사용 안 함 (Codex S-2 — 후속 damage formula 라운드). **X3 smoke test와 파일 0겹침** 유지. ([history/2026-05-13-X4-3-boss-stat-authority.md](../../../../codex-review/history/2026-05-13-X4-3-boss-stat-authority.md))
- ✅ **X4-4 Position control routing** (2026-05-13) — `BossNetworkController3D.cs` EDIT (PNC3D X3-4 직접 미러). `[RequireComponent(typeof(Rigidbody))]` + `[RequireComponent(typeof(Collider))]` 추가. `_rb` cache + Awake 설정 (useGravity=false / FreezeRotationX,Z / Interpolate). `networkPosition` NV (Vector3, server-write everyone-read, default zero) 추가 — PNC3D 명시 NV 패턴 일관성 (NetworkTransform 컴포넌트 미사용). **Codex C-1**: 명시 NV는 server write + client apply 한 세트 — `OnNetworkSpawn` 서버 분기에서 `networkPosition.Value = transform.position` prime + 비서버 분기에서 `transform.position = networkPosition.Value` snap + `networkPosition.OnValueChanged += HandlePositionChanged` 구독. `OnNetworkDespawn` 비서버 분기 unsubscribe. `HandlePositionChanged` immediate snap (보간 X4-N polish). `_lastValidatedServerPosition` 필드 추가. `ApplyPositionOffset(direction, distance)` private helper — `MapBounds3D.ResolveServerPosition` + `_rb.MovePosition` + 즉시 `networkPosition.Value = target`. 3 stub (Knockback / Pull / MoveBy) → `ApplyPositionOffset` 호출 (`IsServer && networkIsAlive.Value && _rb != null` 가드, Codex S-5). **Codex S-2**: FixedUpdate에서 networkPosition 매-tick 갱신 안 함 (FSM 없어 의미 없고 ApplyPositionOffset 즉시 mirror로 충분). **Codex S-4**: `WarnX4Stub` helper 제거 (호출자 없음); `_x4StubWarned` → `_warnedOnce` 리네임 (OnBossDefeated 로그용). FSM (`SetAutoCast(true)` + 동작 패턴) X4-5로 분리. **X3 smoke test와 파일 0겹침** 유지. ([history/2026-05-13-X4-4-boss-position-control.md](../../../../codex-review/history/2026-05-13-X4-4-boss-position-control.md))
- ✅ **X4-5a BossManager 셸 (scene-local singleton)** (2026-05-13) — 1 NEW 파일 `Assets/ArenaCombat/Scripts/Core/Network/BossManager.cs` (~80 LOC). `MonoBehaviour` (NetworkBehaviour 아님 — PlayerSpawnManager 패턴 미러). `[DisallowMultipleComponent]` + Instance 싱글톤. **Codex C-1**: scene-local — `DontDestroyOnLoad` **사용 안 함** (serialized scene Transform `_bossSpawnPoint`와 DDOL 조합이 씬 전환 후 dangling reference 만듦, Boss arena 씬 종속이 자연스러움). `[SerializeField] _bossPrefab / _bossSpawnPoint` 슬롯 (null 허용, X4-5b designer 채움). `_spawnedBoss` NetworkObject 캐시 + `CurrentBoss` getter. `TrySpawnBoss()` stub: warn-once + return false. 호출자 부재 — surface만 노출. **X3 smoke와 파일 0겹침** 유지. ([history/2026-05-13-X4-5a-bossmanager-skeleton.md](../../../../codex-review/history/2026-05-13-X4-5a-bossmanager-skeleton.md))
- ✅ **X4-5b Boss prefab + scene wiring** (2026-05-15) — `Assets/ArenaCombat/Prefabs/Boss/Boss.prefab` NEW (hand-written YAML, 9 components: NetworkObject + BossNetworkController3D + Rigidbody + BoxCollider + StatManager + StateManager + SkillExecutor + SkillManager). `_bossStatsSO` → `BossStatsSO.asset`. `BossStatsSO.BossPhaseThresholds` = [0.75, 0.5, 0.25]. `DefaultNetworkPrefabs.asset`에 5번째 엔트리 등록. 3DScene에 BossManager GO (BossManager 컴포넌트, `_bossPrefab` / `_bossSpawnPoint` 와이어) + BossSpawnPoint (0,1,8) 배치. Codex 1 라운드 (GUID/YAML/settings 전수 검증 PASS, Git HEAD diff false-positive 2건 제외). ([history/2026-05-15-X4-5b-boss-prefab-setup.md](../../../../codex-review/history/2026-05-15-X4-5b-boss-prefab-setup.md))
- ✅ **X4-5c Boss spawn implementation** (2026-05-15) — `BossManager.cs` full rewrite from shell: `TrySpawnBoss()` (IsServer guard + Instantiate + NetworkObject.Spawn + GameManager.RegisterBoss + SkillManager.SetAutoCast(true) + BossDefeated event subscribe), `DespawnBoss()` (unsubscribe + UnregisterBoss + Despawn(true)), `HandleMatchStateChanged` (server-only, InProgress → auto-spawn), `HandleBossDefeated` (TransitionToState(MatchEnd)). `BossNetworkController3D.cs` EDIT: `public event Action<ulong> BossDefeated` + invoke in OnBossDefeated. Codex 1 라운드 PASS. ([history/2026-05-15-X4-5c-boss-spawn-impl.md](../../../../codex-review/history/2026-05-15-X4-5c-boss-spawn-impl.md))
- ✅ **X4-6 Phase tracking** (2026-05-13) — `BossNetworkController3D.cs` EDIT. Buildup `BossController.HandlePhase` / `OnPhaseChanged` 포팅. **Codex C-1**: `NetworkVariable<BossPhase>` 사용 (raw int 아님 — 프로젝트 공용 `BossPhase` enum 재사용으로 X4-7 FSM 해석 충돌 회피). `networkCurrentPhase` NV (default `BossPhase.None`, server-write everyone-read) + `public CurrentPhase` getter. **Codex C-2**: `InitializeStatManager` 성공 시 `BossPhase.Phase1` 설정 — full HP가 None에 머무르지 않도록. **Codex C-3**: `OnBossDefeated`에 `BossPhase.Defeated` write 추가 — phase UI/FSM이 마지막 combat phase에 갇히지 않도록. `HandlePhase()` 헬퍼: thresholds[i] crossed 시 `Phase2` / `Phase3` / `Enrage` 매핑 (Codex S-2). 4번째 이상 threshold는 warn-once + 무시 (`MaxPhaseThresholds=3`, Codex S-3). FixedUpdate에서 HP mirror 후 / defeat check 전 호출 (Codex S-4). `OnPhaseChanged` 현재 log-only — phase-driven behavior wiring은 X4-7 FSM (Codex S-5). **X3 smoke + X4-5a/b와 파일 충돌 0**. ([history/2026-05-13-X4-6-phase-tracking.md](../../../../codex-review/history/2026-05-13-X4-6-phase-tracking.md))
- ✅ **X4-7 Boss skill pool + phase switching** (2026-05-15) — 3 파일. `SkillRoleTag.cs`: `Boss=29` append. `SkillExecutor.cs`: `CooldownScale` property (default 1.0, min 0.1) — CanUse/GetRemainingCooldown에 적용, player 무영향. `BossNetworkController3D.cs`: `_skillMgr`/`_skillExec` Awake 캐시, `PopulateBossSkills(phase)` — registry.GetByRoleTag(Boss) → ClearAll + SetSlot, Enrage→RoundRobin, phase별 CooldownScale (1.0/0.85/0.7/0.5), SetAutoCast(true). InitializeStatManager + OnPhaseChanged에서 호출. Codex 1 라운드 PASS. ([history/2026-05-15-X4-7-boss-skill-phase.md](../../../../codex-review/history/2026-05-15-X4-7-boss-skill-phase.md))
- ✅ **X4-7b Boss SkillDefinition assets** (2026-05-15) — 11 NEW `.asset` + 11 `.meta` + 1 folder `.meta` (`Resources/Skills/BossSkills/`). 11 Boss SkillDefinition SO 생성 (ExecutionSpike_Boss / CrushingBarrage_Boss / ErosionField_Boss / SurvivalPulse_Boss / FortressArmor_Boss / CollapseRoar_Boss / OverchargeMode_Boss / MarkWave_Boss / SealChain_Boss / BarrierBreaker_Boss / RuptureMagazine_Boss). 모든 RoleTags에 `Boss(29)` 포함. SealChain_Boss + RuptureMagazine_Boss는 RuntimeStep=null (UNIMPLEMENTED — multi-direction wrapper 부재). `SkillRegistry.asset._pool` 12→23 엔트리 확장. **PopulateBossSkills()가 9개 구현 스킬 검출 가능.** Codex 1 라운드 PASS. ([history/2026-05-15-X4-7b-boss-skill-assets.md](../../../../codex-review/history/2026-05-15-X4-7b-boss-skill-assets.md))
- ✅ **X4-7d GameManager scene placement** (2026-05-15) — `3DScene.unity` EDIT. GameManager GO (fileID 826500001) 추가 — `_skillRegistry` → SkillRegistry.asset 와이어. **이전에 어느 씬에도 GameManager가 없어서 `SkillBinder.BindAll()` 미호출 → 모든 스킬 IsReady=false 상태였음.** SceneRoots에 Transform 등록. Codex 1 라운드 PASS.
- ✅ **X4-7c Boss targeting fix** (2026-05-15) — 2 파일. `SkillManager.cs`: `FindNearestTarget()` — `_statManager.Kind == CombatantKind.Boss`이면 `_gameManager.Players` 검색, 아니면 `_gameManager.Bosses` 검색 (기존: 항상 Bosses → 보스가 자기 자신을 타겟). Awake에 `_gameManager = GameManager.Instance` fallback 추가 (boss prefab Inspector 미와이어 대응). `PlayerNetworkController3D.cs`: `OnNetworkSpawn` IsServer 블록에 `GameManager.RegisterPlayer(gameObject)` 추가 + `OnNetworkDespawn`에 `UnregisterPlayer` cleanup. Codex 1 라운드 PASS. ([history/2026-05-15-X4-7c-findtarget-fix.md](../../../../codex-review/history/2026-05-15-X4-7c-findtarget-fix.md))
- ✅ **X4-7e Integration wiring** (2026-05-15) — Player A.prefab EDIT: PNC3D `_playerStatsSO` → `PlayerStatsSO.asset` 와이어 추가 (이전 null → StatManager 스탯 미적용). Codex PASS.
- ✅ **X4-N Position interpolation polish** (2026-05-15) — `BossNetworkController3D.cs` EDIT. `HandlePositionChanged` 즉시 snap → `Vector3.Lerp` 보간 (InterpSpeed=18). 클라이언트 전용 `Update()` 추가 (`IsServer` guard). 서버는 기존 `rb.MovePosition` 유지. OnNetworkSpawn 초기 snap 불변. Codex 1 라운드 PASS.
- ✅ **X4-8 ML-Agents inference integration** (2026-05-15) — 4 sub-items:
  - **X4-8a** `manifest.json`: `com.unity.ml-agents` 4.0.2 추가 (Buildup 동일 버전).
  - **X4-8b** NEW `Assets/ArenaCombat/Scripts/Core/AI/BossObservationCollector.cs`: Buildup → BNC3D 적응. 5슬롯 SkillManager, GameManager.Player1/2 런타임 참조. Phase3Size=35 (6+5+24). Burst damage + speed tracking.
  - **X4-8c** NEW `Assets/ArenaCombat/Scripts/Core/AI/BossInferenceAgent.cs`: inference-only Agent (Buildup 1116 LOC → 170 LOC). Obs=40, Actions: B0=4 movement, B1=6 skill. Server authority (`IsServer` guard). Movement → `BNC3D.ApplyMLPosition()` (NV mirror). Skill → SkillManager/SkillExecutor.
  - **X4-8d** `BossNetworkController3D.cs`: `_mlInferenceActive` flag + `ApplyMLPosition()` public API. `PopulateBossSkills` auto-cast 조건부. `BossManager.cs`: ML agent 감지 시 `SetAutoCast(true)` skip.
  - **X4-8e** `Boss.prefab` YAML: BehaviorParameters (Obs=40, B0=4, B1=6, BehaviorName=BossInference, 모델 미할당) + DecisionRequester (period=5) + BossObservationCollector (BNC3D/StatManager/SkillExecutor/SkillManager 와이어) + BossInferenceAgent (**m_Enabled=0**, 모든 참조 와이어). ONNX 드롭인 워크플로: Inspector에서 Agent 켜기 + 모델 할당.
  - Codex 2 라운드 PASS (R1: CI×3 발견 → R2: 수정 후 APPROVED).
  - **ONNX 드롭인 준비 완료** — BossInferenceAgent 비활성 상태에서는 기존 auto-cast 유지. 활성화 + 모델 할당 시 ML 추론 전환.

### X5. Chapter1 씬 활성화 + 동작 검증 — **DONE** (2026-05-15)
- ✅ **X5-a Training GO 비활성화** (2026-05-15) — 6개 training-only GO 비활성화:
  - (Train) Boss PrefabInstance (m_IsActive: 0)
  - LittleGhost_3 P1/P2 (PrefabInstance root GO m_IsActive: 0)
  - SkillBootstrap, SkillDistributor, BossEffectSpawner (m_IsActive: 0)
- ✅ **X5-b Manager 배선** (2026-05-15) — 3개 매니저 Chapter1에 와이어링:
  - GameManager (기존 유지, _players/_bosses 빈 리스트로 클리어)
  - BossManager (새 GO, Boss.prefab + BossSpawnPoint 연결)
  - GameStateManager (새 GO, NetworkObject + GSM 컴포넌트, 3DScene 설정 복제)
  - BossSpawnPoint 이름 "(Train) BossSpawnPoint" → "BossSpawnPoint"로 변경
- ✅ **X5-c UI/Script GUID 수정** (2026-05-15) — X3-S2/S3 패턴 미러:
  - CardManager GUID: LEGACY `ea987e74…` → NEW `180fb7e4…`
  - CardUI ×3: LEGACY `194612b6…` → NEW `82e3ae51…`
  - Choice Canvas: Scale 0→1, AnchorMax 0→1, Pivot 0→0.5, SortingOrder 0→10, OverrideSorting 활성
  - CardManager GO 활성화 (m_IsActive 0→1)
  - allCards: Buildup 16개 → 우리 4개 AbilityCard 자산
  - GameManager._skillRegistry: Buildup GUID → 우리 SkillRegistry GUID
- ✅ **X5-d 씬 전환 플로우 연결** (2026-05-15) — Chapter1을 primary gameplay 씬으로 전환:
  - SampleScene: RelayManager `gameSceneName` "3DScene" → "Chapter1"
  - SampleScene: PlayerSpawnManager `gameSceneName` "3DScene" → "Chapter1"
  - Build Settings: SampleScene(0) + Chapter1(1) + 3DScene(disabled)
  - 4 SpawnPoint: "(Train) SpawnPointN" → "PlayerSpawn N" + PlayerSpawnPoint3D 컴포넌트 추가 (order 0-3)
  - GameSceneInitializer GO 추가 (disconnect → SampleScene 복귀)
- ✅ **X5-e BossManager GSM race fix** (2026-05-15) — `BossManager.cs` EDIT. `OnEnable` → `TrySubscribeGSM()` + `Update` retry (GameStateManager.Instance null race). `_subscribedGSM` 필드로 정확한 구독 해제. 매치 진행 중 catch-up spawn. Codex 1 라운드 PASS. ([history/2026-05-15-X5-e-bossmanager-gsm-race.md](../../../../codex-review/history/2026-05-15-X5-e-bossmanager-gsm-race.md))
- ✅ **X5-f Missing Prefab 정리** (2026-05-15) — Chapter1.unity: 34 YAML 블록 제거 (6 Buildup UI PrefabInstance + Card GO + 26 stripped/added objects). Main Canvas m_Children에서 Card 참조 제거. 8167줄로 축소 (9487→).
- ✅ **X5-g 캐릭터 비주얼 적용** (2026-05-15) — Boss.prefab: LittleGhost_2 네스트 PrefabInstance 추가 (scale 20, Y 180°, physics 제거). Player A.prefab: Y Bot PrefabVariant → standalone prefab 전환 + LittleGhost_3 네스트 PrefabInstance (scale 10, physics 제거). 외부 참조 fileID 보존.
- ✅ **X5-h Main Camera 스크립트 교체** (2026-05-15) — Chapter1 Main Camera: 누락 Buildup CameraFollow GUID → PlayerCamera GUID 교체. player 런타임 할당 (PNC3D.SetTarget).
- 동작 검증 (MCP 콘솔 로그): boss HP 감소 확인, 스킬 발사/적중, 카드 드래프트 정상
- **X5 = Chapter1 활성화 완료**

---

## Phase C — 적응형 AI

### C1. 플레이어 행동 편향 로그 수집 — **DONE**
- 9개 편향 (근접/원거리/공격집중/생존/패링/로프/스킬/팀밀착/팀분산)
- 5초 평가, 15~20초 관측
- 로그만 먼저, 가중치 적용은 나중
- C1-1: PlayerBiasTracker.cs (NEW, Core/AI), counter-based 9-bias server singleton
- C1-1: SkillExecutor.OnExecuted event (+2 lines)
- C1-1: PNC3D record hooks (melee/parry/rope/skill subscription lifecycle, +16 lines)
- C1-1: BNC3D TakeDamage debug log removed (-1 line)
- C1-1: Chapter1.unity PlayerBiasTracker GO added

### C2. BT 기반 시뮬레이션 플레이어 에이전트 (ML 학습 전용) — **DONE**
- **목적**: 보스 ML-Agents 학습용 다양한 플레이스타일 시뮬레이션 (실제 유저 조작 아님)
- 다양한 타입 BT (근접형/원거리형/패링형/생존형/혼합형) → 보스가 다양한 상대 경험
- PNC3D 서버 경로 직접 호출 (InputHandler 경유 아님, 서버 권위 유지)
- ML-Agent 직행하지 않음 — BT 먼저로 baseline 데이터 확보
- **실제 플레이어 조작**: WASD 이동 + 마우스 에임 (커서 방향 바라보기) — 이미 PlayerInputHandler + PNC3D에 구현됨
- C2-1: BTNode.cs (BT framework, Core/AI/BT), BTPlayerAgent.cs (melee personality)
- C2-1: PNC3D ServerSetMoveIntent/ServerSubmitAttack/ServerSubmitParry + IsBTControlled flag
- C2-1: single BT agent constraint (dedicated server only, clientId 0)

### C3. 보스 적응형 가중치 적용 — **DONE**
- C1 데이터를 보스 패턴 가중치에 반영
- 9개 보스 행동 분류로 매핑 (BiasResponseMap: Melee→Ranged, Ranged→Melee, Attack→Shield, Survival→Burst, Parry→AOE, Rope→Zone, Skill→Counter, TeamClose→AOE, TeamSpread→Mark)
- C3-1: BossAdaptiveWeights.cs (NEW, Core/AI) — singleton, ComputeWeight via RoleTags + averaged biases
- C3-1: PlayerBiasTracker.cs GetAverageBiases() (+10 lines)
- C3-1: SkillManager.cs weighted auto-cast path (single CanCast pass, weighted random selection) (+40 lines)
- C3-1: BossNetworkController3D.cs UseAdaptiveWeights wire (+1 line)
- C3-1: Chapter1.unity BossAdaptiveWeights GO added

### C3a. Boss AI Pool Selection (4-archetype × 10-combo variant pool) — **DONE** (2026-05-18)
- 4 player archetypes: Melee / Ranged / CC / Hybrid
- 11 BossAIDefinition SOs (1 Default + 10 archetype-pair combos), order-invariant lookup
- 3-min eval cycle classifies each player; per-frame distance sampling + skill cast / parry hooks
- BossAIPoolManager selects variant on archetype change, applies via BossNetworkController3D.ApplyAIVariant
- Mid-telegraph swaps deferred via OnIdleAfterAction
- Per-variant slotWeights multiplied into BossAdaptiveWeights.ComputeWeight (two-tier adaptation)
- Files (all NEW unless noted):
  - C3a-A: PlayerArchetype.cs, BossAIDefinition.cs (SO), PlayerArchetypeClassifier.cs (skeleton)
  - C3a-B: PlayerBiasTracker.cs (EDIT — Register/Unregister/RecordX forwarding shim + DistToBoss helper)
  - C3a-C: PlayerArchetypeClassifier.cs (FixedUpdate, EvaluateAll, Classify, SamplePassiveDistances, slot CC bias, weight decay)
  - C3a-D: BossNetworkController3D.cs (EDIT — IsBusy, OnIdleAfterAction, ApplyAIVariant, FixedUpdate edge-detect + death-cancel clear). SkillManager.cs (EDIT — IsTelegraphing getter)
  - C3a-E: BossAIPoolManager.cs (NEW — server singleton with lookup table, OnEnable/Update-poll subscription retry, deferred swap, cold-start Default AI)
  - C3a-F: SkillManager.cs (EDIT — \_slotWeights field, SetSlotWeights/GetSlotWeight, ClearAll reset, adaptive-branch multiplication). BossNetworkController3D.cs (EDIT — ApplyAIVariant pushes slotWeights). BossAIDefinition.cs (EDIT — NaN/Inf normalization in OnValidate)
- Variant SO content: placeholders only at this phase; 11 SOs to be authored by designer during play tuning. See Core/AI/AI_VARIANT_PLACEHOLDERS.md for asset paths.
- Known gap: PopulateBossSkills(phase) clobbers variant slots on phase transition. Pool manager re-applies on next archetype change (bounded by ~3-min eval cadence). Explicit phase-event hook = follow-up.

---

## BAL-1 — 밸런스 패스 (2026-05-19)

Plan: `C:\Users\paek6\.claude\plans\foamy-baking-melody.md`

### T1A. 보스 이동 속도 스케일링 — **DONE**
- BossInferenceAgent moveSpeed 페이즈별: Phase1/2=8.4, Phase3=9.2, Enrage=10.1 m/s
- BNC3D.OnPhaseChanged에서 SetMoveSpeed 호출

### T1B. ML 이동 전용 전환 — **DONE**
- BossInferenceAgent: movement-only (skill selection은 서버 auto-cast)
- Boss.prefab BehaviorParameters: Obs=40, B0=4 movement only

### T1C. AIHint 필드 + 23 SO 동기화 — **DONE** (2026-05-19)
- SkillDefinition.cs: `AIHint_ConeOrAoE`, `AIHint_Category` (SkillCategoryFlag enum) 추가
- 23 SO 자산 (11 Boss + 12 Player) 업데이트
- BossObservationCollector: `_maxBurstDmg` 80→120
- GameStateManager: `cardDraftInterval` 180→175
- ML_TRAINING_HANDOFF.md 동기화

### T2. Boss 데미지 + HP/스탯 스케일링 — **DONE** (2026-05-19)
- Boss 7개 스킬 데미지 조정 (SkillLibrary.cs)
- Player/Boss CrushingBarrage: DealDirectionalHit → 0f (detection only, multi-hit만 데미지)
- Projectile detection radius 0.5→1.0 (SkillProjectile.cs + .prefab)
- Boss HP 1000→6000, Player HP 100→150
- Codex R1→R2(FAIL 2건)→R3(PASS). ([history/2026-05-19-BAL-1-T2-R3-damage-hp-scaling.md](../../../../codex-review/history/2026-05-19-BAL-1-T2-R3-damage-hp-scaling.md))

### T3. Phase 스케일링 (4축) — **DONE** (2026-05-19)
- **Damage**: 1.0/1.08/1.16/1.25 — StatManager._phaseDamageScale + SkillContext.DamageScale + SkillComponents 6개 경로
- **Telegraph**: 1.0/0.9/0.78/0.7 — SkillManager.TelegraphScale
- **Cooldown**: 1.0/0.85/0.7/0.5 (기존)
- **Speed**: 8.4/8.4/9.2/10.1 (기존 T1A)
- Codex R1(FAIL: SkillComponents bypass)→R2(PASS). ([history/2026-05-19-BAL-1-T3-phase-scaling.md](../../../../codex-review/history/2026-05-19-BAL-1-T3-phase-scaling.md))

### 핸드오프 문서
- ML_TRAINING_HANDOFF.md Section 5 채널 순서 통일 (A안: remCD→maxCD→range→coneOrAoE→one-hot3)

---

## Phase D — 정리

### D1. 레거시 2D 코드 제거 — **DONE**
- D1-1: 6 legacy .cs 삭제 (PlayerNetworkController, CombatManager, MapBounds, CameraFollow, GrapplingHook, GrappleRangePreview)
- D1-1: PlayerCharacter.prefab 삭제 + DefaultNetworkPrefabs 정리
- D1-1: PNC3D → CombatManager3D 전환 (perk trigger 마이그레이션)
- D1-1: PlayerSpawnManager/PlayerInfoDisplay 레거시 참조 제거
- D1-1: 3DScene CombatManager GO 삭제
- D1-1: Chapter1에 CombatManager3D GO 추가 (NetworkObject, playerLayer=Default)
- D1-1: RelayManager + PlayerSpawnManager gameSceneName → "Chapter1" 전환

---

## DEFERRED (의도적으로 안 함)

- 난투형 PvP
- 1vs1 / 2vs2 / 4인 개인전
- 캐릭터 셔플/조작권 재할당
- 2D 메인 타깃 복귀
- Dedicated server 전환

---

## 작업 진행 규칙

1. **현재 IN PROGRESS는 한 번에 하나만.** 새 항목을 시작하면 이전 항목을 DONE 또는 NEXT/LATER로 이동.
2. 항목 시작 시 이 파일에서 status를 IN PROGRESS로 표시. 끝나면 DONE으로.
3. 스코프가 커지면 (NEXT 항목이 실제로 여러 sub-task로 갈라지면) 작업 시작 직전 sub-list로 풀어서 적는다 — 매번 plan을 따로 세우지 않는다.
4. 로드맵에 없는 작업이 들어오면 사용자에게 "이거 로드맵 어디에 끼울까요?" 한 줄 확인 후 진행.
5. 각 항목 DONE 시 `last updated` 갱신, 코드 변경이 있으면 [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md)도 동기화.

## 코드 변경 게이트 — Codex 검증 필수

**모든 `.cs` 변경은 `[codex-review/](../../../../codex-review/README.md)` 사이클을 거친 뒤에만 적용된다.** 로드맵 항목을 시작했더라도 코드 한 줄 수정 전에:

1. `pending.md` 작성 → 사용자에게 알림 → 멈춤
2. Codex 응답 대기 (`feedback.md` 또는 채팅)
3. 승인 후 실행 → `history/` 아카이브

ROADMAP.md / PROJECT_STRUCTURE.md / 메모리 같은 doc 갱신은 게이트 통과 후 자동으로 같이 진행 (별도 검증 불필요).
