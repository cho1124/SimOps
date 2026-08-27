# ADR-0011: AI 분석 통합

상태: 승인

결정일: 2026-08-27

연결 워크숍: [Workshop-0010](../workshops/0010-ai-analysis-integration.md)

## 고려한 선택지

- Worker 내부 Provider-neutral Adapter
- Agent Framework
- 별도 Python Analysis Service

## 결정

AI 분석은 Worker 내부의 provider-neutral Adapter로 실행한다.

- 입력은 고정된 Metric Snapshot과 허용된 조회 결과로 제한한다.
- 출력은 구조화 Schema와 근거 `metricKeys`를 검증한다.
- AI에는 원시 DB, credential, 승인·배포 권한을 제공하지 않는다.
- 구체적인 Provider와 model은 Adapter 뒤의 배포 설정으로 선택한다.

## 이유

- 현재 AI 작업은 제한된 근거를 해석하는 단일 비동기 Job으로 표현할 수 있다.
- Agent Framework나 별도 Python Service 없이 실패·재시도·권한 경계를 명확히 유지한다.
- Provider 장애가 Experiment Metric과 승인 흐름을 손상시키지 않게 한다.

## 결과와 포기한 것

- 복잡한 다단계 tool orchestration 기능을 직접 구성해야 한다.
- Python 통계·ML pipeline과의 직접 통합은 약하다.
- 무료 공개 배포에서는 로컬 모델 또는 미리 생성한 보고서를 사용할 수 있다.

## 재검토 조건

- 반복적·동적인 tool 계획이 제품 요구가 됨
- 강화학습·통계 모델 학습이 핵심 경로가 되어 Python runtime이 필요함
- AI 작업의 독립 확장·배포·보안 경계가 필요해짐
