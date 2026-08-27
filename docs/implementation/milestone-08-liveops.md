# 마일스톤 8 — 설정 게시·동기화·롤백 폐루프

검증일: 2026-08-27. 상태: 구현 및 자동 통합 검증. Windows 화면·브라우저 조작·Android 실기기 QA는 별도 대기다. 실제 운영 시즌의 설정은 변경하지 않았다.

## 완성한 흐름

```text
사전 등록 실험 → 전수 Replay·비교 결과 → 사람의 후보 승인
  → 승인자 키 + 변경 근거 + 현재 시즌 확인
  → [이전 시즌 종료 + 새 시즌 생성 + 게시 이력] 단일 트랜잭션
  → 새 Ticket → 시즌 고정 Config → Unity/Runner → Worker 재실행·랭킹
  → 게시 설정을 Control로 고정한 후속 실험 초안

롤백 → 과거 설정을 사용하는 별도의 새 시즌 (과거 시즌을 재개하지 않음)
```

M7의 AI 분석은 근거를 설명할 뿐이다. 분석 성공, guardrail 통과, 사람의 승인, 실제 게시는 각각 별개이며 게시를 자동으로 실행하지 않는다. 실제 `difficulty-curve-001`은 두 후보 모두 탈락했고 `analyzing`, 사람 판정 `null`, 게시 이력 0건을 유지한다.

## 승인과 경쟁 상태

- 운영자 키: `X-SimOps-Admin-Key`. 기존 실험 관리·조회 권한.
- 승인자 키: `X-SimOps-Approver-Key`. 후보 승인과 게시·롤백에 추가로 필요하다.
- 로컬 개발 기본 승인자 키는 `simops-local-approver-key`. `SIMOPS_APPROVER_KEY`로 교체할 수 있으며 Development 외에는 설정이 필수다. 비어 있거나 운영자 키와 같으면 API 시작을 거부한다.
- React는 두 키를 탭 메모리에만 보관한다. URL·로그·DB·localStorage에 저장하지 않는다.
- 게시 요청은 `experimentId`, `planHash`, `resultDigest`, `variantId`가 **불변 사람 승인 기록**과 같아야 한다.
- `expectedSeasonId`가 현재 활성 시즌과 달라지면 409 `SEASON_CHANGED`. 다른 관리자의 변경을 덮어쓰지 않는다.
- 같은 `idempotencyKey`와 같은 요청은 같은 게시 결과를 반환한다. 키를 다른 본문에 재사용하면 409 `IDEMPOTENCY_CONFLICT`.
- 대시보드는 새 시즌 이름·근거·기존 Ticket 영향 확인을 요구한다. 시즌·선택 실험·복원 대상이 바뀌면 확인 체크를 해제한다.

이것은 **로컬 공유 키 기반 권한 분리**다. 사용자별 로그인, 개별 승인자 식별, 이중 승인, 별도 DB role까지 구현한 production RBAC가 아니다. 게시 이력의 actor는 역할 `approver`이며 개인 신원을 증명하지 않는다. 외부 공개 전에 이 경계를 재설계해야 한다.

## 실제 API

| 경로 | 권한 | 계약 |
|---|---|---|
| `GET /api/v1/liveops/publications` | 운영자 | 최근 게시·롤백 100건 |
| `POST /api/v1/liveops/publish` | 운영자 + 승인자 | 승인 실험 참조 + `expectedSeasonId`, `name`, `reason`, `idempotencyKey` |
| `POST /api/v1/liveops/rollback` | 운영자 + 승인자 | `targetSeasonId` + 동일 시즌 전환 필드 |
| `GET /api/v1/public/seasons/{id}/config` | 공개 | 현재·과거 시즌의 고정 설정 Snapshot |
| `GET /api/v1/catalog/experiment-template?controlSeasonId={id}` | 운영자 | 해당 시즌을 Control로 고정한 Schema 2 초안 |
| `POST /api/v1/experiments/{id}/decision` | 운영자, 후보 승인 시 승인자 추가 | 기존 불변 판정 계약 유지 |

게시·롤백 성공은 200과 `{id,kind,previousSeasonId,seasonId,configChecksum,experimentId,reason,createdAt}`를 반환한다. POST는 미지의 필드·누락 필드를 거부하며 기존 접수 rate limit을 사용한다. `name`은 80자, `reason`은 2,000자, 멱등 키는 100자 이하다.

