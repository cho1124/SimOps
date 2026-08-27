# Workshop-0007: 인증과 인간 Run 검증

상태: 완료

연결 ADR: [ADR-0008](../decisions/0008-identity-and-run-verification.md)

## 결정할 문제

MVP 플레이어를 어떻게 식별하고 어떤 수준으로 랭킹 Run을 신뢰할 것인가?

## 필수 요구사항

- 회원가입 없이 데모 진입이 쉬워야 한다.
- 임의 점수·상태·Reward 제출은 막아야 한다.
- 검증 실패 원인을 추적할 수 있어야 한다.
- 합성 Actor는 인간 랭킹에 들어갈 수 없어야 한다.

## 선택지

### A. 익명 Credential + Signed Run Ticket + 전체 재실행

- 장점: 진입 장벽이 낮고 결정론적 Core의 가치를 활용하며 조작 상태를 탐지
- 단점: 계정 복구·다기기 동기화가 약하고 외부 Seed 탐색은 방지하지 못함

### B. 정식 계정 + Signed Run Ticket + 전체 재실행

- 장점: 계정 복구, 다기기, 제재와 시즌 이력 관리에 유리
- 단점: 이메일·OAuth·개인정보·복구 흐름이 MVP 범위를 키움

### C. 익명 ID + Client Result Sanity Check

- 장점: 구현이 가장 빠르고 검증 Worker가 불필요
- 단점: 핵심 수치와 점수 조작에 취약하고 Replay 신뢰성이 낮음

## Codex 추천

A. 게임 접근성, 포트폴리오의 검증 설계, MVP 범위가 가장 잘 균형을 이룬다. 계정 요구가 생기면 HumanPlayer identity만 확장하고 Replay 프로토콜은 유지할 수 있다.

## 필수 Spike

[재실행·검증 프로토콜](../specs/replay-verification-protocol.md)의 다음 공격을 자동화한다.

- score·HP 변조
- 존재하지 않는 Reward
- Ticket 재사용
- sequence 삭제·중복
- 종료 후 Action 추가

성공 기준은 모두 명시적 거부 코드와 상태 변경 0이다.

## 프로젝트 소유자 결정

- 선택: A, 익명 Credential + Signed Run Ticket + 전체 재실행
- 이유: 정식 계정의 개인정보·복구 범위를 피하면서 결정론적 Core로 랭킹 신뢰성을 확보한다.
- 감수: 기기 삭제·전환 시 계정 복구가 어렵고 고급 Seed 탐색은 완전히 막지 못한다.
- 재검토: 다기기 동기화, 소셜·구매 이력 또는 강한 안티치트가 실제 요구가 될 때 확장한다.

최종 내용은 [ADR-0008](../decisions/0008-identity-and-run-verification.md)과 [ADR-0004](../decisions/0004-verified-season-leaderboard.md)에 기록했다.
