# PC·모바일 지원 계약

상태: 확정

## 범위

SimOps Arena의 MVP는 하나의 Unity 프로젝트에서 `PC 플랫폼군`과 `모바일 플랫폼군`을 모두 지원한다. 이는 모든 운영체제를 동시에 지원한다는 뜻이 아니다. MVP의 정확한 OS, 배포 채널, 화면 방향과 출시 순서는 [Workshop-0013](../workshops/0013-client-platform-coverage.md)에서 결정한다.

## 공통 계약

- PLATFORM-001: MVP는 최소 한 종류의 PC Player와 한 종류의 모바일 Player를 빌드할 수 있어야 한다.
- PLATFORM-002: 모든 클라이언트는 동일한 Game Core, Game Version, Config, Score Rule과 API 계약을 사용해야 한다.
- PLATFORM-003: 동일한 Game Version, Config, Seed와 Action Log는 지원 플랫폼 간 동일한 Result Hash를 생성해야 한다.
- PLATFORM-004: PC와 모바일 인간 Run은 동일한 서버 재실행 검증을 통과해야 공개 랭킹에 반영된다.
- PLATFORM-005: 기본 정책은 플랫폼 통합 시즌·랭킹이다. 플랫폼별 분리가 필요하면 공정성 근거와 함께 별도 ADR로 변경해야 한다.
- PLATFORM-006: 플랫폼 정보는 분석 Dimension으로 기록할 수 있지만 게임 규칙의 숨은 분기로 사용하면 안 된다.

## 플랫폼 경계

다음 책임은 Game Core 밖의 Adapter로 분리한다.

- 입력: 키보드·마우스, 터치와 시스템 Back 동작
- 표현: 해상도, 화면비, DPI, Safe Area, 그래픽 품질
- 생명주기: focus 상실, pause, background, resume, 종료
- 로컬 기능: 저장 위치, 기기 식별자, 보안 저장소, 알림
- 배포: 서명, 패키지 식별자, 스토어 메타데이터, 빌드 프로파일

Game Core는 UnityEngine 입력, 화면 크기, 프레임 시간, 플랫폼 전처리 분기에 의존하면 안 된다.

## UX 요구사항

- PLATFORM-010: 모든 유효 행동과 보상 선택은 키보드·마우스와 터치에서 수행 가능해야 한다.
- PLATFORM-011: 핵심 전투·보상·결과 UI는 승인된 최소 해상도, 화면비와 Safe Area에서 가려지거나 겹치면 안 된다.
- PLATFORM-012: 터치 대상의 최소 크기와 간격은 UI 기준선에서 정의하고 자동 또는 시각 회귀 검증 대상으로 삼아야 한다.
- PLATFORM-013: 모바일 background 전환은 서버에 Action을 자동 제출하지 않아야 한다.
- PLATFORM-014: 중단된 로컬 Run을 복구할 때 마지막 확정 Action 경계까지만 복구하고 Action을 중복 생성하면 안 된다.

## 네트워크와 계정

- PLATFORM-020: 일시적인 연결 실패는 진행 중인 로컬 Run을 즉시 파기하지 않아야 한다.
- PLATFORM-021: 랭킹 Run 시작에는 유효한 서버 Ticket이 필요하며, 만료·검증 실패 시 로컬 결과는 표시할 수 있어도 공개 랭킹에는 제출할 수 없다.
- PLATFORM-022: 계정 연동을 구현하는 경우 같은 계정은 PC와 모바일에서 같은 시즌 기록을 조회해야 한다. 익명 계정의 기기 간 이전 방식은 Workshop-0013에서 별도로 결정한다.

## 완료 기준

- CI 또는 재현 가능한 로컬 절차로 대상 PC·모바일 Development Build를 생성한다.
- 대상별 Smoke Test와 실제 기기 테스트 결과를 남긴다.
- 공통 Golden Fixture의 Result Hash가 대상 플랫폼에서 일치한다.
- 지원·미지원 OS, 기기, 화면 방향과 배포 채널을 README에 명시한다.
