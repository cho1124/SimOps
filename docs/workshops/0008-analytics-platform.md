# Workshop-0008: 분석·집계 방식

상태: 사용자 결정 대기

연결 ADR: [ADR-0009](../decisions/0009-analytics-strategy.md)

## 결정할 문제

Run/Event에서 Experiment Metric과 Dashboard Query를 어디서 계산할 것인가?

## 필수 요구사항

- [지표 사전](../specs/metric-catalog.md)을 버전별로 재계산할 수 있어야 한다.
- 입력 Run 집합과 계산 코드 hash를 남겨야 한다.
- Control/Treatment paired comparison과 분포를 계산해야 한다.
- AI에는 고정된 Metric Snapshot을 제공해야 한다.

## 선택지

### A. PostgreSQL SQL + Application 집계

- 장점: 데이터 이동이 없고 트랜잭션·조회 구조가 단순
- 단점: 복잡한 통계와 대량 Event 분석이 운영 DB에 영향을 줄 수 있음

### B. PostgreSQL 원본 + Parquet/DuckDB 분석

- 장점: embedded OLAP과 Python/R 연동, Snapshot 재분석과 대용량 scan에 유리
- 단점: export, snapshot, 재처리 파이프라인이 추가됨
- 공식 자료: [Why DuckDB](https://duckdb.org/why_duckdb)

### C. 전용 분석 DB

ClickHouse 등 columnar DB로 Event를 전달한다.

- 장점: 대량 Event와 Dashboard 집계에 최적화
- 단점: dual write 또는 pipeline, schema 운영, 별도 배포가 필요

## Codex 추천

A로 Metric 정의와 제품 흐름을 먼저 검증한다. Event 규모나 쿼리 profile이 Trigger를 넘으면 B를 첫 확장으로 검토한다. B는 별도 서버 없이 분석 학습 폭을 넓힐 수 있다.

## 선택 전 Spike

동일한 100만 Event fixture로 다음을 비교한다.

- Stage funnel
- reward_pick_given_offer
- normalized_reward_entropy
- paired pass-rate difference
- p50/p95 turns

측정:

- query latency
- query·ETL 복잡도
- 재현 가능한 Snapshot 생성 난도
- 운영 데이터에 주는 영향

## 프로젝트 소유자 답변

[공통 선택 설명 형식](../09-decision-workshop.md#선택-설명-형식)을 사용한다.
