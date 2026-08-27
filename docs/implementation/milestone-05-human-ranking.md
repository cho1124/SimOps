# 마일스톤 5: 익명 플레이어·Ticket·시즌 랭킹

상태: 구현·API/Windows 통합 검증 완료, 수동 화면·Android 실기기 QA 대기 (2026-08-27)

## 구현 범위

- 익명 credential은 256-bit 난수로 발급하며 DB에는 SHA-256 hash만 저장한다. 닉네임은 1~24자의 문자·숫자·공백·`_`·`-`로 제한한다.
- 운영자와 플레이어 경로를 분리한다. Unity에는 운영자 키와 Ticket 서명 키를 배포하지 않는다.
- HMAC-SHA256 서명 Ticket은 Player·Season·Core artifact·Config·Score Rule·서버 Seed·nonce·만료를 고정한다. 유효기간은 최대 2시간이다.
- Begin과 Submit 모두 멱등성을 지원한다. 같은 제출의 응답을 잃어버려도 기존 Run을 조회하며, 다른 key로 Ticket을 재사용할 수 없다.
- Ticket 소비, Run/Action 생성, 검증 Job 등록은 같은 트랜잭션이다.
- Worker가 검증한 인간 Run만 개인 최고 기록 후보가 된다. 최고 Run 갱신과 검증 완료는 같은 트랜잭션이다.
- 점수 → Stage → 적은 Turn → 높은 HP 비율 → 먼저 검증된 Run 순서로 비교한다. 전체 조회의 최종 안정 정렬은 Player ID다.
- 종료 시즌은 동결한다. 시즌 컨텍스트 수정과 합성·거부 Run의 랭킹 삽입을 DB trigger로도 막는다.
- 전체 상위·페이지 조회·인증된 내 주변·내 최고 기록을 반환한다. 타인의 private Run status는 읽을 수 없다.
- Unity에서 Ranked Run 시작·결과 제출·검증 대기·상위 랭킹/내 순위 확인을 지원한다. Ticket과 동일 제출 key를 Replay 파일에 함께 저장한다.

