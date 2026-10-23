# 마일스톤 1 — 수직 슬라이스 설계 · 파일별 계획 · 수용 기준

범위: **로비 → 스테이지 선택 → 4인 편성(자동전투 토글) → 전투(이동/일반/스킬/궁극기/회피/태그, 적 2종+보스) → 결과·보상 → 저장**, 1개 맵 + 3개 스테이지.
기술 제안(원 기획 의무 사항 아님): Unity 6 LTS + C# 9 + URP, 모바일 가로.

## 1. 작동 흐름

```
[Play] → LuminaBootstrap ─(씬에 없으면 생성)→ GameApp.Awake
   ├ GameDatabase.Load(Resources/LuminaData/*.json) → Validate() (실패 시 오류 화면)
   ├ GameSession(FileSaveStore(persistentDataPath))  ← 미완료 전투 있으면 무효 처리(A-15)
   └ LobbyScreen
        └ 모험 → StageSelectScreen (IsStageUnlocked / ClearRecord / 보상 표시)
              └ 전투 준비 → PartySetupScreen (SetPartySlot 교환, CanUseAutoBattle ← ClearRecord(stageId))
                    └ 출전 → GameSession.TryStartBattle (검사 후에만 행동력 차감, battleId 발급, pending 저장)
                          └ BattleController (60틱 고정 BattleSim + AutoBattleController/InputMerger)
                                ├ 틱마다 CombatEvent → SfxRouter→SfxPlayer / VfxSystem / Avatar·EnemyView / CameraRig / HUD
                                └ BattleEnd → GameSession.CompleteBattle(battleId) (보상 1회 확정·ClearRecord·저장)
                                      └ ResultScreen (별/통계/보상 · 재도전=재검사·재차감)
```

## 2. 폴더/데이터 구조

```
Assets/Lumina/
  Core/            순수 C#(UnityEngine 비의존, asmdef noEngineReferences) — Unity·dotnet 공용
    Data/          Enums, Defs(CharacterDef·WeaponTypeDef·AttackStepDef·SkillDef·EnemyDef·StageDef·ElementReactionDef·BalanceDef), GameDatabase(로드·검증)
    Combat/        BattleSim(전투 규칙·이벤트), DamageCalc(GDD 공식), Geometry, AutoBattleController, InputMerger
    Progress/      SaveData(Account·Inventory·OwnedCharacter·PartyPresets·ClearRecord·BattleReceipt·PendingBattle), SaveStore(원자적 파일 저장), GameSession
    Audio/         SfxTiming(휘두름 선행), Dsp, SfxSynth(무기별 레시피), SfxRouter(이벤트→소리), SfxAnalysis(WAV·특징량)
  Runtime/         Unity 전용(Assembly-CSharp)
    GameApp.cs     앱 루트·화면 흐름·부트스트랩
    Common/        Theme·Mats(URP/Built-in 자동)·Fonts(한글 OS 폰트)
    UI/            Ui 헬퍼, Widgets(조이스틱·액션버튼), Screens(로비·스테이지·편성·결과), BattleHud
    Battle/        BattleController, ActorViews(아바타·적 절차 포즈), WeaponRig(16무기 모형), VfxSystem, BattleViews(카메라·맵·예고·투사체), BattleInput, ProcMesh
    Audio/         SfxPlayer(합성 캐시·보이스 풀)
  Editor/          LuminaProjectSetup(Boot 씬·빌드 목록·가로 고정·URP 자동 설정·WAV 내보내기)
  Resources/LuminaData/  balance · reactions · weapons · characters_test · roster_canon · enemies · stages (.json)
  Tests/EditMode/  NUnit 76개 — Unity Test Runner 와 dotnet 양쪽에서 동일 파일 실행
tools/
  Lumina.Core.Tests/   dotnet test 진입점
  Lumina.Sim/          헤드리스 흐름(flow) · 효과음 쇼케이스(sfx) · 처치시간(ttk)
  UnityCompileCheck/   Unity 엔진 참조 컴파일 검사(Player/Editor)
docs/handoff/          인계 원문(01·02·04·06)
```

씬: `Assets/Scenes/Boot.unity` 1개(에디터 자동 설정이 생성). 화면 전환은 씬 로드 없이 UI 레이어 교체(모바일 로딩 최소화).

데이터 모델은 GDD 12장 명칭을 유지: `CharacterDef / SkillDef / WeaponTypeDef / EnemyDef / StageDef / ElementReactionDef / ClearRecord / PartyPreset / BattleReceipt`.
정식 16인은 `roster_canon.json`(확인된 필드만, 나머지 null)으로 **조회 전용** 보관, 전투는 `characters_test.json`의 검증용 캐릭터만 사용.

