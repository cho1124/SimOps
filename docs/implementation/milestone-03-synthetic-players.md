# 마일스톤 3: 합성 플레이어

상태: 완료

검증일: 2026-08-27

## 구현 결과

Game Core와 분리된 `.NET Standard 2.1` Agent Core를 만들었다. Agent는 사람 UI와 같은 `GameObservation`과 유효 행동 목록만 받고 `AgentDecision`을 반환한다.

```text
Initialize(AgentContext)
Decide(GameObservation) -> AgentDecision
OnRunEnded(RunResult)
```

Decision에는 선택한 `GameAction`, Policy Version, 결정 당시 Agent RNG State, 선택 근거가 들어간다. Agent 난수는 Game의 Intent·Reward RNG와 다른 `Agent` Subseed Stream을 사용한다.

구현한 Agent Version은 모두 `1.0.0`이다.

| Persona | 구현 정책 |
|---|---|
| Random | 현재 유효 행동과 제시 보상을 균등 무작위 선택 |
| Novice | 단순 단기 효용과 35% seeded mistake 혼합 |
| Aggressive | Strike·Technique와 Offense 보상에 높은 효용 |
| Defensive | Heavy Intent 대응 Guard와 Defense·Sustain 보상에 높은 효용 |
| Greedy | 처치 속도, Heavy 대응, AP·cooldown 보상을 함께 최적화 |
| Explorer | Run 안에서 적게 사용한 행동·보상에 novelty bonus 적용 |

이 이름은 실제 사용자 세그먼트가 아니라 실험용 행동 모델이다.

## Headless Simulation

`SimOps.Simulation.Cli`는 모든 Persona에 같은 Seed 집합을 적용하고 결과·행동·보상·빌드 지표와 처리량을 계산한다.

```powershell
dotnet run --project src/SimOps.Simulation.Cli -c Release -- --runs 1000 --json artifacts/simulation/persona-baseline.json
```

JSON에는 Game·Config·Score Rule Version과 checksum, Run 수, 처리 시간, Persona별 지표가 함께 기록된다. 현재는 로컬 분석 기준선이며 마일스톤 4에서 Run·Event 저장 모델로 연결한다.

## 6,000 Run 기준선

환경: Windows 11, .NET SDK `10.0.101`, Release, 단일 CLI 프로세스
조건: Persona 6종 × Seed 0~999, 기본 Config

| Persona | Clear | Stage 3 Pass | 평균 Turn | 평균 Score | Strike | Guard | Offense 보상 | Defense+Sustain | 보상 Entropy | 고유 Build |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Random | 36.4% | 99.8% | 29.5 | 60,646 | 27.1% | 27.2% | 24.8% | 52.2% | 0.999 | 855 |
| Novice | 97.2% | 100.0% | 18.6 | 84,064 | 47.3% | 12.3% | 36.0% | 50.5% | 0.976 | 790 |
| Aggressive | 95.6% | 100.0% | 13.6 | 83,409 | 50.4% | 0.0% | 60.5% | 11.9% | 0.834 | 391 |
| Defensive | 99.7% | 100.0% | 29.8 | 85,045 | 8.7% | 56.9% | 0.3% | 91.4% | 0.795 | 346 |
| Greedy | 100.0% | 100.0% | 13.7 | 86,261 | 35.7% | 9.0% | 36.2% | 12.4% | 0.841 | 296 |
| Explorer | 99.9% | 100.0% | 19.6 | 86,023 | 32.4% | 31.7% | 24.6% | 50.8% | 1.000 | 567 |

측정 처리량은 약 `17,351 Run/s`였다. 테스트 내부의 별도 3,000 Run 반복 측정은 약 `18,595~22,289 Run/s`였으며, 초기 목표 `100 Run/s`를 크게 넘었다. 장비와 실행 조건이 달라지면 다시 측정한다.

## 자동 검증

- AGENT-001: 6종 × 1,000 Seed가 거부된 상태 전이 없이 terminal 도달
- AGENT-002: 6종 × 100 Seed의 Action Log와 Result Hash 재실행 일치
- AGENT-003: Aggressive 공격·Offense, Defensive Guard·Defense/Sustain, Greedy 효율, Explorer 다양성 신호 확인
- AGENT-004: Headless 처리량이 초기 목표 100 Run/s 이상
- METRIC-001: 분모가 없는 지표는 null과 reason을 반환하고 서로 다른 Agent Version 집계를 거부
- Game Core 기존 13개 명세 회귀 없음

## 해석과 다음 실험 영향

설계 문서의 예시였던 `Novice Stage 3 과도 실패`는 현재 기준선에서 관찰되지 않았다. Novice Stage 3 Pass는 100%이고 전체 Clear도 97.2%다. 따라서 실제 첫 Treatment를 고정할 때 이 가설을 그대로 사용하지 않고, 마일스톤 4 이후 Stage별 저장 지표에서 관찰되는 난도 급증이나 Persona 간 격차를 근거로 새 문제를 선택해야 한다.

또한 Defensive의 평균 Turn이 29.8로 높다. 이것은 방어 성향의 의도된 신호이면서 Turn 증가 Guardrail 후보이므로 이후 실험 대시보드에서 별도로 표시한다.
