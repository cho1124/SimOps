# 마일스톤 6 — 대시보드와 영속 실험

검증일: 2026-08-27. 상태: **구현·자동 검증 완료**, 실제 브라우저 화면/입력 수동 QA·공개 배포 대기.

## 완성된 흐름

```text
React/Vite: 초안 저장 → 정의 확정 → 실행 접수(202)
                             ↓
PostgreSQL: 고정 정의 + Config + Batch + 18 Cell Job + 집계 Job
                             ↓
별도 Worker: Cell 실행·전수 Replay → Cell 단위 원자적 저장 → paired 통계 집계
                             ↓
React/Vite: 진행률·성향 비교·Stage 퍼널·CI·위반 기준·후보 없음
                             ↓
사람의 검토 기록 + 감사 로그 (게임 설정 배포는 아님)
```

기존 모듈형 모놀리스 + 별도 Worker 구조를 유지했다. 새 서버나 메시지 브로커·유료 AI를 추가하지 않았다. 웹 구축 스킬의 단계적 미리보기·검증 절차를 적용하되, 승인된 ADR-0010/0013의 React/Vite·PostgreSQL·배포 대상은 변경하지 않았다. 원격 Sites 생성·소스 업로드·자동 공개 배포·공유용 이미지 생성은 이번 로컬 운영 콘솔 범위에서 수행하지 않았다.

## 실행

필요 환경: .NET SDK 10.0.101, Node.js 24.12.0, npm 11.8.0, Docker Desktop. Unity smoke 옵션은 기존 Windows Player 빌드가 있을 때만 사용한다.

```powershell
# 자동 검증. 최초 실행 시 정식 실험도 실행해 DB에 저장한다.
powershell -ExecutionPolicy Bypass -File scripts/Run-Milestone6.ps1

# 기존 Windows Player의 API·랭킹 회귀 검증 포함
powershell -ExecutionPolicy Bypass -File scripts/Run-Milestone6.ps1 -IncludeUnitySmoke

# 사용 중인 터미널에서 로컬 실험실 유지
powershell -ExecutionPolicy Bypass -File scripts/Start-LocalLab.ps1 -SkipBuild
```

`Run-Milestone6`은 기본 `npm ci`로 lockfile을 재현한다. 미리보기 서버가 같은 `node_modules`를 사용 중이면 먼저 종료한다. 이미 같은 lockfile로 설치한 개발 세션에서는 `-SkipInstall`로 설치만 생략할 수 있다.

- Dashboard: `http://127.0.0.1:5173`
- API: `http://127.0.0.1:5080`
- 개발용 운영자 키: `simops-local-dev-key` (공개 환경 사용 금지)
- DB: 기존 로컬 `simops-postgres`, port 54329. 데이터는 프로세스 종료 후에도 유지된다.
- 호스트 로그: `artifacts/local-lab/`. 검증 로그: `artifacts/backend/logs/`.
- `Start-LocalLab`은 해당 명령이 만든 프로세스만 정리한다. 기존 포트 점유 프로세스를 강제로 종료하지 않는다. Ctrl+C로 종료하며 PostgreSQL은 유지한다.
- `-BackendOnly`는 별도로 실행한 Vite를 유지하면서 API·Worker만 시작할 때 사용한다.

## UI 범위

운영자 키 연결, 최근 100개 실험 목록, 등록 JSON 템플릿, 초안 수정, Ready 확정, 실행/취소, Cell 진행률/재시도, 성향 선택, 클리어율 비교, 누적 실패율 그래프와 정확한 수치 표, 조건부 통과율, 대표 행동 로그, Primary CI, 전체 보호 기준, 전체 결과 JSON 다운로드, 판단 근거를 포함한 검토 기록을 제공한다.

대시보드는 지표를 다시 계산하지 않고 서버의 고정 Snapshot을 표시한다. Plan Hash와 Result Digest가 현재 실험과 다르면 결과를 표시하지 않는다. 원시 Run 목록은 일반 조회에서 제외하고 전체 JSON 내보내기로만 가져온다. `null` 비율은 0%가 아니라 관측 없음으로 표시한다.

후보가 없으면 승인 선택을 비활성화한다. 서버도 검증하므로 UI를 우회해 탈락 후보를 승인할 수 없다. 검토 기록은 `approved_candidate / rejected / rerun` 중 하나와 근거가 필요하다. 실제 `difficulty-curve-001`에는 자동 검토 결론을 쓰지 않았으며 `analyzing` 상태로 남겼다. `rerun`은 새 실험이 필요하다는 기록이고, 기존 사전 등록 실험을 추가 표본으로 덮어쓰는 명령은 아니다.

## 물리 저장 단면

마이그레이션 004와 005를 추가했다. 논리 모델의 모든 테이블을 한 번에 구현하는 대신, Cell 단위로 재개 가능한 실험 저장 단면을 먼저 연결했다.

