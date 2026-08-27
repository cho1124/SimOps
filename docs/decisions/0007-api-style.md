# ADR-0007: API 스타일

상태: 승인

결정일: 2026-08-27

연결 워크숍: [Workshop-0006](../workshops/0006-api-style.md)

## 고려한 선택지

- REST/JSON + OpenAPI
- gRPC/Protobuf
- GraphQL

## 결정

Unity, Dashboard와 Backend의 외부 계약은 버전된 REST/JSON API와 OpenAPI로 제공한다.

- `/api/v1` version prefix를 사용한다.
- 장시간 작업은 `202 Accepted`와 상태 resource를 반환한다.
- 쓰기 요청은 idempotency key를 지원한다.
- 오류는 안정된 code, retryable, correlation ID를 포함한다.

## 이유

- Unity와 Browser 양쪽에서 호출·디버깅·문서화하기 쉽다.
- 현재 요구는 resource 조회, command 접수와 비동기 상태 확인이 중심이며 streaming 요구가 없다.
- OpenAPI로 계약 테스트와 client 생성 경로를 만들 수 있다.

## 결과와 포기한 것

- binary 효율과 강한 IDL은 gRPC보다 약하다.
- Dashboard의 임의 field 조합은 GraphQL보다 제한적이다.
- DTO version과 호환성 규칙을 직접 관리한다.

## 재검토 조건

- 내부 고빈도 streaming 또는 대용량 binary 계약이 실제로 필요해짐
- Dashboard query 조합 폭이 REST endpoint 수와 over-fetching을 과도하게 늘림
- Action Log payload 측정 결과 JSON 비용이 허용 범위를 지속적으로 초과함
