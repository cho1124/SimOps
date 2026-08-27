# 마일스톤 4: 백엔드와 데이터

상태: 구현·로컬 통합 검증 완료 (2026-08-27)

## 구현한 수직 단면

```text
합성 Run + 순서화된 Action Log
→ API: 입력 검사 → Run·Action·Job을 한 트랜잭션으로 저장 → 202
→ 별도 Worker: Job claim → 동일 Game Core 재실행
→ 검증 결과·Stage Summary·권위 있는 Event 저장
→ API에서 상태와 서버 계산 결과 조회
```

- ASP.NET Core API와 .NET Worker는 별도 프로세스로 실행한다. PostgreSQL과 공유 라이브러리는 공통 의존성이므로 장애가 완전히 격리되는 것은 아니다.
- 운영자 키로 합성 Run 제출·조회·OpenAPI 접근을 제한한다. 인간 인증과 Ticket은 마일스톤 5 범위다.
- 입력 한도는 1 MiB / 10,000 Action, 제출은 API 인스턴스당 초당 20건이며 초과 요청은 429로 거부한다. 이는 초기 보호값이지 측정된 최대 용량이 아니다.
- 같은 idempotency key·payload는 같은 Run을 반환하고 다른 payload는 409다.
- Worker는 `FOR UPDATE SKIP LOCKED`, 30초 lease, fencing token, 최대 3회 시도로 중복 실행·중단에 대응한다. 재claim 이후 이전 token의 완료는 반영하지 않는다.
- 결과·이벤트·Job 완료는 하나의 트랜잭션이다. 클라이언트 점수 대신 서버 재실행 결과를 저장한다.
- Game/Config/Score/Agent 버전과 Seed를 추적하며, 현재 Verifier는 baseline 버전만 지원한다.

## 재현

Docker Desktop과 .NET SDK 10.0.101이 준비된 Windows에서:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Run-Milestone4.ps1
```

이 스크립트는 프로젝트 전용 PostgreSQL을 시작하고, 빌드·API·Worker·HTTP 통합·lease 장애 주입·Core/Agent 회귀 검증을 실행한다. 자신이 시작한 API/Worker만 종료하며 DB와 테스트 데이터는 남긴다. 이미 5080 포트가 사용 중이면 중단한다.

- API: `http://127.0.0.1:5080` (검증 실행 중)
- DB: `127.0.0.1:54329`, database/user `simops`
- 로컬 전용 DB password: `simops-local-only`
- 로컬 전용 운영자 header: `X-SimOps-Admin-Key: simops-local-dev-key`
- 로그: `artifacts/backend/logs/`

공개 배포에는 위 개발용 credential을 사용하지 않는다. Development 외 실행은 `SIMOPS_CONNECTION_STRING`과 `SIMOPS_ADMIN_KEY`를 명시해야 한다. 스크립트는 외부 DB 환경변수를 상속하지 않고 로컬 연결로 고정한 뒤 기존 값을 복구한다.

## 검증 근거

- 빌드: 경고 0 / 오류 0.
- HTTP·계약 검증: readiness/OpenAPI, 인증, Event 경계, 동시 중복 8건, payload 충돌, 변조 Hash, 잘못된 sequence, 미제시 보상, 제출 지연.
- 장애 주입: 만료 lease 재claim·stale 완료 차단·완료 재전송, 최대 시도 초과 terminal failure.
- 회귀: Core 13건 / Agent 5건.
- 초기 로컬 제출 p95: **9.32 ms / 10건**. Windows 11, .NET 10 Release, 단일 API/Worker, 로컬 Docker DB, 작은 baseline Run의 warm 순차 제출 결과다. 운영 부하·최대 처리량·공유 DB 포화의 증거로 해석하지 않는다.

## 데이터 설계 적용 범위와 다음 단계

현재 migration은 합성 Run 수집에 필요한 부분집합이다. checksum과 Agent `(id, version)`을 자연키로 사용하고, Action/Event는 `(run_id, sequence)`로 중복을 방지한다. 전체 설계의 Player·Ticket·Season·Leaderboard·Experiment 테이블은 후속 migration으로 확장한다.

남은 검증: 실제 API/Worker/DB 동시 부하, DB 장애 복구, 공개 환경 보안·retention. 장시간 Simulation Job을 도입할 때는 현재 짧은 검증 작업용 고정 lease에 heartbeat와 chunk 처리를 추가해야 한다.

의존성 고정: [Npgsql 10.0.3](https://www.nuget.org/packages/Npgsql/10.0.3), [ASP.NET Core OpenAPI 10.0.11](https://www.nuget.org/packages/Microsoft.AspNetCore.OpenApi/10.0.11). PostgreSQL은 로컬 Compose의 18 major image를 사용하며 완전한 배포 재현에는 image digest 고정이 추가로 필요하다.
