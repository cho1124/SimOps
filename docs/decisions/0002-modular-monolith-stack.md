# ADR-0002: 모듈형 모놀리스와 기술 스택

상태: 승인

## 맥락

SimOps는 플레이 API, 랭킹, 설정, 실험, 분석을 필요로 하지만 MVP는 개인 프로젝트다. 시스템 경계를 보여주면서도 분산 배포의 비용을 통제해야 한다.

## 고려한 선택지

- 기능별 마이크로서비스
- Next.js 중심 단일 풀스택 애플리케이션
- ASP.NET Core 모듈형 모놀리스 + 별도 Worker + Next.js Dashboard

## 결정

- API/Application: ASP.NET Core, .NET 10 LTS
- Worker: .NET 10 Worker Service
- DB: PostgreSQL 18.x
- Dashboard: Next.js 16 Active LTS, TypeScript
- Client: Unity 6.3 LTS
- Local orchestration: Docker Compose

API는 모듈형 모놀리스로 배포하고 장시간 작업만 별도 Worker가 처리한다.

## 이유

- Game Core와 C# 타입·도구 생태계를 자연스럽게 공유한다.
- 단일 트랜잭션이 필요한 랭킹·설정 작업이 많다.
- Next.js로 웹 프런트엔드 역량을 별도로 확장할 수 있다.
- 마이크로서비스 없이도 코드 모듈과 비동기 경계를 분명히 할 수 있다.

## 결과와 포기한 것

얻는 것:

- 단순한 로컬 개발과 배포
- 쉬운 트랜잭션과 디버깅
- 서비스 분리에 대비한 모듈 경계

감수하는 것:

- API 기능별 독립 배포가 불가능하다.
- 잘못된 모듈 참조를 코드 규칙과 테스트로 막아야 한다.
- 전체 API가 하나의 확장 단위다.

## 재검토 조건

- 특정 모듈의 독립 배포·확장이 지속적으로 필요함
- API 배포가 서로 다른 팀의 릴리스 주기를 막음
- 장애 격리 요구가 단일 프로세스의 이점보다 커짐

