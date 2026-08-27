# 데이터 설계

상태: 확정

논리 데이터 요구사항과 PostgreSQL 기반 물리 저장·처리 기준을 함께 정의한다. 구현 세부사항은 승인된 [영속 저장소 ADR](decisions/0006-persistence-platform.md), [Job ADR](decisions/0003-postgresql-durable-jobs.md), [분석 ADR](decisions/0009-analytics-strategy.md)을 따른다.

## 1. 목표와 원칙

- 모든 Run의 게임, 설정, 점수 규칙, 에이전트, 시드를 추적한다.
- 동일 입력으로 결과를 재실행할 수 있다.
- 인간과 합성 플레이 데이터를 명확히 분리한다.
- 실험, 랭킹, LiveOps 조회를 하나의 PostgreSQL에서 시작한다.
- AI 분석가에는 검증·집계된 지표만 제공한다.
- `Run Metadata + Action Log`를 재현의 정본으로 사용한다.
- Telemetry Event는 분석을 위한 append-only 기록으로 사용한다.
- 설정, 점수 규칙, Ready 상태의 실험 정의는 불변으로 관리한다.
- 외부 요청은 idempotency key로 중복 처리를 막는다.

## 2. 데이터 계층

| 계층 | 역할 | 예시 |
|---|---|---|
| Operational | 현재 상태와 권한 | 시즌, 설정 상태, 실험 상태 |
| Replay | Run 재실행 | 시드, 순서화된 행동 로그 |
| Telemetry | 상세 분석 | 턴, 행동, 보상 이벤트 |
| Summary | 빠른 비교 | Run·Stage 요약 |
| Aggregate | 대시보드와 AI 입력 | Cell별 지표, 신뢰구간 |
| Audit | 운영 변경 추적 | 승인, 배포, 롤백 |

## 3. 핵심 관계

```text
GameVersion
├─ GameConfig ──< ExperimentVariant >── Experiment
├─ ScoreRuleVersion
└─ Season ──< Run >── RunActor
                  ├─ RunAction
                  ├─ RunEvent
                  └─ RunStageSummary

RunActor
├─ HumanPlayer
└─ AgentDefinition

Experiment ──< SimulationBatch ──< Run
Verified Human Run ──> LeaderboardEntry
Experiment ──< ExperimentMetric ──< AnalysisReport
```

## 4. 버전과 설정

### 4.1 game_versions

게임 규칙과 결정론 알고리즘의 버전이다.

| 컬럼 | 설명 |
|---|---|
| id | 내부 UUID |
| version | SemVer 문자열, unique |
| core_checksum | 배포된 Game Core 식별 해시 |
| replay_schema_version | 지원 행동 로그 스키마 |
| status | development, active, retired |
| created_at | 생성 시각 |

게임 규칙, 상태 전이 순서, 난수 파생 방식이 바뀌면 새 Game Version을 발급한다.

### 4.2 game_configs

수치와 활성 콘텐츠의 불변 스냅샷이다.

| 컬럼 | 설명 |
|---|---|
| id | UUID |
| game_version_id | 호환 Game Version |
| version | 설정 SemVer |
| parent_config_id | 파생 원본, nullable |
| schema_version | JSON 스키마 버전 |
| status | draft, validated, simulated, approved, published, retired |
| content | 전체 설정 JSONB |
| checksum | canonical JSON 해시 |
| created_by / approved_by | 운영자 |
| created_at / approved_at | 감사 시각 |

`validated` 이후 content를 직접 수정하지 않는다. 변경은 새 row를 만든다.

### 4.3 score_rule_versions

| 컬럼 | 설명 |
|---|---|
| id | UUID |
| version | SemVer |
| game_version_id | 계산기 호환 버전 |
| definition | 점수 계수와 Par Turn JSONB |
| checksum | 정의 해시 |
| created_at | 생성 시각 |

### 4.4 seasons

