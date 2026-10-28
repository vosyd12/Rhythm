# 참고자료 목록

외부 리소스 고지와 로컬 원본 복원 참고 기록을 분류한 폴더다. 과거 경로·기획·작업 상태를 현재 지시로 적용하지 않는다.

| 파일 | 내용 |
|---|---|
| [ThirdPartyNotices.md](ThirdPartyNotices.md) | 외부 폰트·TMP·패키지의 사용 위치와 출처 |
| [라이선스 목록](라이선스/README.md) | 외부 자료의 원본 라이선스 고지 |
| `LegacyArchiveReadme.md` | 로컬 전용: 기존 리듬 보관본의 원래 설명 |
| `manifest.json` | 로컬 전용: 원본 스냅샷 317개 파일의 크기·SHA256 |
| `external-guid-references.json` | 로컬 전용: 보관 당시 외부 GUID 참조 조사 |
| `source-git-status.txt` | 로컬 전용: 원본 보관 당시 Git 작업 상태 |
| `Verify-Archive.ps1` | 로컬 전용: 원래 `Snapshot` 폴더용 검증 도구 |

현재 프로젝트에는 원래의 `Snapshot` 폴더가 없으므로 `Verify-Archive.ps1`은 현재 프로젝트 검증용으로 실행하지 않는다. 현재 수정·검증 결과는 [검증결과](../검증결과/README.md)에서 관리한다.

로컬 전용 자료는 공개 저장소에서 제외한다. 외부 리소스의 원본 고지는 공개 소스에 함께 포함한다.
