# 실험 지표 사전

상태: 확정

## 공통 규칙

- 모든 Metric은 `metric_key`와 `metric_version`을 가진다.
- Population, Game Version, Config, Agent Version을 섞어 집계하지 않는다.
- 분모가 0이면 0이 아니라 null과 reason을 반환한다.
- 평균만 저장하지 않고 표본 수와 가능한 경우 분포·신뢰구간을 함께 저장한다.
- Run 집합과 계산 코드의 hash를 기록한다.

## 차원

기본 dimension:

- population
- experiment
- variant
- agent_definition
- game_version
- config_version
- stage
- outcome
- reward_category
- reward_id
- action_type
- client_platform (인간 Run 플랫폼 비교 시에만 사용)

`client_platform`은 품질·호환성 관측용 차원이다. 플랫폼별 표본 수를 함께 표시하고, 합성 실험의 Variant 효과와 혼합해 해석하지 않는다.

## 결과 Metric

### run_clear_rate.v1

```text
victory Run 수 / 유효 terminal Run 수
```

제외: error, validation rejected. aborted는 원인별 별도 표시 후 기본 분모 포함 정책을 실험 정의에 고정한다.

### stage_entry_rate.v1

```text
Stage N 진입 Run 수 / 유효 시작 Run 수
```

### stage_pass_rate.v1

```text
Stage N 클리어 Run 수 / Stage N 진입 Run 수
```

첫 실험 Primary Metric은 Novice의 `stage_pass_rate.v1, stage=3`이다.

### total_turns.v1

유효 Run의 총 Turn 분포. mean, median, p10, p90, p95를 계산한다.

### encounter_turns.v1

Stage별 사용 Turn 분포다.

### final_hp_ratio.v1

```text
max(0, final_hp) / max_hp
```

## 선택 다양성

### action_share.v1

```text
특정 action_type 선택 수 / 전체 player action 선택 수
```

### reward_pick_share.v1

```text
특정 reward_id 선택 수 / 전체 reward 선택 수
```

선택 기회가 달랐는지 확인하기 위해 offer 수와 offer 대비 pick rate도 함께 본다.

### reward_pick_given_offer.v1

```text
특정 Reward 선택 수 / 해당 Reward가 제시된 횟수
```

### normalized_reward_entropy.v1

```text
H = -Σ p_i log(p_i)
normalized_H = H / log(k)
```

`k`는 해당 집계에서 선택 가능한 Reward 수다. 선택 수가 없거나 k가 1 이하면 null이다.

### unique_build_count.v1

정렬된 Reward ID와 stack count로 만든 build_signature의 고유 개수다. 표본 수와 함께 해석한다.

## 가드레일

### dominant_reward_share.v1

가장 많이 선택된 Reward의 reward_pick_share다. 0.5 이상 지속 시 이상 후보로 표시한다.

### max_turn_reached_rate.v1

```text
최대 Turn 제한으로 종료된 Encounter 수 / 전체 Encounter 수
```

### invalid_transition_count.v1

Game Core가 거부한 내부 상태 전이 수다. 정상 Agent Simulation에서는 0이어야 한다.

### replay_mismatch_count.v1

같은 입력의 Authoritative Result Hash 불일치 수다. 검증 세트에서 0이어야 한다.

## 시스템 Metric

- simulation_runs_per_second.v1
- simulation_run_duration_ms.v1: p50/p95
- verification_duration_ms.v1: p50/p95
- job_claim_delay_ms.v1
- job_retry_count.v1
- event_batch_insert_duration_ms.v1
- ai_analysis_duration_ms.v1
- ai_schema_failure_count.v1

## 비교 결과

Control과 Treatment 비교에는 다음을 저장한다.

- absolute_difference
- relative_difference
- paired_sample_size
- confidence_interval
- calculation_method

결과 화면과 AI 입력은 Metric 정의 링크를 포함해야 한다.