## 3. 파일별 구현 계획과 수용 기준

| 파일 | 책임 | 수용 기준(자동 검증) | 상태 |
|---|---|---|---|
| `Core/Data/*`, `LuminaData/*.json` | 정의 데이터·검증 | 9속성/16무기(8·4·4)/13반응 쌍·배율/16인 이름 순서가 04_MACHINE_RULES 와 일치, 로스터 미확정 필드 null, 시험체가 실제·구 콘셉트 이름 미사용 | 완료 |
| `Core/Combat/DamageCalc.cs` | 피해·방어·레벨 공식 | K=300(1+0.02Lv), 관통/무시 각 1회, 스태거×1.5, 스킬 배율표 | 완료 |
| `Core/Combat/BattleSim.cs` | 전투 규칙 | 공명 +4/+10/+18/+3·궁극기 −100, 스킬 6초(캐릭터별 유지), 태그 1초·일반쿨 50%↓, 회피 2.5초·무적 0.65초, 브레이크 100/150→5초·×1.5, 180초 패배, 승리 1회, KO 자동 교대·KO 태그 금지 | 완료 |
| 〃 | 명중 프레임 동기 | 16무기 전부: 피해 적용 틱 = Hit 이벤트 틱, 근접 명중 = 시작+선딜, 휘두름 큐 = 명중−선행(±1틱), 원거리: 발사 → 비행 → 명중 | 완료 |
| `Core/Combat/AutoBattleController.cs` | 자동전투 AI | 일반·스킬·회피·궁극기 모두 자동 사용, 1스테이지 자동 클리어 | 완료(AI 세부는 가정) |
| `Core/Progress/GameSession.cs` | 출전·보상·저장 | 검사 후에만 행동력 차감, 미클리어 자동전투 출전 거부, battleId 멱등 보상, 최초/반복 분리, 최고 별 유지, 계정 EXP 1000/Lv, 강제 종료 복구, 개발 재화 권한 | 완료 |
| `Core/Audio/*` | 무기별 효과음 | 16무기 명중음 특징량 상호 거리 ≥0.25, 휘두름/발사음 ≥0.15, 1타<피니시 음량, 휘두름 피크≈선행 시간, 재질별 피격 반응 차이 | 완료(청음 미검증) |
| `Runtime/GameApp.cs`, `UI/Screens.cs` | 로비·스테이지·편성·결과 | 컴파일 통과 / Unity 실행 시: 미클리어 스테이지에서 자동전투 버튼 잠금+사유, 클리어 후 ON 저장, 출전 시 행동력 차감 | 코드 완료·실행 미검증 |
| `Runtime/Battle/BattleController.cs` | 전투 진행·이벤트 연출 | 히트스톱 동안 시뮬 정지(모션/소리 동기), 결과 전 보상 확정, 포커스 상실 일시정지 | 코드 완료·실행 미검증 |
| `Runtime/Battle/ActorViews.cs`, `WeaponRig.cs` | 무기별 모션 | 스윙 중심 = 명중 시각(포즈를 actionTime 으로 계산), 16무기 모형·고유 연출(시위·장전 반동·스크롤 전개·링 회전) | 코드 완료·육안 미검증 |
| `Runtime/UI/BattleHud.cs` | 전투 HUD | 터치 조이스틱+4버튼(누름 즉시), 키보드 J/K/L/Space/1~4, AUTO 표시 전용 | 코드 완료·실기기 미검증 |
| `Editor/LuminaProjectSetup.cs` | 클린 체크아웃 설정 | 처음 열면 Boot 씬·빌드 목록·가로 고정·URP 에셋 자동 생성 | 코드 완료·실행 미검증 |

## 4. 다음 단계(기능별 완료 기록 순서)

1. **Unity 실행 검증(지금 1순위)**: Windows에서 열기 → Play → 3스테이지 클리어·자동전투 해금·저장 재시작 확인, 콘솔 에러 0, 16무기 청음 피드백.
2. 전투 손맛 2차: 무기별 히트스톱/경직/카메라 튜닝, 이펙트 풀링, 피격 반응 애니메이션, 사운드 버스(SFX/UI/BGM) 분리.
3. 9속성/13반응 심화: 상태이상(화상·침수…) 지속/내성 — 수치 승인 후.
4. 16인 연결: D-01 자료 수령 → `CharacterDef` 교체(시험체는 무기 테스트용으로 유지).
5. 성장(레벨·돌파·스킬) → 전용무기 + 유물 +15(D-03) → 가챠(D-02, 캐릭터/무기 픽업 상태 분리) → 25개 화면 확장 → 콘텐츠.
