# ADR-0014: PC·모바일 플랫폼 범위

상태: 승인

결정일: 2026-08-27

연결 워크숍: [Workshop-0013](../workshops/0013-client-platform-coverage.md)

## 확정된 상위 요구사항

- 하나의 Unity 프로젝트에서 PC와 모바일을 모두 지원한다.
- Game Core와 서버 검증 계약은 플랫폼 간 공유한다.

## 고려한 선택지

- Windows + Android
- Windows + Android + iOS
- Windows·macOS + Android·iOS

## 결정

MVP 대상은 Windows와 Android다.

후속 결정: [ADR-0015](0015-web-prototype.md)에 따라 WebGL 프로토타입을 추가한다. 아래 네이티브 대상·계약은 유지한다.

- 화면 방향: 가로 고정
- PC 배포: itch.io Windows ZIP
- Android 배포: itch.io APK, 필요 시 GitHub Releases mirror
- 실제 기기 검증: Android 실기기 최소 1대
- 시즌·랭킹: Windows·Android 통합
- 익명 계정의 기기 간 이전: MVP에서 지원하지 않음
- 구현 순서: Windows 수직 단면 완성 후 Android Adapter와 Build 추가
- iOS·macOS: MVP 이후 실제 필요가 생기면 검토

## 결과와 포기한 것

얻는 것:

- 현재 Windows 개발 환경에서 PC·모바일 공통 Core와 플랫폼 Adapter를 검증한다.
- Apple build·signing 없이 멀티플랫폼 포트폴리오 범위를 완성한다.
- 동일 규칙·API·랭킹을 서로 다른 입력·생명주기 환경에서 검증한다.

감수하는 것:

- iOS·macOS 사용자 접근성과 Apple 배포 경험을 MVP에서 포기한다.
- Android APK 직접 설치의 사용자 마찰이 있다.
- 익명 계정은 PC와 Android 사이에 이전되지 않는다.

## 재검토 조건

- 실제 사용자 또는 평가자의 iOS·macOS 요구가 반복됨
- Apple build·signing 환경과 비용을 확보함
- Windows·Android 완료 후 플랫폼 확장이 LiveOps 폐루프보다 높은 학습 가치를 가짐
