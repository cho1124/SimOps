import { useEffect, useMemo, useState } from "react";
import { Api, ApiError } from "./api";
import type { Detail, ExperimentListItem, Report } from "./contracts";
import Results from "./Results";
import Analysis from "./Analysis";

export const labels: Record<string, string> = {
  draft: "초안",
  ready: "정의 확정",
  running: "실행 중",
  analyzing: "검토 대기",
  decided: "검토 완료",
  failed: "실행 실패",
  queued: "대기",
  completed: "완료",
  cancelled: "취소",
  succeeded: "저장 완료",
};
const message = (e: unknown) =>
  e instanceof ApiError
    ? `${e.status === 401 ? "운영자 키를 확인하세요. " : ""}${e.message} (${e.code}${e.correlationId ? ` · ${e.correlationId}` : ""})`
    : e instanceof Error
      ? e.message
      : "요청을 처리하지 못했습니다.";

export default function App() {
  const [inputKey, setInputKey] = useState(""),
    [key, setKey] = useState("");
  const [items, setItems] = useState<ExperimentListItem[]>([]),
    [selected, setSelected] = useState("");
  const [detail, setDetail] = useState<Detail | null>(null),
    [report, setReport] = useState<Report | null>(null);
  const [error, setError] = useState(""),
    [notice, setNotice] = useState(""),
    [busy, setBusy] = useState(false);
  const [refresh, setRefresh] = useState(0),
    [editor, setEditor] = useState<string | null>(null),
    [revision, setRevision] = useState(0);
  const api = useMemo(() => (key ? new Api(key) : null), [key]);
  useEffect(() => {
    if (!api) return;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout>,
      cachedDigest: string | null = null;
    const load = async () => {
      try {
        const list = await api.list(controller.signal);
        if (controller.signal.aborted) return;
        setItems(list);
        const id =
          selected ||
          list.find((x) => x.id === "difficulty-curve-001")?.id ||
          list[0]?.id;
        if (!id) {
          setDetail(null);
          setReport(null);
          return;
        }
        if (!selected) {
          setSelected(id);
          return;
        }
        const current = await api.detail(id, controller.signal);
        if (controller.signal.aborted) return;
        setDetail(current);
        if (current.batch?.status === "completed") {
          if (cachedDigest !== current.batch.resultDigest) {
            const result = await api.results(id, false, controller.signal);
            if (controller.signal.aborted) return;
            if (
              result.planHash !== current.planHash ||
              result.resultDigest !== current.batch.resultDigest
            )
              throw new Error("계획과 결과가 다릅니다. 새로고침해 주세요.");
            setReport(result);
            cachedDigest = result.resultDigest;
          }
        } else {
          setReport(null);
          cachedDigest = null;
        }
        setError("");
      } catch (e) {
        if (!controller.signal.aborted) setError(message(e));
      } finally {
        if (!controller.signal.aborted) timer = setTimeout(load, 3000);
      }
    };
    void load();
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, [api, selected, refresh]);
  async function command(action: () => Promise<unknown>, success: string) {
    if (busy) return;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await action();
      setNotice(success);
      setRefresh((x) => x + 1);
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }
  async function connect() {
    setBusy(true);
    setError("");
    try {
      setItems(await new Api(inputKey).list());
      setKey(inputKey);
      setInputKey("");
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }
  function choose(id: string) {
    setSelected(id);
    setDetail(null);
    setReport(null);
    setEditor(null);
    setError("");
    setNotice("");
  }
  function disconnect() {
    setKey("");
    setInputKey("");
    setItems([]);
    choose("");
  }
  const path = detail
    ? `/api/v1/experiments/${encodeURIComponent(detail.id)}`
    : "";
  async function template() {
    await command(async () => {
      const definition = await api!.template();
      setEditor(
        JSON.stringify(
          { ...definition, experimentId: `draft-${Date.now()}` },
          null,
          2,
        ),
      );
      setRevision(0);
    }, "새 실험의 가설·시드·판정 기준을 검토한 뒤 확정하세요.");
  }
  async function save() {
    await command(async () => {
      const saved = await api!.request<Detail>("/api/v1/experiments", {
        definition: JSON.parse(editor!),
        expectedRevision: revision,
      });
      choose(saved.id);
      setDetail(saved);
    }, "초안을 저장했습니다. 아직 실행되지 않았습니다.");
  }
  async function download() {
    await command(async () => {
      const full = await api!.results(detail!.id, true);
      const url = URL.createObjectURL(
        new Blob([JSON.stringify(full, null, 2)], { type: "application/json" }),
      );
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `${detail!.id}.json`;
      anchor.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    }, "전체 결과 JSON을 내보냈습니다.");
  }
  return (
    <div className="shell">
      <header>
        <a className="brand" href="#">
          S<span>SimOps</span>
        </a>
        <span className="eyebrow">SYNTHETIC PLAYER LAB</span>
        <span className="badge">LOCAL · NO PUBLICATION</span>
        {api && (
          <button className="secondary" onClick={disconnect} disabled={busy}>
            연결 해제
          </button>
        )}
      </header>
      <main>
        <div className="page-title">
          <div>
            <p className="eyebrow">EXPERIMENTS / MILESTONE 07</p>
            <h1>
              {api ? (
                "실험을 비교하고, 판단을 남기다."
              ) : (
                <>
                  운영의 판단을,
                  <br />
                  재현 가능한 실험으로.
                </>
              )}
            </h1>
            <p className="muted">
              같은 시드, 다른 설정. 합성 플레이어의 결과를 비교하고 근거를
              남깁니다.
            </p>
          </div>
          {api && (
            <div className="toolbar">
              <button
                className="secondary"
                onClick={() => setRefresh((x) => x + 1)}
                disabled={busy}
              >
                새로고침
              </button>
              <button onClick={template} disabled={busy}>
                새 실험 초안
              </button>
            </div>
          )}
        </div>
        {error && (
          <p className="notice error" role="alert">
            {error}
          </p>
        )}
        {notice && (
          <p className="notice" role="status">
            {notice}
          </p>
        )}
        {!api ? (
          <>
            <section className="connect panel">
              <div>
                <p className="eyebrow">OPERATOR ACCESS</p>
                <h2>로컬 실험실 연결</h2>
                <p>
                  운영자 키로 저장된 실험과 결과를 불러옵니다.
                  <br />
                  키는 탭 메모리에만 보관되며 새로고침하면 지워집니다.
                </p>
              </div>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  void connect();
                }}
              >
                <label htmlFor="key">운영자 키</label>
                <input
                  id="key"
                  type="password"
                  value={inputKey}
                  onChange={(e) => setInputKey(e.target.value)}
                  autoComplete="off"
                  required
                />
                <button disabled={busy || !inputKey.trim()}>
                  {busy ? "연결 중…" : "실험실 연결"}
                </button>
              </form>
            </section>
            <div className="steps">
              <article>
                <span>01 / REGISTER</span>
                <h3>판정 기준부터 고정</h3>
                <p>
                  가설·변경안·시드를 확정하면 실험 정의를 더 이상 덮어쓰지
                  않습니다.
                </p>
              </article>
              <article>
                <span>02 / SIMULATE</span>
                <h3>18개 단위로 실행</h3>
                <p>
                  3개 설정 × 6개 성향. 별도 Worker가 실행하고 행동 로그를
                  재검증합니다.
                </p>
              </article>
              <article>
                <span>03 / REVIEW</span>
                <h3>후보 없음도 결과</h3>
                <p>
                  보호 기준 위반을 숨기지 않습니다. 검토 기록과 게임 설정 배포는
                  분리합니다.
                </p>
              </article>
            </div>
          </>
        ) : (
          <div className="workspace">
            <aside className="panel sidebar">
              <h2>실험 목록 ({items.length})</h2>
              {items.map((item) => (
                <button
                  key={item.id}
                  className={`experiment-link ${selected === item.id ? "selected" : ""}`}
                  onClick={() => choose(item.id)}
                  disabled={busy}
                >
                  {item.id}
                  <small>
                    {labels[item.status]} · v{item.revision}
                  </small>
                </button>
              ))}
              {!items.length && (
                <p className="muted">저장된 실험이 없습니다.</p>
              )}
            </aside>
            <div className="stack">
              {editor !== null && (
                <section className="panel">
                  <h2>{revision ? "초안 수정" : "새 실험 초안"}</h2>
                  <p className="muted">
                    설정 3개 × 성향 6개, Cell당 최대 1,000회. 확정 후 수정 불가.
                  </p>
                  <label htmlFor="definition">실험 정의 JSON</label>
                  <textarea
                    id="definition"
                    value={editor}
                    onChange={(e) => setEditor(e.target.value)}
                  />
                  <div className="toolbar">
                    <button onClick={save} disabled={busy}>
                      초안 저장
                    </button>
                    <button
                      className="secondary"
                      onClick={() => setEditor(null)}
                      disabled={busy}
                    >
                      편집 닫기
                    </button>
                  </div>
                </section>
              )}
              {detail ? (
                <>
                  <section className="panel">
                    <div className="toolbar">
                      <span className={`status ${detail.status}`}>
                        {labels[detail.status]}
                      </span>
                      <span className="eyebrow">
                        PLAN REVISION {detail.revision}
                      </span>
                    </div>
                    <h2>{detail.id}</h2>
                    <p className="muted">{detail.definition.hypothesis}</p>
                    <p className="hash">PLAN {detail.planHash}</p>
                    <div className="toolbar">
                      {detail.status === "draft" && (
                        <>
                          <button
                            className="secondary"
                            disabled={busy}
                            onClick={() => {
                              setEditor(
                                JSON.stringify(detail.definition, null, 2),
                              );
                              setRevision(detail.revision);
                            }}
                          >
                            초안 수정
                          </button>
                          <button
                            disabled={busy}
                            onClick={() =>
                              void command(
                                () =>
                                  api.request(path + "/ready", {
                                    planHash: detail.planHash,
                                  }),
                                "실험 정의를 확정했습니다. 이후 수정은 새 실험으로 등록하세요.",
                              )
                            }
                          >
                            정의 확정 · 수정 잠금
                          </button>
                        </>
                      )}
                      {detail.status === "ready" && (
                        <button
                          disabled={busy}
                          onClick={() =>
                            void command(
                              () =>
                                api.request(path + "/batches", {
                                  planHash: detail.planHash,
                                  idempotencyKey: `dashboard-${detail.planHash}`,
                                }),
                              "실행을 접수했습니다. Worker가 비동기로 처리합니다.",
                            )
                          }
                        >
                          시뮬레이션 실행
                        </button>
                      )}
                      {detail.batch &&
                        ["queued", "running"].includes(detail.batch.status) && (
                          <button
                            className="danger"
                            disabled={busy}
                            onClick={() =>
                              void command(
                                () =>
                                  api.request(
                                    `/api/v1/simulation-batches/${detail.batch!.id}/cancel`,
                                    {},
                                  ),
                                "취소했습니다. 저장된 Cell은 보존하고 최종 지표에서는 제외합니다.",
                              )
                            }
                          >
                            실행 취소
                          </button>
                        )}
                      {report && (
                        <button
                          className="secondary"
                          disabled={busy}
                          onClick={download}
                        >
                          전체 결과 JSON
                        </button>
                      )}
                    </div>
                    <details>
                      <summary>고정된 실험 정의와 시드 보기</summary>
                      <pre>{JSON.stringify(detail.definition, null, 2)}</pre>
                    </details>
                  </section>
                  {detail.batch && (
                    <section className="panel">
                      <div className="toolbar">
                        <h3>실행 진행률</h3>
                        <span className={`status ${detail.batch.status}`}>
                          {labels[detail.batch.status]}
                        </span>
                      </div>
                      <p className="muted">
                        {detail.batch.completedCells} /{" "}
                        {detail.batch.expectedCells} Cell ·{" "}
                        {detail.batch.completedRuns.toLocaleString()} /{" "}
                        {detail.batch.expectedRuns.toLocaleString()} Run 저장
                        완료
                      </p>
                      <progress
                        aria-label="완료된 Cell"
                        value={detail.batch.completedCells}
                        max={detail.batch.expectedCells}
                      />
                      {detail.batch.completedCells ===
                        detail.batch.expectedCells &&
                        detail.batch.status === "running" && (
                          <p className="notice">
                            전수 Replay 완료. 신뢰구간과 보호 기준을 계산
                            중입니다.
                          </p>
                        )}
                      <details>
                        <summary>작업별 상태와 재시도</summary>
                        <div className="jobs">
                          {detail.batch.jobs.map((job) => (
                            <div
                              className="job"
                              key={`${job.kind}/${job.variantId}/${job.agentId}`}
                            >
                              {job.kind === "aggregate"
                                ? "통계 집계"
                                : `${job.variantId} / ${job.agentId}`}
                              <small>
                                {labels[job.status]} · 시도 {job.attempts}
                                {job.lastError && ` · ${job.lastError}`}
                              </small>
                            </div>
                          ))}
                        </div>
                      </details>
                    </section>
                  )}
                  {report && (
                    <Results
                      report={report}
                      detail={detail}
                      api={api}
                      busy={busy}
                      command={command}
                    />
                  )}
                  {report && (
                    <Analysis
                      key={`${report.experimentId}/${report.resultDigest}`}
                      api={api}
                      result={report}
                    />
                  )}
                  {!report && detail.status === "failed" && (
                    <p className="notice error">
                      실패·취소된 작업입니다. 부분 결과를 최종 성과로 표시하지
                      않습니다. 오류를 확인한 뒤 새 실험으로 재등록하세요.
                    </p>
                  )}
                </>
              ) : (
                editor === null && (
                  <section className="panel empty">
                    <h2>
                      {selected ? "실험 불러오는 중" : "첫 실험을 등록하세요"}
                    </h2>
                    <p className="muted">
                      새 초안을 만들거나 목록에서 선택하세요.
                    </p>
                  </section>
                )
              )}
            </div>
          </div>
        )}
      </main>
      <footer>
        SimOps / Synthetic Player LiveOps Lab{" "}
        <span>
          합성 결과는 실제 사용자의 재미를 증명하지 않습니다. 검토 승인은 게임
          설정 배포가 아닙니다.
        </span>
      </footer>
    </div>
  );
}
