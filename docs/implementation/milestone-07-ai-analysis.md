# 마일스톤 7 — 근거 제한 AI 분석가

검증일: 2026-08-27. 상태: **구현·자동 검증·실제 로컬 모델 검증 완료**, 실제 브라우저 수동 QA 대기. 공개 배포와 설정 게시를 포함하지 않는다.

## 구현 범위

ADR-0011의 Worker 내부 provider-neutral Adapter를 구현했다. 완료된 실험 결과를 고정 Metric Snapshot으로 변환하고, 운영자 요청에 따라 별도 분석 Job을 실행한다. 계산과 모델 해석, 사람의 판정은 서로 다른 책임이다.

```text
완료된 실험 Summary → 고정 Metric Snapshot + SHA-256
  → POST analyses (202, 멱등 키) → PostgreSQL 분석 Job
  → AnalysisWorker → 로컬 모델의 지표·가설 선택
  → 서버가 원본 수치 연결 → Schema·근거·결론 검증
  → 불변 보고서 저장 → React 근거·미검증 가설·다음 실험 표시
```

모델이 요청할 수 있는 동적 도구 실행 루프는 만들지 않았다. 허용 조회 중 `GetMetricSnapshot`과 `GetGuardrailViolations`에 해당하는 읽기 결과를 미리 해석해 주입한다. 원시 Run, 임의 SQL, 운영자 키, Player credential, 사람의 판정, 초안의 자유 서술 가설은 전달하지 않는다. 정의·대표 Run의 동적 조회는 필요할 때 추가할 범위로 남겼다.

## 왜 자유 서술을 제한했는가

숫자 정규식만 검사하면 “백 명”처럼 단어로 적은 수치나, 존재하는 숫자를 다른 의미로 인용한 내용을 놓친다. 이번 버전은 **제한된 해석 어휘를 선택하는 분석가**다. 범용 자유 서술 챗봇이 아니다.

- 모델 출력: `schemaVersion`, `assessment`, `observations[{metricKey}]`, `hypotheses[{code,metricKeys}]`, `nextExperiments[{code,metricKeys}]`.
- 모델은 숫자 필드를 작성할 수 없다. 서버 Adapter가 선택된 지표의 원본 값을 연결한다.
- 최종 보고서는 `observations[{metricKey,value}]`를 갖는다. 저장 경계에서 값이 Snapshot과 정확히 같은지 다시 검사한다.
- 관측 없는 지표(`null`), 모르는 key, 중복 관측, 추가 필드, 잘못된 결론·해석 코드, 해석과 호환되지 않는 근거는 거부한다.
- 결론은 계산기가 정한 검토 후보 유무와 일치해야 한다. 모델이 승인 여부를 결정하지 않는다.
- 가설: `failure_concentration`(실패 집중 가능성), `policy_sensitivity`(행동 정책별 영향 차이 가능성).
- 다음 실험: `redistribute_pressure`(총량 고정 후 구간 배분 비교), `replicate_seeds`(새 시드에서 재검증).
- UI의 설명 문장은 버전 관리되는 고정 문구다. 모델은 근거와 허용된 해석을 선택한다. 가설은 인과적 결론이 아니며 인간의 재미·유지율을 입증하지 않는다.

실제 초기 모델 검증에서 소수점 복사 오류가 발견되어 저장이 거부됐다. 이를 허용 오차로 통과시키지 않고 **지표 참조만 생성하고 수치는 서버에서 연결**하는 계약으로 개선했다. 실패 Job은 이력으로 유지한다.

## Provider와 비용

| 설정 | 동작 |
|---|---|
| 미설정 또는 `SIMOPS_ANALYSIS_PROVIDER=offline` | 규칙 기반 데모. 화면에 **LLM 아님** 표시 |
| `SIMOPS_ANALYSIS_PROVIDER=ollama` | 설치된 로컬 모델을 사용. 기본 모델 `qwen2.5:3b` |
| `SIMOPS_ANALYSIS_MODEL` | 설치된 로컬 모델 tag. Worker 시작 시 설정 |
| 지원하지 않는 Provider | 해당 분석만 실패. 다른 Worker 기능을 중지하지 않음 |

Ollama Adapter는 `127.0.0.1:11434`만 사용하고 HTTP redirect·proxy를 끈다. 모델 목록과 상세 정보에서 설치된 GGUF 모델임을 확인하고 remote/cloud 모델을 거부한다. 호출 전후 model digest를 비교하여 모델 tag 변경도 거부한다. 모델 다운로드, API key, 카드 등록, 유료 서비스 호출은 하지 않는다. 실패 후 offline으로 조용히 전환하지 않는다.

