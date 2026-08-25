# Workshop-0009: 운영 Dashboard 플랫폼

상태: 사용자 결정 대기

연결 ADR: [ADR-0010](../decisions/0010-dashboard-platform.md)

## 결정할 문제

Experiment, Metric, Ranking, Config 승인 화면을 어떤 Web UI 기술로 만들 것인가?

## 필수 요구사항

- 표, filter, distribution chart, 진행률, 상세 Run link가 필요하다.
- Backend API와 독립적으로 타입·오류를 처리해야 한다.
- 공개 포트폴리오에서 접근 가능해야 한다.
- 운영자 권한과 승인 작업을 표현해야 한다.

## 선택지

### A. Next.js / TypeScript

- 장점: routing, server/client rendering, 배포 생태계가 통합된 React framework
- 단점: 별도 Backend가 이미 있어 server feature가 중복될 수 있고 cache/rendering 모델 학습 범위가 큼
- 공식 자료: [Next.js support policy](https://nextjs.org/support-policy)

### B. React + Vite SPA / TypeScript

- 장점: 운영 Dashboard 요구에 충분하고 Backend 책임이 명확하며 구성이 단순
- 단점: 인증·routing·data fetching 선택을 직접 조합하고 SEO·server rendering은 제공하지 않음
- 공식 자료: [Vite guide](https://vite.dev/guide/)

### C. Blazor Web App / C#

- 장점: C#과 DTO 공유, ASP.NET 통합, JavaScript 사용 감소
- 단점: TypeScript/React 학습 기회가 줄고 일부 chart·UI library 선택 폭이 다름
- 공식 자료: [ASP.NET Core Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/?view=aspnetcore-10.0)

## Codex 추천

B를 약간 우선한다. Dashboard는 SEO가 필요 없고 별도 API가 권위 서버이므로 SPA 경계가 선명하다. 다만 포트폴리오에서 Next.js 자체를 학습 목표로 삼는다면 A도 합리적이며, 선택 이유가 단순 유행이어서는 안 된다.

## 선택 전 Spike

세 후보 중 최종 두 개로 동일 화면을 만든다.

- Experiment 3 Variant × 6 Agent table
- Metric filter
- Stage pass-rate chart
- Loading/error/empty state

비교:

- 2시간 내 구현량
- 타입 생성과 API 오류 처리
- chart 통합
- 테스트 경험
- 배포 결과 크기와 초기 로드

## 프로젝트 소유자 답변

[공통 선택 설명 형식](../09-decision-workshop.md#선택-설명-형식)을 사용한다.
