# 검증결과 목록

| 파일 | 내용 | 상태 |
|---|---|---|
| [BootFixVerification.md](BootFixVerification.md) | Boot 한정 수정 내용과 수정 후 검증 | 정적 검사 통과·사용자 정상 작동 확인, 에이전트 상세 Play 재검증 미수행 |
| `BootFixEvidence.jsonl` | 로컬 전용: Boot 수정·검증의 Unity 응답 | 공개 업로드 제외 |
| `BootFixFileIntegrity.json` | 로컬 전용: 수정 전후 파일·GUID 대조 | 공개 업로드 제외 |
| [BootIsolatedResult.json](BootIsolatedResult.json) | 별도 검증 사본 실행 결과 | 라이선스·IPC 연결 제한으로 실행 차단 |
| [RuntimeVerification.md](RuntimeVerification.md) | 최초 Boot·BaseCamp 진단 | 과거 기록; 현재 상태는 Boot 수정 보고서 참고 |
| `RuntimeEvidence.jsonl` | 로컬 전용: 최초 실행 진단의 Unity 응답 | 공개 업로드 제외 |
| `FileIntegrity.json` | 로컬 전용: 최초 진단 당시 파일 보존 확인 | 공개 업로드 제외 |

## 화면 목록

`화면` 폴더는 실행 화면을 보관한다. `BootInitial.png`, `BootScreen.png`, `BaseCamp.png`는 수정 전 기록이다. 수정 후 화면은 `BootFixed*.png` 파일명으로 구분하고 현재 검증 보고서에서 설명한다.

실행 로그의 임시 조회 코드 오류와 게임 코드 오류를 구분한다. 검증하지 않은 입력·완주·성능을 통과로 기록하지 않는다.
