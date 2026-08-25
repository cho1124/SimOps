# 논리 인터페이스 설계

상태: 검토 중

이 문서는 REST, gRPC, GraphQL 또는 Backend 언어를 선택하기 전의 의미상 계약이다. Transport별 DTO와 Endpoint는 [Workshop-0006](workshops/0006-api-style.md) 승인 후 확정한다.

## 1. 원칙

- Domain Command와 외부 Transport를 분리한다.
- DB Entity를 외부 계약으로 노출하지 않는다.
- 모든 쓰기 Command는 actor, correlation ID, idempotency key를 받을 수 있어야 한다.
- 비동기 작업은 접수 결과와 상태 resource를 분리한다.
- 오류는 code, retryable, correlation ID를 가진다.
- Game, Config, Score Rule, Agent, Schema Version을 명시적으로 전달한다.

## 2. Game Core 계약

### RunContext

```text
gameVersion
gameCoreChecksum
configVersion
configChecksum
scoreRuleVersion?
baseSeed
actorKind
clientPlatform?
clientBuild?
```

`clientPlatform`과 `clientBuild`는 검증·관측 메타데이터이며 Game Core 상태 전이와 Result Hash에 영향을 주지 않는다.

### GameObservation

Agent와 사람 UI가 판단에 사용하는 공개 상태다.

```text
runId
stage
turn
phase
playerPublicState
enemyPublicState
enemyIntent
offeredRewards
validActions[]
```

숨은 RNG 상태, 다음 Reward, 미래 Enemy Intent를 포함하지 않는다.

### GameAction

```text
sequence
stage
turn
phase
actionType
targetId?
rewardId?
clientMetadata?
```

`clientMetadata`는 Game Core 판정과 Result Hash에 영향을 줄 수 없다.

### StepResult

```text
accepted
rejectionCode?
nextObservation
emittedDomainEvents[]
isTerminal
terminalResult?
```

### GameSimulation

언어 중립 의미:

```text
Reset(RunContext) -> GameObservation
Apply(GameAction) -> StepResult
GetCanonicalResult() -> RunResult
```

## 3. Agent 계약

```text
Initialize(AgentContext)
Decide(GameObservation) -> AgentDecision
OnRunEnded(RunResult)
```

AgentDecision:

```text
selectedAction
policyVersion
decisionSeed
optionalScores[]
optionalReason
```

`optionalReason`과 utility score는 분석 정보이며 Game Core 결과에 영향을 주지 않는다.

## 4. Replay 계약

### ActionLog

- schemaVersion
- runId
- ordered actions
- actionCount

### RunResult

- outcome
- clearedStage
- totalTurns
- finalPlayerState
- buildSignature
- stageSummaries
- scoreComponents
- finalScore
- resultHash

### VerificationResult

```text
status: verified | rejected | retryable_error
authoritativeResult?
rejectionCode?
firstMismatchSequence?
correlationId
```

상세 절차는 [재실행·검증 프로토콜](specs/replay-verification-protocol.md)을 따른다.

## 5. 플레이어 Use Case

### RegisterAnonymousPlayer

입력:

- requestedNickname
- clientInstanceId

출력:

- playerId
- credential
- normalizedNickname

### GetActiveSeason

출력:

- Season identity와 기간
- Game, Config, Score Rule 버전·checksum
- Config content 또는 다운로드 reference

### BeginHumanRun

입력:

- player identity
- seasonId
- clientGameCoreChecksum
- idempotencyKey

출력:

- runId
- signed Run Ticket
- RunContext
- expiresAt

### SubmitHumanRun

입력:

- Run Ticket
- Action Log
- client Result Hash와 Summary
- idempotencyKey

출력:

- runId
- status resource
- acceptedAt

처리는 비동기일 수 있다.

### GetRunStatus

출력:

- submitted, verifying, verified, rejected
- VerificationResult
- verified score와 rank, 가능한 경우

