# ADR-0013: 배포·호스팅과 CI/CD

상태: 승인

결정일: 2026-08-27

연결 워크숍: [Workshop-0012](../workshops/0012-deployment-and-ci.md)

## 고려한 선택지

- Container PaaS + Managed PostgreSQL
- 단일 VPS + Docker Compose
- Cloud-native Managed Service

## 결정

월 비용 0원, 결제 카드 미등록을 고정 제약으로 두고 Container PaaS + Managed PostgreSQL + 정적 Hosting을 조합한다.

현재 공개 데모 기준선:

- Dashboard: GitHub Pages
- API: Render Free Web Service의 Docker 배포
- Worker: 별도 Render Free Web Service로 배포하고 인증된 wake·health endpoint만 노출
- Database: Neon Free PostgreSQL
- CI: 공개 GitHub 저장소의 표준 GitHub Actions runner
- Windows·Android build: itch.io 배포, GitHub Releases 선택적 mirror

Provider 무료 정책은 배포 직전에 공식 문서로 다시 검증한다.

2026-08-27 확인 기준:

- Render Free Web Service는 유휴 시 종료되고 무료 Background Worker instance는 제공하지 않는다: [Render free services](https://render.com/docs/free), [Render free worker limitation](https://render.com/docs/your-first-deploy)
- Neon Free는 카드 없이 사용할 수 있는 PostgreSQL을 제공한다: [Neon pricing](https://neon.com/pricing)
- 공개 저장소의 표준 GitHub Actions runner와 GitHub Pages를 무료로 사용할 수 있다: [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions), [GitHub Pages](https://docs.github.com/en/pages/getting-started-with-github-pages)
- itch.io는 무료 계정으로 플랫폼별 다운로드 build를 배포할 수 있다: [itch.io creator guide](https://itch.io/docs/creators/getting-started)

## 이유

- 비용과 카드 등록 없이 공개 Dashboard, API, 영속 DB와 game build 접근 경로를 제공한다.
- 로컬 Docker Compose의 API·Worker 분리 구조를 공개 환경에서도 별도 배포 단위로 유지한다.
- 항상 켜진 운영 서비스보다 재현 가능한 포트폴리오 데모를 우선한다.

## 결과와 포기한 것

감수하는 것:

- Cold Start, 제한된 compute·storage·bandwidth와 비보장 uptime
- Render 무료 Background Worker가 없어 Web Service 형태의 wake endpoint가 필요함
- 두 Render service의 무료 instance hour를 공유하며 장시간 상시 실행할 수 없음
- Neon 저장 한도에 맞춘 Summary 우선·원시 Event 보존 정책 필요
- 외부 AI 유료 호출은 기본 공개 데모에서 보장하지 않음

## 재검토 조건

- 무료 정책 변경 또는 카드 등록 요구
- Cold Start 때문에 핵심 데모를 안정적으로 수행할 수 없음
- 저장·실행 한도가 대표 실험과 검증 흐름을 수용하지 못함
- 실제 사용자에게 항상 켜진 서비스와 SLA가 필요해짐

재검토 시 유료 전환을 자동 승인하지 않고 월 예산을 다시 결정한다.
