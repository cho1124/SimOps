# 마일스톤 2: Unity 플레이와 리플레이

상태: 구현·Windows/Android Build 완료, Android 실기기 QA 대기

검증일: 2026-08-27

## 구현 결과

Unity `6000.3.9f1` 프로젝트를 `unity-client`에 만들고 마일스톤 1의 `.NET Standard 2.1` Game Core DLL을 embedded package로 연결했다.

- Unity 6 기본 UI Toolkit으로 가로형 반응형 전투 화면 구성
- 키보드 `1`~`5`와 Space 입력, Windows pointer와 Android touch가 같은 Button callback 사용
- `Screen.safeArea` 변화에 따라 runtime padding 갱신
- Windows focus 상실, Android pause, 앱 종료 시 저장 hook 실행
- 승인된 Action마다 Game Version, Config·Score checksum, Seed, Action Log를 JSON으로 저장
- 시작 시 저장 로그를 재실행하고 유효하지 않거나 버전이 다른 로그는 거부
- 현재 Action Log를 처음부터 다시 적용하는 Replay 기능
- Runtime UI와 첫 행동을 실제 Player에서 확인하는 headless smoke mode

Game Core에는 Unity 타입이나 저장 I/O가 추가되지 않았다. Unity Client가 표현, 입력, 기기 생명주기와 저장을 Adapter 경계에서 담당한다.

## DLL 패키징

`scripts/Build-GameCorePackage.ps1`은 Core를 Release로 빌드하고 다음 embedded package에 DLL과 로컬 debug symbol을 복사한다.

```text
unity-client/Packages/com.simops.game-core/Runtime/Plugins/SimOps.Game.Core.dll
```

DLL은 저장소에 포함하고 PDB는 로컬 생성물로만 유지한다. Game Core를 변경한 뒤 Unity를 검증할 때 항상 같은 스크립트를 먼저 실행한다.

## 자동화

```powershell
# DLL 패키징 + Unity Host 골든 검증
powershell -ExecutionPolicy Bypass -File scripts/Run-Milestone2.ps1 -Target Verify

# Windows Development Build + Player smoke test
powershell -ExecutionPolicy Bypass -File scripts/Run-Milestone2.ps1 -Target Windows

# Android ARM64 IL2CPP Development APK
powershell -ExecutionPolicy Bypass -File scripts/Run-Milestone2.ps1 -Target Android

# 전체 실행
powershell -ExecutionPolicy Bypass -File scripts/Run-Milestone2.ps1 -Target All
```

Unity 버전은 `ProjectSettings/ProjectVersion.txt`에서 읽고, 로그와 Build는 Git에서 제외된 `artifacts/unity` 아래에 생성한다.

## 자동 검증 결과

| 검증 | 결과 |
|---|---|
| Unity C# compile | 성공 |
| Unity Host Golden Seed 42 | `c50ea84e374db937ec1dd17ea94428b60afdb169b4d64dd5eeec64128fa2fa78`, .NET Runner와 일치 |
| Windows x64 Development Build | 성공, 전체 Build report 약 138 MiB |
| Windows Player smoke | 성공, UI 초기화 후 첫 Strike Action 1건 적용 |
| Android ARM64 IL2CPP Development APK | 성공, APK 약 40.9 MiB |

## 현재 남은 수동 검증

자동 빌드 시점에 ADB 연결 기기가 없었다. 따라서 다음 항목은 Android 실기기를 연결한 뒤 수행한다.

- APK 설치와 실제 touch 입력
- 기기 해상도와 notch·Safe Area 시각 확인
- Action 선택 직전 background 전환 시 의도하지 않은 입력이 없는지 확인
- 매 Action 저장 후 강제 종료·복귀 시 누락·중복 없이 복구되는지 확인
- Android Player에서 Golden Fixture Hash 수집

Windows에서도 실제 키보드·pointer 조작은 수동 체크 항목으로 남긴다. 2026-08-27 [화면 개선](unity-ui-refresh.md)에서 세 해상도의 UI 렌더 출력과 한글·배치를 확인했다. 렌더 검사와 실제 기기 사용성이 검증됐다는 사실은 구분한다.
