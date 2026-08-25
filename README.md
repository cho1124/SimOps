# SimOps

Synthetic Player LiveOps Lab.

SimOps는 Unity 게임을 합성 플레이어가 반복 플레이하고, 그 결과를 수집·분석하여 밸런스 변경안을 실험하는 LiveOps 연구·개발 프로젝트다.

이 프로젝트의 첫 번째 질문은 다음과 같다.

> 서로 다른 성향의 합성 플레이어에게 Control과 여러 Treatment를 플레이시킨 뒤, 어떤 설정이 더 안정적인지 데이터로 판단할 수 있는가?

## 현재 상태

- 단계: 기술 의사결정 워크숍
- 구현: 시작 전
- 확정된 게임 형식: 6번의 짧은 전투와 전투 사이 보상 선택이 있는 턴제 미니 로그라이크
- 비동기 경쟁 요소: 시즌·버전 기반 랭킹
- 확정된 설계: 제품·시스템·실험·데이터·아키텍처
- 첫 번째 목표: 합성 플레이어 기반 Control/Treatment 실험의 전체 흐름 검증

## 설계 문서 읽는 순서

1. [프로젝트 로드맵](docs/00-roadmap.md)
2. [프로젝트 정의](docs/01-product-brief.md)
3. [시스템 기획](docs/02-system-design.md)
4. [실험 및 측정 기획](docs/03-experiment-design.md)
5. [아키텍처 설계](docs/04-architecture.md)
6. [데이터 설계](docs/05-data-design.md)
7. [인터페이스 설계](docs/06-interface-design.md)
8. [구현 계획](docs/07-implementation-plan.md)
9. [검증 및 포트폴리오 계획](docs/08-validation-and-portfolio.md)

문서 상태와 작성 규칙은 [문서 안내](docs/README.md)를 따른다. 중요한 기술·제품 의사결정은 [ADR](docs/decisions/README.md)에 별도로 기록한다.

기술 선택은 [기술 의사결정 워크숍](docs/09-decision-workshop.md)의 절차에 따라 프로젝트 소유자가 직접 근거를 설명하고 승인한다.

플랫폼과 무관하게 확정 가능한 계약은 [플랫폼 독립 명세](docs/specs/README.md)에, 자동 진행 결과와 재개 위치는 [자동 진행 결과](docs/autonomous-preparation.md)에 정리돼 있다.

## 핵심 원칙

- Unity 전문성을 중심으로 백엔드, 웹, 데이터, AI, 운영 영역을 연결한다.
- 실제 사용자 행동을 합성 플레이어가 완전히 대체한다고 주장하지 않는다.
- 동일한 설정, 시드, 행동은 동일한 결과를 만들어야 한다.
- AI가 계산된 사실을 생성하지 않도록 수치 계산과 해석을 분리한다.
- AI가 제안한 운영 변경은 시뮬레이션과 사람 승인을 거쳐야 한다.
- 기능 수보다 하나의 완전한 실험 폐루프를 먼저 완성한다.

## 첫 번째 완성 흐름

```text
Control과 Treatment 설정 생성
→ 성향이 다른 합성 플레이어로 반복 실행
→ 텔레메트리 수집
→ 지표 비교
→ AI 분석가가 결과와 다음 가설 설명
→ 사람이 설정 승인
→ Unity 클라이언트에 반영
→ 동일 조건으로 재실험
```
