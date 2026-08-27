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
| PLATFORM-001 | 대상별 Development Build | PC 1종·모바일 1종 빌드 성공 |
| PLATFORM-003 | 공통 Golden Run 교차 실행 | 플랫폼 간 Result Hash 불일치 0 |
| PLATFORM-010 | 입력별 전체 Action 탐색 | 키보드·마우스·터치 누락 0 |
| PLATFORM-011 | 해상도·화면비·Safe Area 시각 검증 | 핵심 UI 겹침·잘림 0 |
| PLATFORM-013 | Action 선택 중 background 전환 | 의도하지 않은 Action 0 |
| PLATFORM-014 | Action 경계별 종료·복구 | 누락·중복 Action 0 |
| DEPLOY-001 | 유휴 API·DB 첫 요청 | Cold Start 시간 기록 후 핵심 흐름 성공 |
| DEPLOY-002 | 유휴 Worker wake 후 Job 실행 | Job 유실·중복 0, 상태 조회 가능 |
| DEPLOY-003 | 무료 할당량 초과 방지 | 카드·과금 없이 중단 또는 사전 차단 |

## 현재 자동화 현황

마일스톤 1에서 아래 항목을 dependency-free Console Spec Harness로 자동화했다. 나머지 항목은 해당 마일스톤에서 계속 추가한다.

| 테스트 | 구현 상태 | 최근 결과 |
|---|---|---|
| CORE-001 | 완료 | 1,000개 Seed의 실행·Replay Hash 불일치 0 |
| CORE-002·003 | 완료 | RNG Stream 분리 및 ko-KR/tr-TR locale 비교 통과 |
| CORE-013·022·024·028 | 완료 | 종료·무효 행동·Item·Turn 제한 불변조건 통과 |
| CORE-030·031·034 | 완료 | 후보 3개·미제시 선택 거부·Pool 사전 검증 통과 |
| CORE-041·043 | 완료 | checksum 및 Config 구조 검증 통과 |
| Golden Seed 42 | 완료 | Debug·Release Result Hash 일치 |
| AGENT-001·002 | 완료 | 6종 × 1,000 Run 유효 전이 및 6종 × 100 Seed 행동 로그 재현 통과 |
| AGENT-003 | 완료 | 공격·방어·효율·탐색 성향 신호 분리 통과 |
| AGENT-004 | 완료 | 개발 장비 Headless 처리량 목표 100 Run/s 초과 |
| METRIC-001 | 완료 | 0분모 null·reason 및 Agent Version 혼합 거부 |
| API-001·002·003 | 완료 | readiness/OpenAPI·운영자 인증·warm 제출 지연 검증 |
| EVENT-001 | 완료 | Run별 Encounter 시작·종료 수와 Stage Summary 일치 |
| VERIFY-001 | 완료 | 합성 8건·인간 6건 동시 제출 하나의 Run, 다른 payload 충돌 |
| VERIFY-002 / JOB-001 | 완료 | 만료 lease 회수·stale token 차단·중복 완료·최대 재시도 실패 |
| VERIFY-003·004·005 | 완료 | Hash 변조·불연속 sequence·미제시 보상 거부 |
| PLAYER-001 / TICKET-001~004 | 완료 | 익명 hash·소유권·서명·버전·만료·재사용·멱등성 검증 |
| RANK-001~004 / SEASON-002 | 완료 | 최고점·동점·동시 완료·합성/거부 제외·종료 시즌 동결·시즌 불변성 |
| DB-001 | 완료 | 새 임시 DB에서 전체 migration과 catalog를 두 번 초기화하고 익명 인증 검증 통과 |
| EXP-CALC-001·002 | 완료 | 누락/미지원 정의·중복 Cell·Seed overflow 거부, 공격력만 변경하고 Control 보존 |
| EXP-CALC-003·004·005 | 완료 | paired bootstrap·시작 cohort MAE·공통 생존자 Turn 비교 검증 |
| EXP-CALC-006·007·008·009 | 완료 | 전수 Replay·반복 digest·입력 snapshot·취소·0분모·문화권 독립 검증 |
| EXP-001 / CFG-001 | 완료 | Ready 이후 정의·Variant·Config 수정 거부, 감사 로그 append-only |
| EXP-002 / BATCH-001·002·004·005 | 완료 | 18 Cell 이후 집계, 중복 완료·lease 회수·취소·한도 초과 실패·동시 Batch 제한 |
| EXP-HTTP-001·002·003 | 완료 | 운영자 인증·엄격한 스키마·자원 제한·멱등 접수·18,000 Run 저장 결과 일치 |
| UI-001~006 | 자동 검증 완료 | React 컴포넌트 인증·후보 없음·성향 선택·검토 근거·Snapshot 불일치·오류·0분모 |

