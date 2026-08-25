# 검증 매트릭스

상태: 확정

## 목적

플랫폼 선택과 구현이 완료 기준을 만족하는지 요구사항 ID로 추적한다.

## 테스트 레벨

| 레벨 | 검증 대상 | 외부 의존성 |
|---|---|---|
| Unit | 상태 전이, 점수, Agent 효용 | 없음 |
| Property | 임의 유효·무효 입력의 불변조건 | 없음 |
| Golden Determinism | 고정 Fixture의 Result Hash | 없음 |
| Contract | API·Event·Action Log 스키마 | 최소 |
| Integration | DB 트랜잭션, Job, 검증기 | 실제 호환 구성 |
| End-to-End | Unity/Runner → API → DB → Dashboard | 전체 로컬 스택 |
| Failure Injection | 중단, 중복, 지연, 재시도 | 전체 또는 축소 스택 |
| Performance | Run 처리량, 검증·조회 latency | 고정 장비·데이터셋 |

## 핵심 매트릭스

| 요구사항 | 테스트 | 성공 기준 |
|---|---|---|
| CORE-001 | 동일 입력 1,000회 및 실행 환경 비교 | Result Hash 불일치 0 |
| CORE-022 | 임의 무효 Action property test | 상태 변경 0, 명시 오류 |
| CORE-031 | 미제시 Reward 선택 | 거부, 상태 변경 0 |
| CORE-034 | 후보 부족 Config | 실행 전 검증 실패 |
| RUN-002 | terminal 이후 모든 전이 | 전부 거부 |
| EXP-001 | Ready Experiment 수정 | 저장 거부 |
| CFG-001 | Validated Config content 수정 | 저장 거부 |
| SEASON-002 | Active Season FK 변경 | 저장 거부 |
| BATCH-001 | 같은 Cell·Seed 중복 완료 | 결과 한 건 |
| VERIFY-001 | 동일 Ticket 동시 제출 | Run 한 건 |
| VERIFY-002 | Worker 완료 직전 종료 | 재실행 후 결과 한 건 |
| RANK-001 | 낮은 점수 제출 | 기존 최고 기록 유지 |
| RANK-002 | 높은 동점 기록 | 동점 규칙대로 교체 |
| AI-001 | Metric에 없는 숫자 출력 | schema·근거 검증 실패 |
| AI-002 | AI Provider 장애 | Metric과 Experiment 결과 유지 |

## Golden Fixture

Game Version마다 최소 다음 Fixture를 유지한다.

- 기본 공격만 사용한 승리 또는 최대 진행 Run
- Guard와 Technique를 섞은 Run
- Item 사용 Run
- Reward 계열별 대표 Build
- Stage 3 패배
- Boss 승리
- 최대 Turn 종료
- 경계 HP와 Block

각 Fixture:

- Config checksum
- Score Rule checksum
- Base Seed
- Action Log
- Stage Summary
- Final Result
- Result Hash

## Failure Injection

| 실패 지점 | 기대 결과 |
|---|---|
| Run 제출 응답 전 API 종료 | 같은 idempotency key로 같은 Run 반환 |
| Job claim 후 Worker 종료 | heartbeat 만료 후 재claim |
| Batch insert 중 DB 오류 | Chunk 원자성 또는 누락 Seed 재처리 |
| AI 응답 schema 오류 | 보고서 미승인, Job 제한 재시도 |
| Config publish 중 오류 | Season과 Publication 부분 적용 없음 |
| 네트워크 중복 Event batch | sequence unique로 중복 제거 |

## 성능 측정 규칙

- 장비, OS, runtime, 빌드 모드, 동시성, 데이터 크기를 기록한다.
- warm-up과 측정 구간을 분리한다.
- 평균뿐 아니라 p50, p95, 처리량을 기록한다.
- 성능 변경 전후에 같은 Fixture와 Seed를 사용한다.
- 목표 미달이 곧 특정 기술 교체를 의미하지 않으며 profile 결과를 먼저 확인한다.

## 추적 규칙

- 테스트 이름에 요구사항 ID를 포함한다.
- 구현 PR은 충족하거나 변경하는 요구사항을 명시한다.
- 불변조건 변경은 Golden Fixture와 Game Version 영향을 검토한다.
