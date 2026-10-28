# Unity 실행 검증 결과

> 최초 진단의 과거 기록이다. 현재 작업 범위는 Boot로 한정하며, 수정 후 상태는 [BootFixVerification.md](BootFixVerification.md)를 참고한다.

- 검증일: 2026-10-28 (KST)
- 프로젝트: `C:/Users/PC/Rhythm`
- Unity: `6000.5.10f1`, Windows Editor, DX12
- 범위: 실제 Play 모드의 Boot·BaseCamp 실행, 음원 로드, BPM 분석, 초기 패턴 생성, UI·콘솔·셰이더 확인
- 상태: **진단 완료, 결함 수정 미수행. 전체 리듬 플레이 검증 완료를 의미하지 않음.**

## 핵심 결과

프로젝트는 Play 모드에 진입하며, 포함된 MP3의 로드와 BPM 분석도 동작한다. 그러나 TextMeshPro 필수 리소스가 빠져 글자가 표시되지 않는다. BaseCamp에서는 TMP 클리핑 중 `NullReferenceException`도 발생했다. 따라서 현재 상태를 정상 플레이 가능한 복원본으로 판정할 수 없다.

## 확인된 문제

### UI-01 / 높음 — TMP 설정 누락과 텍스트 초기화 중단

**재현 방법**

1. `Assets/Scenes/Boot.unity`를 열고 Play를 실행한다.
2. 음원 선택·불러오기·시작 버튼과 상태 안내를 확인한다.
3. BaseCamp를 실행하면 캠프 UI에서도 같은 현상이 나타난다. 이번 검증의 BaseCamp 이동은 일반 정산 버튼이 아닌 테스트용 씬 로드로 수행했다.

**실제 결과**

- 버튼 배경·패널은 보이지만 글자가 표시되지 않는다.
- Boot의 활성 TMP 텍스트 16개에서 문자·메시 버텍스 수가 모두 0이었다.
- BaseCamp의 활성 TMP 텍스트 44개 중 문자 지오메트리를 가진 항목은 0개였다.
- `Resources.Load<TMP_Settings>("TMP Settings")`가 `null`이다.
- BaseCamp 진입 후 다음 예외가 2건 기록됐다.

```text
NullReferenceException: Object reference not set to an instance of an object
TMPro.TextMeshProUGUI.Cull (... TextMeshProUGUI.cs:399)
UnityEngine.UI.RectMask2D.PerformClipping (...)
```

**원인 판단**

현재 `Assets/TextMesh Pro/Resources/TMP Settings.asset`이 없다. 설치된 UGUI 패키지의 `TextMeshProUGUI.Awake()`는 Editor에서 TMP 설정이 없으면 초기화를 마치기 전에 반환한다. 이 코드와 관측 결과가 일치하므로 TMP 필수 리소스 누락을 우선 원인으로 판단한다. 리소스를 복원하는 비교 실험은 수행하지 않았으므로, 복원 후 글자와 예외를 다시 확인해야 한다.

**조치 제안**

Play를 종료한 상태에서 현재 설치된 UGUI/TMP 버전에 맞는 Essential Resources를 복원한다. 기존 폰트·머티리얼·씬의 GUID와 참조를 대조하고, Boot와 BaseCamp에서 텍스트 메시 생성 및 예외 해소를 재검증한다.

증거: [Boot 초기 화면](화면/BootInitial.png), [음원 로드 후 실제 Game 화면](화면/BootScreen.png), [BaseCamp 실제 Game 화면](화면/BaseCamp.png).

### SHADER-01 / 높음 — TMP 셰이더 include 파일 누락

**재현 방법**

현재 프로젝트에서 아래 두 셰이더를 대상으로 `UnityEditor.ShaderUtil.GetShaderMessages()`를 조회한다.