| 컬럼 | 설명 |
|---|---|
| id | UUID |
| name | 표시 이름 |
| game_version_id | 고정 Game Version |
| published_config_id | 고정 공개 Config |
| score_rule_version_id | 고정 점수 규칙 |
| starts_at / ends_at | UTC 기간 |
| status | scheduled, active, closed, cancelled |
| closed_reason | 조기 종료 이유 |

한 시즌은 하나의 공개 Config만 사용한다. 공개 설정을 변경하거나 롤백해야 하면 현재 시즌을 종료하고 새 시즌을 시작하여 랭킹 공정성을 유지한다.

## 5. 사용자와 에이전트

### 5.1 human_players

| 컬럼 | 설명 |
|---|---|
| id | UUID |
| nickname | 표시 이름 |
| credential_hash | 익명 기기 토큰 해시 |
| status | active, blocked, deleted |
| created_at | 생성 시각 |

MVP는 이메일, 실명, 결제 정보를 저장하지 않는다.

### 5.2 agent_definitions

| 컬럼 | 설명 |
|---|---|
| id | UUID |
| agent_type | random, novice, aggressive 등 |
| version | Agent Version |
| policy_kind | rule, ml, llm |
| parameters | 가중치와 실수·탐색 파라미터 JSONB |
| implementation_checksum | 실행 코드 식별 해시 |
| status | draft, validated, retired |
| created_at | 생성 시각 |

정책이나 파라미터가 바뀌면 같은 이름을 덮어쓰지 않고 새 버전을 만든다.

### 5.3 run_actors

| 컬럼 | 설명 |
|---|---|
| id | UUID |
| population | human, synthetic |
| human_player_id | 인간일 때 FK |
| agent_definition_id | 합성일 때 FK |
| label | 표시용 스냅샷 |

DB CHECK 제약으로 human이면 human_player_id만, synthetic이면 agent_definition_id만 존재하게 한다.

### 5.4 run_tickets

인간 Run 시작 시 서버가 발급하는 검증 문맥이다.

| 컬럼 | 설명 |
|---|---|
| id | UUID |
| actor_id / season_id | 주체와 시즌 |
| game_version_id / config_id | 고정 실행 버전 |
| score_rule_version_id | 고정 점수 규칙 |
| base_seed | 서버 발급 시드 |
| nonce | 재사용 방지 값 |
| expires_at / used_at | 만료와 사용 시각 |
| signature | 서버 검증값 |

Run Ticket은 임의 점수·설정 조작을 줄이지만, 공개된 시드로 최적 행동을 외부 탐색하는 도구 보조 플레이까지 완전히 막는 안티치트는 아니다.

## 6. 실험

### 6.1 experiments

| 컬럼 | 설명 |
|---|---|
| id / name | 식별자와 이름 |
| hypothesis | 사전 가설 |
| primary_metric | 주요 지표 키 |
| decision_rules | 판정 기준 JSONB |
| seed_policy | 시드 수와 파생 정책 |
| status | draft, ready, running, analyzing, decided, archived |
| created_by | 운영자 |
| created_at / started_at / ended_at | 시각 |

`ready` 이후 가설, 판정 규칙, Variant를 수정하지 않는다.

### 6.2 experiment_variants

| 컬럼 | 설명 |
|---|---|
| id / experiment_id | 식별자와 실험 FK |
| name | control, treatment_a 등 |
| role | control, treatment |
| config_id | 불변 Game Config |
| ordinal | 표시 순서 |

### 6.3 simulation_batches

| 컬럼 | 설명 |
|---|---|
| id / experiment_id | 식별자와 실험 FK |
| requested_runs_per_cell | Cell당 목표 반복 수 |
| runner_version | Runner 코드 버전 |
| status | queued, running, completed, failed, cancelled |
| requested_at / started_at / completed_at | 시각 |
| completed_runs / failed_runs | 진행률 |
| failure_reason | 배치 실패 이유 |

재시도는 같은 Batch의 실패 Run을 보충하되 동일한 `Variant + Agent + Seed` 조합을 중복 완료 처리하지 않는다.

## 7. Run과 재실행

