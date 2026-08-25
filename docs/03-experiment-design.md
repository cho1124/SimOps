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

Control 기준에서 Novice 페르소나가 Stage 3에서 과도하게 실패한다고 가정한다.

실제 Control 시뮬레이션에서 이 현상이 나타나지 않으면, 첫 실험 문제는 관찰된 다른 가장 큰 난도 급증으로 변경한다. 실험 결과를 만들기 위해 문제를 인위적으로 단정하지 않는다.

### 3.2 가설

중반 적 공격력을 직접 낮추는 것보다 Stage 2 이후 회복 보상의 접근성을 높이는 것이 Novice의 중반 통과율을 개선하면서 다른 페르소나의 난도와 빌드 다양성을 덜 훼손한다.

### 3.3 Variant

| Variant | 변경 | 역할 |
|---|---|---|
| Control | 현재 공개 후보 설정 | 비교 기준 |
| Treatment A | Stage 3 적 공격력 감소 | 직접 난도 완화 |
| Treatment B | Stage 2 보상에서 Sustain 출현 가중치 증가 | 간접 생존 지원 |

정확한 변경 폭은 Control 기준선 결과를 확인한 후 고정한다.

### 3.4 실행 구성

- 에이전트: Random, Novice, Aggressive, Defensive, Greedy, Explorer
- Variant: Control, Treatment A, Treatment B
- 반복: Cell당 1,000 Run을 기본값으로 사용
- 총 기본 실행 수: `6 × 3 × 1,000 = 18,000 Run`
- 모든 Variant는 Agent별 동일한 기준 시드 집합을 사용한다.

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

`Novice Stage 3 Pass Rate`

```text
Stage 3을 클리어한 Novice Run 수 / Stage 3에 진입한 Novice Run 수
```

첫 실험의 직접 목표다.

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

## 7. 첫 실험의 잠정 판정 규칙

Treatment 후보는 다음을 모두 만족해야 승인 후보가 된다.

- Novice Stage 3 Pass Rate가 Control보다 의미 있게 개선된다.
- Greedy와 Aggressive의 전체 클리어율이 과도하게 상승하지 않는다.
- 평균 총 턴이 Control 대비 15% 이상 증가하지 않는다.
- 정규화된 보상 선택 엔트로피가 Control 대비 10% 이상 감소하지 않는다.
- 상태 전이 오류와 재현 불일치가 0건이다.

`의미 있는 개선`의 최초 기준은 절대 8%p 이상으로 시작하되, Control 분포를 본 뒤 실험 실행 전에 확정한다. 결과를 본 뒤 기준을 바꾸지 않는다.

## 8. 분석 방법

- Cell별 평균만 보지 않고 분포와 신뢰구간을 표시한다.
- 동일 시드 Control/Treatment 결과의 paired difference를 계산한다.
- 이진 결과는 통과 여부 차이와 효과 크기를 함께 표시한다.
- 턴 수와 체력은 중앙값, 분위수, 분포를 함께 본다.
- 부트스트랩 신뢰구간을 기본 비교 수단으로 사용한다.
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

