# Unity WebGL 프로토타입

2026-08-27. [ADR-0015](../decisions/0015-web-prototype.md)에 따라 기존 Unity 클라이언트를 브라우저에서도 실행하도록 확장했다. Windows·Android, React 운영 화면, .NET API·Worker·PostgreSQL은 유지한다. 외부 공개 배포는 하지 않았다.

## 연결 구조

```text
브라우저 Unity WebGL ── 정적 파일 ── 로컬 게임 호스트 :5174
         └── /api ──────────────── 플레이어 전용 프록시
                                      ↓
                                 .NET API :5080
                                      ↓
                                 PostgreSQL ← Worker 재실행 검증

React 운영 대시보드 :5173 ── 기존 운영 API 경로
```

Game Core가 WebAssembly에서 실행될 뿐, 브라우저가 DB나 Worker를 직접 실행하지 않는다. 웹 게임 호스트는 새로운 비즈니스 서버가 아니라 정적 파일/동일 출처 요청을 연결하는 로컬 개발 도구다. API·Worker 실행 위치와 계정·검증·랭킹 계약을 바꾸지 않았다.

## 실행

필요 환경: Unity 6000.3.9f1 + Web Build Support, .NET SDK 10.0.101, Node.js 24.12.0. 랭킹에는 Docker Desktop의 기존 SimOps PostgreSQL과 API·Worker가 필요하다.

```powershell
# 첫 실행: 서버 솔루션을 준비하고 로컬 실험실 실행. 이미 실행 중이면 중복 실행하지 않는다.
powershell -ExecutionPolicy Bypass -File scripts/Start-LocalLab.ps1

# 별도 터미널: Web 빌드 후 게임 호스트 실행
powershell -ExecutionPolicy Bypass -File scripts/Run-WebClient.ps1

# 이후 빌드 생략
powershell -ExecutionPolicy Bypass -File scripts/Run-WebClient.ps1 -SkipBuild
```

