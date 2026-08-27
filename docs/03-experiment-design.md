# 실험 및 측정 기획

상태: 확정

## 1. 목적

합성 플레이어를 이용해 밸런스 변경의 방향, 회귀, 극단적인 선택 집중을 출시 전에 반복 가능한 조건에서 탐색한다.

이 문서의 지표는 합성 플레이 실험을 위한 것이며 실제 리텐션, 매출, 인간 만족도를 직접 의미하지 않는다.

## 2. 실험 단위

### Experiment

하나의 가설, Control, 하나 이상의 Treatment, 대상 에이전트, 시드 정책, 성공 기준을 묶는다.

### Cell

`Variant × Agent Definition` 조합이다.

### Run

특정 Game Version, Config Version, Agent Version, Seed로 실행한 한 번의 게임이다.

## 3. 첫 번째 대표 실험

### 3.1 관찰 문제

2026-08-27 소유자 승인으로 첫 실험을 **난도 곡선 재조정**으로 변경했다.

기준선 Seed 0~999에서 Novice는 Stage 1~5를 모두 통과하고 보스에서만 28회 실패했다. Greedy는 1,000회 모두 클리어했다. 따라서 최초의 `Novice Stage 3 과도 실패` 가정과 통과율 8%p 개선 기준은 폐기한다. 실제 인간의 난도·만족도는 아직 검증하지 않았다.

### 3.2 가설

Stage 1을 유지한 채 단계별 적 기본 공격력을 점진적으로 강화하면, 일괄 강화보다 Novice의 누적 실패를 목표 곡선에 가깝게 분산하면서 새 보스전 급증과 다른 Persona의 성적 손실을 제한할 수 있는지 검증한다. 성공은 보장하지 않는다.

### 3.3 Variant

| Variant | 변경 | 역할 |
|---|---|---|
| Control | 기존 baseline, 변경 없음 | 비교 기준 |
| Uniform | Stage 2~6 기본 공격력 150% | 일괄 강화 |
| Ramped | Stage 1~6 기본 공격력 100/120/140/160/180/200% | 점진 강화 |

정수 공격력은 올림한다. HP·보상·Agent 정책은 변경하지 않는다. 두 후보는 총 강화량도 다르므로 곡선 형태만의 독립적 인과효과로 해석하지 않는다. [실행 전 사전 등록](experiments/difficulty-curve-001.md)과 [JSON 정본](experiments/difficulty-curve-001.json)에 변경 폭과 판정 기준을 고정했다.

### 3.4 실행 구성

- 에이전트: Random, Novice, Aggressive, Defensive, Greedy, Explorer
- Variant: Control, Uniform, Ramped
- 반복: Cell당 1,000 Run을 기본값으로 사용
- 총 기본 실행 수: `6 × 3 × 1,000 = 18,000 Run`
- 모든 Variant는 Agent별 동일한 기준 시드 집합을 사용한다.
- 탐색에 사용한 Seed 0~999와 겹치지 않는 평가 Seed 10000~10999를 사용한다.

## 4. 공정한 난수 정책

하나의 기준 Seed에서 하위 시스템별 서브시드를 파생한다.

```text
baseSeed
├─ encounterSeed
├─ intentSeed
├─ rewardSeed
└─ agentSeed
```

목적:

- 전투 수치 변경이 보상 후보의 난수 순서를 불필요하게 바꾸지 않게 한다.
- Agent의 탐색 노이즈와 게임 환경의 난수를 분리한다.
- Control과 Treatment의 paired comparison을 가능하게 한다.

서브시드 파생 알고리즘은 Game Version에 포함하고 테스트로 고정한다.

## 5. 합성 페르소나 검증

밸런스 실험 전에 에이전트가 의도한 차이를 보이는지 검증한다.

| 페르소나 | 기대 검증 신호 |
|---|---|
| Random | 유효 선택 내 높은 분산, 가장 낮은 평균 효율 |
| Novice | Intent 대응 실패와 비효율 행동이 Greedy보다 많음 |
| Aggressive | Strike·Technique와 Offense 보상 비율이 높음 |
| Defensive | Guard·회복과 Defense/Sustain 보상 비율이 높음 |
| Greedy | 1턴 기대 효용과 Tempo 지표가 높음 |
| Explorer | 보상 조합 수와 선택 엔트로피가 높음 |

기대 신호가 나타나지 않는 Agent Version은 밸런스 실험에 사용하지 않는다.

## 6. 핵심 지표

### 6.1 Primary Metric

`novice_curve_target_mae.v1`

```text
각 Stage의 누적 실패율 = 1 - 해당 Stage 클리어 Run 수 / 전체 유효 시작 Run 수
목표 누적 실패율 = [0%, 2%, 5%, 10%, 20%, 30%]
MAE = 각 Stage의 |실제 누적 실패율 - 목표| 합 / 6
```

낮을수록 사전 목표에 가깝다. 목표 곡선은 첫 탐색용 설계 제약이며 관찰로 추정한 인간 선호가 아니다. 조건부 Stage 통과율은 보조 지표로 유지한다.

### 6.2 Outcome Metrics

- Agent별 전체 클리어율
- Stage별 진입·통과·실패율
- Run당 총 턴 수
- Encounter별 턴 수
- 종료 시 체력 비율
- 아이템 소비 시점

### 6.3 Choice Diversity Metrics

- 행동 선택 비율
- 보상 계열·개별 보상 선택 비율
- 고유 빌드 조합 수
- 선택 엔트로피
- 가장 많이 선택된 보상의 점유율

엔트로피는 비교를 쉽게 하기 위해 가능한 선택 수로 정규화한 `0~1` 값도 함께 저장한다.