| 파일 | 셰이더 | 확인된 오류 |
|---|---|---|
| `Assets/TextMesh Pro/Shaders/TMP_SDF.shader` | `TextMeshPro/Distance Field` | `Couldn't open include file 'TMPro_Properties.cginc'.` |
| `Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader` | `TextMeshPro/Mobile/Distance Field` | 동일 오류 |

**실제 결과와 영향**

두 파일 모두 누락된 `TMPro_Properties.cginc`를 참조한다. 일반 SDF 셰이더가 추가로 참조하는 `TMPro.cginc`도 현재 셰이더 폴더에 없다. 두 셰이더는 버튼·상태 안내용 폰트와 판정 텍스트 머티리얼에서 실제 사용 중이다. TMP 설정만 복원해도 셰이더 의존성이 누락된 상태는 별도로 남을 수 있다.

**조치 제안**

UI-01과 함께 TMP Essential Resources의 셰이더 의존 파일을 복원하고 두 셰이더의 오류가 사라지는지 확인한다. 기존 커스텀 판정 머티리얼을 임의로 교체하거나 GUID를 재생성하지 않는다. 이번 검증에서는 복원을 수행하지 않았다.

## 이식 시 확인할 위험

### PATH-01 — Boot의 기본 음원 경로가 원본 프로젝트에 의존

Boot 씬에 직렬화된 `LocalMusicLoader.filePath`는 다음 경로다.

```text
C:/Users/PC/RhythmDefense/Assets/Audio/Test/PIKASONIC  Couple N - Shine.mp3
```

현재 PC에는 해당 파일이 존재하므로 이번 실행에서 이 경로 때문에 실패했다고 판정하지 않는다. 다만 원본 프로젝트를 삭제하거나 다른 PC로 옮기면 기본 경로를 사용할 수 없게 된다. 저장된 마지막 선택 경로가 기본값을 덮어쓸 수도 있다. 이번 정상 음원 검증에는 현재 프로젝트 안의 `Assets/Audio/Test/80 - 265.mp3`를 명시적으로 사용했다.

## 실제 수행한 검증

| 항목 | 결과 | 근거·범위 |
|---|---|---|
| Boot Play 진입·종료 | 통과 | 실제 Editor Play 실행 및 종료 응답 확인 |
| 현재 C# 컴파일 상태 | 오류 미관측 | Play 진입 성공, `compiling=false`; 재컴파일 요청은 `up_to_date` 응답. 전체 강제 재빌드는 아님 |
| Missing Script | 0개 | Boot와 BaseCamp에 로드된 GameObject 전체에서 조회 |
| 정상 MP3 로드 | 통과 | `IsAudioLoaded=true`, `HasLoadFailed=false` |
| BPM·위상 분석 | 통과 | 후보 3개와 위상 계산 완료, 선택 후보 약 149.996 BPM |
| BPM 후보 전환 | 통과 | 후보 0→1 전환, 약 75.000 BPM 확인 후 후보 0으로 복귀 |
| 초기 패턴 생성 | 부분 통과 | 실제 `TryPrepareForGameplay()` 호출 후 초기 Hold 1개·Tap 1개 생성. ID 중복 없음, 시간 정렬·곡 길이 범위 확인 |
| 없는 파일 입력 | 예상대로 거부 | 실패 상태·안내 문구 설정, `CanLoad=true`로 재시도 가능 |
| 지원하지 않는 확장자 | 예상대로 거부 | 기존 `.md` 파일로 검증; WAV·OGG·MP3만 허용한다는 경고 확인 |
| 실패 후 정상 MP3 재시도 | 통과 | 로드 성공 및 BPM 후보 3개 생성 확인 |
| BaseCamp 직접 실행 | UI 결함 확인 | 씬 로드·숲 채집 프리팹 연결 확인, TMP 예외 2건·텍스트 미표시 |
| 프로젝트 자동 테스트 검색 | 0개 | Unity Test Runner 조회: `Found 0 test(s).` 테스트 실패를 뜻하지 않음 |

