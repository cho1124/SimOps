# SimOps

Synthetic Player LiveOps Lab.

SimOps는 Unity 게임을 합성 플레이어가 반복 플레이하고, 그 결과를 수집·분석하여 밸런스 변경안을 실험하는 LiveOps 연구·개발 프로젝트다.

이 프로젝트의 첫 번째 질문은 다음과 같다.

> 서로 다른 성향의 합성 플레이어에게 Control과 여러 Treatment를 플레이시킨 뒤, 어떤 설정이 더 안정적인지 데이터로 판단할 수 있는가?

## 현재 상태

- 단계: 마일스톤 1·3·4 완료, 2·5 구현·통합 검증 완료, 6·7·8 실험·AI 분석·LiveOps 폐루프 구현 및 자동 검증 완료(브라우저·Windows 수동 화면/Android 실기기 QA 대기)
- 구현: 결정론적 Game Core, Unity Windows·Android Client와 WebGL 프로토타입, 합성 플레이어 6종, API·Worker·PostgreSQL 재실행 검증, 익명 Player·Ticket·시즌 랭킹, 사전 등록 비교 엔진, React 실험 대시보드와 영속 Batch, 로컬 AI 분석·불변 보고서, 승인 Config 게시·동기화·롤백·후속 실험
- 확정된 게임 형식: 6번의 짧은 전투와 전투 사이 보상 선택이 있는 턴제 미니 로그라이크
- 확정된 클라이언트 범위: 하나의 Unity 프로젝트에서 Windows·Android 지원, WebGL 브라우저 프로토타입 추가(공개 호스팅 미실시)
- 비동기 경쟁 요소: 시즌·버전 기반 랭킹
- 확정된 설계: 제품·시스템·실험·아키텍처·데이터·인터페이스·검증 계획과 ADR 15건
- 첫 번째 목표: 합성 플레이어 기반 Control/Treatment 실험의 전체 흐름 검증

마일스톤별 구현 범위와 검증 결과는 [구현 기록](docs/implementation/README.md)에 남겨두었다.

Unity 플레이 화면은 한국어 전투·보상·결과 패널로 개선했다. HP·행동력·적 행동 예고, 보상 카드, 결과 제출 상태와 덮어쓰기 확인창을 제공한다. [UI 개선과 렌더 검사](docs/implementation/unity-ui-refresh.md)에 실행 방법과 실기기 QA 범위를 정리했다.

브라우저 플레이는 [WebGL 프로토타입](docs/implementation/web-prototype.md)을 따른다. 게임은 Unity 그대로 실행되고 API·Worker·PostgreSQL은 서버에 남는다. 운영 대시보드(5173)와 게임(5174)은 별도 화면이다.

## 로컬 실행

필요 환경은 .NET SDK 10.0.101이다. 저장소의 `global.json`이 같은 SDK 계열을 고정한다.

```powershell
dotnet restore SimOps.slnx
dotnet build SimOps.slnx -c Release --no-restore
dotnet run --project tests/SimOps.Game.Core.Specs/SimOps.Game.Core.Specs.csproj -c Release --no-build
dotnet run --project src/SimOps.Runner/SimOps.Runner.csproj -c Release --no-build -- 42
```

Unity 6.3 LTS가 설치된 환경에서는 아래 명령으로 DLL 패키징, Unity 골든 검증, Windows Build·smoke test, Android APK Build를 재현한다.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Run-Milestone2.ps1 -Target All
```

Web은 Unity 6000.3.9f1의 **Web Build Support**가 필요하며 `All`에 포함하지 않는다. 랭킹을 사용하려면 별도 터미널에서 `scripts/Start-LocalLab.ps1`을 실행한 뒤 아래 명령을 사용한다. 이미 빌드했다면 `-SkipBuild`를 추가한다.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Run-WebClient.ps1
```