주요 오류는 401 `UNAUTHORIZED`, 403 `APPROVER_REQUIRED`, 400 `PUBLICATION_INVALID` / `PUBLICATION_CONFIG_UNSUPPORTED`, 409 `CONFIG_NOT_APPROVED` / `SEASON_CHANGED` / `CONFIG_ALREADY_ACTIVE` / `ROLLBACK_TARGET_INVALID` / `IDEMPOTENCY_CONFLICT`다. 저장 실패 시 전체 트랜잭션이 취소되며 500 응답 후에도 같은 멱등 키로 재확인할 수 있다.

## DB 경계와 기존 Ticket

Migration 008의 `config_publications`가 요청 hash·이전/새 시즌·설정·실험·근거를 함께 기록한다. UPDATE/DELETE는 DB trigger로 금지한다. `previous_season_id`, `season_id`, `idempotency_key`는 각각 unique다.

1. transaction advisory lock으로 게시·롤백을 직렬화한다.
2. 활성 시즌을 `FOR UPDATE`로 잠그고 요청의 현재 시즌을 비교한다.
3. 승인된 Config 또는 실제 이전 게시 경로의 호환 Config를 검사한다.
4. 이전 시즌 종료, 새 시즌 생성, 게시 이력을 **한 번에 commit**한다.

Ticket 발급·제출과 랭킹 반영의 시즌 shared lock은 위 전환과 교차 실행되지 않는다. 변경 전 발급됐지만 아직 제출하지 않은 Ticket은 시즌 종료 후 거부한다. 이미 접수된 검증은 완료될 수 있지만 종료 시즌 랭킹을 바꾸지 않는다. 과거 랭킹·Config·Replay는 보존한다. 서버 재시작의 seed 작업은 과거 시즌을 다시 열지 않는다.

MVP는 즉시 전환만 지원한다. 예약 게시·유예 제출·점진 rollout·진행 중 게임의 실시간 규칙 변경은 지원하지 않는다.

## 공유 Config와 호환성

`SimOps.Game.Transport`는 .NET Standard 2.1 DLL이며 Game Core와 별개다. 순수 Core의 코드와 DLL hash를 바꾸지 않고 Unity `JsonUtility`와 서버 JSON이 읽는 public field DTO를 공유한다.

```json
{
  "schemaVersion": 1,
  "gameVersion": "0.1.0",
  "configVersion": "baseline-0.1.0",
  "checksum": "388792f0b3f1dafe41f787c69894931fc2af1106e3edf098b10ed251bdda710f",
  "attackPowers": [4, 5, 6, 7, 8, 10]
}
```

위는 baseline Snapshot이다. 실제 값은 등록 Config에서만 생성한다. 현재 계약은 기존 난도 실험의 6개 Stage 공격력 변경만 지원한다. Stage 1은 불변이고 공격력은 원본 기준선의 1~3배 범위여야 한다. 나머지 필드는 baseline에서 재구성한 뒤 전체 Config checksum을 다시 계산한다. 지원하지 않는 변경은 조용히 무시하지 않고 거부한다.

- Unity는 발급된 Ticket의 **시즌 ID**로 설정을 가져온다. 그 사이 활성 시즌이 바뀌어도 다른 설정과 섞지 않는다.
- 받은 설정과 Ticket의 게임/Core/Config/점수 규칙을 비교한 뒤 시작한다. 로컬 Replay 저장에도 Snapshot을 넣는다.
- 네트워크 재시도 시 같은 시즌은 같은 발급 멱등 키를 사용한다. 활성 시즌이 바뀌면 키를 바꾸며 만료 Ticket은 새 시작을 요구한다.
- 오프라인 Practice는 항상 baseline으로 시작한다. 직전 온라인 시즌 설정이 유출되지 않는다.
- Worker는 제출 Context의 등록된 불변 Config를 로드해 재실행한다. baseline만으로 검증하던 경계를 확장했다.
- 콘솔 Runner는 `42 --season <UUID>`로 localhost:5080의 같은 Snapshot을 읽는다. `--api-url http://127.0.0.1:5081`은 격리 테스트용 포트만 허용한다.

게임 버전 `0.1.0`, Core DLL SHA-256 `0f0bb340e522605ecd54ce231b143a14b91c861881e98e5cb8224e139d0b9d2b`, 골든 Seed 42 결과 hash `c50ea84e374db937ec1dd17ea94428b60afdb169b4d64dd5eeec64128fa2fa78`를 유지한다.

