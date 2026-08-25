# 아키텍처 설계

상태: 사전 초안

이 문서는 시스템·실험 기획이 확정되기 전의 방향이며 구현 기준이 아니다.

## 목표 구조

```text
Game.Core
├─ Unity Client
└─ Simulation Runner
       ↓ telemetry batch
Telemetry API
       ↓
PostgreSQL
├─ Web Dashboard
├─ Ranking API
└─ AI Analyst

LiveOps Config Service
       ↓
Unity Client / Simulation Runner
```

## 컴포넌트 책임

### Game.Core

- Unity에 의존하지 않는 순수 C# 게임 규칙
- 결정론적 상태 전이
- 점수 계산에 필요한 결과 생성
- 행동 유효성 검증
- 시드 기반 난수

### Unity Client

- 사람 입력
- 화면, 애니메이션, 사운드
- 플레이 및 리플레이 시각화
- 설정 내려받기
- 행동 로그 제출

### Simulation Runner

- 합성 플레이어 실행
- 다중 Run 병렬 처리
- 같은 시드를 Variant 간 공유하는 실험 지원
- 이벤트 배치 전송
- 실패 Run 재현

### Backend API

- 익명 사용자와 운영자 인증
- 실행 결과 수신
- 행동 로그 기반 서버 검증
- 랭킹 계산과 조회
- 실험·설정·배포 관리

### Web Dashboard

- 실험 생성과 상태 확인
- Variant 및 페르소나 비교
- 랭킹과 플레이 분포 분석
- 설정 승인과 롤백
- AI 분석 결과 표시

### AI Analyst

- 계산된 지표 해석
- 이상 변화 요약
- 원인 가설과 다음 실험 제안
- 직접 배포 권한 없음

## 기술 스택 후보

- Game Core 및 Runner: .NET/C#
- 게임 클라이언트: Unity/C#
- 백엔드: ASP.NET Core 우선 검토
- 데이터베이스: PostgreSQL
- 웹: Next.js/TypeScript 우선 검토
- AI·고급 분석: Python 서비스 또는 백엔드 도구 호출
- 로컬 인프라: Docker Compose

각 선택은 시스템 요구사항을 확정한 뒤 ADR로 결정한다.

## 주요 품질 요구사항

- 재현성: 같은 입력은 같은 결과를 생성한다.
- 추적성: 모든 결과에서 사용한 버전을 식별할 수 있다.
- 분리성: 게임 화면 없이 시뮬레이션할 수 있다.
- 검증성: 서버가 행동 로그로 결과를 재구성할 수 있다.
- 안전성: AI와 운영 설정 배포 사이에 승인 경계가 있다.
- 관측성: 실험과 검증 실패 원인을 추적할 수 있다.

## 주요 설계 쟁점

- Game.Core 코드를 서버 검증과 Unity가 어떻게 공유할지
- 시뮬레이션을 프로세스·스레드·작업 큐 중 어떻게 병렬화할지
- 원시 행동 로그 보관 비용과 요약 이벤트의 균형
- 랭킹 즉시성과 서버 재실행 비용의 균형
- AI 분석을 별도 서비스로 분리할 시점

