# ADR-0005: Backend 플랫폼

상태: 승인

결정일: 2026-08-27

연결 워크숍: [Workshop-0003](../workshops/0003-backend-platform.md)

## 맥락

API와 Worker의 언어·프레임워크는 Game Core 공유, 웹·데이터 학습 범위, 운영 복잡도에 영향을 준다.

## 고려한 선택지

- ASP.NET Core / C#
- FastAPI / Python
- NestJS / TypeScript

## 결정

API는 ASP.NET Core, Worker는 .NET Worker Service를 사용한다. 구현 시점의 지원 중인 .NET LTS 최신 patch를 고정한다.

## 이유

- `SimOps.Game.Core`를 Backend와 Worker가 직접 참조해 서버 검증과 Simulation의 구현 drift를 줄인다.
- C# 타입과 테스트 자산을 공유하면서 웹 API, 트랜잭션, 비동기 처리와 운영 설계 학습에 집중한다.
- 인증, OpenAPI, Background Service와 OpenTelemetry 통합 경로가 성숙해 있다.

## 결과와 포기한 것

얻는 것:

- Game Core와 Backend의 단일 언어 경계
- API·Worker의 공통 Application·Domain 코드
- 일관된 dependency injection, configuration, logging과 test host

감수하는 것:

- Python 또는 TypeScript Backend를 통한 언어 학습 폭은 줄어든다.
- AI·ML이 Python 중심으로 커지면 별도 서비스 경계가 필요할 수 있다.

## 재검토 조건

- Game Core 직접 참조보다 언어 독립 서비스 계약의 가치가 커짐
- Python 전용 분석·ML runtime이 핵심 제품 경로가 됨
- ASP.NET Core가 요구 latency·운영 조건을 합리적인 복잡도로 충족하지 못함