### 7.1 runs

모든 인간·합성 Run의 공통 문맥과 최종 요약이다.

| 컬럼 | 설명 |
|---|---|
| id / actor_id | Run과 주체 |
| population | human, synthetic |
| season_id / run_ticket_id | 인간 Run 문맥, nullable |
| experiment_variant_id / simulation_batch_id | 합성 Run 문맥, nullable |
| game_version_id / config_id | 사용 규칙과 설정 |
| score_rule_version_id | 점수 규칙, nullable |
| client_platform / client_build | 인간 클라이언트의 플랫폼군·빌드, 합성 Run은 nullable |
| base_seed | 기준 시드 |
| status | created, submitted, verifying, verified, rejected, failed |
| outcome | victory, defeat, aborted, error |
| cleared_stage / total_turns | 진행과 효율 |
| final_hp / max_hp | 종료 체력 |
| final_score | 서버 계산 점수, nullable |
| build_signature | 정규화된 보상 조합 해시 |
| result_hash | 최종 상태 canonical hash |
| started_at / completed_at / verified_at | 시각 |
| rejection_code | 검증 실패 코드 |

제약:

- human Run에는 season_id와 run_ticket_id가 필요하다.
- synthetic Run에는 experiment_variant_id와 simulation_batch_id가 필요하다.
- 한 Batch 안에서 `variant + agent + base_seed`는 unique다.
- 랭킹은 `population = human AND status = verified`만 사용한다.

### 7.2 run_actions

재실행에 필요한 의사결정만 저장한다.

| 컬럼 | 설명 |
|---|---|
| run_id / sequence | 복합 PK, 0부터 증가 |
| stage_index / turn_index | 게임 위치 |
| phase | player_action, reward_choice |
| action_type | strike, guard, technique, item, end_turn, choose_reward |
| payload | 대상·보상 ID 등 JSONB |

상태 전체는 저장하지 않는다. 서버는 시드와 순서화된 행동으로 상태를 재구성한다.

### 7.3 행동 로그 예시

```json
{
  "schemaVersion": 1,
  "runId": "4f1d...",
  "actions": [
    {
      "sequence": 0,
      "stage": 1,
      "turn": 1,
      "phase": "player_action",
      "type": "guard",
      "payload": {}
    },
    {
      "sequence": 1,
      "stage": 1,
      "turn": 1,
      "phase": "player_action",
      "type": "strike",
      "payload": {"target": "enemy_0"}
    },
    {
      "sequence": 12,
      "stage": 1,
      "turn": 5,
      "phase": "reward_choice",
      "type": "choose_reward",
      "payload": {"rewardId": "reinforced_guard"}
    }
  ]
}
```

서버는 sequence, phase, 유효 행동, 실제로 제시된 보상 후보를 재검증한다.

## 8. Telemetry와 요약

### 8.1 run_events

| 컬럼 | 설명 |
|---|---|
| event_id | UUID |
| run_id / sequence | Run FK와 이벤트 순서 |
| event_type | snake_case 이벤트명 |
| schema_version | 이벤트 스키마 |
| stage_index / turn_index | 게임 위치 |
| emitted_at | UTC 기록 시각 |
| payload | 이벤트별 JSONB |

Unique 제약은 `run_id + sequence`다. 이벤트 재전송은 같은 키로 idempotent하게 처리한다.

### 8.2 이벤트 Envelope

```json
{
  "eventId": "3125...",
  "runId": "4f1d...",
  "sequence": 17,
  "eventType": "reward_selected",
  "schemaVersion": 1,
  "stage": 2,
  "turn": 0,
  "emittedAt": "2026-08-25T10:00:00Z",
  "payload": {
    "rewardId": "recovery",
    "category": "sustain",
    "offeredRewardIds": ["recovery", "power", "quick_technique"]
  }
}
```

Game Version, Config, Actor, Seed는 Run FK로 조회한다. 게임 시간 순서는 wall clock이 아니라 sequence로 판단한다.

### 8.3 이벤트 스키마 정책

