# 상태 전이 명세

상태: 확정

## Run

```mermaid
stateDiagram-v2
    [*] --> Created
    Created --> Submitted: 인간 Action Log 제출
    Created --> Running: 합성 실행 시작
    Running --> Completed: 합성 실행 종료
    Running --> Failed: 실행 오류
    Submitted --> Verifying
    Verifying --> Verified: 재실행 일치
    Verifying --> Rejected: 만료·불일치·규칙 위반
    Completed --> Verified: 내부 결과 검증·저장
    Verified --> [*]
    Rejected --> [*]
    Failed --> [*]
```

규칙:

- RUN-001: terminal 상태는 Verified, Rejected, Failed다.
- RUN-002: terminal 상태에서 다른 상태로 이동할 수 없다.
- RUN-003: 인간 Run만 Submitted와 Verifying을 사용한다.
- RUN-004: 합성 Run만 Running과 Completed를 사용한다.
- RUN-005: Leaderboard 갱신은 Verified 인간 Run 전이와 같은 트랜잭션에서 처리한다.

## Experiment

```mermaid
stateDiagram-v2
    [*] --> Draft
    Draft --> Ready: 정의 검증
    Ready --> Running: Batch 시작
    Running --> Analyzing: 필수 Cell 완료
    Running --> Failed: 복구 불가능
    Analyzing --> Decided: 사람 판정
    Analyzing --> Running: 보충 Run
    Decided --> Archived
    Failed --> Archived
```

규칙:

- EXP-001: Ready 이후 가설, Variant, 판정 규칙은 변경할 수 없다.
- EXP-002: 필수 Cell의 목표 Run이 충족돼야 Analyzing으로 이동한다.
- EXP-003: AI Report 생성은 Decided 전 필수가 아니며, 계산된 Metric은 필수다.
- EXP-004: Decided에는 approved_candidate, rejected, rerun 중 하나의 결론이 필요하다.

## Game Config

```mermaid
stateDiagram-v2
    [*] --> Draft
    Draft --> Validated: schema + domain validation
    Validated --> Simulated: 필수 실험 완료
    Simulated --> Approved: 운영자 승인
    Approved --> Published: 새 Season 연결
    Published --> Retired: Season 종료
    Approved --> Retired: 미배포 폐기
```

규칙:

- CFG-001: Validated 이후 content는 불변이다.
- CFG-002: 수정은 새 Version과 parent_config_id로 표현한다.
- CFG-003: Published Config는 정확히 하나 이상의 Publication 이력을 가진다.
- CFG-004: MVP에서 Published Config 교체는 새 Season으로 처리한다.

## Season

```mermaid
stateDiagram-v2
    [*] --> Scheduled
    Scheduled --> Active: starts_at 도달
    Scheduled --> Cancelled
    Active --> Closed: ends_at 또는 조기 종료
    Closed --> [*]
    Cancelled --> [*]
```

규칙:

- SEASON-001: MVP에서 동시에 Active인 공개 Season은 하나다.
- SEASON-002: Season의 Game, Config, Score Rule FK는 생성 후 변경하지 않는다.
- SEASON-003: Closed Season의 Leaderboard는 동결한다.

## Simulation Batch

```mermaid
stateDiagram-v2
    [*] --> Queued
    Queued --> Running
    Running --> Completed: 모든 필수 조합 완료
    Running --> Failed: 재시도 한도 초과
    Queued --> Cancelled
    Running --> Cancelled
```

규칙:

- BATCH-001: 같은 Batch의 Variant, Agent, Seed 조합은 한 번만 완료 처리한다.
- BATCH-002: 재시도는 누락 또는 실패 조합만 대상으로 한다.
- BATCH-003: Cancelled Batch의 완료 Run은 감사·디버깅을 위해 유지할 수 있지만 최종 Metric에는 기본 포함하지 않는다.
