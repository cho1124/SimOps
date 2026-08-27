# ADR-0009: 분석·집계 방식

상태: 승인

결정일: 2026-08-27

연결 워크숍: [Workshop-0008](../workshops/0008-analytics-platform.md)

## 고려한 선택지

- 운영 DB SQL + Application 집계
- PostgreSQL 원본 + Parquet/DuckDB
- 전용 분석 DB

## 결정

원본·Summary는 PostgreSQL에 두고, 버전된 Application 집계 코드가 Metric을 계산해 불변 Metric Snapshot을 PostgreSQL에 저장한다.

## 이유

- MVP 데이터 규모에서는 별도 ETL과 분석 저장소 없이 실험 폐루프를 검증하는 편이 단순하다.
- Metric 정의, 입력 Run 집합 hash와 계산 코드 version을 Application에서 명시적으로 통제할 수 있다.
- AI에는 원시 DB가 아니라 확정된 Metric Snapshot만 제공할 수 있다.

## 결과와 포기한 것

- 운영 DB와 분석 작업이 자원을 공유한다.
- 복잡한 통계·대량 Event scan은 DuckDB나 전용 분석 DB보다 불리할 수 있다.
- 재계산 가능한 집계 코드와 Snapshot immutability를 직접 관리한다.

## 재검토 조건

- Event 수·DB 크기가 ADR-0006 Trigger를 넘음
- 집계가 운영 API latency 또는 Job backlog를 지속적으로 악화시킴
- 반복적인 임의 분석과 columnar scan 요구가 제품 기능이 됨

첫 확장 후보는 PostgreSQL Snapshot을 Parquet로 내보내 DuckDB에서 분석하는 방식이다.
