# ADR-0006: 영속 저장소

상태: 승인

결정일: 2026-08-27

연결 워크숍: [Workshop-0004](../workshops/0004-persistence-platform.md)

## 고려한 선택지

- PostgreSQL 단일 저장소
- SQLite + DuckDB
- PostgreSQL + 별도 분석 DB

## 결정

MVP의 운영 데이터, Replay, Telemetry Summary, Metric Snapshot과 Job을 단일 PostgreSQL에 저장한다.

## 이유

- 트랜잭션과 FK, unique, check 제약으로 Run·시즌·랭킹 데이터 무결성과 중복 방지를 표현할 수 있다.
- JSONB로 버전된 Config와 Event payload를 함께 관리할 수 있다.
- 랭킹 Window Function과 초기 실험 집계를 별도 데이터 파이프라인 없이 처리할 수 있다.

## 결과와 포기한 것

얻는 것:

- 하나의 정본과 단순한 로컬·공개 환경
- 검증 Run과 개인 최고 기록의 원자적 갱신
- Schema·Migration·Index·Query 최적화 학습

감수하는 것:

- API, Worker와 분석이 같은 DB 자원을 경쟁한다.
- 대량 원시 Event 분석에는 columnar 저장소보다 불리하다.
- 무료 배포의 저장 용량에 맞춘 보존 정책이 필요하다.

## 재검토 조건

- 원시 Event 1,000만 건 또는 20GB 초과
- 핵심 API·Dashboard query p95 목표의 지속적 위반
- 분석 작업이 운영 트랜잭션 latency에 반복적으로 영향을 줌
- 보존·삭제 작업이 운영 성능을 방해함
