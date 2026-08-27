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
- 전체 시스템 기술 선택지 Workshop 0001~0013
- PC·모바일 공통 계약과 플랫폼 검증 기준

## 결정 전 보류했던 작업

다음 작업은 프로젝트 소유자의 선택을 선행하므로 준비 단계에서는 구현하지 않았다.

- Backend·Dashboard 프로젝트 생성
- Game Core 배포 형식 결정
- DB schema와 migration 도구 생성
- Queue·Job 구현
- REST/gRPC/GraphQL schema 생성
- 인증 library와 credential 형식 확정
- AI SDK와 Provider 선택
- 관측성 Backend 선택
- Hosting과 비용 발생 배포

## 기술 결정 완료

2026-08-27에 Workshop 0001~0013의 선택과 반례 검토를 완료하고 ADR 0001~0014를 승인했다.

## 재개 위치

2026-08-27 갱신: M8까지 구현·자동 검증했다. 상세 상태는 [구현 기록](implementation/README.md)을 기준으로 한다.

1. [M8 LiveOps 폐루프](implementation/milestone-08-liveops.md) 구현·격리 통합 검증 완료. 다음 실제 실험은 실패 결과를 바탕으로 가설·후보·판정 기준에 대한 소유자 판단이 필요하다.
2. 현재 실험 `difficulty-curve-001`은 검토 후보 없음, `analyzing`, 사람 판정 미작성이다. 자동 승인·게시는 하지 않는다.
3. [M7 로컬 AI 분석](implementation/milestone-07-ai-analysis.md)의 근거 제한·실패 격리·반복 검증 결과 확인
4. 별도 QA: 실제 브라우저 조작, Windows 화면, Android 실기기 검증

## 구현 시작 조건

첫 Game Core Spike의 선행 Workshop은 모두 승인됐다.

- 0001 전체 배포 단위
- 0002 Game Core 공유 방식
- 0003 Backend 플랫폼

첫 Backend 수직 단면의 선행 Workshop도 모두 승인됐다.

- 0004 영속 저장소
- 0005 비동기 Job
- 0006 API 스타일
- 0007 인증과 Run 검증
