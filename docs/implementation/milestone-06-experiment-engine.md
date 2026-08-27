# 마일스톤 6 — 사전 등록 실험 엔진

검증일: 2026-08-27. 상태: **로컬 계산·비교 엔진 완료, 마일스톤 6 전체는 진행 중**.

후속 구현: [M6 대시보드·영속 Batch 기록](milestone-06-dashboard.md)에서 DB/Worker·웹 연결을 완료했다. 이 문서의 미구현 항목은 엔진 단독 구현 시점의 기록이다.

## 이번에 완성한 부분

`SimOps.Experiments`와 Simulation CLI를 연결했다. 사전 등록 JSON에서 Control과 두 Treatment를 만들고, 동일 Persona/Seed의 실행을 짝지어 비교한다. 각 Run의 행동 로그를 해당 설정으로 다시 실행해 Result Hash를 검증한다. 잘못된 입력·전이·Replay 불일치는 실행 실패이며, 보호 기준을 충족한 후보가 없는 것은 정상 결과다.

결과 JSON에는 Stage별 진입/통과/실패, 누적 실패율, Turn 분포, 보상 선택·엔트로피, Run별 결과/행동 로그 Hash, 대표 Replay, paired bootstrap CI, 각 보호 기준의 판정과 설정/계산기/표본 Hash를 담는다. 실행 중 호출자가 입력 컬렉션을 바꿔도 고정된 snapshot을 사용한다.

DB에 Experiment를 저장하거나 Worker가 대량 실행하는 기능은 아직 없다. 기존 백엔드 Run 제출·Verifier는 baseline 설정만 지원하며 이번 Treatment 결과를 그 API에 제출할 수 있다고 주장하지 않는다. 이번 엔진은 등록된 Treatment 설정을 직접 사용해 Replay한다.

## 사전 등록과 재현

