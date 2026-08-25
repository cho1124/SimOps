# Workshop-0005: 비동기 Job 플랫폼

상태: 사용자 결정 대기

연결 ADR: [ADR-0003](../decisions/0003-postgresql-durable-jobs.md)

## 결정할 문제

Run 검증, Simulation Chunk, Metric 집계, AI 분석을 어떻게 내구성 있게 전달하고 재시도할 것인가?

## 필수 요구사항

- 프로세스 종료 후 Job이 유실되면 안 된다.
- at-least-once 실행에서 결과가 중복되면 안 된다.
- retry, timeout, dead job, 취소, 진행률을 표현해야 한다.
- 한 Experiment의 18,000 Run을 Chunk 단위로 처리할 수 있어야 한다.

## 선택지

### A. PostgreSQL Job Table

- `FOR UPDATE SKIP LOCKED`로 claim하고 heartbeat·attempt를 직접 관리한다.
- 장점: 추가 인프라 없음, 도메인 변경과 Job 생성의 원자적 트랜잭션
- 단점: Queue 기능을 직접 구현하고 API와 DB 자원을 공유
- 공식 근거: PostgreSQL은 queue-like table의 다중 consumer 경합 회피 용도로 SKIP LOCKED를 설명한다. [PostgreSQL SELECT](https://www.postgresql.org/docs/18/sql-select.html)

### B. Redis Streams

- Consumer Group과 acknowledge/claim을 사용한다.
- 장점: 독립적인 전달 계층, pending 추적과 worker 확장
- 단점: Redis 운영과 DB write 사이 dual-write·일관성 처리 필요
- 공식 자료: [Redis Streams](https://redis.io/docs/latest/develop/data-types/streams/)

### C. RabbitMQ

- Work Queue, acknowledgment, redelivery를 사용한다.
- 장점: 성숙한 메시지 라우팅과 Worker 분리
- 단점: 별도 broker 운영, 도메인 트랜잭션과 publish 일관성을 위해 outbox 필요
- 공식 자료: [RabbitMQ Work Queues](https://www.rabbitmq.com/tutorials/tutorial-two-dotnet)

## Codex 추천

A. MVP에서는 Job 생성과 도메인 상태 전이를 한 DB 트랜잭션에 묶는 단순성이 가장 크다. Queue 경합이 측정되면 Redis/RabbitMQ로 교체할 Trigger를 ADR에 둔다.

## 필수 Failure Spike

1. Worker가 Job을 claim한다.
2. Run 저장 직전·직후 프로세스를 강제 종료한다.
3. 다른 Worker가 재claim한다.
4. 최종 Run과 Metric이 한 번만 저장되는지 확인한다.

성공 기준:

- Job 유실 0
- 완료 결과 중복 0
- 재시도 원인 추적 가능

## 프로젝트 소유자 답변

[공통 선택 설명 형식](../09-decision-workshop.md#선택-설명-형식)을 사용한다.
