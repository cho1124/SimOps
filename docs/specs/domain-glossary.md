# 용어 사전

상태: 확정

| 용어 | 정의 |
|---|---|
| Game Core | Unity 표현과 외부 IO에 의존하지 않는 게임 규칙·상태 전이 |
| Game Version | 규칙, 상태 전이 순서, RNG 알고리즘을 식별하는 불변 버전 |
| Game Config | 특정 Game Version에 적용되는 수치·활성 콘텐츠의 불변 스냅샷 |
| Score Rule Version | 점수 계수, Par Turn, 반올림 규칙을 식별하는 불변 버전 |
| Season | 하나의 Game Version, 공개 Config, Score Rule로 진행되는 인간 랭킹 기간 |
| Actor | Run을 수행한 주체의 공통 표현 |
| Human Actor | 실제 사용자가 조작한 Actor |
| Synthetic Actor | 규칙·ML·LLM Agent가 조작한 Actor |
| Persona | 실험 목적의 행동 성향 개념 |
| Agent Definition | Persona를 구현한 정책, 파라미터, 코드 checksum의 특정 버전 |
| Run | 특정 버전·설정·시드에서 시작해 승리·패배·중단·오류로 끝난 한 세션 |
| Run Ticket | 인간 Run 시작 시 서버가 버전·설정·시드·만료를 고정한 검증 문맥 |
| Action Log | Run 재실행에 필요한 순서화된 플레이어 의사결정 |
| Telemetry Event | 분석·관측을 위해 기록하는 append-only 사실 |
| Result Hash | 최종 상태를 canonical 형식으로 직렬화해 계산한 식별 해시 |
| Replay | Action Log를 같은 문맥에서 다시 적용해 Run을 재구성하는 과정 |
| Experiment | 하나의 가설, Variant, 대상 Agent, 지표, 판정 규칙의 묶음 |
| Variant | 실험에서 비교할 불변 Config |
| Control | 변경 효과를 비교하는 기준 Variant |
| Treatment | Control과 비교할 변경 Variant |
| Cell | Variant와 Agent Definition의 조합 |
| Simulation Batch | 한 Experiment의 목표 Cell·Seed Run을 실행하는 작업 묶음 |
| Base Seed | Run의 모든 결정론적 난수 흐름을 파생하는 시작 값 |
| Subseed | Encounter, Intent, Reward, Agent용으로 분리된 난수 Seed |
| Metric | 버전이 부여된 계산 정의와 결과 |
| Metric Snapshot | AI 분석과 승인 검토에 사용되는 변경 불가능한 지표 집합 |
| Analysis Report | Metric Snapshot을 근거로 AI가 생성한 구조화된 해석 |
| Publication | 승인 Config를 새 Season에 연결한 운영 변경 기록 |
| Guardrail | 주요 지표 개선과 동시에 악화를 허용하지 않을 안전 지표 |
| Spike | 기술 위험 하나를 제한된 시간과 성공 기준으로 검증하는 최소 구현 |

## 사용 금지 또는 주의 표현

- Synthetic Persona를 실제 인간 세그먼트와 동일하다고 표현하지 않는다.
- 합성 Run의 실패를 인간 이탈 또는 불만으로 표현하지 않는다.
- AI의 해석을 시스템이 계산한 사실과 구분한다.
- Config의 수정과 새 Version 생성을 같은 의미로 사용하지 않는다.