현재 생성 설정: temperature 0, seed 42, context 16,384, 최대 출력 1,800 token. 반복 결과가 언제나 같다고 보장하지 않는다. 모델/드라이버/하드웨어 변경에 따른 재평가가 필요하다. Snapshot에는 계산기·Agent·Game Core artifact hash도 포함한다.

## 실행과 검증

```powershell
# 기존 게임·랭킹·실험 + offline 분석 + DB 장애 주입 + UI 테스트
powershell -ExecutionPolicy Bypass -File scripts/Run-Milestone7.ps1
# 동일 lockfile 설치가 있고 Vite가 실행 중이면 -SkipInstall

# 이미 모델이 설치되어 있고 Ollama가 실행 중인 환경에서만
$env:SIMOPS_ANALYSIS_PROVIDER = 'ollama'
$env:SIMOPS_ANALYSIS_MODEL = 'qwen2.5:3b'
powershell -ExecutionPolicy Bypass -File scripts/Start-LocalLab.ps1 -SkipBuild

# 위 실험실을 유지한 채 별도 터미널에서 실제 모델 3회 검증
dotnet run --project tests/SimOps.Backend.Specs -c Release --no-build -- --analysis-ollama
```

실제 모델 검증은 `difficulty-curve-001`에 분석 이력만 추가한다. 실험당 이력 상한은 10개이므로 무제한 반복 실행하지 않는다. 검증 스크립트는 유료 모델이나 미설치 모델을 자동 다운로드하지 않는다. 기본 자동 검증은 별도 테스트 실험에 offline 보고서를 생성한다.

대시보드: `http://127.0.0.1:5173`, 개발용 키 `simops-local-dev-key`. 결과 아래 ‘근거 기반 AI 분석’에서 요청·진행 상태·이력·모델 출처·근거 수치·지표 정의·미검증 가설을 확인한다. 로컬 키는 공개 환경에서 사용하지 않는다.

## 저장·실패 경계

마이그레이션 006의 `analysis_jobs`가 입력 Snapshot, 멱등 키, lease, 오류 코드, 검증된 보고서를 함께 보존한다. 007은 JSON 필드 누락이 PostgreSQL CHECK의 NULL 판정으로 통과하지 못하도록 Snapshot/보고서 정합성을 강화한다. 논리 `analysis_reports`를 독립 테이블로 나누지 않고 성공 Job의 불변 JSONB로 구현했다.

- API: `GET/POST /api/v1/experiments/{id}/analyses`, 기존 운영자 인증 적용. POST는 strict Schema이고 Provider/Model 지정 필드를 허용하지 않는다.
- 완료된 실험의 Plan Hash·Result Digest가 요청과 같아야 접수한다. 원시 Run을 다시 읽거나 통계를 재계산하지 않는다.
- 전역 동시 대기/실행 2개, 실험당 이력 10개. 접수는 advisory lock으로 직렬화한다.
- `queued → running → succeeded / failed`. 일시 오류는 최대 3회, 2초 backoff. Schema/근거 오류는 즉시 실패한다.
- lease 30초, heartbeat 5초, Provider 작업 제한 120초. 만료 lease는 회수 가능하며 stale 완료·heartbeat는 거부한다.
- Provider 처리 중 DB connection/transaction을 유지하지 않는다. 모델 호출 동시성은 Worker당 1이다.
- 입력/완료/실패 이력은 DB trigger로 수정·삭제를 금지한다. 원시 모델 오류 응답과 credential은 오류 컬럼에 넣지 않는다.
- Snapshot·모델 digest·Prompt/Validator version·Output/Conclusion hash를 성공 보고서에 저장한다. 잘못된 보고서는 저장하지 않는다.
- 분석 코드에는 Experiment 상태 변경, 사람 판정, 시즌 게시·롤백 호출이 없다. 현재의 하나의 DB role은 프로세스 전체 권한이므로 SQL role 수준의 완전한 분리는 아니다.

## 검증 결과

`Run-Milestone7.ps1 -SkipInstall`의 최종 실행: **82개 테스트 통과**, .NET 빌드 경고/오류 없음, React production build 성공.

| 범위 | 통과 |
|---|---:|
| Core / Agent | 13 / 5 |
| Run API / lease·fresh DB | 8 / 3 |
| 인간 랭킹 HTTP / DB | 4 / 6 |
| 실험 계산기 / HTTP / DB | 9 / 3 / 6 |
| 분석 순수·Adapter / HTTP / DB | 9 / 2 / 4 |
| React 컴포넌트 | 10 |