### 6.4 Guardrail Metrics

- Greedy 전체 클리어율의 과도한 상승
- 평균 전투 턴의 과도한 증가
- 특정 보상 선택 점유율 과반 지속
- 최대 턴 제한 도달률
- 유효 행동 없음 또는 상태 전이 오류
- 특정 페르소나에서만 발생하는 비정상 실패

### 6.5 System Metrics

- 초당 완료 Run 수
- p50/p95 Run 실행 시간
- 이벤트 배치 크기와 처리 시간
- 서버 재실행 검증 시간
- 재현 결과 불일치 수

## 7. 첫 실험의 사전 고정 판정 규칙

Treatment 후보는 다음을 모두 만족해야 승인 후보가 된다.

- Novice MAE가 Control보다 2%p 이상 감소하고 paired bootstrap 차이의 95% CI 상한이 0 미만이다.
- Novice 클리어율은 60~85%, Stage 1 통과율 ≥99%, Stage 3까지 누적 실패 ≤10%다.
- Novice의 인접 Stage 조건부 실패율 증가폭은 15%p 이하다. 표본이 없는 Stage는 평가 불가로 탈락한다.
- Greedy 전체 클리어율 ≥90%, Greedy−Novice 격차 ≥10%p, Aggressive/Defensive/Explorer 클리어율 각각 ≥60%다.
- 모든 Persona의 양쪽 Variant 모두를 클리어한 동일 Seed 집합에서 평균 Turn 비율 ≤1.15다. 교집합이 없으면 탈락한다.
- 모든 Persona의 정규화된 보상 선택 엔트로피가 Control의 90% 이상이다. 단일 보상 점유율 ≤50%, Turn 제한 종료 Encounter 비율 ≤1%다.
- 상태 전이 오류와 재현 불일치가 0건이다.

수치 기준은 실행 전에 고정했다. 후보가 없으면 정상적인 실험 기각 결과로 기록한다. 결과를 본 뒤 기존 기준을 바꾸지 않고 새 실험 ID를 발급한다. 후보 통과도 게시 승인이 아니며 기존 인간 랭킹 시즌을 자동 변경하지 않는다.

## 8. 분석 방법

- Cell별 평균만 보지 않고 분포와 신뢰구간을 표시한다.
- 동일 시드 Control/Treatment 결과의 paired difference를 계산한다.
- 이진 결과는 통과 여부 차이와 효과 크기를 함께 표시한다.
- 턴 수와 체력은 중앙값, 분위수, 분포를 함께 본다.
- 부트스트랩 신뢰구간을 기본 비교 수단으로 사용한다.
- 첫 실험은 같은 Seed 인덱스를 함께 복원 추출하는 2,000회 percentile bootstrap(고정 Seed 20260827)을 사용한다. 곡선 MAE는 각 재표집의 누적 비율부터 다시 계산한다.
- CI는 합성 Seed 표본 변동성이다. 다중 비교 보정된 확증 결과나 인간 효과로 해석하지 않는다.
- p-value 하나만으로 배포 결정을 내리지 않는다.

Cell당 1,000회는 MVP의 기본값이며 고정된 통계적 보장은 아니다. 효과 크기와 분산을 확인한 후 반복 수를 조정할 수 있다.

## 9. 이벤트 요구사항

| 이벤트 | 필요한 이유 |
|---|---|
| `run_started` | 실행 문맥과 시작 검증 |
| `encounter_started` | Stage 진입과 적 상태 |
| `turn_started` | Intent와 턴 단위 상태 |
| `action_selected` | 행동 분포와 리플레이 |
| `reward_offered` | 선택 가능 집합 기록 |
| `reward_selected` | 보상 선호와 빌드 구성 |
| `encounter_ended` | Stage 통과·실패와 효율 |
| `run_ended` | 최종 결과와 요약 |
| `validation_failed` | 인간 Run 검증 실패 원인 |
| `simulation_failed` | 실행 오류와 재시도 판단 |

행동 로그는 재실행의 정본이고, 이벤트는 분석을 위한 관측 기록이다. 두 데이터의 역할을 구분한다.

## 10. AI 분석가

### 입력

- 실험 정의와 사전 판정 규칙
- 서버가 계산한 Cell별 지표
- Control 대비 효과 크기와 신뢰구간
- 가드레일 위반
- 대표 성공·실패 Run 링크

### 출력

- 가장 큰 변화
- 페르소나별 영향
- 가설과 일치하거나 충돌하는 결과
- 확인해야 할 부작용
- 다음 실험 제안
- 사용한 근거 지표

### 제한

- 원시 DB에 임의 SQL을 실행하지 않는다.
- 계산되지 않은 숫자를 사실처럼 생성하지 않는다.
- 설정을 승인·배포·롤백하지 않는다.
- 합성 결과를 실제 사용자 효과로 표현하지 않는다.

## 11. 인간 데이터가 생긴 이후

- 인간과 합성 데이터를 별도 Population으로 저장한다.
- 점수, 행동, 보상, 실패 구간의 분포 거리를 비교한다.
- 인간 로그를 기준으로 Agent 파라미터를 보정하되 새 Agent Version을 발급한다.
- 보정 데이터와 평가 데이터를 분리한다.
- 한 세션 게임에서는 리텐션이나 이탈 의도를 추정하지 않는다.

## 12. 실험 생명주기

```text
Draft
→ Ready: 가설·Variant·지표·판정 규칙 고정
→ Running
→ Analyzing
→ Decided: 승인 후보·기각·재실험
→ Archived
```

Running 이후에는 가설, Variant 내용, 판정 규칙을 수정하지 않는다. 변경이 필요하면 새 실험을 만든다.
