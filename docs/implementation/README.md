# 구현 기록

설계 문서를 실제 코드와 검증 결과로 연결한 마일스톤별 기록이다.

| 마일스톤 | 상태 | 기록 |
|---|---|---|
| 1. 결정론적 게임 코어 | 완료 | [Game Core 구현 및 검증](milestone-01-game-core.md) |
| 2. Unity 플레이와 리플레이 | 구현·빌드 완료 | [Unity Client 구현 및 검증](milestone-02-unity-client.md) |
| 3. 합성 플레이어 | 완료 | [Agent 계약·Persona 기준선](milestone-03-synthetic-players.md) |
| 4. 백엔드와 데이터 | 완료 | [API·Worker·PostgreSQL 통합 검증](milestone-04-backend-data.md) |
| 5. 랭킹 | 구현·통합 검증 완료 | [익명 Player·Ticket·인간 랭킹](milestone-05-human-ranking.md), 수동 화면/실기기 QA 대기 |
| 6. 실험 대시보드 | 구현·자동 검증 완료, 브라우저 수동 QA 대기 | [사전 등록 엔진](milestone-06-experiment-engine.md), [React·영속 Batch·검토 기록](milestone-06-dashboard.md) |
| 7. AI 분석가 | 구현·자동 검증·로컬 모델 검증 완료 | [근거 제한 분석·실제 모델 반복 검증](milestone-07-ai-analysis.md), 브라우저 수동 QA 대기 |
| 8. LiveOps 폐루프 | 구현·자동 통합 검증 완료 | [승인 게시·Unity/Runner 동기화·롤백·후속 실험](milestone-08-liveops.md), 수동 화면/실기기 QA 대기 |

각 기록은 구현 범위, 재현 명령, 고정된 버전과 checksum, 자동 검증 결과, 남은 위험을 함께 남긴다.