| 테이블 | 책임 |
|---|---|
| `experiments` | 정의 JSONB·Plan Hash·낙관적 revision·상태·최종 검토 기록 |
| `experiment_variants` | 확정 시 생성하는 Variant → 불변 Config FK |
| `simulation_batches` | 실험당 1개 Batch·멱등 키·실행 artifact fingerprint·완료 Snapshot |
| `simulation_jobs` | 18 Cell + 1 집계 작업의 lease·재시도·실패 상태 |
| `experiment_cells` | Batch/Variant/Agent PK, 해당 Cell의 Run 증거·지표·대표 Replay JSONB |
| `experiment_audit` | 초안/확정/실행/완료/취소/사람 판정의 append-only 감사 기록 |

기존 인간/단일 합성 Run 검증용 `jobs`는 유지하고, 동일 PostgreSQL 안에 실험용 typed job 테이블을 분리했다. 두 루프는 같은 Worker 프로세스에서 동작한다. 별도 마이크로서비스로 분리한 것은 아니다.

Cell당 최대 1,000개 Seed를 하나의 트랜잭션으로 저장한다. Run 배열은 사전 Seed 순서·개수·표본 Hash를 검증하고, Cell PK 및 소유 lease를 확인한 뒤 기록한다. 같은 Cell이 두 번 완료돼도 저장/진행률은 한 번만 반영된다. 모든 합성 행동/이벤트를 기존 `run_actions`/`run_events` 테이블에 복제하는 형태는 아직 아니며, Run별 결과/행동 Hash와 대표 행동 로그 중심이다. 완전한 모든 행동 로그의 장기 보관·chunk 단위 보존 삭제는 후속 범위다.

확정된 Config·Variant·Cell·감사 기록, Ready 이후 정의, terminal Batch Snapshot은 DB trigger로 수정/삭제를 차단한다. 초안 수정에는 현재 revision이 필요하다. 동일 정의 저장과 동일 실험 시작 재전송은 기존 리소스를 돌려준다.

## Worker와 자원 제한