접속: [게임](http://127.0.0.1:5174/), [운영 화면](http://127.0.0.1:5173/). 터미널을 유지한다. 각각 Ctrl+C로 종료한다. 게임 호스트는 loopback에만 바인딩하며 외부 공개용 서버가 아니다.

빌드만 하려면 `scripts/Run-Milestone2.ps1 -Target WebGL`. `-Target All`의 기존 의미(Windows·Android)는 유지한다. Unity 메뉴 `SimOps/Automation/Build Web Prototype`도 같은 경로를 사용한다.

산출물: `artifacts/unity/web/`, 로그: `artifacts/unity/logs/build-web.log`. Release/IL2CPP + gzip이며 Wasm/JS/data에 맞는 MIME와 `Content-Encoding: gzip`을 제공한다. 빌드 산출물은 Git에 넣지 않는다. 폰트 라이선스는 `licenses/NotoSansKR/`에 포함한다.

## 변경한 경계

- `SimOpsOnlineClient`: Web에서 페이지 출처의 `/api`를 사용한다. 익명 Credential 키는 페이지 디렉터리별로 구분한다. 네이티브 주소와 기존 계정 키는 유지한다.
- Web HTML 템플릿: 로딩 진행, 실행 오류, 전체 화면 버튼, 가로 화면 권장, 브라우저 저장 정책을 표시한다. DPR은 최대 2로 제한한다.
- 좁은 화면의 행동·보상 카드는 한 열로 배치하여 줄바꿈 시 후속 안내와 겹치지 않게 한다. 가로 플레이를 권장하며 세로에서는 스크롤이 필요하다.
- `ReplayStore`: Web은 IDBFS의 닫힌 파일 쓰기를 통해 저장하고 템플릿의 `autoSyncPersistentDataPath`를 사용한다. 네이티브 임시 파일→복사 방식과 JSON 스키마는 유지한다.
- 빌드 패키징: Transport/Core 프로젝트만 빌드하여 실행 중인 API·Worker 출력 DLL을 건드리지 않는다. Client 표시 테스트는 별도로 빌드·실행한다.
- Node 로컬 호스트: 기본 포트 5174, 고정 loopback upstream 5080/5081, 플레이어/공개 API만 허용, 운영자 헤더 미전달, 1MiB 요청 제한, Host/Origin·경로 검증, API 불통 시 503을 반환한다.

## 저장과 보안 제한

최신 한 판만 보관하며 새 도전 전에 덮어쓰기 확인창을 표시한다. Web 저장은 브라우저·출처·배포 경로에 종속되므로 주소/경로를 유지해야 한다. 네이티브 계정 이전, 클라우드 세이브, 같은 경로의 동시 탭 플레이는 지원하지 않는다. 브라우저 데이터 삭제·저장 차단·프라이빗 모드·저장소 정리는 진행과 익명 계정의 손실을 유발할 수 있다.

IDBFS 반영은 비동기다. 행동 직후 강제 종료·브라우저 충돌까지 동기식 내구성을 보장하지 않으며 백그라운드에서 자동 제출하지 않는다. 게임 정적 호스트만 켜도 연습은 가능하지만 PWA/오프라인 캐시를 구현한 것은 아니다.

DB 비밀번호, Ticket 서명 키, 운영자·승인자 키를 Web 빌드에 넣지 않는다. 브라우저에는 본인의 익명 Credential과 플레이용 Ticket만 존재한다. 공개 배포 전에는 HTTPS 게이트웨이, 요청 제한/모니터링, 비밀 관리, 서버·DB 운영 정책을 별도로 검토해야 한다. 이 로컬 프록시를 그대로 인터넷에 노출하지 않는다.

## 검증 재현

```powershell
node --test scripts/web-server.spec.mjs
powershell -ExecutionPolicy Bypass -File scripts/Start-WebTestLab.ps1 -Minutes 30
```

Web 테스트 호스트는 이미 빌드된 API/Worker와 Web 산출물을 사용한다. 5081·5175가 비어 있어야 한다. 자동 테스트의 mock API도 5081을 사용하므로 두 명령은 동시에 실행하지 않는다.

출력된 UUID 경로로 브라우저를 열면 매번 별도 익명 계정/Replay 경로와 임시 DB로 플레이한다. 연습→랭킹 시작→전투/보상→결과 제출→검증 완료→시즌 랭킹→새로고침 복원을 확인한다. 타임아웃 또는 출력된 정확한 `.stop` 파일 생성 시 자신이 시작한 프로세스와 `simops_web_spec_<GUID>` DB만 정리한다. 로그는 `artifacts/web-test/`에 남는다. 일반 실험실·사용자 저장은 사용하지 않는다.

## 검증 기록

- Core 13건, Agent 5건, 실험 계산 9건, 표시 문구 9건 통과.
- Node Web 호스트 테스트 20건 통과(상위 2건 + 세부 18건): 압축 MIME, 공개/플레이어 프록시, 관리자 경로 차단, 헤더 필터링, 크기 제한, 경로·출처 검증, 격리 mount, API 불통 처리.
- Unity WebGL Release/IL2CPP + gzip 빌드 성공. 초기 디버깅용 약 64.6MB에서 약 12.8MB로 줄였다. 캐시·네트워크·기기별 실제 로딩 시간은 별도 측정 대상이다.
- 최종 Web 빌드 보고 크기는 12,810,005 bytes이며 `wasm.gz`는 7,232,944 bytes다.
- 격리 DB에서 브라우저 포인터 입력으로 6전투·보상 선택·회복·승리를 수행했다. 27개 행동, 87,566점, 11턴, HP 84/90의 결과를 Worker가 검증하고 시즌 랭킹 1위로 반영했다.
- 클라이언트와 서버 Result Hash가 `d0dc6011e8a6be3affc46e6739b866fa9574cb50a2ba2b371a9ca897650f6966`으로 같았다. 재제출 후에도 Player/Run/랭킹/완료 Job은 각각 1개였다.
- 랭킹 결과를 새로고침 후 복원하고 같은 익명 계정으로 검증 상태를 재확인했다. 브라우저 콘솔의 오류·경고는 없었다.
- 회귀 과정에서 연속 저장의 이전 행동 복원 및 연습 Replay의 빈 `onlineTicket` 처리를 확인했다. Web 직접 쓰기를 적용하고, 티켓 식별자가 전부 비어 있으면 오프라인 기록으로 정규화했다. JSON 왕복 검사를 Unity 검증 단계에 추가했다.
- 수정 후 연습 기록의 0개/1개 행동 복원, 키보드 공격 입력, 확인창 중 입력 차단과 Escape 취소를 실제 브라우저에서 확인했다.
- 같은 브라우저 계정으로 두 번째 랭킹 Ticket을 발급하고 진행 중인 1개 행동도 새로고침 후 복원했다. Player는 1개로 유지됐다.
- Unity Editor의 JSON 왕복 2건과 Seed 42 골든 검증, Windows x64 빌드/Player smoke, Android ARM64 APK 빌드가 통과했다. Android 실기기 실행은 이번에 수행하지 않았다.
- Windows 렌더 회귀는 1600×900, 1280×720, 1560×720에서 30개 상태 PNG와 경계/입력 검사를 통과했다. Android APK 실파일은 66,895,402 bytes다.
- 데스크톱 브라우저 1280×720 및 844×390/390×844 viewport에서 배치를 확인했다. 세로 화면의 줄바꿈 겹침은 한 열 카드로 수정했다. 작은 가로 화면은 페이지 스크롤 또는 전체 화면이 필요할 수 있으며 실제 모바일 터치 검증으로 간주하지 않는다.
- Game Core DLL SHA256은 `0f0bb340e522605ecd54ce231b143a14b91c861881e98e5cb8224e139d0b9d2b`로 유지됐다. 기존 사용자 Windows Replay의 작업 전후 SHA256도 동일했다.
- 검증 후 임시 DB와 테스트용 API/Worker/게임 호스트를 종료·정리했다. 실제 실험실 DB·시즌·승인·게시 설정은 변경하지 않았다. 일반 로컬 게임 호스트는 5174에서 별도로 실행했다.

## 남은 범위

- Android/iOS 실기기 브라우저의 터치, 메모리, 로딩, 백그라운드/복귀 품질. 데스크톱 화면 크기 변경은 실기기 검증이 아니다.
- 외부 공개 URL과 호스팅 계정 연결. 비용 0원·카드 등록 없음 조건은 그대로이며 이번에 새 계정·클라우드 리소스를 만들지 않았다.
- 장시간 부하, 네트워크 단절 재시도 UX, 여러 미제출 기록 보관함, 계정 이전·동시 탭 충돌 해결.

## 공식 참고 자료

- [Unity Web 네트워킹](https://docs.unity3d.com/6000.3/Documentation/Manual/webgl-networking.html)
- [Unity Web 템플릿 설정](https://docs.unity3d.com/6000.3/Documentation/Manual/web-templates-build-configuration.html)
- [Unity Web 브라우저 호환성](https://docs.unity3d.com/6000.3/Documentation/Manual/webgl-browsercompatibility.html)
