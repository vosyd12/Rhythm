# Boot 수정·검증 결과

기준일: 2026-10-28. 현재 범위는 [Boot 작업 범위](../작업관리/ProjectScope.md)를 따른다.

## 현재 상태

**Boot 수정 반영과 정적 검사 후, 사용자가 현재 프로젝트의 정상 작동을 확인했다(2026-10-28). 에이전트의 상세 Play 재검증은 자동화 연결 제한으로 미수행이며 사용자 확인과 구분한다.**

사용자 확인은 정상 작동에 대한 총괄 평가다. 아래의 개별 입력·완주·성능 항목을 에이전트가 측정해 통과한 것으로 바꾸지 않는다.

이번 작업에서는 BaseCamp·전투·캠페인을 실행하거나 개발하지 않았다. 최초 진단의 캠프 오류는 과거 기록으로 유지한다.

## 수정 목록

| 문제 | 반영한 변경 | 현재 검증 상태 |
|---|---|---|
| UI-01: TMP 설정 누락·글자 미표시 | 설치된 UGUI 패키지에서 `TMP Settings.asset`, 기본 스타일, 줄바꿈 데이터 2개와 원본 `.meta` 복원 | 설정의 모든 GUID 참조 5개 해석 확인; 실제 글자 표시·예외 해소는 재검증 대기 |
| SHADER-01: TMP include 누락 | `TMPro_Properties.cginc`, `TMPro.cginc`와 원본 `.meta` 복원 | 두 TMP 셰이더의 include 7개 모두 파일 해석 가능; GPU 셰이더 재컴파일 결과는 미검증 |
| PATH-01: 원본 프로젝트 절대 경로 의존 | Boot 기본 경로를 `Assets/Audio/Test/80 - 265.mp3`로 변경 | 상대 경로에 실제 MP3 존재 확인; 별도 Player 빌드의 파일 배포 방식은 아직 범위 밖 |
| PATH-01의 저장 경로 복원 | `LocalMusicLoader.Awake()`가 마지막 경로를 복원하기 전에 `File.Exists` 검사 | 삭제된 이전 경로가 새 기본값을 덮어쓰지 않는 조건 확인; 실행 회귀는 대기 |

## 복원 방식과 변경 범위

- 새 패키지를 설치하거나 버전을 바꾸지 않았다. 현재 설치된 `com.unity.ugui`의 `TMP Essential Resources.unitypackage`에서 Boot에 필요한 누락 파일 **6개**만 복원했다.
- 기본 스타일용 폴더 1개와 각 리소스의 원본 `.meta`를 함께 추가했다. 기존 폰트·머티리얼·TMP 셰이더 파일은 덮어쓰지 않았다.
- 복원한 TMP 설정에서 현재 존재하지 않는 기본 스프라이트 GUID는 `null`로 비웠다. Boot가 사용하지 않는 이모지·샘플 리소스를 추가하지 않기 위한 변경이다. 나머지 설정은 설치된 패키지 원본과 일치한다.
- Boot 씬의 의도한 변경은 기본 음원 경로 1곳이다. `LocalMusicLoader.cs`의 의도한 변경은 저장 경로 유효성 조건 1곳이다. 해당 C# 파일은 기존 인코딩과 개행·비ASCII 바이트를 보존했다.
- 기존 파일 323개 기준으로 삭제 0개, 기존 `.meta` GUID 변경 0개다. `Packages`·`ProjectSettings`의 기존 파일은 변경하지 않았다.
- 작업 중 `Assets/TextMesh Pro/Examples & Extras`와 `Assets/Fonts/NanumGothic-Regular SDF.asset`의 별도 추가·변경이 관측됐다. 이번 에이전트 수정과 구분하고 보존했다. 그 자료를 임의로 삭제하거나 되돌리지 않았다.
- 수정 전 사본과 해시는 `Temp/수정백업/Boot-20261028`에 확보했다. Unity의 `Library`·기존 사용자 저장 파일은 백업 대상으로 복제하거나 수정하지 않았다.

## 통과한 정적 검사

