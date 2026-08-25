# ADR-0006: 영속 저장소

상태: 제안

연결 워크숍: [Workshop-0004](../workshops/0004-persistence-platform.md)

## 고려한 선택지

- PostgreSQL 단일 저장소
- SQLite + DuckDB
- PostgreSQL + 별도 분석 DB

## 결정

사용자 결정 대기.

## 결과와 포기한 것

쿼리 Spike 이후 작성한다.

## 재검토 조건

선택 시 데이터량·latency Trigger를 정의한다.
