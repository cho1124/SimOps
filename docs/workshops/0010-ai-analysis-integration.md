# Workshop-0010: AI 분석 통합 방식

상태: 사용자 결정 대기

연결 ADR: [ADR-0011](../decisions/0011-ai-analysis-integration.md)

## 결정할 문제

Metric Snapshot을 해석하는 AI를 제품에 어떤 책임과 실행 형태로 연결할 것인가?

## 필수 요구사항

- AI는 원시 DB와 배포 권한을 가질 수 없다.
- 입력 Metric Snapshot과 prompt/model version을 기록해야 한다.
- 출력은 구조화 schema와 metric key 근거를 가져야 한다.
- Provider 장애가 실험 Metric과 승인 흐름을 손상시키면 안 된다.

## 선택지

### A. Worker 내부 Provider-neutral Adapter

- 장점: 호출 흐름과 권한이 단순하고 Metric Snapshot 경계가 명확
- 단점: 고급 Python 분석·모델 파이프라인을 같은 process에서 사용하기 어려움

### B. Agent Framework

여러 tool 호출과 planner를 제공하는 framework를 사용한다.

- 장점: 반복 탐색, tool orchestration, tracing 기능을 빠르게 구성
- 단점: MVP의 제한된 분석에는 추상화가 크고 version·실패 원인 통제가 어려울 수 있음

### C. 별도 Python Analysis Service

- 장점: 통계·ML·LLM 생태계를 한 경계에 모으고 독립 확장 가능
- 단점: 서비스·계약·배포가 추가되고 초기에는 얇은 wrapper가 될 위험

## Codex 추천

A. 먼저 한 번의 구조화 호출과 제한된 도구로 근거 연결을 검증한다. 강화학습·통계 모델이 실제 요구가 되면 C로 분리하고, 복잡한 반복 tool 사용이 필요할 때만 B를 검토한다.

## 필수 평가

고정 Metric Snapshot으로 반복 실행한다.

성공 기준:

- 모든 수치 Claim에 존재하는 metric key 연결
- schema validation 통과
- 존재하지 않는 수치 생성 시 거부
- 같은 Snapshot의 핵심 결론 안정성 측정
- Provider timeout 시 Experiment 상태 보존

## 프로젝트 소유자 답변

[공통 선택 설명 형식](../09-decision-workshop.md#선택-설명-형식)을 사용한다.
