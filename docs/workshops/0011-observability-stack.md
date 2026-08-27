# Workshop-0011: 관측성

상태: 완료

연결 ADR: [ADR-0012](../decisions/0012-observability-stack.md)

## 결정할 문제

Unity 요청부터 API, Job, Simulation, DB, AI 호출까지 어떻게 추적하고 어디서 볼 것인가?

## 필수 요구사항

- correlation ID로 Run·Experiment·Job 흐름을 연결해야 한다.
- log, metric, trace 중 필요한 신호를 구분해야 한다.
- secret과 전체 Action payload를 기본 로그에 남기면 안 된다.
- 로컬 실패 재현과 공개 배포 진단이 가능해야 한다.

## 선택지

### A. Structured Log + 기본 Metric

- 장점: 가장 빠르고 운영 요소가 적음
- 단점: 여러 경계를 지난 지연·실패 원인 연결이 수동적

### B. OpenTelemetry 계측 + 교체 가능한 Backend

- 장점: vendor-neutral trace·metric·log correlation, 공개 배포 Backend 교체 가능
- 단점: 계측 범위와 Collector·Backend 운영 학습 필요
- 공식 자료: [OpenTelemetry documentation](https://opentelemetry.io/docs/)

### C. 특정 SaaS SDK 직접 통합

- 장점: 빠른 Dashboard, alert, error tracking
- 단점: vendor lock-in과 비용, 로컬·공개 환경 차이

## Codex 추천

B의 계측 API를 초기에 넣되, 로컬 MVP는 console exporter와 최소 Collector로 시작한다. Grafana 계열 또는 SaaS Backend 선택은 배포 결정과 함께 늦춘다.

## 필수 Spike

하나의 Experiment 요청에서 다음 span을 연결한다.

```text
HTTP request
→ create batch
→ claim simulation chunk
→ execute runs
→ batch insert
→ aggregate metrics
→ AI call
```

성공 기준:

- correlation ID 한 개로 전체 경로 조회
- 실패 Job과 retry 구분
- Agent/Player credential이 telemetry에 없음

## 프로젝트 소유자 결정

- 선택: B, OpenTelemetry 계측 + 교체 가능한 Backend
- 이유: API, Worker, DB와 AI 호출을 하나의 trace로 연결하고 특정 시각화 Vendor와 계측을 분리한다.
- 감수: OpenTelemetry 자체는 시각화 제품이 아니므로 Collector·Backend 선택과 cardinality 통제가 필요하다.
- 초기 범위: console exporter와 최소 Collector로 시작하고 공개 Backend는 비용·배포 조건에 따라 늦게 선택한다.

최종 내용은 [ADR-0012](../decisions/0012-observability-stack.md)에 기록했다.