### GetLeaderboard

입력:

- seasonId
- page 또는 cursor
- optional aroundPlayerId

출력:

- frozen/active status
- ranked entries
- current player position
- ranking rule metadata

## 6. Experiment Use Case

### CreateExperimentDraft

입력:

- name
- hypothesis
- primaryMetric
- decisionRules
- Seed Policy

### AddVariant

입력:

- experimentId
- role: control 또는 treatment
- immutable configId

### AddAgentDefinition

입력:

- experimentId
- validated agentDefinitionId
- runsPerCell

### MarkExperimentReady

검증:

- Control 정확히 하나
- Treatment 하나 이상
- Agent 하나 이상
- Metric 정의 존재
- 판정 규칙 존재
- 모든 Config와 Agent가 validated

### StartSimulationBatch

출력:

- batchId
- expectedCells
- expectedRuns
- progress resource

### GetExperimentResults

출력:

- Experiment definition
- Cell completion
- Metric Snapshot
- guardrail violations
- representative Run references
- AI Analysis Report, 존재하는 경우

### RecordExperimentDecision

입력:

- conclusion
- selectedVariantId?
- reason
- approver

AI는 이 Command를 호출할 수 없다.

## 7. Config와 Season Use Case

### CreateConfigDraft

- parentConfigId?
- Game Version
- Config content

### ValidateConfig

출력:

- schema errors
- domain range errors
- missing content IDs
- checksum

### RequestConfigApproval

조건:

- Validated
- 필요한 Simulation 결과 연결

### ApproveConfig

입력:

- configId
- approver
- reason

### PublishSeason

입력:

- approved configId
- scoreRuleVersionId
- startsAt / endsAt

결과:

- immutable Season
- Publication audit record

### RollbackPublishedConfig

MVP 의미:

- 현재 Season 종료
- 이전 approved Config를 사용하는 새 Season 생성
- reason과 영향받은 Season 기록

## 8. AI Tool 계약

허용 Tool:

- GetExperimentDefinition
- GetMetricSnapshot
- GetGuardrailViolations
- GetRepresentativeRuns

금지:

- 임의 SQL
- Config content 수정
- ApproveConfig
- PublishSeason
- Rollback
- Player credential 조회

AnalysisReport는 각 Claim에 `metricKeys[]`를 포함해야 한다.

- AI-001: 존재하지 않는 metric key나 Snapshot에 없는 수치 Claim은 보고서 검증에서 거부한다.
- AI-002: AI Provider 장애·timeout·schema 오류가 Experiment Metric과 사람의 판정 상태를 변경하면 안 된다.

## 9. 오류 Envelope

```json
{
  "code": "ACTION_SEQUENCE_INVALID",
  "message": "Action sequence must be contiguous.",
  "retryable": false,
  "correlationId": "...",
  "details": {
    "expected": 12,
    "actual": 14
  }
}
```

`message`는 사람용이며 client logic은 `code`를 사용한다. details에는 secret과 내부 stack trace를 포함하지 않는다.

## 10. 호환성과 Version

- 외부 계약은 schema version을 가진다.
- optional 필드 추가는 기존 client가 무시할 수 있어야 한다.
- 필드 의미·타입 변경은 새 schema version이다.
- Server는 지원하지 않는 상위 schema를 명시적으로 거부한다.
- Game Core 계약 변경은 Game Version과 Replay compatibility를 함께 검토한다.
- 종료 Season의 Replay를 지원하지 못하면 이유와 필요한 legacy version을 표시한다.

## 11. Transport 결정 후 남은 작업

- Endpoint 또는 service method 이름
- HTTP status, gRPC status 또는 GraphQL error mapping
- pagination/cursor 형식
- generated client 전략
- payload 압축과 Action Log 크기 제한
- OpenAPI/Proto/GraphQL schema
- 인증 header와 credential 전달 방식