- 이벤트 이름은 과거형 snake_case를 사용한다.
- optional 필드 추가는 같은 버전에서 허용한다.
- 의미·타입 변경과 필수 필드 제거는 새 버전을 발급한다.
- 알 수 없는 필드는 무시하되 알 수 없는 event_type과 상위 버전은 격리한다.
- 자주 필터링하는 payload 필드는 관계형 컬럼으로 승격한다.

### 8.4 run_stage_summaries

| 컬럼 | 설명 |
|---|---|
| run_id / stage_index | 복합 PK |
| enemy_type / outcome | 적과 결과 |
| turns_used | 사용 턴 |
| start_hp / end_hp | 체력 |
| damage_dealt / damage_taken / blocked | 전투 요약 |
| item_uses | 아이템 사용 수 |

Stage 퍼널과 난도 곡선은 원시 이벤트 전체가 아니라 이 테이블에서 계산한다.

## 9. 랭킹

### 9.1 leaderboard_entries

| 컬럼 | 설명 |
|---|---|
| season_id / player_id | 복합 unique |
| best_run_id | 검증된 최고 Run |
| score | 정렬 값 |
| cleared_stage / total_turns / final_hp_ratio | 동점 규칙 |
| achieved_at | 기록 시각 |

인간 Run 검증 완료 트랜잭션에서 개인 최고 기록일 때만 upsert한다. 실제 rank 번호는 MVP에서 window function으로 계산한다.

### 9.2 랭킹 불변성

- 종료 시즌의 LeaderboardEntry를 동결한다.
- 시즌의 Game, Config, Score Rule을 변경하지 않는다.
- 공개 설정 롤백은 새 시즌으로 처리한다.
- 합성 Run은 LeaderboardEntry를 만들 수 없다.

## 10. 실험 집계와 AI

### 10.1 experiment_metrics

| 컬럼 | 설명 |
|---|---|
| experiment_id / variant_id | 실험 Cell |
| agent_definition_id | Agent |
| metric_key / metric_version | 지표와 계산 코드 버전 |
| sample_size | Run 수 |
| value | 대표 값 |
| lower_bound / upper_bound | 신뢰구간 |
| distribution | 분위수 또는 histogram JSONB |
| input_run_set_hash | 입력 Run 집합 해시 |
| computed_at | 계산 시각 |

Unique 제약은 `experiment + variant + agent + metric_key + metric_version`다.

### 10.2 analysis_reports

| 컬럼 | 설명 |
|---|---|
| id / experiment_id | 식별자와 실험 |
| metric_snapshot_hash | 입력 지표 스냅샷 |
| model_provider / model_name | 사용 모델 |
| prompt_version | 분석 프롬프트 버전 |
| report | 구조화된 분석 JSONB |
| created_at | 생성 시각 |

AI에는 experiment_metrics, 판정 규칙, 대표 Run 링크만 제공한다.

M7 물리 구현에서는 별도 `analysis_reports` 테이블 대신 `analysis_jobs.report`의 불변 JSONB로 저장한다. 동일 행에 Snapshot/hash, 멱등 키, 시도 횟수, lease·오류 코드를 보관한다. 모델 입력은 계산된 Metric Snapshot과 위반 지표로 더 좁혔으며 원시 Run은 전달하지 않는다. [M7 저장 경계](implementation/milestone-07-ai-analysis.md)를 참고한다.

## 11. 감사 데이터

`config_publications`는 설정 승인·배포·롤백과 시즌 전환을 기록한다.

`audit_logs`는 다음을 공통 기록한다.

- actor
- action
- target_type / target_id
- before / after reference
- reason
- occurred_at
- correlation_id

감사 로그는 수정하지 않는다.

## 12. 데이터 처리 흐름

합성 Run:

```text
Run 완료
→ actions/events 배치 저장
→ Run·Stage Summary 계산
→ Cell 완료 수 갱신
→ Batch 완료 시 Experiment Metrics 계산
→ 고정 Metrics Snapshot 생성
→ AI Analysis Report 생성
```

