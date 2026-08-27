import { useEffect, useRef, useState } from "react";
import { Api, percent, points } from "./api";
import type {
  AnalysisJob,
  AnalysisMetric,
  Interpretation,
  Report,
} from "./contracts";

const explanations: Record<string, string> = {
  failure_concentration:
    "난이도가 특정 구간에 집중되었을 가능성. 인접 구간 실패율 급증과 연결된 가설이며, 인과관계가 입증된 것은 아닙니다.",
  policy_sensitivity:
    "행동 정책에 따라 같은 설정의 영향이 달라질 가능성. 실제 인간의 선호나 유지율로 일반화하지 않습니다.",
  redistribute_pressure:
    "전체 난이도 총량을 고정하고 구간별 배분만 바꾼 새 실험을 사전 등록해 비교하세요. 현재 결과만으로 총량과 곡선 형태의 효과를 분리할 수 없습니다.",
  replicate_seeds:
    "새 시드 집합에서도 성향별 결과 차이가 유지되는지 반복 검증하세요. 결과를 본 뒤 기존 판정 기준을 바꾸지 않습니다.",
};
const definitions: Record<string, string> = {
  "valid-run-count": "실험 계산기에 포함된 유효 Run 수.",
  "replay-mismatch-count":
    "저장된 행동의 재실행 결과와 원래 결과 해시가 다른 Run 수.",
  "review-candidate-count":
    "사전 등록된 모든 기준을 통과한 설정 수. 사람의 승인 건수와 다릅니다.",
  "clear-rate": "승리 Run 수 / 해당 설정·성향의 전체 유효 Run 수.",
  "curve-target-mae":
    "각 구간의 누적 실패율과 사전 목표의 절대 차이 평균. delta는 Treatment − Control입니다.",
  "conditional-stage-pass-rate":
    "해당 구간 통과 Run 수 / 해당 구간 진입 Run 수. 진입이 없으면 관측 없음.",
  "cumulative-failure-rate":
    "전체 시작 집단 중 해당 구간까지 통과하지 못한 비율. 이전 구간 사망을 포함합니다.",
  "paired-bootstrap-ci":
    "같은 시드 쌍을 재표집한 MAE 차이의 백분위 신뢰구간 경계. 다중 비교 보정 없음.",
  "guardrail-observation":
    "사전 등록 기준의 관측값. 기준별 단위·임계값은 위 실험 결과의 보호 기준 표에서 확인합니다.",
};
const formatMetric = (m: AnalysisMetric) =>
  m.value == null
    ? "관측 없음"
    : m.unit === "ratio"
      ? percent(m.value)
      : m.unit === "ratio_delta"
        ? points(m.value)
        : m.value.toLocaleString(undefined, { maximumFractionDigits: 6 });

