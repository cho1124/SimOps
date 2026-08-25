# 플랫폼 독립 명세

이 디렉터리는 Backend, DB, Queue, Dashboard 플랫폼을 선택하기 전에도 확정할 수 있는 제품·도메인 계약을 보관한다.

## 문서

- [용어 사전](domain-glossary.md)
- [Game Core 불변조건](game-core-invariants.md)
- [상태 전이](state-machines.md)
- [재실행·검증 프로토콜](replay-verification-protocol.md)
- [지표 사전](metric-catalog.md)
- [검증 매트릭스](validation-matrix.md)
- [위험 목록](risk-register.md)

## 사용 규칙

- 기술 후보는 이 명세를 만족하는지를 기준으로 비교한다.
- 구현과 명세가 충돌하면 의도된 변경인지 먼저 확인한다.
- 의도된 변경은 관련 기획 문서와 ADR을 함께 수정한다.
- 명세의 MUST는 MVP 완료에 필수, SHOULD는 기본 권장, MAY는 선택 사항이다.
