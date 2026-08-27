import { useEffect, useState } from "react";
import { Api, percent, points } from "./api";
import type { Cell, Detail, Report } from "./contracts";

export default function Results({
  report,
  detail,
  api,
  busy,
  command,
}: {
  report: Report;
  detail: Detail;
  api: Api;
  busy: boolean;
  command: (action: () => Promise<unknown>, message: string) => Promise<void>;
}) {
  const [agent, setAgent] = useState("novice"),
    [conclusion, setConclusion] = useState("rejected"),
    [variant, setVariant] = useState(""),
    [reason, setReason] = useState("");
  useEffect(() => {
    setConclusion("rejected");
    setVariant("");
    setReason("");
    setAgent("novice");
  }, [detail.id]);
  const cells = report.cells.filter((cell) => cell.agentId === agent),
    variants = detail.definition.variants;
  return (
    <>
      <div className="metrics">
        <article className="metric">
          <small>VALIDATED RUNS</small>
          <strong>{report.completedRuns.toLocaleString()}</strong>
          <small>
            Replay {report.replayCheckedRuns.toLocaleString()} · 불일치{" "}
            {report.replayMismatchCount}
          </small>
        </article>
        <article className="metric">
          <small>REVIEW CANDIDATES</small>
          <strong>
            {report.reviewCandidateIds.length
              ? `${report.reviewCandidateIds.length}개`
              : "후보 없음"}
          </strong>
          <small>사전 고정된 모든 보호 기준 통과 필요</small>
        </article>
        <article className="metric">
          <small>PUBLICATION</small>
          <strong>미배포</strong>
          <small>실험 결과와 공개 시즌은 분리</small>
        </article>
      </div>
      <section className="panel">
        <div className="toolbar">
          <h2>플레이어별 클리어율</h2>
          <label htmlFor="agent">상세 성향</label>
          <select
            id="agent"
            value={agent}
            onChange={(e) => setAgent(e.target.value)}
          >
            {detail.definition.agentIds.map((id) => (
              <option key={id}>{id}</option>
            ))}
          </select>
        </div>
        <div className="table-wrap">
          <table>
            <caption>각 성향·설정의 유효 Run 기준</caption>
            <thead>
              <tr>
                <th>Persona</th>
                {variants.map((v) => (
                  <th key={v.id}>{v.id}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {detail.definition.agentIds.map((id) => (
                <tr key={id}>
                  <td>{id}</td>
                  {variants.map((v) => (
                    <td key={v.id}>
                      {percent(
                        report.cells.find(
                          (c) => c.agentId === id && c.variantId === v.id,
                        )?.clearRate,
                      )}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
      <section className="panel">
        <h2>{agent} · 실패가 발생하는 구간</h2>
        <p className="muted">
          누적 실패율 · 전체 시작 Run이 분모입니다. 앞 단계에서 실패한 Run도
          포함합니다.
        </p>
        <div className="legend">
          {variants.map((v, i) => (
            <span key={v.id}>
              <i className={["control", "uniform", "ramped"][i]} />
              {v.id}
            </span>
          ))}
        </div>
        <div className="chart" aria-hidden="true">
          {[1, 2, 3, 4, 5, 6].map((stage) => (
            <div key={stage}>
              <div className="stage-bars">
                {cells.map((cell, i) => (
                  <div
                    key={cell.variantId}
                    className={`bar ${["control", "uniform", "ramped"][i]}`}
                    style={{
                      height: `${100 * cell.stages[stage - 1].cumulativeFailureRate}%`,
                    }}
                    title={`${cell.variantId}: ${percent(cell.stages[stage - 1].cumulativeFailureRate)}`}
                  />
                ))}
              </div>
              <div className="stage-label">S{stage}</div>
            </div>
          ))}
        </div>
        <div className="table-wrap">
          <table>
            <caption>그래프의 정확한 수치</caption>
            <thead>
              <tr>
                <th>Stage</th>
                {cells.map((c) => (
                  <th key={c.variantId}>{c.variantId}</th>
                ))}
                <th>사전 목표 (Novice)</th>
              </tr>
            </thead>
            <tbody>
              {[1, 2, 3, 4, 5, 6].map((stage) => (
                <tr key={stage}>
                  <td>S{stage}</td>
                  {cells.map((c) => (
                    <td key={c.variantId}>
                      {percent(c.stages[stage - 1].cumulativeFailureRate)}
                    </td>
                  ))}
                  <td>
                    {percent(
                      detail.definition.targetCumulativeFailureRates[stage - 1],
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <details>
          <summary>진입 수·조건부 통과율·대표 Replay</summary>
          {cells.map((cell) => (
            <CellEvidence key={cell.variantId} cell={cell} />
          ))}
        </details>
      </section>
      <section className="panel">
        <h2>사전 기준에 따른 후보 판정</h2>
        <p className="muted">
          Novice 곡선 MAE 차이: Treatment − Control. 음수면 목표에 가까워집니다.
          95% CI는 paired Seed bootstrap이며 다중 비교 보정·인간 효과 추정이
          아닙니다.
        </p>
        <div className="comparison">
          {report.comparisons.map((c) => (
            <article className="panel" key={c.variantId}>
              <h3>
                {c.variantId}{" "}
                <span
                  className={`status ${c.eligibleForHumanReview ? "analyzing" : "failed"}`}
                >
                  {c.eligibleForHumanReview ? "검토 후보" : "기준 미달"}
                </span>
              </h3>
              <p>{points(c.noviceMaeDifference.difference)}</p>
              <p className="muted">
                95% CI [{points(c.noviceMaeDifference.lower95)},{" "}
                {points(c.noviceMaeDifference.upper95)}]
              </p>
              <ul className="fail-list">
                {c.checks
                  .filter((x) => !x.passed)
                  .map((check) => (
                    <li key={check.key}>
                      {check.key}
                      <br />
                      {check.observed?.toFixed(6) ?? "관측 없음"} ·{" "}
                      {check.requirement}
                    </li>
                  ))}
              </ul>
              <details>
                <summary>전체 보호 기준 ({c.checks.length})</summary>
                <div className="table-wrap">
                  <table>
                    <thead>
                      <tr>
                        <th>지표</th>
                        <th>관측</th>
                        <th>기준</th>
                        <th>판정</th>
                      </tr>
                    </thead>
                    <tbody>
                      {c.checks.map((check) => (
                        <tr key={check.key}>
                          <td>{check.key}</td>
                          <td>{check.observed?.toFixed(6) ?? "관측 없음"}</td>
                          <td>{check.requirement}</td>
                          <td>{check.passed ? "통과" : "미달"}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </details>
            </article>
          ))}
        </div>
        <p className="muted">
          {report.treatmentMaeDifferenceDirection}:{" "}
          {points(report.treatmentMaeDifference.difference)} · 95% CI [
          {points(report.treatmentMaeDifference.lower95)},{" "}
          {points(report.treatmentMaeDifference.upper95)}]
        </p>
        <p className="hash">
          RESULT {report.resultDigest}
          <br />
          CALCULATOR {report.calculatorVersion}
        </p>
      </section>
      <section className="panel">
        <h2>사람의 검토 기록</h2>
        <p className="notice">
          이 기록은 게시 명령이 아닙니다. 기존 Unity 설정과 랭킹 시즌은 변경하지
          않습니다. 확정된 검토 기록도 덮어쓸 수 없습니다.
        </p>
        {detail.decision ? (
          <>
            <p>
              <strong>{detail.decision.conclusion}</strong>
              {detail.decision.selectedVariantId &&
                ` · ${detail.decision.selectedVariantId}`}
            </p>
            <p>{detail.decision.reason}</p>
          </>
        ) : (
          <form
            className="decision-form"
            onSubmit={(e) => {
              e.preventDefault();
              void command(
                () =>
                  api.request(
                    `/api/v1/experiments/${encodeURIComponent(detail.id)}/decision`,
                    {
                      planHash: detail.planHash,
                      resultDigest: report.resultDigest,
                      conclusion,
                      selectedVariantId:
                        conclusion === "approved_candidate" ? variant : null,
                      reason,
                    },
                  ),
                "검토 결과를 기록했습니다. 게임 설정은 배포하지 않았습니다.",
              );
            }}
          >
            <div className="two-columns">
              <div>
                <label htmlFor="conclusion">결론</label>
                <select
                  id="conclusion"
                  value={conclusion}
                  onChange={(e) => setConclusion(e.target.value)}
                >
                  <option value="rejected">후보 기각</option>
                  <option value="rerun">새 실험 필요</option>
                  <option
                    value="approved_candidate"
                    disabled={!report.reviewCandidateIds.length}
                  >
                    후보 승인 (게시 아님)
                  </option>
                </select>
              </div>
              {conclusion === "approved_candidate" && (
                <div>
                  <label htmlFor="variant">통과한 후보</label>
                  <select
                    id="variant"
                    required
                    value={variant}
                    onChange={(e) => setVariant(e.target.value)}
                  >
                    <option value="">선택하세요</option>
                    {report.reviewCandidateIds.map((id) => (
                      <option key={id}>{id}</option>
                    ))}
                  </select>
                </div>
              )}
            </div>
            <label htmlFor="reason">판단 근거</label>
            <textarea
              id="reason"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              required
              maxLength={2000}
              placeholder="어떤 지표를 근거로 이 결론을 내렸나요?"
            />
            <button
              disabled={
                busy ||
                !reason.trim() ||
                detail.status !== "analyzing" ||
                (conclusion === "approved_candidate" && !variant)
              }
            >
              검토 결과 확정
            </button>
          </form>
        )}
      </section>
    </>
  );
}
function CellEvidence({ cell }: { cell: Cell }) {
  return (
    <div>
      <h3>{cell.variantId}</h3>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Stage</th>
              <th>진입</th>
              <th>통과</th>
              <th>실패</th>
              <th>조건부 통과율</th>
            </tr>
          </thead>
          <tbody>
            {cell.stages.map((s) => (
              <tr key={s.stage}>
                <td>{s.stage}</td>
                <td>{s.entries}</td>
                <td>{s.clears}</td>
                <td>{s.failures}</td>
                <td>{percent(s.conditionalPassRate)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="muted">
        Turn 중앙값 {cell.turns.median} · P90 {cell.turns.p90} · 보상 엔트로피{" "}
        {cell.rewardEntropy?.toFixed(3) ?? "관측 없음"}
      </p>
      <p className="hash">
        CONFIG {cell.configChecksum}
        <br />
        SAMPLE {cell.sampleHash}
      </p>
      {cell.examples.map((example) => (
        <details key={example.seed}>
          <summary>Seed {example.seed} · 행동 로그</summary>
          <pre>{JSON.stringify(example, null, 2)}</pre>
        </details>
      ))}
    </div>
  );
}