- API는 계획 검증·접수·조회만 한다. Game 실행과 bootstrap을 API 요청 안에서 수행하지 않는다.
- 서버 입력 한도: 3 Variant × 6 Agent × Cell당 1,000 Run, bootstrap 최대 2,000회.
- 전역 활성 Batch 최대 2개. 접수 트랜잭션의 advisory lock으로 동시 요청에서도 한도를 지킨다.
- Worker 프로세스당 시뮬레이션 작업 동시성 1. 인간 검증 루프는 별도로 계속 실행한다. CPU/DB 자원을 완전히 격리한 것은 아니다.
- lease 30초, 5초마다 heartbeat, 최대 시도 3회, 실패 재시도 2초 지연.
- 모든 변경은 Batch → Job 순서로 잠근다. 다른 Worker가 잠근 대상을 건너뛰는 `FOR UPDATE SKIP LOCKED` 방식은 PostgreSQL의 queue-like 처리 용도와 맞춘다. [PostgreSQL SELECT 문서](https://www.postgresql.org/docs/18/sql-select.html)
- 취소/lease 상실/heartbeat 실패 시 계산을 취소하고 늦게 도착한 결과를 폐기한다. bootstrap 반복 중에도 취소를 확인한다.
- 중단 시 완료한 Cell은 다시 실행하지 않는다. 아직 커밋되지 않은 Cell 전체(최대 1,000 Run)는 재계산할 수 있다. Exactly-once **실행**이 아니라 at-least-once 실행 + exactly-once **저장 효과**다.
- 취소·복구 불가능 실패는 Batch에 구분해서 저장한다. Experiment는 기존 상태 모델의 `failed`로 전이하며, 부분 결과로 최종 Metric을 만들지 않는다.
- API가 접수할 때 Core/Agent/계산기 assembly fingerprint를 고정한다. 실행 도중 다른 빌드가 섞이면 `EXECUTION_ARTIFACT_CHANGED`로 거부한다. 실패 Batch의 강제 재개는 제공하지 않으며, 같은 빌드로 재시도하거나 새 실험 ID를 사용한다.

`jsonb`의 객체 key 순서는 C# Dictionary 삽입 순서와 다를 수 있다. DB에서 읽은 Cell을 정의의 Variant/Agent 순서로 재배열하고 카운트 Dictionary를 ordinal 정렬한 뒤 Digest를 계산한다. 이 처리로 DB 왕복 후에도 기존 CLI Digest를 보존했다.

## REST 계약

모든 경로는 기존 `X-SimOps-Admin-Key`가 필요하다. 쓰기 요청은 기존 제출 rate limit의 적용을 받는다.

| 메서드/경로 | 의미 |
|---|---|
| GET `/api/v1/catalog/experiment-template` | 사전 등록 001 템플릿 |
| GET `/api/v1/catalog/configs/{checksum}` | 저장된 불변 Config 조회 |
| GET/POST `/api/v1/experiments` | 최근 목록 / `{definition, expectedRevision}` 초안 저장 |
| GET `/api/v1/experiments/{id}` | 정의·상태·진행률·검토 기록 |
| POST `.../{id}/ready` | `{planHash}`로 정의 잠금 |
| POST `.../{id}/batches` | `{planHash, idempotencyKey}` → 202 + Batch ID |
| GET `/api/v1/simulation-batches/{id}` | Cell 진행률·작업 상태/오류 |
| POST `.../simulation-batches/{id}/cancel` | queued/running Batch 취소 |
| GET `.../experiments/{id}/results` | 요약 Snapshot. `?full=true`는 원시 Run 증거 포함 |
| POST `.../experiments/{id}/decision` | Plan Hash·Result Digest·결론·선택 후보·근거의 검토 기록 |

중요 오류: `EXPERIMENT_INVALID`/`EXPERIMENT_LIMIT`/`REQUEST_INVALID` 400, `EXPERIMENT_LOCKED`/`PLAN_CHANGED`/`CANDIDATE_INVALID` 409, `SIMULATION_CAPACITY` 429. 존재하지 않는 결과(미완료/취소 포함)는 404다. 오류는 correlation ID를 포함한다.

기존 `/synthetic-runs`·인간 Ticket Verifier는 여전히 baseline 전용이다. Treatment는 이번 실험 엔진이 등록된 Config로 직접 Replay한다. M8 공개 설정·클라이언트 동기화를 완료한 것처럼 취급하지 않는다.

## 검증 결과

`Run-Milestone6.ps1 -IncludeUnitySmoke -SkipInstall` 통과:

| 범위 | 통과 수 |
|---|---:|
| 기존 Core·Agent·백엔드·랭킹 | 39 |
| 실험 계산기 | 9 |
| 실험 DB | 6 |
| 실험 HTTP + 실제 Worker | 3 |
| React 컴포넌트/API client | 6 |
| 중복 제외 합계 | **63** |

- DB: Ready 변경 거부, Config/Variant/Audit 불변성, 만료 lease 회수·늦은 완료 차단, 동시 중복 완료, 저장된 Cell 재사용, 취소 증거 보존, 재시도 한도, 동시 Batch 한도, terminal Snapshot 불변성, 전체 Cell 완료 후 집계, DB 재접속 후 상태/결과 유지.
- HTTP: 인증 누락·누락 필드·과도한 반복 수 거부, revision·Ready·4개 동시 시작 요청의 단일 Batch 반환, 40개 동시 진행률 조회, 등록 Config 조회, 실제 18,000 Run 저장 및 이전 Digest 일치. 중첩 조회 전에 DB 연결을 반환하여 pool 고갈을 피한다.
- 4개 동시 시작 요청 응답: 로컬 측정 총 31ms. 일반 제출 p95는 최근 회귀 실행에서 8.38ms(10개 표본). 이는 이 개발 장비의 warm 소규모 측정이며 공개 환경 SLA나 부하 한계가 아니다.
- 정식 001 Result Digest: `3bf0513a6d9eb46554b81a17ea8860cb9fbeb1a5be36bccf30d9c7707e9dbb08`. 두 후보 모두 사전 기준 미달, 공개 시즌 변경 0, 자동 사람 판정 0.
- Windows Player의 실제 API·Worker·랭킹 연결 `SIMOPS_ONLINE_SMOKE_PASS` 확인. Game Core와 Unity 패키지는 수정하지 않았다.
- 대시보드 TypeScript 검사·정적 빌드 성공. JS 211.30kB(gzip 66.64kB), CSS 6.07kB. React/Vite 정적 SPA 구조는 승인된 ADR과 공식 가이드를 따른다. [React 가이드](https://react.dev/learn/build-a-react-app-from-scratch), [Vite 가이드](https://vite.dev/guide/)
- UI 검증은 jsdom 기반 컴포넌트 테스트다. 실제 브라우저에서 클릭·스크린샷·화면비를 검증한 것은 아니다. 첫 미리보기는 HTTP 200 및 컴파일 후 제공했다.

## 보안·미완료 범위

운영자 키는 코드·URL·VITE 환경변수·localStorage에 넣지 않는다. 입력한 키는 탭 메모리에서만 요청 header로 전달하며 리디렉션을 거부한다. API CORS는 두 localhost 개발 origin만 허용한다. 개별 운영자 계정/RBAC가 없는 단일 운영자 콘솔이므로 감사 actor는 `operator`다. 공개 배포 전 TLS·허용 origin·운영자 인증 정책을 검토해야 한다.

남은 작업은 실제 브라우저 수동 QA, M7 근거 제한 AI 분석, M8 설정 게시·롤백·Unity 동기화다. Config lifecycle 전체·Seed 범위 chunk 분할·개별 모든 행동 로그 저장·장기 보존 정책·공개 무료 인프라 검증은 아직 없다. 동작하는 분석과 영속 실행을 갖췄지만 합성 정책의 한계나 인간 재미 평가 문제를 해결했다고 주장하지 않는다.
