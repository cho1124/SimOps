# ADR-0009: 분석·집계 방식

상태: 제안

연결 워크숍: [Workshop-0008](../workshops/0008-analytics-platform.md)

## 고려한 선택지

- 운영 DB SQL + Application 집계
- PostgreSQL 원본 + Parquet/DuckDB
- 전용 분석 DB

## 결정

사용자 결정 대기.

## 결과와 포기한 것

대표 Metric query Spike 이후 작성한다.

## 재검토 조건

선택 시 Event 수·DB 크기·query p95 Trigger를 정의한다.
