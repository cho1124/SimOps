# 마일스톤 1: 결정론적 Game Core

상태: 완료
검증일: 2026-08-27

## 구현 결과

Unity와 분리된 `.NET Standard 2.1` 순수 C# Game Core를 만들었다. 하나의 Run은 Stage 1~5 일반 전투와 각 전투 뒤의 보상 선택, Stage 6 Boss 전투로 구성된다.

- `SimOps.Game.Core`: 상태, 행동, 전투, 보상, 점수, 결정론적 RNG와 Hash
- `SimOps.Runner`: 고정 정책으로 하나의 Run을 끝까지 실행하는 Console Host
- `SimOps.Game.Core.Specs`: 외부 테스트 패키지 없이 실행되는 명세 Harness
- 입력 경계: `RunContext`와 순서가 고정된 `GameAction` Log
- 출력 경계: 불변 Snapshot, Domain Event, Stage·Reward 기록, 최종 점수와 Result Hash

Core는 `UnityEngine`, 현재 시각, 전역 난수, 파일·네트워크 I/O에 의존하지 않는다. 이후 Unity, 서버 검증기, 합성 플레이어가 같은 DLL과 계약을 재사용한다.

## 결정론 기준선

| 항목 | 값 |
|---|---|
| Game Version | `0.1.0` |
| Config Version | `baseline-0.1.0` |
| Config checksum | `388792f0b3f1dafe41f787c69894931fc2af1106e3edf098b10ed251bdda710f` |
| Score Rule Version | `0.1.0-floor` |
| Score Rule checksum | `9a2d4ea68678a800b862a3cfbf53691d0cfcbf7363e436bbdcf86f8ab25abbb6` |
| Golden Seed | `42` |
| Golden Result Hash | `c50ea84e374db937ec1dd17ea94428b60afdb169b4d64dd5eeec64128fa2fa78` |

RNG는 Base Seed에서 `Encounter`, `Intent`, `Reward`, `Agent` Stream별 Subseed를 파생한다. 현재 전투에서는 Intent와 Reward Stream을 소비하며, 다른 Stream의 draw 횟수가 Reward 결과에 영향을 주지 않는지 자동 검증한다.

## Golden Run 결과

Seed 42를 기본 정책으로 실행한 기준 결과다.

```text
actions=38
outcome=Victory
clearedStages=6
totalTurns=23
finalHealth=36/90
finalScore=83700
resultHash=c50ea84e374db937ec1dd17ea94428b60afdb169b4d64dd5eeec64128fa2fa78
```

Debug와 Release 빌드가 같은 결과를 만들었다.

## 자동 검증

Release 기준 13개 명세가 모두 통과했다.

- CORE-001: 1,000개 Seed 각각을 Action Log로 Replay하고 Result Hash·점수·결과 비교
- CORE-002·003: RNG Stream 독립성과 locale 독립성
- CORE-013·022·024·028: 종료 후 행동, 무효 행동 무변경, Item 제한, 최대 Turn 종료
- CORE-030·031·034: 서로 다른 보상 3개, 미제시 보상 거부, 5회 선택 전 Pool 고갈 사전 차단
- CORE-041·043: checksum과 Config 구조 검증
- Golden: Seed 42 Result Hash 고정

```powershell
dotnet restore SimOps.slnx
dotnet build SimOps.slnx -c Release --no-restore
dotnet run --project tests/SimOps.Game.Core.Specs/SimOps.Game.Core.Specs.csproj -c Release --no-build
dotnet run --project src/SimOps.Runner/SimOps.Runner.csproj -c Release --no-build -- 42
```

## 설계상 선택

- 부동소수점 대신 정수 수치와 명시적 정수 나눗셈을 사용한다.
- collection 순서와 문자열 직렬화 순서를 고정하고 SHA-256으로 checksum과 Result Hash를 계산한다.
- 행동은 sequence가 맞고 현재 Phase에서 유효할 때만 적용한다. 거부된 행동은 상태와 Action Log를 바꾸지 않는다.
- Config와 Score Rule은 Run 시작 전에 version과 checksum을 대조한다.
- 보상 후보 부족은 플레이 도중이 아니라 Config 생성 시점에 실패시킨다.

## 남은 범위

마일스톤 2에서 Unity 표현 계층과 입력 Adapter를 붙이고, 같은 Golden Run을 Unity Editor·Windows Build·Android 실기기에서 비교한다. 현재 수치와 Score Rule은 제품 밸런스 확정값이 아니라 전체 파이프라인을 검증하기 위한 기준선이다.
