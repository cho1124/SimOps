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
- [ADR-0002: 모듈형 모놀리스와 기술 스택](0002-modular-monolith-stack.md)
- [ADR-0003: PostgreSQL 기반 내구성 Job](0003-postgresql-durable-jobs.md)
- [ADR-0004: 재실행 검증과 시즌별 단일 설정 랭킹](0004-verified-season-leaderboard.md)

다음 후보:

- 이벤트 로그 보존 정책의 구현 세부사항
- AI 분석가의 모델·데이터 접근 Adapter
- 첫 공개 배포 플랫폼
