# Workshop-0012: 배포·호스팅과 CI/CD

상태: 완료

연결 ADR: [ADR-0013](../decisions/0013-deployment-and-ci.md)

## 결정할 문제

PC·모바일 Unity build, API, Worker, Dashboard, Database를 어디에 어떻게 배포하고 검증할 것인가?

## 선행 결정

- Workshop-0001 전체 배포 단위
- Workshop-0003 Backend
- Workshop-0004 저장소
- Workshop-0009 Dashboard
- Workshop-0011 관측성
- Workshop-0013 클라이언트 플랫폼 범위

## 필수 요구사항

- 포트폴리오 관람자가 Dashboard와 게임 build에 접근할 수 있어야 한다.
- secret과 환경 설정을 저장소에서 분리해야 한다.
- migration, rollback, health check가 필요하다.
- 비용 상한과 유휴 시 동작을 이해해야 한다.

## 선택지

### A. Container PaaS + Managed PostgreSQL

- 장점: HTTPS, 배포, process 분리, DB backup을 비교적 빠르게 확보
- 단점: provider 제약, 유휴 sleep, 사용량 비용과 migration 관리 필요

### B. 단일 VPS + Docker Compose

- 장점: 전체 stack과 네트워크를 직접 제어하며 비용 예측 가능
- 단점: patch, firewall, TLS, backup, 장애 복구를 직접 운영

### C. Cloud-native Managed Service

Container App/ECS 계열, managed DB, observability를 조합한다.

- 장점: 확장·권한·운영 기능이 풍부하고 실무 cloud 경험
- 단점: 개인 MVP에 구성과 비용 모델이 복잡하고 provider 종속성이 큼

## Codex 잠정 추천

첫 공개 데모는 A가 완주와 운영 경험의 균형이 좋다. 다만 월 예산, 무료 tier 의존 허용, 항상 켜진 데모 필요 여부가 없으면 최종 추천할 수 없다.

## CI 공통 기준

호스팅 선택과 무관하게 다음 pipeline이 필요하다.

```text
format/lint
→ unit/property/determinism tests
→ integration tests
→ Game Core DLL + checksum
→ Unity 공통 test
→ 대상 PC·모바일 build와 smoke test
→ API/Worker/Dashboard image
→ migration compatibility check
→ staging smoke test
→ manual production approval
```

## 프로젝트 소유자 결정

- 선택: A, Container PaaS + Managed PostgreSQL의 무료 구성
- 고정 제약: 월 비용 0원, 결제 카드 등록 없음
- 허용: Cold Start, 제한된 uptime과 무료 tier 한도
- 기준선: GitHub Pages Dashboard, Render Free API·Worker, Neon Free PostgreSQL, GitHub Actions CI, itch.io game build
- 감수: Render Worker를 Web Service 형태로 깨우는 타협과 무료 정책 재검증이 필요하다.
- 재검토: 무료 정책·카드 요구 변경, 핵심 데모 불안정 또는 용량·실행 한도 초과 시 예산부터 다시 결정한다.

최종 내용은 [ADR-0013](../decisions/0013-deployment-and-ci.md)에 기록했다.
