# ADR-0010: 운영 Dashboard 플랫폼

상태: 승인

결정일: 2026-08-27

연결 워크숍: [Workshop-0009](../workshops/0009-dashboard-platform.md)

## 고려한 선택지

- Next.js / TypeScript
- React + Vite SPA / TypeScript
- Blazor Web App / C#

## 결정

운영 Dashboard는 React + Vite SPA와 TypeScript로 구현한다.

## 이유

- 별도 ASP.NET Core API가 권위 Backend이므로 Next.js의 서버 기능과 책임이 중복된다.
- 운영 도구는 SEO와 server-side rendering보다 표·필터·차트·상태 처리의 단순성이 중요하다.
- C# 중심 영역 밖에서 TypeScript·React 클라이언트 설계 경험을 확보한다.

## 결과와 포기한 것

- 인증, routing, data fetching과 cache 정책을 직접 조합한다.
- server rendering과 Backend-for-Frontend 기능을 기본 제공하지 않는다.
- C# DTO를 직접 공유하지 않고 OpenAPI 기반 타입 생성 또는 별도 TypeScript 계약이 필요하다.

## 재검토 조건

- 공개 제품 페이지의 SEO·server rendering이 핵심 요구가 됨
- Dashboard 전용 server orchestration이나 edge rendering 필요가 커짐
- SPA 초기 로드와 client-side 데이터 처리가 측정 목표를 지속적으로 위반함