분석 검증에는 숫자 변조·존재하지 않는 지표·null 지표·추가 필드·잘못된 결론·근거와 해석 불일치 거부, Adapter 숫자 필드 거부, provider 취소, local-only 검사, 멱등 접수·용량 제한, 만료 lease 회수·중복 완료 거부, DB 불변성·실패 시 기존 사람 판정과 지표 보존을 포함한다. Provider timeout은 모의 HTTP 취소로 검증했고, Worker의 120초 실시간 timeout을 기다리는 장시간 테스트는 수행하지 않았다.

실제 Ollama `qwen2.5:3b` 검증 1개 시나리오에서 **3회 모두 성공**했다. 각 Job은 1회 시도에서 완료됐다.

| Job | 접수 후 완료 관측 시간 |
|---|---:|
| `8ba9469f-0872-4867-a56e-f4c0f5999433` | 4,650 ms |
| `d0eb5421-728e-478a-9abd-ebc6f93b6542` | 2,061 ms |
| `cdee07a9-997a-43b7-9677-bf5391c8d3db` | 2,056 ms |

위 시간은 HTTP polling 간격을 포함한 로컬 관측치다. 모두 다음의 동일한 근거와 모델을 사용했다.

- Experiment: `difficulty-curve-001`, 18,000 Run.
- Model digest: `357c53fb659c5076de1d65ccb0b397446227b71a42be9d1603d46168015c9e4b`.
- Metric Snapshot: `6f822292141afe7f8402165f8b214db4f09de6b17e46a4eb1a441f425e852e1f`.
- Conclusion hash: `429ed6e070230867dfd3adc51db3ce0e903370be9f5ac0ee60c6f4b3171c26ac`, **3/3 동일**.
- 선택된 가설: `failure_concentration`. 다음 실험: `redistribute_pressure`, `replicate_seeds`.
- 근거로 선택한 두 인접 실패율 급증 지표는 Ramped `0.511205298297129`, Uniform `0.28947697697697694`. 숫자는 모델이 아닌 서버 Snapshot에서 연결했다.

Assessment는 계산된 후보 유무와 일치하도록 강제하므로, 그 일치만으로 모델 추론 능력을 평가하지 않는다. Conclusion hash는 assessment와 선택한 가설·다음 실험 코드의 정렬된 집합이며, 자유 서술이나 모든 근거 순서의 일치도를 뜻하지 않는다.

초기 숫자 복사 계약에서 거부된 Job `b9df564a-c412-4a32-8fa3-3cd66fee21e4`도 삭제하지 않았다. 초기 실패를 제외한 채 처음부터 모두 성공한 것처럼 표현하지 않는다.

기존 실험 Result Digest `3bf0513a6d9eb46554b81a17ea8860cb9fbeb1a5be36bccf30d9c7707e9dbb08`, `analyzing` 상태, `decision=null`, baseline 시즌 `10000000-0000-0000-0000-000000000002`는 유지됐다. Game Core DLL SHA-256도 기존 `0f0bb340e522605ecd54ce231b143a14b91c861881e98e5cb8224e139d0b9d2b`와 같다.

## 남은 범위

- 실제 브라우저 화면/입력 수동 QA, Windows 화면·Android 실기기 QA는 별도다. jsdom 컴포넌트 검증을 실제 브라우저 QA로 표현하지 않는다.
- 제한된 어휘 밖의 자유 가설·설명은 지원하지 않는다. 확장할 때는 새 Schema/Prompt/Validator version과 부정 평가 사례가 필요하다.
- 반복 결론 일치율은 이 고정 Snapshot의 관측치다. 일반적인 분석 능력·인간 전문가와의 동등성을 입증하지 않는다.
- Ollama CPU/GPU 사용량과 장시간 다중 작업 부하는 별도 측정 과제다. 응답 시간은 전체 성능 보장이 아니다.
- 서버의 기존 Run 검증기는 baseline-only다. M8에서 승인된 Config 게시·Unity 동기화·롤백을 연결해야 한다.
- 공개 배포와 실제 운영 설정의 승인·게시는 수행하지 않았다.

## 참고한 공식 계약

- [Ollama Chat API](https://docs.ollama.com/api/chat): 구조화 응답과 비스트리밍 호출.
- [Ollama Structured Outputs](https://docs.ollama.com/capabilities/structured-outputs): JSON Schema 제한.
- [Ollama 모델 상세 조회](https://docs.ollama.com/api-reference/show-model-details): 로컬 모델 메타데이터 확인.

웹 구축 스킬의 기존 화면 유지·미리보기·빌드 검증 절차를 적용했으며, 승인된 React/Vite·ASP.NET·PostgreSQL 구조와 로컬 실행 범위를 보존했다.
