# Architecture Decision Records

중요한 설계 결정을 짧은 문서로 남긴다.

## 작성 시점

- 기술 스택을 선택할 때
- 컴포넌트 책임 경계를 정할 때
- 성능과 단순성 사이에서 선택할 때
- 데이터 보존 또는 보안 정책을 정할 때
- 되돌리기 어려운 제품 결정을 할 때

## 파일 이름

```text
0001-decision-title.md
0002-next-decision.md
```

## 템플릿

```markdown
# ADR-0000: 결정 제목

상태: 제안 | 승인 | 대체 | 폐기

## 맥락

어떤 문제와 제약이 있는가?

## 고려한 선택지

- 선택지 A
- 선택지 B

## 결정

무엇을 선택했는가?

## 이유

왜 이 선택이 현재 제약에 적합한가?

## 결과와 포기한 것

얻는 것과 감수하는 단점은 무엇인가?

## 재검토 조건

어떤 조건에서 이 결정을 다시 검토하는가?
```

## 승인된 ADR

| ADR | 승인 결정 |
|---|---|
| [0001](0001-shared-deterministic-game-core.md) | .NET Standard 2.1 결정론적 Game Core DLL 공유 |
| [0002](0002-modular-monolith-stack.md) | 모듈형 모놀리스 API + 별도 Worker |
| [0003](0003-postgresql-durable-jobs.md) | PostgreSQL 기반 내구성 Job |
| [0004](0004-verified-season-leaderboard.md) | 서버 재실행 검증 + 시즌별 단일 설정 랭킹 |
| [0005](0005-backend-platform.md) | ASP.NET Core API + .NET Worker Service |
| [0006](0006-persistence-platform.md) | PostgreSQL 단일 저장소 |
| [0007](0007-api-style.md) | REST/JSON + OpenAPI |
| [0008](0008-identity-and-run-verification.md) | 익명 Credential + Signed Ticket + 전체 재실행 |
| [0009](0009-analytics-strategy.md) | PostgreSQL 원본 + Application 집계·Snapshot |
| [0010](0010-dashboard-platform.md) | React + Vite SPA / TypeScript |
| [0011](0011-ai-analysis-integration.md) | Worker 내부 Provider-neutral AI Adapter |
| [0012](0012-observability-stack.md) | OpenTelemetry + 교체 가능한 Backend |
| [0013](0013-deployment-and-ci.md) | 카드 없는 월 0원 PaaS·Managed PostgreSQL·정적 Hosting |
| [0014](0014-client-platform-coverage.md) | Windows + Android, 가로, itch.io, 통합 랭킹 |

## 다음 후보

- 이벤트 로그 보존 정책의 구현 세부사항
- Game Config canonical serialization과 checksum
- Legacy Replay 보존·실행 방식
