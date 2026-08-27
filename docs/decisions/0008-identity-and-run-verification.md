# ADR-0008: 사용자 식별과 인간 Run 검증

상태: 승인

결정일: 2026-08-27

연결 워크숍: [Workshop-0007](../workshops/0007-auth-and-run-verification.md)

## 고려한 선택지

- 익명 Credential + Ticket + 전체 Replay
- 정식 계정 + Ticket + 전체 Replay
- 익명 ID + Client Result 검사

## 결정

MVP 플레이어는 익명 credential로 식별한다. 인간 랭킹 Run은 Signed Run Ticket과 전체 Action Log 서버 재실행으로 검증한다.

- 서버에는 credential hash만 저장한다.
- Ticket은 player, season, Game Version, Config, Score Rule, Seed, nonce와 만료에 바인딩한다.
- 서버가 계산한 결과와 점수만 권위 있는 값으로 사용한다.
- PC·Android 간 익명 계정 이전은 MVP에서 지원하지 않는다.

## 이유

- 회원가입·복구·개인정보 범위를 추가하지 않고 데모 진입 장벽을 낮춘다.
- 결정론적 Game Core를 이용해 임의 점수·상태·Reward 조작을 탐지한다.
- Replay와 랭킹 검증이 같은 Action Log를 사용한다.

## 결과와 포기한 것

- 기기 삭제·교체와 PC·Android 전환 시 익명 계정 복구가 어렵다.
- Seed를 외부 도구로 탐색하는 고급 보조 플레이는 완전히 막지 못한다.
- 제출 이후 비동기 검증 대기 시간이 생긴다.

## 재검토 조건

- 실제 사용자에게 다기기 동기화·계정 복구가 중요해짐
- 제재·소셜·구매 이력처럼 지속 Identity가 필요한 기능을 도입함
- 재실행 비용이 처리량 목표를 넘거나 더 강한 서버 권위 모델이 필요해짐