export function AnalysisResult({ job }: { job: AnalysisJob }) {
  if (!job.report) return null;
  const report = job.report;
  if (report.snapshotHash !== job.snapshotHash)
    return <p role="alert">분석 근거가 일치하지 않습니다.</p>;
  const metrics = new Map(job.snapshot.metrics.map((m) => [m.key, m]));
  function evidence(keys: string[]) {
    return (
      <ul className="analysis-evidence">
        {keys.map((key) => {
          const metric = metrics.get(key);
          return (
            <li key={key}>
              <div>
                <code>{key}</code>
                {metric && (
                  <small className="muted">
                    {definitions[metric.definitionKey]}
                  </small>
                )}
              </div>
              <strong>{metric ? formatMetric(metric) : "근거 없음"}</strong>
            </li>
          );
        })}
      </ul>
    );
  }
  function interpretations(items: Interpretation[]) {
    return items.map((item) => (
      <article key={item.code}>
        <p>{explanations[item.code] || "지원하지 않는 해석입니다."}</p>
        {evidence(item.metricKeys)}
      </article>
    ));
  }
  return (
    <div className="analysis-report">
      <p className="eyebrow">
        {report.provider === "offline"
          ? "규칙 기반 데모 · LLM 아님"
          : `로컬 AI · ${report.model}`}
      </p>
      <h3>
        {report.output.assessment === "no_candidates"
          ? "현재 기준을 모두 통과한 검토 후보가 없습니다."
          : "검토 후보가 있습니다. 최종 판단은 사람에게 남깁니다."}
      </h3>
      <h4>선택한 관측 지표</h4>
      {evidence(report.output.observations.map((x) => x.metricKey))}
      <h4>미검증 가설</h4>
      {interpretations(report.output.hypotheses)}
      <h4>다음 실험 제안 · 자동 실행하지 않음</h4>
      {interpretations(report.output.nextExperiments)}
      <details>
        <summary>분석 출처와 고정된 근거 확인</summary>
        <p className="hash">
          SNAPSHOT {report.snapshotHash}
          <br />
          MODEL {report.model} / {report.modelDigest}
          <br />
          PROMPT {report.promptVersion}
          <br />
          VALIDATOR {report.validationVersion}
          <br />
          CONCLUSION {report.conclusionHash}
        </p>
        <p className="muted">
          문장은 허용된 해석 코드의 고정 설명입니다. AI는 근거와 가설을 선택하며
          자유 서술이나 새로운 수치를 작성하지 않습니다.
        </p>
        <div className="table-wrap">
          <table>
            <caption>입력 지표 사전 · 단위와 정의</caption>
            <thead>
              <tr>
                <th>지표</th>
                <th>값</th>
                <th>단위</th>
                <th>정의 키</th>
              </tr>
            </thead>
            <tbody>
              {job.snapshot.metrics.map((m) => (
                <tr key={m.key}>
                  <th>
                    <code>{m.key}</code>
                  </th>
                  <td>{formatMetric(m)}</td>
                  <td>{m.unit}</td>
                  <td>
                    <a href={`#definition-${m.definitionKey}`}>
                      {m.definitionKey}
                    </a>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <dl>
          {Object.entries(definitions).map(([key, value]) => (
            <div key={key} id={`definition-${key}`}>
              <dt>
                <code>{key}</code>
              </dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      </details>
    </div>
  );
}

export default function Analysis({
  api,
  result,
}: {
  api: Api;
  result: Report;
}) {
  const [jobs, setJobs] = useState<AnalysisJob[]>([]);
  const [selected, setSelected] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [refresh, setRefresh] = useState(0);
  // Retain a request key on transport failure so retrying does not create duplicate paid/compute work.
  const pendingKey = useRef<string | null>(null);
  const path = `/api/v1/experiments/${encodeURIComponent(result.experimentId)}/analyses`;
  useEffect(() => {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout>;
    async function load() {
      try {
        const current = await api.request<AnalysisJob[]>(
          path,
          undefined,
          controller.signal,
        );
        if (controller.signal.aborted) return;
        if (
          current.some(
            (j) =>
              j.snapshot.planHash !== result.planHash ||
              j.snapshot.resultDigest !== result.resultDigest,
          )
        )
          throw new Error("분석과 실험 결과가 일치하지 않습니다.");
        setJobs(current);
        setError("");
      } catch (e) {
        if (!controller.signal.aborted)
          setError(
            e instanceof Error ? e.message : "분석을 불러오지 못했습니다.",
          );
      } finally {
        if (!controller.signal.aborted) timer = setTimeout(load, 3000);
      }
    }
    void load();
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, [api, path, result.planHash, result.resultDigest, refresh]);
  async function start() {
    if (busy) return;
    setBusy(true);
    setError("");
    pendingKey.current ??= `analysis-${crypto.randomUUID()}`;
    try {
      const receipt = await api.request<{ jobId: string }>(path, {
        planHash: result.planHash,
        resultDigest: result.resultDigest,
        idempotencyKey: pendingKey.current,
      });
      pendingKey.current = null;
      setSelected(receipt.jobId);
      setRefresh((x) => x + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "분석 요청 실패");
    } finally {
      setBusy(false);
    }
  }
  const job = jobs.find((j) => j.id === selected) || jobs[0];
  const active = jobs.some((j) => ["queued", "running"].includes(j.status));
  return (
    <section className="panel analysis">
      <div className="toolbar">
        <h2>근거 기반 AI 분석</h2>
        <button
          onClick={() => void start()}
          disabled={busy || active || jobs.length >= 10}
        >
          {busy || active
            ? "분석 처리 중…"
            : jobs.length
              ? "같은 근거로 다시 분석"
              : "분석 요청"}
        </button>
      </div>
      <p className="muted">
        Worker에서 비동기 실행합니다. 분석 실패·재시도는 실험 결과, 사람의 판정,
        시즌에 영향을 주지 않습니다. 모델 미설정 시 규칙 기반 데모로 표시합니다.
      </p>
      {error && (
        <p role="alert" className="notice error">
          {error}
        </p>
      )}
      {jobs.length > 0 && (
        <>
          <label htmlFor="analysis-history">분석 이력 ({jobs.length}/10)</label>
          <select
            id="analysis-history"
            value={job?.id}
            onChange={(e) => setSelected(e.target.value)}
          >
            {jobs.map((j) => (
              <option key={j.id} value={j.id}>
                {new Date(j.createdAt).toLocaleTimeString()} · {j.status} ·{" "}
                {j.report?.model || "처리 대기"}
              </option>
            ))}
          </select>
        </>
      )}
      {job && (
        <p className="muted" role="status">
          상태: {job.status} · 시도 {job.attempts}
          {job.lastError && ` · ${job.lastError}`}
        </p>
      )}
      {job?.status === "failed" && (
        <p className="notice error">
          분석을 저장하지 못했습니다. 근거 없는 결과를 표시하거나 데모로 자동
          대체하지 않습니다.
        </p>
      )}
      {job?.report && <AnalysisResult job={job} />}
    </section>
  );
}