`EXP-CALC-*`는 계산기 검증이며 DB/HTTP/화면 검증과 구분한다. M6 기준 중복 제외 63개 테스트가 통과했다. React 테스트는 jsdom 컴포넌트 테스트로, 실제 브라우저의 화면·입력 QA를 대체하지 않는다. [엔진 결과](../implementation/milestone-06-experiment-engine.md)와 [대시보드·영속 실행 검증](../implementation/milestone-06-dashboard.md)을 함께 따른다.

실행 명령과 고정값은 [마일스톤 1 구현 기록](../implementation/milestone-01-game-core.md)에 있다.

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
| 모바일 Action 도중 background·강제 종료 | 마지막 확정 Action까지만 복구 |
| 플랫폼 전환 후 만료된 Ticket 제출 | 로컬 결과 유지, 랭킹 거부 |

## 플랫폼 검증 행렬

정확한 대상은 ADR-0014에서 확정하되 각 지원 대상에 다음을 기록한다.

| 대상 | Build | 설치·실행 | 입력 | UI·Safe Area | Pause·Resume | Golden Hash | API·랭킹 |
|---|---|---|---|---|---|---|---|
| Windows | Development Build 성공 | Player·온라인 smoke 성공 | 코드 완료·수동 QA 대기 | 테마·크기 적용, 정상 캡처 미확보·수동 QA 대기 | 저장 hook 완료·수동 QA 대기 | Editor Host 일치 | 실제 API·Worker 검증 후 내 순위 조회 성공 |
| Android 실기기 | ARM64 IL2CPP APK 성공 | 기기 미연결로 대기 | 터치 UI 코드 완료·실기기 대기 | Safe Area 코드 완료·실기기 대기 | 저장 hook 완료·실기기 대기 | APK 실행 후 측정 대기 | 공통 네트워크 코드·빌드 완료, 실기기 대기 |

M8 추가: 격리 PostgreSQL DB·API:5081에서 실제 게시·비기준선 Ticket·Worker 검증·Windows Player·Runner·롤백·후속 실험을 검증한다. 게시 이력 insert 시 강제 DB 실패로 부분 시즌 전환이 남지 않는 것도 확인한다. 실험 판정 기준은 양성 경로를 위한 테스트 전용이며 운영 데이터의 개선 근거가 아니다. [세부 검증 기록](../implementation/milestone-08-liveops.md#검증-결과)을 참고한다.

## 성능 측정 규칙

- 장비, OS, runtime, 빌드 모드, 동시성, 데이터 크기를 기록한다.
- warm-up과 측정 구간을 분리한다.
- 평균뿐 아니라 p50, p95, 처리량을 기록한다.
- 성능 변경 전후에 같은 Fixture와 Seed를 사용한다.
- 목표 미달이 곧 특정 기술 교체를 의미하지 않으며 profile 결과를 먼저 확인한다.
- 무료 공개 환경은 warm latency와 Cold Start 시간을 분리해 기록한다.

## 추적 규칙

- 테스트 이름에 요구사항 ID를 포함한다.
- 구현 PR은 충족하거나 변경하는 요구사항을 명시한다.
- 불변조건 변경은 Golden Fixture와 Game Version 영향을 검토한다.
