# Workshop-0013: PC·모바일 플랫폼 범위

상태: 완료

연결 ADR: [ADR-0014](../decisions/0014-client-platform-coverage.md)

## 이미 확정된 것

- 하나의 Unity 프로젝트로 PC와 모바일을 모두 지원한다.
- Game Core, Config, API와 기본 시즌·랭킹 계약은 플랫폼 간 공유한다.
- 입력, UI, 기기 생명주기와 배포 설정은 플랫폼 Adapter로 격리한다.

## 결정할 문제

MVP에서 어떤 OS와 배포 채널까지 실제 지원 대상으로 약속할 것인가?

## 선택지

### A. Windows + Android

- 장점: 현재 Windows 개발 환경에서 PC와 모바일의 구조적 차이를 가장 적은 배포 부담으로 증명 가능
- 단점: Apple 생태계와 iOS build·signing 경험은 보여주지 못함

### B. Windows + Android + iOS

- 장점: 대표 PC 한 종과 양대 모바일 생태계를 검증
- 단점: macOS/Xcode, Apple signing과 실제 기기 검증이 추가되고 CI 범위가 커짐

### C. Windows·macOS + Android·iOS

- 장점: 넓은 멀티플랫폼 포트폴리오와 배포 자동화 경험
- 단점: MVP 폐루프보다 빌드·서명·기기·스토어 대응이 프로젝트 중심이 될 위험이 큼

Unity 6은 데스크톱과 모바일 Player를 지원하며 플랫폼별 시스템·도구 요구사항이 다르다. 특히 iOS 개발·배포에는 Xcode와 Apple 측 요구사항이 결합된다. Unity Build Profiles로 플랫폼마다 개발·릴리스 설정을 분리할 수 있다.

- [Unity 6 시스템 요구사항](https://docs.unity3d.com/kr/current/Manual/system-requirements.html)
- [Unity Build Profiles](https://docs.unity3d.com/kr/6000.0/Manual/build-profiles.html)

## Codex 잠정 추천

MVP는 A로 멀티플랫폼 구조와 검증을 먼저 완성하고, LiveOps 폐루프 완성 후 iOS를 확장 목표로 추가하는 안을 우선 검토한다. 이는 iOS를 중요하지 않게 보는 선택이 아니라, 현재 Windows 환경에서 증명 가능한 범위와 별도 Apple build 환경이 필요한 범위를 분리하는 선택이다.

## 함께 결정할 세부 항목

- PC OS: Windows만 또는 macOS 포함
- 모바일 OS: Android만 또는 iOS 포함
- 화면 방향: 세로, 가로, 회전 지원
- PC 배포: 직접 다운로드, itch.io, Steam 등
- 모바일 배포: 내부 테스트, 스토어 비공개 테스트, 공개 출시
- 실제 기기 최소 검증 목록
- 익명 계정의 기기 간 이전·연동 범위
- 첫 공개 데모의 대표 플랫폼과 후속 플랫폼 순서

## 최소 Spike

1. 동일한 단일 전투 Scene을 Windows와 Android Development Build로 생성한다.
2. 키보드·마우스와 터치가 같은 `GameAction`을 생성하는지 확인한다.
3. 같은 Golden Action Log의 Result Hash를 비교한다.
4. Android에서 전투 중 background·resume 복구를 확인한다.

성공 기준:

- 두 대상 build 성공
- Golden Hash 불일치 0
- 입력 Adapter 밖의 Game Core 플랫폼 분기 0
- background·resume 후 누락·중복 Action 0

## 프로젝트 소유자 결정

- 선택: A, Windows + Android
- 화면: 가로 고정
- 배포: itch.io Windows ZIP·Android APK, GitHub Releases 선택적 mirror
- 검증: Android 실기기 최소 1대와 Windows·Android 교차 Golden Test
- 랭킹: Windows·Android 통합
- 익명 계정: 기기 간 이전을 MVP에서 지원하지 않음
- 순서: Windows 수직 단면 완성 후 Android Adapter·Build 추가
- 확장: iOS·macOS는 MVP 이후 실제 요구와 build 환경이 생길 때 검토

최종 내용은 [ADR-0014](../decisions/0014-client-platform-coverage.md)에 기록했다.