1. `TMP Settings.asset`과 기본 스타일·줄바꿈 데이터의 GUID가 현재 Assets 또는 설치된 UGUI의 `.meta`로 해석된다.
2. 복원한 6개 리소스의 GUID가 설치된 패키지 원본과 일치한다.
3. TMP 설정의 의도한 스프라이트 참조 변경을 제외한 복원 파일은 설치된 패키지 원본과 동일하다.
4. `TMP_SDF.shader`·`TMP_SDF-Mobile.shader`의 TMP include와 Unity 기본 include를 모두 찾을 수 있다.
5. 원래 리소스와 씬·스크립트의 기존 GUID가 보존됐다. 기본 샘플 경로에 파일이 존재한다.
6. `LocalMusicLoader.cs`에서 저장 경로 조건 외 바이트가 바뀌지 않았음을 원본 백업과 대조했다.

이 검사들은 실제 C# 재컴파일, 셰이더 GPU 컴파일 또는 Play 검증을 대신하지 않는다.

## 실행 검증 시도와 제한

- 수정 전에는 원본 에디터의 Boot 편집 모드와 저장되지 않은 씬 변경 없음까지 확인했다.
- TMP 파일 복원 후 동기 에셋 새로고침을 요청했다. 원본 에디터가 리소스·스크립트 갱신 중 자동화 서버 포트를 다시 열었지만 이후 HTTP 요청에 응답하지 않았다. 성공 여부를 알 수 없는 Play 명령을 반복해서 보내지 않았다.
- 원본 에디터의 재시작·강제 종료는 수행하지 않았다. 현재 Play 상태·텍스트 메시·콘솔을 새로 확인했다고 주장하지 않는다. 사용자에게 에디터 재시작을 요청한 상태다.
- 원본 작업을 보호하기 위해 현재 파일의 별도 검증 사본을 `Temp/실행검증/독립검증`에 만들었다. 검증 사본만 회사·제품 이름을 달리해 PlayerPrefs를 분리했다.
- 첫 별도 실행은 Package Manager IPC에 연결하지 못했다. 기존 패키지 캐시를 복사하고 `-noUpm`으로 다시 실행했지만 Unity Licensing Client IPC·라이선스 초기화 단계에서 막혔다. **Boot 검증 코드의 실행 결과는 생성되지 않았다.**
- 별도 검증용으로 띄운 Editor 프로세스만 종료했다. 원본 에디터는 종료하지 않았다. 현재 환경에서는 유효한 수정 후 실행 캡처를 확보하지 못했다.
- 자동화 연결을 복구하기 위해 작성한 임시 Editor 도구는 최종 `Assets`에서 제거하고 `Temp` 아래로 옮겼다. 게임 실행 코드에 연결 복구 기능을 남기지 않았다.

Unity의 `-noUpm`은 Package Manager를 비활성화하는 옵션이다. 검증 사본의 실행 옵션으로만 사용했고 원본 설정에는 적용하지 않았다. [Unity 공식 명령행 문서](https://docs.unity3d.com/6000.0/Documentation/Manual/EditorCommandLineArguments.html)

## 연결 복구 후 재검증할 목록

- [ ] C# 재컴파일 오류·Missing Script 확인.
- [ ] Boot 초기 화면의 한글·영문 버튼과 상태 글자 표시 확인.
- [ ] 두 TMP 셰이더의 컴파일 오류가 0개인지 확인.
- [ ] 존재하지 않는 파일·지원하지 않는 확장자 거부 후 정상 MP3 재시도.
- [ ] 저장 경로가 없는 경우와 삭제된 경우에 Boot 기본 샘플 경로가 사용되는지 확인.
- [ ] BPM 후보 전환, 정상 시작 버튼, Tap·Hold·Sync 입력, 일시정지·재개 확인.
- [ ] Boot 안에서 곡 종료·정산까지 확인. BaseCamp 이동 이후는 현재 범위 밖으로 유지.

## 증거 목록

- `BootFixFileIntegrity.json`: 로컬 전용 파일·GUID·include·TMP 설정 참조 대조. 공개 업로드 제외.
- `BootFixEvidence.jsonl`: 로컬 전용 원본 에디터 요청·연결 실패 기록. 공개 업로드 제외. 성공한 Play 검증 로그가 아니다.
- [BootIsolatedResult.json](BootIsolatedResult.json): 별도 검증 사본의 환경 차단 상태.
- [RuntimeVerification.md](RuntimeVerification.md): 수정 전 실제 실행에서 확인한 증상. 현재 해결 상태와 혼동하지 않는다.

`화면` 폴더의 기존 이미지는 모두 수정 전 기록이다. 수정 후 이미지로 재사용하지 않는다.
