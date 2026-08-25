# 인터페이스 설계

상태: 사전 초안

## Game Core 계약 후보

```csharp
public interface IGameSimulation
{
    GameState Reset(GameConfig config, int seed);
    StepResult Step(GameAction action);
}
```

## Agent 계약 후보

```csharp
public interface IPlayerAgent
{
    GameAction Decide(GameObservation observation);
}
```

## Telemetry 계약 후보

```csharp
public interface ITelemetrySink
{
    void Record(GameEvent gameEvent);
    Task FlushAsync(CancellationToken cancellationToken);
}
```

## 주요 API 후보

### 플레이

- 익명 플레이어 생성
- 활성 시즌과 설정 조회
- Run 시작
- 행동 로그 및 결과 제출
- 검증 상태 조회

### 랭킹

- 상위 랭킹 조회
- 내 주변 랭킹 조회
- 플레이어 최고 기록 조회
- Run 리플레이 조회

### 실험

- 실험과 Variant 생성
- 시뮬레이션 실행 요청
- 실행 진행률과 결과 조회
- AI 분석 요청과 결과 조회

### LiveOps

- 설정 초안 생성
- 설정 검증
- 시뮬레이션 연결
- 승인
- 배포
- 롤백

## 설계 원칙

- 에이전트에는 유효한 관찰과 행동만 제공한다.
- 클라이언트가 점수와 결과의 최종 권위가 되지 않는다.
- 모든 변경 API는 설정 버전과 감사 정보를 남긴다.
- AI 분석 API에는 원시 DB 접근 권한 대신 제한된 분석 도구를 제공한다.
- 오류 응답은 재시도 가능 여부를 구분한다.

구체적인 DTO와 API 명세는 아키텍처와 데이터 모델 확정 후 작성한다.

