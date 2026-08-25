# ADR-0003: PostgreSQL 기반 내구성 Job

상태: 승인

## 맥락

인간 Run 검증, 합성 시뮬레이션, 집계, AI 분석은 HTTP 요청 안에서 끝내기 어려운 비동기 작업이다. MVP에서 별도 메시지 브로커를 운영할지 결정해야 한다.

## 고려한 선택지

- API 프로세스의 in-memory queue
- Redis 또는 전용 메시지 브로커
- PostgreSQL jobs 테이블과 별도 Worker

## 결정

MVP는 PostgreSQL `jobs` 테이블을 사용한다.

- Worker가 `FOR UPDATE SKIP LOCKED`로 claim한다.
- at-least-once 처리를 전제로 한다.
- idempotency key와 unique 제약으로 중복 결과를 막는다.
- heartbeat가 만료된 Job은 재claim한다.
- Simulation은 기본 100 Seed의 Chunk 단위로 작업한다.

## 이유

- 프로세스 재시작 후에도 작업이 보존된다.
- 별도 인프라 없이 트랜잭션과 작업 생성 원자성을 확보한다.
- 18,000 Run 규모는 Chunk로 충분히 처리 가능하다.

## 결과와 포기한 것

얻는 것:

- 낮은 운영 복잡도
- 내구성과 재시도
- 도메인 데이터와 Job 생성의 원자적 처리

감수하는 것:

- Job과 API가 DB 자원을 공유한다.
- 복잡한 라우팅과 대규모 fan-out에는 부적합하다.
- queue claim 경합이 증가할 수 있다.

## 재검토 조건

- Worker 경합으로 처리량 목표를 지속적으로 달성하지 못함
- Job backlog가 API latency에 영향을 줌
- 복잡한 우선순위, 지연 큐, 다중 consumer 요구가 생김