게임 주소는 [로컬 Web 게임](http://127.0.0.1:5174/)이다. 이 주소는 본인 PC 전용이며 외부에 공유해도 접속되지 않는다. 서버를 종료해도 정적 게임 호스트가 실행 중이면 연습 플레이는 가능하지만, 랭킹 시작·제출·조회는 백엔드가 필요하다.

합성 플레이어 6종을 동일 Seed 집합으로 실행하고 JSON 기준선을 만들 수 있다.

```powershell
dotnet run --project src/SimOps.Simulation.Cli -c Release -- --runs 1000 --json artifacts/simulation/persona-baseline.json
```

로컬 백엔드 통합 검증은 Docker Desktop 실행 후 `powershell -File scripts/Run-Milestone4.ps1`로 재현한다. [백엔드 구현 기록](docs/implementation/milestone-04-backend-data.md)에 API 계약·개발용 설정·검증 범위를 정리했다.

Unity 빌드와 실제 서버 랭킹까지 전체 검증은 `powershell -File scripts/Run-Milestone5.ps1`로 실행한다. 최신 빌드가 있으면 `-SkipClientBuild`를 사용할 수 있다. [랭킹 구현 기록](docs/implementation/milestone-05-human-ranking.md)에 직접 실행 방법과 남은 QA를 정리했다.

난도 곡선 재설계로 방향을 변경한 첫 실험은 아래 명령으로 테스트와 18,000 Run 실험을 두 번 실행하고 결과 해시 일치를 검사한다. Docker나 유료 AI 호출은 필요 없다.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Run-DifficultyExperiment.ps1
```

두 후보 모두 사전 기준에 미달하여 **게시 후보 없음**으로 종료했다. [실험 엔진 구현·결과 기록](docs/implementation/milestone-06-experiment-engine.md)에 실패 이유와 다음 구현 범위를 남겼다. 기존 게임 설정·공개 시즌은 변경하지 않았다.

실험 대시보드와 영속 Worker까지 검증하려면 Node.js 24.12.0과 Docker Desktop이 필요하다.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Run-Milestone6.ps1
powershell -ExecutionPolicy Bypass -File scripts/Start-LocalLab.ps1 -SkipBuild
```

검증 명령은 등록된 18,000 Run 실험을 DB에 보존한다. 로컬 실험실은 `http://127.0.0.1:5173`에서 개발용 운영자 키 `simops-local-dev-key`로 연결한다. 키는 탭 메모리에만 보관되며 공개 서비스에 사용하면 안 된다. 실행 터미널을 유지하고 Ctrl+C로 API·Worker·대시보드를 종료한다. [M6 대시보드·DB 구현 기록](docs/implementation/milestone-06-dashboard.md)에 API·재시도·보안 범위를 정리했다.

M7 분석까지 포함한 자동 검증은 `powershell -File scripts/Run-Milestone7.ps1`이다. 기본은 **규칙 기반 데모(LLM 아님)**이며, 실제 AI는 설치된 Ollama 모델을 사용하도록 Worker를 설정한다. 로컬 `qwen2.5:3b`로 3회 실제 분석을 검증했다. 모델은 지표와 허용된 가설을 선택하고 수치는 서버가 연결한다. [M7 실행·검증 기록](docs/implementation/milestone-07-ai-analysis.md)에 설정 방법과 한계를 정리했다.

M8까지 전체 검증은 `powershell -File scripts/Run-Milestone8.ps1`이다. 실제 운영 설정은 그대로 두고 임시 DB에서 게시→Unity/Runner 플레이→후속 실험→롤백을 검증한다. 운영자 키와 별도로 개발용 승인자 키 `simops-local-approver-key`가 있어야 후보 승인·게시·롤백이 가능하다. [M8 계약·검증 기록](docs/implementation/milestone-08-liveops.md)에 사용법·Ticket 영향·남은 QA를 정리했다.

## 설계 문서 읽는 순서

1. [프로젝트 로드맵](docs/00-roadmap.md)
2. [프로젝트 정의](docs/01-product-brief.md)
3. [시스템 기획](docs/02-system-design.md)
4. [실험 및 측정 기획](docs/03-experiment-design.md)
5. [아키텍처 설계](docs/04-architecture.md)
6. [데이터 설계](docs/05-data-design.md)
7. [인터페이스 설계](docs/06-interface-design.md)
8. [구현 계획](docs/07-implementation-plan.md)
9. [검증 및 포트폴리오 계획](docs/08-validation-and-portfolio.md)

문서 상태와 작성 규칙은 [문서 안내](docs/README.md)를 따른다. 중요한 기술·제품 의사결정은 [ADR](docs/decisions/README.md)에 별도로 기록한다.

기술 선택은 [기술 의사결정 워크숍](docs/09-decision-workshop.md)의 절차에 따라 프로젝트 소유자가 직접 근거를 설명하고 승인한다.

플랫폼과 무관하게 확정 가능한 계약은 [플랫폼 독립 명세](docs/specs/README.md)에, 자동 진행 결과와 재개 위치는 [자동 진행 결과](docs/autonomous-preparation.md)에 정리돼 있다.

## 핵심 원칙

- Unity 전문성을 중심으로 백엔드, 웹, 데이터, AI, 운영 영역을 연결한다.
- 실제 사용자 행동을 합성 플레이어가 완전히 대체한다고 주장하지 않는다.
- 동일한 설정, 시드, 행동은 동일한 결과를 만들어야 한다.
- PC와 모바일은 동일한 Game Core와 서버 계약을 사용하고, 입력·화면·기기 생명주기만 플랫폼 경계에서 분리한다.
- AI가 계산된 사실을 생성하지 않도록 수치 계산과 해석을 분리한다.
- AI가 제안한 운영 변경은 시뮬레이션과 사람 승인을 거쳐야 한다.
- 기능 수보다 하나의 완전한 실험 폐루프를 먼저 완성한다.

## 첫 번째 완성 흐름

```text
Control과 Treatment 설정 생성
→ 성향이 다른 합성 플레이어로 반복 실행
→ 텔레메트리 수집
→ 지표 비교
→ AI 분석가가 결과와 다음 가설 설명
→ 사람이 설정 승인
→ Unity 클라이언트에 반영
→ 동일 조건으로 재실험
```
