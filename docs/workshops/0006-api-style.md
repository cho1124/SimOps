# Workshop-0006: API 스타일

상태: 완료

연결 ADR: [ADR-0007](../decisions/0007-api-style.md)

## 결정할 문제

Unity, Dashboard, Backend 사이의 외부 계약을 어떤 API 스타일로 제공할 것인가?

## 필수 요구사항

- Unity에서 안정적으로 호출 가능해야 한다.
- DTO와 오류를 버전 관리해야 한다.
- 비동기 resource의 상태 조회를 표현해야 한다.
- 문서와 테스트 client를 생성할 수 있어야 한다.

## 선택지

### A. REST/JSON + OpenAPI

- 장점: Unity와 Browser 도구 지원이 넓고 디버깅이 쉬움, 공개 API 문서화 용이
- 단점: streaming과 강한 schema 계약은 별도 규칙 필요

### B. gRPC/Protobuf

- 장점: 강한 IDL, code generation, 효율적인 binary, streaming
- 단점: Browser는 gRPC-Web 등 추가 계층이 필요하고 운영 Dashboard의 단순 조회에는 복잡
- 공식 자료: [gRPC introduction](https://grpc.io/docs/what-is-grpc/introduction/)

### C. GraphQL

- 장점: Dashboard가 필요한 필드를 조합하고 여러 분석 View를 유연하게 조회
- 단점: Run 제출·Job command·파일형 Action Log에는 이점이 작고 cache·권한·쿼리 비용 통제가 필요

## Codex 추천

A. Unity와 Dashboard가 함께 사용하는 첫 계약은 REST/JSON이 가장 투명하다. 내부 Simulation streaming 요구가 실제로 생기면 gRPC를 해당 경계에 제한적으로 추가할 수 있다.

## 선택 전 Spike

다음 세 API를 각 후보에서 모델링한다.

- Run Ticket 발급
- Action Log 제출 후 202/비동기 상태 조회
- Experiment Cell Metric filter 조회

비교:

- Unity client 생성과 오류 처리
- Browser debugging
- schema evolution
- payload 크기
- 문서 가독성

## 프로젝트 소유자 결정

- 선택: A, REST/JSON + OpenAPI
- 이유: Unity와 Browser에서 범용적으로 호출·디버깅하기 쉽고 현재 계약은 조회·명령·비동기 상태 확인이 중심이다.
- 감수: gRPC의 binary·IDL 효율과 GraphQL의 field 조합 유연성은 포기한다.
- 재검토: streaming, 대용량 payload 또는 과도한 REST query 조합이 측정될 때 해당 경계만 확장한다.

최종 내용은 [ADR-0007](../decisions/0007-api-style.md)에 기록했다.
