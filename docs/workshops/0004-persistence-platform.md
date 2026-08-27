# Workshop-0004: 영속 저장소

상태: 완료

연결 ADR: [ADR-0006](../decisions/0006-persistence-platform.md)

## 결정할 문제

시즌·설정·실험·Run·랭킹·이벤트를 어떤 저장 구조로 시작할 것인가?

## 필수 요구사항

- 인간 Run 검증과 Leaderboard 갱신을 원자적으로 처리해야 한다.
- 관계와 unique/check 제약이 필요하다.
- JSON 형태 Config와 Event payload를 저장할 수 있어야 한다.
- Window function과 실험 집계가 가능해야 한다.
- 로컬과 공개 배포에서 재현 가능해야 한다.

## 선택지

### A. PostgreSQL 단일 저장소

- 장점: 트랜잭션, 관계형 제약, JSONB, window function, 운영 경험을 한 시스템에서 확보
- 단점: 원시 Event가 커지면 OLTP와 분석 부하가 경쟁할 수 있음
- 확장: Summary·partition·별도 분석 저장소를 측정 후 추가

### B. SQLite + DuckDB

- SQLite는 운영 데이터, DuckDB는 export된 분석 데이터를 담당한다.
- 장점: 설치와 로컬 데모가 단순하고 DuckDB의 OLAP 학습 가능
- 단점: 두 저장소 동기화, 공개 동시 쓰기, Background Worker 확장이 복잡
- 공식 자료: [Why DuckDB](https://duckdb.org/why_duckdb)

### C. PostgreSQL + 분석 DB를 처음부터 분리

PostgreSQL은 운영, ClickHouse 또는 DuckDB/Parquet는 Event 분석을 담당한다.

- 장점: OLTP와 OLAP 책임이 명확하고 대량 Event 분석에 유리
- 단점: 데이터 파이프라인과 일관성·재처리 부담이 MVP보다 큼

## Codex 추천

A. 현재 데이터 규모와 강한 트랜잭션 요구에 적합하다. 분석 병목은 아직 가설이므로 Summary와 index로 시작한 뒤 실제 측정으로 분리를 결정한다.

## 선택 전 Spike

후보 저장소에서 다음을 구현하거나 SQL로 검증한다.

- 검증 Run과 개인 최고 기록의 원자적 upsert
- 상위 100과 내 주변 Ranking
- Variant × Agent별 Stage 3 pass rate
- Reward pick entropy 계산 입력 추출
- 100만 Event에서 대표 집계 `EXPLAIN ANALYZE`

## 프로젝트 소유자 결정

- 선택: A, PostgreSQL 단일 저장소
- 이유: 트랜잭션과 관계·unique·check 제약으로 Run, 시즌, 랭킹의 무결성과 중복 방지를 표현한다.
- 감수: API·Worker·분석이 같은 DB 자원을 경쟁하고 대량 Event 분석에는 불리할 수 있다.
- 재검토: Event 수, DB 크기, query p95와 운영 부하 Trigger를 측정한다.

최종 내용은 [ADR-0006](../decisions/0006-persistence-platform.md)에 기록했다.
