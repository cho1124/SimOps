# Workshop-0002: Game Core 공유 방식

상태: 완료

연결 ADR: [ADR-0001](../decisions/0001-shared-deterministic-game-core.md)

## 결정할 문제

동일한 Game Core를 Unity, Headless Simulation, 서버 검증에 어떤 방식으로 배포·참조할 것인가?

## 필수 요구사항

- [Game Core 불변조건](../specs/game-core-invariants.md)을 만족해야 한다.
- 세 실행 환경이 동일한 코드와 checksum을 사용해야 한다.
- UnityEngine과 외부 IO가 Core에 유입되면 안 된다.
- CI에서 버전과 산출물 일치를 검증할 수 있어야 한다.

## 선택지

### A. .NET Standard 관리 DLL

Core를 `netstandard2.1`로 빌드해 Backend/Worker는 프로젝트 참조, Unity는 관리 DLL 패키지로 사용한다.

- 장점: 실행 코드가 실제로 동일하고 checksum 관리가 명확하다.
- 단점: Unity 디버깅과 반복 개발에 패키징 단계가 추가된다.

### B. Source UPM Package

Core 소스를 Unity Package로 두고 .NET 프로젝트도 같은 소스를 compile item으로 포함한다.

- 장점: Unity에서 소스 탐색·디버깅이 쉽고 패키지 경계가 보인다.
- 단점: 빌드 설정 중복, source include, analyzer 차이로 환경별 compile 결과 관리가 복잡하다.

### C. 규약만 공유하고 별도 구현

Unity와 서버가 상태 전이 규약·Fixture만 공유하고 각각 구현한다.

- 장점: 런타임과 언어를 자유롭게 선택할 수 있다.
- 단점: 중복 구현과 drift 위험이 가장 크며 검증 비용이 높다.

## Codex 추천

A. MVP의 핵심 가치가 재현성이므로 동일 binary 계보와 checksum을 가장 단순하게 증명할 수 있다.

## 필수 Spike

같은 Config, Seed, Action Log를 Unity Test Runner와 .NET test host에서 실행한다.

성공 기준:

- Final Result JSON byte 또는 canonical hash 일치
- Stage Summary 일치
- 1,000개 Seed에서 불일치 0
- DLL 교체 시 Unity에서 버전 불일치 탐지

## 프로젝트 소유자 결정

- 선택: A, .NET Standard 관리 DLL
- 핵심 이유: Unity, Simulation과 서버 검증기가 동일한 규칙 바이너리를 실행해 구현 drift를 줄인다.
- 신뢰 근거: 동일 Game Version·Config·Seed·Action Log가 같은 Result Hash를 생성하는지 서버 재실행으로 확인한다.
- 부가 가치: Unity 표현 계층이 바뀌어도 호환되는 C#/.NET host에서 Core를 재사용할 수 있다.
- 감수: DLL 패키징·symbol·checksum과 Unity AOT/IL2CPP 호환성을 관리한다.

최종 내용은 [ADR-0001](../decisions/0001-shared-deterministic-game-core.md)에 기록했다.
