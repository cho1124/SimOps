# Workshop-0011: 관측성

상태: 사용자 결정 대기

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

## 프로젝트 소유자 답변

[공통 선택 설명 형식](../09-decision-workshop.md#선택-설명-형식)을 사용한다.
