# Unity 게임 화면 개선

2026-08-27. 기능 실험용 화면을 전투·보상·결과 중심으로 재구성했다. 대시보드나 게임 규칙은 변경하지 않았다.

## 변경 범위

- 플레이어/적 HP 바, 행동력·방어도·공격력·회복약, 6단계 진행 표시.
- 다음 적 행동과 방어도 적용 전 피해량 표시.
- 행동 카드에 한국어 이름, 실제 설정/누적 보상이 반영된 효과, 행동력 비용, 사용 불가 사유 표시.
- 공격·회복·방어 변화 문구와 0.7초 색상 피드백. 색상만으로 의미를 전달하지 않는다.
- 보상 3개를 분류·효과가 있는 선택 카드로 표시.
- 결과 전용 패널: 점수, 통과 단계, 턴 수, 체력, 연습/랭킹 구분, 수동 제출·검증 상태·랭킹 조회.
- 개발 정보/행동 로그/리플레이 검증은 기본 접힌 영역으로 이동.
- 최신 한 판만 저장하는 기존 정책을 명시하고, 새 도전으로 덮어쓸 때 확인창 제공. 확인창 및 네트워크 처리 중 게임 입력 차단.
- PC 키보드 1–5/Space, 버튼 포인터·터치 입력 유지. 화면 비율 스케일링, 세로 스크롤, Safe Area 여백 적용.
- 한국어 폰트 동봉. 별도 설치·외부 에셋 서비스·유료 호출 없음.

## 경계와 유지한 계약

`SimOpsGameController`는 진행·저장·서버 연동, `ArenaView`는 UI Toolkit 구성·표시·피드백, `ArenaText`는 순수 C# 표시 문구를 담당한다. Game Core의 `ValidActionTypes`가 입력 허용 여부의 기준이다. 표시 계층에서 전투 상태를 수정하거나 독자적으로 승패를 계산하지 않는다.

Game Core DLL SHA256은 변경 전후 동일하다.

```text
0f0bb340e522605ecd54ce231b143a14b91c861881e98e5cb8224e139d0b9d2b
```

Unity/.NET 골든 Seed 42의 결과도 동일하다.

```text
c50ea84e374db937ec1dd17ea94428b60afdb169b4d64dd5eeec64128fa2fa78
score=83700, Victory, actions=38
```

서버 API/JSON, Ticket 서명, 설정 snapshot, 저장 파일 스키마, 점수·랭킹 정책은 변경하지 않았다. **자동 제출이나 여러 판의 미제출 기록 보관함은 추가하지 않았다.** 복구한 랭킹 기록은 같은 제출 키로 서버 상태를 재확인한다. 검증 완료 표시와 해당 시즌 개인 최고 기록 반영 여부는 구분한다.

## 재현

빌드 전 같은 저장소의 API/Worker가 빌드 출력 DLL을 사용 중이라면 해당 로컬 호스트를 종료한다. PostgreSQL과 저장 파일은 삭제하지 않는다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Run-Milestone2.ps1 -Target All
dotnet run --project tests/SimOps.Client.Specs -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Run-UnityUiPreview.ps1
dotnet run --project tests/SimOps.Backend.Specs -c Release --no-build -- --liveops-unity
```

실행 파일은 `artifacts/unity/windows/SimOps.exe`, APK는 `artifacts/unity/android/SimOps.apk`다. 폰트 고지는 각 배포 폴더의 `licenses/NotoSansKR`에도 복사된다. APK 단독 배포 시에도 라이선스 고지를 함께 제공한다.

렌더 검사는 Unity Player의 **동일한 UI Toolkit 패널**을 GPU RenderTexture로 출력한다. 숨김 Windows 창의 화면 버퍼 캡처가 검게 나오는 환경을 피하기 위한 테스트 전용 경로이며, 실제 플레이는 화면에 직접 출력한다. UI 상태만 확인하는 headless smoke와 다르다. 이미지의 비어 있음, 핵심 컨트롤의 화면 경계, 입력 잠금을 검사하고 PNG를 사람이 확인한다.

출력: `artifacts/unity/windows/ui-preview/`, 로그: `artifacts/unity/logs/ui-preview-*.log`.

## 검증 기록

- Unity 6000.3.9f1 컴파일·골든 검증, Windows x64 Development Build/Player smoke, Android ARM64 IL2CPP Development APK 빌드 성공(APK 66,895,963 bytes).
- 표시 문구 9건, Core 13건, Agent 5건, 실험 계산 9건 통과.
- 별도 임시 DB의 LiveOps 통합 9건 통과. 실제 Windows Player가 게시 설정으로 플레이 → 제출 → Worker 검증 → 랭킹 조회하고 UI에 검증 완료가 반영되는 경로 포함.
- 1600×900, 1280×720, 1560×720에서 전투·피격·보상·승리·패배·회복·덮어쓰기 확인창, 제출 전/중/완료 표시를 렌더링 검사하여 30개 PNG를 생성했다. 빈 이미지·핵심 컨트롤 잘림·입력 잠금 검사가 통과했고, 주요 화면을 직접 열어 한글과 배치를 확인했다. 좁은 높이에서 보조 안내/개발 영역은 세로 스크롤로 접근한다.
- 제출 상태 PNG 중 `*-fixture-*`는 **화면 검사용 상태 주입**이며 실제 서버 제출의 증거가 아니다. 실제 제출은 위의 격리 통합 테스트가 별도로 검증한다.
- 렌더 검사에서는 사용자 Replay 읽기/쓰기 및 네트워크 요청을 하지 않는다. 기존 사용자 Replay 파일의 SHA256이 작업 전후 동일한 것을 별도로 확인했다.
- 실제 활성 시즌·실험 승인·게시 설정은 변경하지 않았다.

## 남은 확인

- 렌더 출력과 상태 검증은 OS의 실제 포인터/키보드 조작을 대신하지 않는다. 직접 플레이로 버튼 감각·포커스·창 크기 변경을 확인해야 한다.
- Android APK 빌드와 Windows에서의 가로 비율 검사는 Android 실기기 검증이 아니다. 실제 터치, 노치, DPI/글자 크기, 백그라운드 복귀, 기기별 프레임 시간을 확인해야 한다.
- 기본 서버 주소는 기존처럼 PC 로컬 API다. 실제 Android 기기에서 랭킹을 시험하려면 별도로 USB 포트 리버스 등 기존 로컬 연결 구성이 필요하다.
- 3D/캐릭터 아트, 사운드, 전투 연출 타임라인, 누적 미제출 보관함은 이번 범위에 포함하지 않는다.

## 폰트와 참고 자료

- [공식 Noto CJK Sans2.004](https://github.com/notofonts/noto-cjk/tree/Sans2.004)의 한국어 Regular OTF를 원본 그대로 동봉했다. `Resources/Fonts/NOTICE.txt`에 출처·저작권·SHA256, `OFL.txt`에 라이선스를 보관한다.
- 폰트 연결: [Unity FontDefinition.FromFont](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.FontDefinition.FromFont.html).
- 테스트 렌더 타깃: [Unity PanelSettings.targetTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.PanelSettings-targetTexture.html).