초기 패턴 검증은 게임 UI의 시작 버튼을 통과한 시험이 아니라 공개 패턴 준비 함수를 직접 호출한 시험이다. 전체 곡 패턴·Sync 생성·입력 판정의 정상 동작까지 확장해 해석하지 않는다.

## 환경과 미검증 범위

- `http://127.0.0.1:8099/mcp` 서버는 응답했으나 연결된 Unity 인스턴스가 0개였다. 실제 실행 검증에는 이미 설치된 Unity Pipeline API를 사용했다. MCP 재연결 설정은 변경하지 않았다.
- 자동화 중 `Application.isFocused=false`였다. 게임 코드가 이에 따라 음악·월드 진행을 일시정지하고 시작 버튼을 비활성화했다. 이 동작 자체를 결함으로 분류하지 않는다.
- 에디터의 Game 창 포커스 요청 후에도 위 값은 false였고, 네이티브 화면 캡처도 타임아웃됐다. 포커스 검사를 우회하는 코드 변경은 하지 않았다.
- 따라서 실제 시작 버튼을 통한 재생 시작, 좌·우·Space 입력 판정, Hold 유지·해제, 전체 곡 완주, 보상·정산 버튼을 통한 복귀는 **미검증**이다.
- WAV·OGG, 무음·손상 음원, 다른 해상도, 빌드 실행, 오디오 청취·싱크 체감·프레임 성능도 미검증이다.
- 검증 중 `Cannot import package in play mode` 오류 1건도 관측됐다. TMP 리소스 가져오기 창에서 발생한 Editor 작업 오류이며, 게임 코드 예외와 구분한다. 검증 에이전트가 패키지 가져오기를 실행한 것은 아니다.
- 증거 JSONL에는 임시 조회 코드의 잘못된 속성명·인수형 때문에 실패한 `eval` 응답도 포함돼 있다. 이는 게임 소스의 C# 컴파일 오류가 아니며 위 문제 집계에서 제외했다. 이전 사용자 실행의 판정 로그도 이번 입력 검증 통과 근거로 사용하지 않았다.

## 종료 상태와 파일 보존

- 종료 시 Play 모드는 꺼져 있고, `Boot` 씬이 열려 있으며 저장되지 않은 변경은 없다.
- 검증 중 백그라운드 프레임 처리를 위해 런타임의 `Application.runInBackground`를 잠시 true로 설정했다가 원래 false로 복원했다. 게임 소스·프로젝트 설정에는 반영하지 않았다.
- 검증 전후 `Assets`, `Packages`, `ProjectSettings`의 기존 파일 **323개**를 SHA256으로 비교했다. 변경·삭제·최종 추가 파일은 모두 0개다. 캡처 명령이 잠시 생성한 에셋은 `Assets` 밖으로 옮겨 정리했다.
- 음원 로드 시험은 기존 로더의 마지막 경로 저장 동작을 실행하므로 마지막 선택 음원은 현재 프로젝트의 `80 - 265.mp3`가 될 수 있다. 음량 등 다른 사용자 설정은 조작하지 않았다.
- 게임 코드·씬·프리팹·패키지·GUID를 수정하지 않았다. 삭제된 기획·마일스톤 문서는 열거나 재생성하지 않았다.

## 증거 파일

- `RuntimeEvidence.jsonl`: 로컬 전용 실행 명령, UTC 시각, Unity의 실제 응답. 인증 헤더·토큰은 기록하지 않는다. 공개 업로드 제외.
- `FileIntegrity.json`: 로컬 전용 검증 전후 파일 비교 결과. 공개 업로드 제외.
- `화면/*.png`: 확인한 실행 화면. `BootInitial.png`는 카메라 렌더 캡처이며, `BootScreen.png`·`BaseCamp.png`는 Game 화면 캡처다.

권장 후속 순서는 TMP 필수 리소스 복원 → 두 씬의 UI·예외 재검증 → 실제 포커스를 확보한 리듬 입력·완주·정산 검증이다.
