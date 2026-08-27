# ADR-0001: 결정론적 Game Core 공유

상태: 승인

결정일: 2026-08-27

연결 워크숍: [Workshop-0002](../workshops/0002-game-core-distribution.md)

## 맥락

Unity의 사람 플레이, Headless 합성 플레이, 서버의 랭킹 검증이 같은 게임 규칙을 사용해야 한다. 규칙을 세 번 구현하면 미세한 차이가 실험과 랭킹의 신뢰성을 깨뜨린다.

## 고려한 선택지

- Unity 프로젝트 안에 규칙을 두고 서버가 결과만 신뢰한다.
- Unity와 서버에 규칙을 각각 구현한다.
- 엔진 독립적인 C# Game Core를 만들고 모든 실행 환경이 공유한다.

## 결정

`SimOps.Game.Core`를 `netstandard2.1` 순수 C# 라이브러리로 구현한다.

- Unity는 관리 DLL 패키지로 참조한다.
- Worker와 Backend는 ProjectReference로 참조한다.
- 상태 전이는 정수 또는 명시적 fixed-point 규칙을 사용한다.
- RNG, 서브시드 파생, 계산 순서를 Game Version에 고정한다.
- UnityEngine, IO, 네트워크, 현재 시각에 의존하지 않는다.

## 이유

- Unity 6은 .NET Standard 2.1 관리 플러그인을 지원한다.
- 동일 코드를 공유해야 서버 재실행 검증과 paired simulation이 의미를 갖는다.
- 기준 게임을 다른 표현 계층이나 실행기로 재사용할 수 있다.

## 결과와 포기한 것

얻는 것:

- 결정론과 재현성
- 중복 구현 제거
- 빠른 Headless 실행
- 서버 권위 랭킹 검증

감수하는 것:

- Unity 전용 타입을 Core에서 직접 사용할 수 없다.
- DLL 빌드·패키징 과정이 필요하다.
- 플랫폼 차이를 막기 위한 엄격한 코딩 규칙이 필요하다.

## 재검토 조건

- Unity가 선택한 .NET 표준 타깃을 더 이상 지원하지 않음
- 다른 언어 서버가 Game Core를 직접 실행해야 함
- 결정론적 DLL 공유보다 별도 권위 서버 모델이 제품 요구에 적합해짐

## 검증 의무

- Unity, .NET Test Host, Windows, Android에서 같은 Golden Fixture의 Result Hash 불일치 0건
- Game Core 산출물 checksum과 배포된 Game Version의 연결 검증
- UnityEngine, 외부 IO, 현재 시각과 비시드 난수 의존성 차단