- 문제 변경: Novice Stage 3 완화 → 전체 난도 곡선 재설계. 소유자 승인 후 진행했다.
- [실험 정의](../experiments/difficulty-curve-001.json)와 [판정 규칙](../experiments/difficulty-curve-001.md)을 Treatment 실행 전에 커밋했다: `85a90f1`.
- 기존 탐색 Seed 0~999와 겹치지 않는 평가 Seed 10000~10999를 사용했다.
- 6 Persona × 3 Variant × 1,000 Seed = 18,000 Run. 동일 실험 2회, 총 36,000 실행과 36,000 Replay에서 불일치 0건.
- Game Version `0.1.0`, Agent Version `1.0.0`, 계산기 `difficulty-calculator-1.0.0`.
- Bootstrap은 paired Seed 인덱스 복원 추출 2,000회, Seed `20260827`, percentile 95% CI. 다중 비교 보정이나 실제 인간 효과의 추정은 아니다.
- 결과를 본 뒤 이 실험의 목표·공격력·정책·보호 기준을 수정하지 않았다.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Run-DifficultyExperiment.ps1
# 최신 Release 빌드가 있으면 -SkipBuild
```

산출물은 Git 제외 경로 `artifacts/experiments/difficulty-curve-001/report.json`과 `repeat.json`이다. 대용량 원시 결과는 위 명령으로 재생성한다. 아래 Hash와 요약은 저장소에 보존한다.

| 증거 | 값 |
|---|---|
| Plan Hash (정규화 정의 JSON) | `650f260a457e8e7e41d6dc7889de2ad09edb91dc1453142bf412a85354037891` |
| Result Digest (두 실행 동일) | `3bf0513a6d9eb46554b81a17ea8860cb9fbeb1a5be36bccf30d9c7707e9dbb08` |
| Control checksum | `388792f0b3f1dafe41f787c69894931fc2af1106e3edf098b10ed251bdda710f` |
| Uniform checksum | `b27b5378445b0c3a37def2dafba4890d1ef9b8f86689f1c7cd8caf514010b954` |
| Ramped checksum | `618f7414fee7d164a5f0e57a9a219c2433baa99b1c5caa0a704e6a7862f91fc9` |
| 기존과 동일한 Core DLL SHA-256 | `0f0bb340e522605ecd54ce231b143a14b91c861881e98e5cb8224e139d0b9d2b` |

Result Digest는 계획·표본·지표·판정에 대한 Hash다. 빌드 메타데이터가 바뀔 수 있는 assembly Hash는 별도 provenance 필드이며, 실행 시간과 함께 결정론 비교에서 제외한다.

## 결과: 게시 후보 없음

Stage 1을 보존하고 적 기본 공격력만 바꿨다. Control은 `[4,5,6,7,8,10]`, Uniform은 `[4,8,9,11,12,15]`, Ramped는 `[4,6,9,12,15,20]`이다. 총 강화량도 달라지므로 순수한 곡선 형태만의 인과효과로 해석하지 않는다.

| Persona | Control 클리어 | Uniform 클리어 | Ramped 클리어 |
|---|---:|---:|---:|
| Random | 32.2% | 4.6% | 2.2% |
| Novice | 96.8% | 67.1% | 39.8% |
| Aggressive | 96.2% | 70.1% | 52.4% |
| Defensive | 99.7% | 87.1% | 48.3% |
| Greedy | 100.0% | 97.1% | 89.0% |
| Explorer | 100.0% | 93.0% | 72.8% |

Primary는 Novice의 Stage별 **누적 실패율**과 목표 `[0%,2%,5%,10%,20%,30%]` 사이 MAE다. 아래는 모두 %p 단위이며 낮을수록 목표에 가깝다. 목표는 이번 탐색용 설계 제약이지 검증된 인간 선호가 아니다.

| 지표 | Control | Uniform | Ramped |
|---|---:|---:|---:|
| 목표 곡선 MAE | 10.6167 | 6.2333 | 10.0833 |
| Control 대비 MAE 차이 | — | -4.3833 | -0.5333 |
| 차이의 paired 95% CI | — | [-4.8667, -3.8667] | [-1.0671, 0.0004] |
| 인접 조건부 실패율 최대 증가 | 3.0031 | 28.9477 | 51.1205 |

Uniform은 Primary 개선 폭과 CI, Novice 클리어율 60~85% 조건을 만족했다. 하지만 Stage 5 실패율 약 2.30%에서 보스전 31.25%로 급증하여 허용 증가폭 15%p를 넘었다. **전체 클리어율만 보면 그럴듯한 변경도 실패 위치를 보면 탈락할 수 있다.**

Ramped는 MAE 개선 폭·CI, Novice 클리어율, 인접 실패율 급증, Greedy/Aggressive/Defensive 클리어율 기준을 위반했다. 두 Treatment 직접 비교도 Ramped−Uniform MAE 차이 +3.8500%p, CI [3.3167, 4.4000]%p로 Ramped가 목표에서 더 멀었다. 이 결과는 두 특정 후보에 관한 것이며 모든 점진 난도 설계의 실패를 뜻하지 않는다.

| Novice Stage | Control 누적 실패 | Uniform 누적 실패 | Ramped 누적 실패 |
|---|---:|---:|---:|
| 1 | 0% | 0% | 0% |
| 2 | 0% | 0% | 0% |
| 3 | 0% | 0% | 0% |
| 4 | 0% | 0.1% | 0.2% |
| 5 | 0.1% | 2.4% | 6.5% |
| 6 | 3.2% | 32.9% | 60.2% |

## 검증과 한계

- 실험 계산기 Spec 9개: 잘못된 정의·누락 필드·중복·overflow, 설정 격리, paired bootstrap, cohort MAE, 양쪽 생존자 Turn, 전수 Replay/재현, snapshot/취소, 관측 없음, 문화권 독립성.
- Core 13개 + Agent 5개와 함께 로컬 실험 스크립트에서 검증한다. Run 수와 전이/Replay 오류 수, 두 결과 Digest를 검사한다.
- `Run-Milestone5.ps1 -SkipClientBuild` 회귀 검증도 통과했다: 기존 Core/Agent/백엔드/랭킹 39개 + 새 실험 9개 = 중복 제외 48개. 기존 Windows Player의 실제 API·Worker 검증과 랭킹 조회도 `SIMOPS_ONLINE_SMOKE_PASS`를 확인했다. 로컬 테스트 Run은 추가됐지만 시즌의 고정 설정은 바뀌지 않았다. Android 실기기와 수동 화면 QA는 여전히 대기다.
- 이번 계산은 로컬 .NET의 결정론 검증이다. Treatment의 Android 실행·Unity 화면 검증이나 인간 행동 보정은 아니다.
- 관측 없는 조건부 비율은 `null`과 사유를 기록하고 후보 판정에서 통과시키지 않는다. Turn 제한은 실제 제한 종료 이벤트를 세며 제한 Turn에서 정상 승리한 Run과 구분한다.
- CI는 Seed 변동성을 보여줄 뿐, 고정된 합성 Persona의 모델 편향을 제거하지 않는다. Turn 비율도 양쪽에서 살아남은 Seed 집합에 한정된 진단 지표다.
- 기존 Game Core·Unity 패키지·공개 Config·시즌은 변경하지 않았다. 유료 서비스·카드 등록·공개 배포는 수행하지 않았다.

## 다음 구현 범위

1. Experiment/Variant·실행 결과 영속화, Ready 이후 수정 금지, 버전별 Config 조회·검증.
2. 별도 Worker의 Batch 실행·취소·재시도·lease heartbeat·중복 완료 방지. 로컬 CLI의 순차 루프를 API 요청 안에서 실행하지 않는다.
3. React/Vite 대시보드에서 같은 지표·CI·보호 기준과 **후보 없음** 상태를 표시한다.
4. M7의 근거 제한 AI 해석, M8의 사람 승인·게시·롤백을 연결한다.

추가 난도 후보는 보스 집중 현상을 줄이는 새 가설을 별도 Experiment ID와 새 평가 Seed로 사전 등록한 뒤 실행한다. 이번 결과를 좋게 만들기 위해 001을 덮어쓰지 않는다. 후보가 없어도 결과 보존·비교 UI 구현은 계속 진행할 수 있다.
