# Workshop-0003: Backend 플랫폼

상태: 사용자 결정 대기

연결 ADR: [ADR-0005](../decisions/0005-backend-platform.md)

## 결정할 문제

Run, Ranking, Config, Experiment, Analytics API와 Worker의 주 개발 플랫폼을 무엇으로 할 것인가?

## 필수 요구사항

- 트랜잭션, 인증, 비동기 Job, 구조화 로그, OpenAPI를 지원해야 한다.
- Game Core 또는 그 결과 계약과 안정적으로 통합돼야 한다.
- 동일 저장소에서 테스트와 컨테이너 실행이 가능해야 한다.
- AI 연동은 Backend 언어 자체가 아니라 Adapter 경계로 분리할 수 있어야 한다.

## 선택지

### A. ASP.NET Core / C#

- 장점: Game Core 직접 참조, 강한 타입과 성숙한 인증·호스팅·관측성, Worker Service 공유
- 단점: C# 익숙함이 새로운 언어 학습 폭을 줄일 수 있음
- 학습 초점: 웹·트랜잭션·비동기 시스템 개념
- 공식 자료: [ASP.NET Core overview](https://learn.microsoft.com/en-us/aspnet/core/overview?view=aspnetcore-10.0)

### B. FastAPI / Python

- 장점: 간결한 OpenAPI·검증, 데이터·AI 생태계와 가까움, 새로운 언어·비동기 경험
- 단점: Game Core 직접 공유 불가, Python Worker와 C# Simulation 경계 추가, runtime type 안정성 관리 필요
- 학습 초점: Python 웹·데이터·AI 서비스
- 공식 자료: [FastAPI features](https://fastapi.tiangolo.com/features/)

### C. NestJS / TypeScript

- 장점: Dashboard와 언어 통일, 명시적인 module/DI 구조, Node 생태계
- 단점: Game Core와 언어 분리, CPU Simulation은 별도 C# process 필요
- 학습 초점: TypeScript 풀스택과 Node Backend
- 공식 자료: [NestJS documentation](https://docs.nestjs.com/guide/large-scale-apps)

## Codex 추천

A. 이 프로젝트에서 넓혀야 할 것은 언어 수보다 서비스 설계다. Game Core와 검증기를 직접 공유해 핵심 위험을 줄이고, TypeScript는 Dashboard, Python은 이후 ML 영역에서 학습하는 편이 책임 경계가 자연스럽다.

## 선택 전 Spike

각 후보로 전체 API를 만들지 않는다. 다음 세로 단면만 비교한다.

```text
Run Ticket DTO
→ Action Log validation
→ Game Core 또는 verifier 호출
→ 결과 반환
```

비교:

- 코드·설정 양
- Game Core 연결 난도
- 테스트 작성 경험
- 오류 모델과 OpenAPI 품질
- 개발자가 직접 설명 가능한 정도

## 프로젝트 소유자 답변

[공통 선택 설명 형식](../09-decision-workshop.md#선택-설명-형식)을 사용한다.
