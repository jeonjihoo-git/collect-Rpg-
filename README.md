# LUMINA CHRONICLE (루미나 크로니클) — 재구축 저장소

모바일 **4인 파티 실시간 액션** 캐릭터 수집형 RPG를 인계 문서(`docs/handoff/`) 기준으로 새로 만드는 Unity 프로젝트입니다.
기존 소스/APK/에셋은 인계 패키지에 없어서 **새 프로젝트로 시작**했습니다. 현재는 마일스톤 1(수직 슬라이스) 단계입니다.

- 현재 상태와 근거: [`docs/STATUS.md`](docs/STATUS.md)
- 임시 가정값·승인 요청 목록: [`docs/ASSUMPTIONS.md`](docs/ASSUMPTIONS.md)
- 구조·파일별 계획·수용 기준: [`docs/VERTICAL_SLICE.md`](docs/VERTICAL_SLICE.md)

> 캐릭터는 전부 **검증용 임시 캐릭터 16체**(무기 1:1)입니다. 정식 16인(세레나 등)의 속성·역할 데이터가 없어서 실제 이름에 수치를 지어내지 않았습니다.
> 모델·애니메이션은 도형 + 절차 포즈, 효과음은 무기별 절차 합성으로 만든 **임시 자산**입니다.

## 1. Windows에서 실행 (Unity)

1. Unity Hub에서 **Unity 6 LTS(6000.0.x)** 를 설치하고, Android 빌드가 필요하면 *Android Build Support* 모듈도 함께 설치합니다.
2. Unity Hub → **Add → Add project from disk** → 이 저장소 폴더를 선택합니다. 다른 6000.x 버전으로 열어도 됩니다(버전 변경 확인 창이 뜨면 진행).
3. 처음 열면 `Lumina` 자동 설정이 한 번 실행됩니다. 이때 `Assets/Scenes/Boot.unity`, 빌드 목록, 가로 화면 고정, URP 에셋이 생성됩니다.
   - 자동 설정이 실행되지 않으면 메뉴 **Lumina → 1. 프로젝트 설정 실행**을 누르세요.
4. **Play**를 누르면 로비가 뜹니다. 씬 설정이 없어도 아무 씬에서 Play하면 GameApp이 자동 생성됩니다.
5. 처음 열고 나서 Unity가 생성한 `.meta` 파일을 커밋하면, 다른 PC에서도 같은 GUID를 쓰게 됩니다.

조작
| 동작 | 키보드 | 터치 |
|---|---|---|
| 이동 | WASD / 방향키 | 좌하단 조이스틱 |
| 일반 공격(누르고 있으면 콤보) | J | 공격 |
| 스킬 / 궁극기 / 회피 | K / L / Space | 스킬 / 궁극기 / 회피 |
| 태그(교대) | 1~4 | 좌측 파티 초상 탭 |
| 일시정지 | Esc | II |

확인 순서: 로비 → **모험** → 검증 1 → **전투 준비** → 자동전투 버튼이 "잠금"인지 확인 → 출전·클리어 → 같은 스테이지 편성 화면에서 자동전투 ON → 재출전.
저장 위치는 `Application.persistentDataPath/lumina_save.json`입니다. 초기화하려면 로비의 개발 패널이나 메뉴 **Lumina → 2**를 쓰세요.

Android 빌드: *File → Build Profiles → Android → Switch Platform → Build*. 화면 방향은 가로로 고정되어 있습니다.
URP 패키지 버전 오류가 나면 *Window → Package Manager*에서 Universal RP를 에디터 권장 버전으로 업데이트하세요.

## 2. Unity 없이 검증 (Windows/Mac/Linux, .NET 8 SDK)

```bash
dotnet test tools/Lumina.Core.Tests                       # 코어 규칙 테스트 76개 (QA 체크리스트 매핑)
dotnet run --project tools/Lumina.Sim -- flow             # 로비→스테이지→편성→전투→결과→저장 헤드리스 전체 흐름
dotnet run --project tools/Lumina.Sim -- sfx sim_out/sfx  # 16무기 효과음 WAV + 실제 전투 타이밍 쇼케이스
dotnet run --project tools/Lumina.Sim -- ttk              # 무기별 처치 시간 표
dotnet build tools/UnityCompileCheck/Player/UnityCompileCheck.Player.csproj   # Unity 런타임 코드 컴파일 검사
dotnet build tools/UnityCompileCheck/Editor/UnityCompileCheck.Editor.csproj   # 에디터 스크립트 컴파일 검사
```

`Assets/Lumina/Tests/EditMode`의 테스트는 Unity *Test Runner(EditMode)* 에서도 같은 파일 그대로 실행됩니다.

## 3. 구조 요약

```
Assets/Lumina/Core       순수 C# 규칙(데이터·전투·저장·효과음 합성) — Unity·dotnet 공용
Assets/Lumina/Runtime    Unity 화면·연출·입력·오디오
Assets/Lumina/Editor     자동 설정·WAV 내보내기
Assets/Lumina/Resources/LuminaData   정의 데이터(JSON, status=Confirmed/Prototype/Temp)
Assets/Lumina/Tests      공용 NUnit 테스트
tools/                   dotnet 테스트·헤드리스 도구·Unity 컴파일 검사
docs/                    상태·가정값·계획, handoff 원문
```

## 4. 원칙 (인계 문서 준수)
- 방치형이 아닌 **실시간 조작형 4인 파티** 전투를 기준으로 합니다.
- 장비는 **전용무기 + 4부위 속성 유물 +15** 구조를 따르고, 구판 5부위 +5는 쓰지 않습니다(유물·가챠는 아직 미구현).
- 자동전투는 **파티 편성 화면**에서 켜고 끄며, `ClearRecord(stageId)`에 클리어 기록이 있는 스테이지에서만 쓸 수 있습니다. 일반공격·스킬·회피·궁극기를 모두 자동으로 씁니다.
- 명중음·피격 연출은 피해가 들어간 그 틱의 `Hit` 이벤트로만 재생합니다. 모든 무기에 같은 징이나 종소리를 쓰지 않습니다.
- 서버, 결제, PvP, 협동 기능은 **구현되어 있지 않습니다**. 저장은 기기 로컬 파일입니다.
