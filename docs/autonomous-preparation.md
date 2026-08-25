# 자동 진행 결과

상태: 완료

## 완료한 작업

- 기술 선택과 독립적인 용어 정의
- Game Core 결정론·Run·Turn·Reward 불변조건
- Run, Experiment, Config, Season, Batch 상태 전이
- 인간 Run 재실행·검증 프로토콜과 거부 코드
- 실험 Metric 계산 정의와 dimension
- 테스트 수준·Golden Fixture·Failure Injection 기준
- 기술·제품 위험과 재검토 Trigger
- Transport 독립 논리 인터페이스
- 전체 시스템 기술 선택지 Workshop 0001~0012

## 의도적으로 진행하지 않은 작업

다음 작업은 프로젝트 소유자의 선택을 선행하므로 자동으로 확정하거나 구현하지 않았다.

- Backend·Dashboard 프로젝트 생성
- Game Core 배포 형식 결정
- DB schema와 migration 도구 생성
- Queue·Job 구현
- REST/gRPC/GraphQL schema 생성
- 인증 library와 credential 형식 확정
- AI SDK와 Provider 선택
- 관측성 Backend 선택
- Hosting과 비용 발생 배포

## 재개 위치

1. [Workshop-0001: 전체 배포 단위](workshops/0001-deployment-unit.md)의 프로젝트 소유자 답변 작성
2. Codex와 반례·Spike 검토
3. ADR-0002 승인 또는 변경
4. Workshop-0002부터 순서대로 반복

## 구현 시작 조건

최소 다음 Workshop이 승인되면 첫 Game Core Spike를 시작할 수 있다.

- 0001 전체 배포 단위
- 0002 Game Core 공유 방식
- 0003 Backend 플랫폼

다음이 승인되면 첫 Backend 수직 단면을 시작할 수 있다.

- 0004 영속 저장소
- 0005 비동기 Job
- 0006 API 스타일
- 0007 인증과 Run 검증
