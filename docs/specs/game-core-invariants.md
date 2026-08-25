# Game Core 불변조건

상태: 확정

## 식별 규칙

요구사항 ID prefix는 `CORE`를 사용한다.

## 결정론

- CORE-001: 같은 Game Version, Config checksum, Score Rule, Base Seed, Action Log는 같은 최종 상태와 Result Hash를 만들어야 한다.
- CORE-002: Encounter, Intent, Reward, Agent RNG는 분리된 Subseed를 사용해야 한다.
- CORE-003: 현재 시각, 프레임 속도, 전역 난수, OS locale이 결과에 영향을 주면 안 된다.
- CORE-004: 결과에 영향을 주는 collection 순서는 명시적으로 고정해야 한다.
- CORE-005: 판정 수치는 정수 또는 명시된 fixed-point·반올림 규칙을 사용해야 한다.
- CORE-006: RNG 알고리즘과 Subseed 파생 함수 변경은 새 Game Version을 요구한다.

## Run

- CORE-010: Run은 Stage 1에서 시작한다.
- CORE-011: 일반 Stage 1~5와 Boss Stage 6만 존재한다.
- CORE-012: 승리, 패배, 중단, 오류 중 하나의 종료 결과만 가질 수 있다.
- CORE-013: 종료된 Run에 추가 행동을 적용할 수 없다.
- CORE-014: 플레이어 체력이 0 이하가 되면 해당 해결 단계에서 패배한다.
- CORE-015: Boss를 처치하면 승리하며 추가 보상을 제시하지 않는다.
- CORE-016: 일반 Stage를 클리어해야 다음 Reward Phase로 이동할 수 있다.
- CORE-017: Reward를 하나 선택해야 다음 Stage로 이동할 수 있다.

## Turn과 행동

- CORE-020: Turn Start에서 플레이어 AP는 Config의 기본값으로 초기화되며 MVP 기본 규칙은 2 AP다.
- CORE-021: Enemy Intent는 Player Phase 시작 전에 공개된다.
- CORE-022: 유효 행동 목록에 없는 행동을 적용하면 상태를 변경하지 않고 오류를 반환한다.
- CORE-023: AP 비용이 현재 AP보다 큰 행동은 유효하지 않다.
- CORE-024: Use Item은 충전이 있어야 하며 한 Turn에 한 번만 사용할 수 있다.
- CORE-025: End Turn은 항상 유효하고 남은 AP를 소비한다.
- CORE-026: Player Phase 종료 후 공개된 Enemy Intent를 정확히 한 번 실행한다.
- CORE-027: Turn 종료 시 임시 Block과 상태 효과를 정의된 순서로 갱신한다.
- CORE-028: 최대 Turn 제한에 도달하면 명시된 aborted 또는 defeat 규칙으로 종료하며 무한 진행할 수 없다.

## 보상

- CORE-030: 일반 Stage 클리어 시 활성 Pool에서 서로 다른 Reward 후보 3개를 제시한다.
- CORE-031: 제시되지 않은 Reward를 선택할 수 없다.
- CORE-032: Reward 후보 생성은 Reward Subseed에만 의존한다.
- CORE-033: Reward 중첩 한도를 초과하는 후보는 제공하지 않는다.
- CORE-034: 가능한 후보가 3개 미만이면 Game Config 검증 실패로 취급한다.
- CORE-035: 선택된 Reward와 당시 제시된 후보 전체를 기록해야 한다.

## 버전과 설정

- CORE-040: Game Core는 실행 시작 후 Game Version과 Config를 교체할 수 없다.
- CORE-041: Config checksum 불일치는 Run 시작 전에 거부해야 한다.
- CORE-042: Config에 존재하지 않는 Content ID는 명시적 검증 오류다.
- CORE-043: 수치 범위와 가중치 합은 Run 실행 전에 검증한다.
- CORE-044: Game Version과 호환되지 않는 Config는 실행할 수 없다.

## 점수

- CORE-050: 점수는 검증된 최종 상태에서만 계산한다.
- CORE-051: 입력 Action Log가 같은 경우 점수도 같아야 한다.
- CORE-052: 피해량과 처치 수는 MVP Score Rule에 직접 가산하지 않는다.
- CORE-053: 점수 계산은 Score Rule Version에 기록된 반올림 규칙을 따라야 한다.
- CORE-054: 합성 Run의 점수는 분석에 사용할 수 있지만 인간 LeaderboardEntry를 만들 수 없다.

## 테스트 적용

- 모든 MUST 불변조건은 자동 테스트 ID와 연결한다.
- 결정론 Golden Fixture는 Game Version마다 유지한다.
- Config 경계값과 유효하지 않은 Action은 property-based test 후보로 관리한다.
