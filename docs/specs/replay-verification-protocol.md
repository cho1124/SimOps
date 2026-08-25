# 재실행·검증 프로토콜

상태: 확정

## 목적

인간 Run의 임의 상태·점수 조작을 탐지하고, 합성 Run과 오류를 동일한 입력으로 재현한다.

## 권위 있는 입력

```text
Game Version
+ Game Core Checksum
+ Config Version/Checksum
+ Score Rule Version/Checksum
+ Base Seed
+ Ordered Action Log
= Authoritative Result
```

클라이언트가 제출한 final score, HP, Stage, Result Hash는 비교 자료일 뿐 권위 있는 결과가 아니다.

## 인간 Run 발급

1. 클라이언트가 활성 Season으로 Run 시작을 요청한다.
2. 서버가 Player, Season, Game, Config, Score Rule, Base Seed, nonce, 만료를 고정한다.
3. 서버가 위 문맥에 서명한 Run Ticket을 발급한다.
4. Ticket은 한 번만 제출할 수 있다.
5. 클라이언트는 Ticket의 Config와 Game Core checksum을 확인한 뒤 시작한다.

## 제출 Envelope

```json
{
  "runTicket": "...",
  "idempotencyKey": "...",
  "clientGameCoreChecksum": "...",
  "actionLogSchemaVersion": 1,
  "actions": [],
  "clientResultHash": "...",
  "clientSummary": {
    "outcome": "victory",
    "clearedStage": 6,
    "totalTurns": 31
  }
}
```

## 검증 순서

1. Envelope와 Action Log 크기 제한 확인
2. 인증, Ticket 서명, 소유자, 만료, 재사용 확인
3. Game·Config·Score Rule checksum 확인
4. Action sequence 연속성 확인
5. 같은 Game Core에서 Base Seed로 Reset
6. Action을 순서대로 적용하며 phase·유효성 확인
7. 종료 후 추가 Action 존재 여부 확인
8. Authoritative Result와 Result Hash 계산
9. 클라이언트 Summary·Hash와 비교
10. 검증 성공 시 점수 계산과 Leaderboard upsert

## 대표 거부 코드

| 코드 | 의미 | 재시도 |
|---|---|---|
| TICKET_INVALID | 서명 또는 형식 오류 | 아니오 |
| TICKET_EXPIRED | 제출 기한 초과 | 아니오 |
| TICKET_REUSED | 이미 사용된 Ticket | 아니오 |
| VERSION_MISMATCH | Game·Config·Score Rule 불일치 | 아니오 |
| CHECKSUM_MISMATCH | Game Core 또는 Config hash 불일치 | 아니오 |
| ACTION_SEQUENCE_INVALID | 누락·중복·역순 sequence | 아니오 |
| ACTION_NOT_ALLOWED | 현재 상태에서 불가능한 행동 | 아니오 |
| REWARD_NOT_OFFERED | 제시되지 않은 Reward 선택 | 아니오 |
| ACTION_AFTER_TERMINAL | 종료 후 행동 존재 | 아니오 |
| RESULT_MISMATCH | 클라이언트와 권위 결과 불일치 | 아니오 |
| VERIFY_INTERNAL_ERROR | 검증기 내부 오류 | 예 |

## Canonical Result

Result Hash 입력에는 다음을 포함한다.

- Game·Config·Score Rule checksum
- Base Seed
- outcome
- Stage와 Turn
- 플레이어·적의 최종 상태
- 정렬된 Reward와 상태 효과
- 점수 구성 요소
- Action count

Map과 Set은 key를 정렬하고 숫자·문자열 인코딩을 고정한다. 디버그 시각, 표시 텍스트, locale은 제외한다.

## Idempotency

- VERIFY-001: 같은 Player, Ticket, idempotency key의 동시·반복 제출은 하나의 Run resource만 만들어야 한다.
- VERIFY-002: 검증 Worker가 중단돼 Job이 재실행돼도 Verified Run과 Leaderboard 결과는 한 번만 반영돼야 한다.
- 같은 Player와 idempotency key의 재제출은 기존 Run resource를 반환한다.
- Ticket used 처리와 Run 생성은 같은 트랜잭션으로 수행한다.
- 검증 Job은 at-least-once 실행될 수 있으므로 Verified 결과와 Leaderboard upsert는 중복 안전해야 한다.

## 랭킹 요구사항

- RANK-001: 검증된 점수가 기존 개인 최고점보다 낮으면 LeaderboardEntry를 변경하지 않는다.
- RANK-002: 점수가 같으면 Score Rule에 고정된 Stage, Turn, HP, 달성 시각 순서로 최고 Run을 결정한다.
- RANK-003: 합성 Run과 Rejected 인간 Run은 LeaderboardEntry를 만들 수 없다.
- RANK-004: Closed Season의 LeaderboardEntry는 변경할 수 없다.

## 리플레이

- 공개 리플레이는 Ticket credential과 내부 감사정보를 제외한다.
- 리플레이에는 Game·Config·Score Rule 버전, Seed, Action Log가 필요하다.
- Game Core가 해당 Game Version을 지원하지 않으면 재생 불가 이유를 명시한다.
- 종료 Season의 상위 기록과 대표 실패 Run은 보존 대상으로 pin할 수 있다.

## 위협 모델과 한계

방지 또는 탐지 대상:

- 임의 점수·체력·공격력 제출
- 존재하지 않는 Action·Reward
- Config 변경
- Ticket 재사용
- Action 누락·추가

MVP에서 완전히 방지하지 않는 대상:

- 공개된 Seed와 규칙을 외부 도구로 반복 탐색
- 입력 자동화
- 클라이언트 메모리 읽기
- 계정·기기 다중 생성

해당 위협이 실제 제품 요구가 되면 서버 권위 진행, Seed 정보 제한, 행동 시간 제약, 별도 안티치트 정책을 재검토한다.
