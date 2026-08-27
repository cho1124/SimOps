# ADR-0012: 관측성

상태: 승인

결정일: 2026-08-27

연결 워크숍: [Workshop-0011](../workshops/0011-observability-stack.md)

## 고려한 선택지

- Structured Log + 기본 Metric
- OpenTelemetry + 교체 가능한 Backend
- 특정 SaaS SDK

## 결정

API, Worker와 외부 호출은 OpenTelemetry API로 계측하고 구조화 로그를 함께 사용한다.

- 로컬 MVP는 console exporter와 선택적 최소 Collector로 시작한다.
- 시각화·저장 Backend는 OpenTelemetry와 분리하고 배포 환경에 따라 선택한다.
- correlation ID로 HTTP, Job, Simulation, DB와 AI 호출을 연결한다.

## 이유

- 별도 프로세스를 통과하는 지연과 실패 원인을 하나의 trace로 추적할 수 있다.
- 특정 SaaS에 직접 결합하지 않고 시각화 Backend를 교체할 수 있다.
- Worker 부하가 API와 공유 DB에 주는 영향을 검증하는 근거를 만든다.

## 결과와 포기한 것

- OpenTelemetry 자체는 시각화 제품이 아니므로 Collector·Backend 구성이 별도로 필요하다.
- 과도한 span과 label cardinality를 통제해야 한다.
- 무료 공개 환경에서는 보존 기간과 시각화 기능이 제한될 수 있다.

## 재검토 조건

- 계측 운영 비용이 진단 가치보다 커짐
- 특정 SaaS의 오류 추적 기능이 프로젝트 요구에 명확히 우수함
- 장애 원인 파악 시간이 목표를 반복적으로 초과함