## 변경 후 재실험

대시보드의 ‘현재 게시 설정으로 후속 실험 초안’은 읽기 요청으로 초안을 편집기에 넣을 뿐이다. 자동 저장·확정·실행하지 않는다. 새로운 실험 ID·가설·Seed·후보·판정 기준은 사람이 확인한다.

Experiment Schema 2는 `controlSnapshot`을 정의에 포함해 게시 설정을 Control로 고정한다. 후보의 공격력 비율은 이 Control에 적용한다. Schema 1에서는 optional Snapshot을 JSON에서 생략하므로 기존 Plan Hash와 Result Digest가 변하지 않는다. 후속 실험은 원래 실험 정의·결과를 덮어쓰지 않는다.

현재 난도 계약은 공격력 증가만 허용한다. 반복 증가로 클라이언트 지원 범위를 넘는 후보는 게시가 거부된다. 공격력 감소·다른 변수·새 지표로 실험 범위를 넓히는 것은 별도 판단과 계약 확장이 필요하다.

## 재현

```powershell
# API:5080과 격리 테스트:5081을 비운 상태에서 실행
powershell -ExecutionPolicy Bypass -File scripts/Run-Milestone8.ps1
# Vite가 이미 실행 중이고 lockfile 의존성이 설치되어 있으면 -SkipInstall
# Unity Editor 없이 HTTP/DB/Runner만 검증하려면 -SkipUnity

# 실험실 실행 (.NET / PostgreSQL / React, 실제 시즌 유지)
powershell -ExecutionPolicy Bypass -File scripts/Start-LocalLab.ps1 -SkipBuild

# 실행 중 API의 특정 시즌을 Headless로 플레이
dotnet run --project src/SimOps.Runner -c Release --no-build -- 42 --season 10000000-0000-0000-0000-000000000002
```

M8 검증은 기존 전체 회귀 테스트 후 Unity Windows·Android를 빌드하고, UUID로 이름 붙인 **임시 PostgreSQL DB와 별도 API/Worker:5081**에서만 실제 게시·롤백을 실행한다. 해당 테스트가 생성한 프로세스·DB만 정리한다. 일반 회귀 검증은 기존 방식대로 기본 DB에 테스트 실험·Run을 추가하지만 실제 대표 실험의 판정·시즌은 바꾸지 않는다. 공개 배포·카드 등록·유료 API 호출은 없다.

양성 게시 테스트는 의도적으로 느슨한 **테스트 전용** 판정 기준을 사용한다. 이는 운영 밸런스 개선의 근거가 아니며 실제 실험의 guardrail을 낮추지 않는다.

## 검증 결과

- 기존 회귀 85건: React 13, Backend·DB·Core·Agent·실험·분석 72.
- 격리 LiveOps 9건: 권한·strict 입력, DB 마지막 insert 실패의 원자적 복구, 승인·동시 멱등·stale 시즌, 비기준선 Ticket·Replay·랭킹, 실제 Runner, 실제 Windows Player, 게시 Control 후속 실험, 롤백·재시작, 이력 불변성.
- Unity Editor 골든 테스트, Windows Development Build·오프라인 smoke, Android ARM64 IL2CPP APK 빌드.
- 실제 대표 실험의 결과 digest `3bf0513a6d9eb46554b81a17ea8860cb9fbeb1a5be36bccf30d9c7707e9dbb08` 보존.

남은 검증은 실제 브라우저 화면·입력, Windows 수동 조작, Android 실기기 설치·네트워크·Pause/Resume다. jsdom 테스트나 batchmode 실행을 이 수동 QA의 완료로 계산하지 않는다. Sites 스킬의 기존 UI 보존·빌드·로컬 미리보기 확인 절차만 적용했고, 승인된 React/Vite·ASP.NET·PostgreSQL 구조는 그대로 유지했다.

## 다음 판단 지점

구현된 폐루프가 작동하는 것과 실제 밸런스 개선 후보를 찾은 것은 다르다. 다음 실제 실험은 [첫 실험 실패 기록](milestone-06-experiment-engine.md)을 보고 가설·변경 폭·성공 기준을 새로 정해야 한다. 그 결정 전에는 자동으로 후보를 승인하거나 운영 시즌을 게시하지 않는다.