## 재현 명령

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Run-Milestone5.ps1
```

이미 최신 Windows/Android 빌드가 있다면 `-SkipClientBuild`로 백엔드와 Windows 온라인 smoke를 실행한다. 스크립트는 로컬 DB에 테스트 기록을 추가하며, 자신이 시작한 API·Worker·Player만 종료한다. PostgreSQL volume과 기록은 보존한다.

직접 플레이하려면 Docker Desktop 실행 후:

```powershell
docker compose -p simops up -d --wait postgres
# 별도 터미널 1
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/SimOps.Api -c Release -- --urls http://127.0.0.1:5080
# 별도 터미널 2
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project src/SimOps.Worker -c Release
```

이후 `artifacts/unity/windows/SimOps.exe`를 실행한다. Android는 APK 설치 후 개발용 USB 연결에서 `adb reverse tcp:5080 tcp:5080`으로 같은 로컬 API에 접근한다. 기기 연결·설치·터치 검증은 별도이며 자동으로 수행했다고 간주하지 않는다.

## 공개 계약

| Method | Path | 인증 |
|---|---|---|
| POST | `/api/v1/player/register` | 없음, rate limit |
| GET | `/api/v1/public/seasons/active` | 없음 |
| POST | `/api/v1/player/tickets` | Player Bearer credential |
| POST | `/api/v1/player/runs` | Player Bearer credential |
| GET | `/api/v1/player/runs/{id}` | 해당 Player만 |
| GET | `/api/v1/public/seasons/{id}/leaderboard` | 전체 공개, `around=true`는 Player 인증 |

페이지 파라미터: `offset >= 0`, `limit 1..100`. Public ranking은 닉네임·Player ID·최고 Run ID·점수 정보만 반환하며 Ticket/credential은 포함하지 않는다.

## 검증과 남은 한계

- HTTP 4건: 플레이어 전체 흐름, 6건 동시 Begin/Submit, 변조·타인 Ticket 거부, Core checksum·schema 거부.
- DB 6건: 낮은 점수 유지, 동점 규칙/동시 완료, 합성·거부 제외, 시즌 동결, 시즌 불변성, 만료 Ticket.
- Backend 11건(새 DB의 migration·catalog 초기화 포함) + Core 13건 + Agent 5건 회귀를 함께 실행한다. 새 DB 검증은 프로젝트의 localhost:54329에서만 실행하며 테스트가 만든 임시 DB만 제거한다.
- 최종 실행: 39개 자동 Spec 통과, Windows Player의 실제 Ticket → 플레이 → API → Worker → 랭킹 smoke 통과. Unity Golden Hash 유지, Windows/Android Development Build 성공. 최종 Android APK는 66,772,201 bytes이며 symbol/intermediate를 포함한 Unity Build Report 크기와 구분한다.
- 숨김 창/배치 실행에서 정상 화면 캡처를 확보하지 못했다. UI 객체와 1600×900 layout bounds는 확인했지만 화면 표시·입력·Safe Area가 정상이라는 시각 QA 증거는 아니다. 두 플랫폼의 수동 화면 QA를 완료로 표시하지 않는다.
- 동점 DB 검증은 게임 난수에 의존하지 않는 trusted-worker 결과 fixture를 사용한다. HTTP와 Unity E2E 검증은 실제 Game Core 결과를 제출한다. 테스트용 인간 경로의 자동 플레이는 실제 인간 데이터가 아니다.
- Unity 익명 credential은 현재 개발용 PlayerPrefs에 저장한다. 실서비스 보안 저장소·복구·다기기 이전·계정 제재 UX는 아직 없다. 익명 credential을 잃으면 계정도 복구할 수 없다.
- 개발 빌드에서만 HTTP를 허용한다. 공개 배포 전 HTTPS, 별도 키 설정·회전, 등록/조회 abuse 방지, 신뢰 경계 재검토가 필요하다.
- 초기 로컬 baseline 시즌은 종료 시각이 없는 개발 세션이다. 실제 시즌 기간·게시·롤백은 M8에서 명시적으로 처리한다.

## Core artifact 재현성과 데이터 보존

동일 Game Core 소스인데 Git HEAD가 assembly metadata와 SourceLink에 포함되어 DLL checksum이 바뀌는 문제를 발견했다. Core만 Git metadata 포함을 해제하고 소스 경로를 고정했다. 같은 SDK·Release 설정으로 만든 패키지의 checksum을 Unity Resource에 포함하고 Ticket과 비교한다. SDK 또는 실제 Core 변경 시에는 새로운 artifact/시즌이 필요하다.

`SourceRevisionId`를 다른 값으로 바꾸어 Rebuild해도 DLL checksum이 유지되는 것을 확인했다: `0f0bb340e522605ecd54ce231b143a14b91c861881e98e5cb8224e139d0b9d2b`.

초기 로컬 테스트 시즌 `...0001`의 컨텍스트를 덮어쓰지 않고 migration 003에서 종료했다. 해당 Ticket·Run·랭킹은 보존하며 안정화한 artifact는 새 시즌 `...0002`에서 사용한다. 이는 공개 서비스 운영 변경이 아니라 이번 구현 중 생성한 개발용 데이터의 일회성 migration이다.

화면 QA 시도 중 런타임 생성 PanelSettings의 Theme 누락 경고를 발견했다. [Unity PanelSettings 문서](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.PanelSettings-themeStyleSheet.html)에 맞춰 Theme와 PanelSettings를 빌드 자산으로 포함하고 경고가 사라진 것을 확인했다. UI 전용 카메라·루트 크기도 명시했다. 숨김 창 캡처는 검은 화면이었고 배치 모드에서는 새 캡처가 생성되지 않아 시각 QA 근거로 사용하지 않는다. 정상 게임 화면 여부는 사용자가 보이는 창에서 확인해야 한다.