인간 Run:

```text
Run 제출
→ actions 임시 저장
→ 서버 재실행
→ result hash·규칙 검증
→ verified Run과 summary 저장
→ 같은 트랜잭션에서 leaderboard entry 갱신
```

## 13. 인덱스와 조회 전략

초기 필수 인덱스:

- `runs (experiment_variant_id, actor_id, status)`
- `runs (season_id, population, status)`
- `runs (simulation_batch_id, status)`
- `run_events (run_id, sequence)` unique
- `run_actions (run_id, sequence)` primary
- `run_stage_summaries (stage_index, outcome)`
- `leaderboard_entries (season_id, score DESC, total_turns ASC)`
- `experiment_metrics (experiment_id, variant_id, agent_definition_id)`
- `game_configs (game_version_id, version)` unique

JSONB에는 실제 쿼리가 확인되기 전 범용 GIN 인덱스를 만들지 않는다.

## 14. 파티셔닝과 확장 기준

MVP에서는 단일 PostgreSQL과 일반 테이블로 시작한다.

다음 중 하나를 만족하면 run_events 파티셔닝을 검토한다.

- 원시 이벤트 1,000만 건 초과
- 이벤트 테이블 20GB 초과
- 핵심 대시보드 쿼리 p95가 목표를 지속적으로 초과
- 보존 정책에 따른 대량 삭제가 운영에 영향을 줌

Redis, 별도 분석 DB, 메시지 브로커는 측정된 병목이 생기기 전 도입하지 않는다.

## 15. 보존 정책

| 데이터 | MVP 기본 보존 |
|---|---|
| 설정·점수 규칙·실험 정의·감사 로그 | 영구 |
| Run Summary와 Experiment Metric | 영구 |
| 시즌 최고 기록 리플레이 | 프로젝트 수명 |
| 일반 인간 행동 로그 | 시즌 종료 후 90일 |
| 일반 합성 행동·이벤트 로그 | 실험 종료 후 30일 |
| 대표 성공·실패·이상 Run | pin 후 영구 |

초기에는 실제 삭제 전에 만료 대상 dry-run 보고서를 만든다.

무료 공개 DB에서는 저장소 할당량의 70%를 경고 기준으로 삼는다. 기준에 도달하면 원시 합성 Event의 보존 만료를 우선 실행하되, Metric Snapshot의 입력 Run 집합 hash와 pin된 대표 Replay는 유지한다. Provider별 실제 용량은 배포 시점에 확인해 설정값으로 관리한다.

## 16. 개인정보와 보안

- 익명 UUID, 닉네임, credential hash만 저장한다.
- 원본 기기 토큰과 비밀번호를 평문 저장하지 않는다.
- 분석 데이터에는 IP와 기기 고유 식별자를 포함하지 않는다.
- 닉네임 삭제 요청 시 표시명을 익명화할 수 있다.
- AI 입력에는 credential, token, IP를 포함하지 않는다.

## 17. 트랜잭션 경계

다음은 하나의 DB 트랜잭션으로 처리한다.

- 인간 Run 검증 완료 + 점수 계산 + 개인 최고 랭킹 갱신
- 설정 승인 + 감사 로그
- 시즌 종료 + 새 시즌 공개 설정 연결
- Batch Run 결과 저장 + Cell 완료 수 증가

외부 AI 호출은 DB 트랜잭션 안에서 실행하지 않는다. 지표 스냅샷을 먼저 확정한 뒤 비동기로 분석한다.

## 18. 데이터 품질 검증

- Run의 버전 FK와 checksum 존재 확인
- sequence 연속성과 중복 확인
- 합성·인간 Population CHECK 제약
- 인간 Run Ticket 만료·재사용 확인
- Stage와 Turn의 단조 증가 규칙 확인
- Run Summary와 원시 이벤트의 샘플 대조
- 동일 재현 입력의 result hash 일치
- Metric 계산 코드 버전과 입력 Run 집합 해시 기록
