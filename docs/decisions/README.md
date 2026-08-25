# Architecture Decision Records

중요한 설계 결정을 짧은 문서로 남긴다.

## 작성 시점

- 기술 스택을 선택할 때
- 컴포넌트 책임 경계를 정할 때
- 성능과 단순성 사이에서 선택할 때
- 데이터 보존 또는 보안 정책을 정할 때
- 되돌리기 어려운 제품 결정을 할 때

## 파일 이름

```text
0001-decision-title.md
0002-next-decision.md
```

## 템플릿

```markdown
# ADR-0000: 결정 제목

상태: 제안 | 승인 | 대체 | 폐기

## 맥락

어떤 문제와 제약이 있는가?

## 고려한 선택지

- 선택지 A
- 선택지 B

## 결정

무엇을 선택했는가?

## 이유

왜 이 선택이 현재 제약에 적합한가?

## 결과와 포기한 것

얻는 것과 감수하는 단점은 무엇인가?

## 재검토 조건

어떤 조건에서 이 결정을 다시 검토하는가?
```

## 예정된 ADR

- [ADR-0001: 결정론적 Game Core 공유](0001-shared-deterministic-game-core.md)
- [ADR-0002: 전체 배포 단위](0002-modular-monolith-stack.md)
- [ADR-0003: PostgreSQL 기반 내구성 Job](0003-postgresql-durable-jobs.md)
- [ADR-0004: 재실행 검증과 시즌별 단일 설정 랭킹](0004-verified-season-leaderboard.md)
- [ADR-0005: Backend 플랫폼](0005-backend-platform.md)
- [ADR-0006: 영속 저장소](0006-persistence-platform.md)
- [ADR-0007: API 스타일](0007-api-style.md)
- [ADR-0008: 사용자 식별과 인간 Run 검증](0008-identity-and-run-verification.md)
- [ADR-0009: 분석·집계 방식](0009-analytics-strategy.md)
- [ADR-0010: 운영 Dashboard 플랫폼](0010-dashboard-platform.md)
- [ADR-0011: AI 분석 통합](0011-ai-analysis-integration.md)
- [ADR-0012: 관측성](0012-observability-stack.md)
- [ADR-0013: 배포·호스팅과 CI/CD](0013-deployment-and-ci.md)
- [ADR-0014: PC·모바일 플랫폼 범위](0014-client-platform-coverage.md)

다음 후보:

- 이벤트 로그 보존 정책의 구현 세부사항
- Game Config canonical serialization과 checksum
- Legacy Replay 보존·실행 방식
