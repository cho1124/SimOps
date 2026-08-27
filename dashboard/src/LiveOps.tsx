import { useEffect, useRef, useState } from "react";
import { Api } from "./api";
import type { Detail } from "./contracts";

interface Season {
  seasonId: string;
  name: string;
  configChecksum: string;
}
interface Publication {
  id: string;
  kind: string;
  seasonId: string;
  previousSeasonId: string;
  configChecksum: string;
  reason: string;
  createdAt: string;
}

export default function LiveOps({
  api,
  detail,
  onFollowup,
}: {
  api: Api;
  detail: Detail | null;
  onFollowup: (seasonId: string) => void;
}) {
  const [active, setActive] = useState<Season | null>(null),
    [history, setHistory] = useState<Publication[]>([]);
  const [approver, setApprover] = useState(""),
    [name, setName] = useState(""),
    [reason, setReason] = useState("");
  const [target, setTarget] = useState(""),
    [confirmed, setConfirmed] = useState(false),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const pending = useRef<{ signature: string; key: string } | null>(null);
  useEffect(() => {
    setConfirmed(false);
  }, [
    active?.seasonId,
    detail?.id,
    detail?.decision?.selectedVariantId,
    target,
  ]);
  useEffect(() => {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout>;
    async function load() {
      try {
        const season = await api.request<Season>(
          "/api/v1/public/seasons/active",
          undefined,
          controller.signal,
        );
        const rows = await api.request<Publication[]>(
          "/api/v1/liveops/publications",
          undefined,
          controller.signal,
        );
        if (!controller.signal.aborted) {
          setActive(season);
          setHistory(rows);
        }
      } catch (e) {
        if (!controller.signal.aborted)
          setError(e instanceof Error ? e.message : "운영 상태 조회 실패");
      } finally {
        if (!controller.signal.aborted) timer = setTimeout(load, 5000);
      }
    }
    void load();
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, [api, refresh]);
  const approved = detail?.decision?.conclusion === "approved_candidate";
  const ready =
    !!active &&
    !!approver &&
    !!name.trim() &&
    !!reason.trim() &&
    confirmed &&
    !busy;
  async function execute(kind: "publish" | "rollback") {
    if (!ready || !active) return;
    const body =
      kind === "publish"
        ? {
            experimentId: detail!.id,
            planHash: detail!.planHash,
            resultDigest: detail!.decision!.resultDigest,
            variantId: detail!.decision!.selectedVariantId,
            expectedSeasonId: active.seasonId,
            name,
            reason,
          }
        : {
            targetSeasonId: target,
            expectedSeasonId: active.seasonId,
            name,
            reason,
          };
    const signature = JSON.stringify({ kind, body });
    if (pending.current?.signature !== signature)
      pending.current = {
        signature,
        key: `publication-${crypto.randomUUID()}`,
      };
    setBusy(true);
    setError("");
    try {
      await api.request(
        `/api/v1/liveops/${kind}`,
        { ...body, idempotencyKey: pending.current.key },
        undefined,
        approver,
      );
      pending.current = null;
      setConfirmed(false);
      setReason("");
      setName("");
      setRefresh((x) => x + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "운영 명령 실패");
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="panel">
      <p className="eyebrow">LIVEOPS · HUMAN CONTROL</p>
      <h2>시즌 게시와 롤백</h2>
      <p>
        {active ? `${active.name} · ${active.seasonId}` : "현재 시즌 조회 중"}
      </p>
      {active && (
        <>
          <p className="hash">CONFIG {active.configChecksum}</p>
          <button
            className="secondary"
            onClick={() => onFollowup(active.seasonId)}
            disabled={busy}
          >
            현재 게시 설정으로 후속 실험 초안
          </button>
        </>
      )}
      <p className="muted">
        게시와 롤백은 현재 시즌을 종료하고 새 시즌을 만듭니다. 기존 랭킹은
        보존되지만 이전 시즌의 미제출 Ticket은 더 이상 제출할 수 없습니다.
      </p>
      {error && (
        <p role="alert" className="notice error">
          {error}
        </p>
      )}
      <details>
        <summary>운영 변경 · 명시적 확인 필요</summary>
        {!approved && (
          <p className="notice">
            현재 선택한 실험에는 사람이 승인한 후보가 없습니다. AI 분석만으로
            게시할 수 없습니다.
          </p>
        )}
        <label htmlFor="publication-key">
          게시 승인자 키 (탭 메모리만 사용)
        </label>
        <input
          id="publication-key"
          type="password"
          autoComplete="off"
          value={approver}
          onChange={(e) => setApprover(e.target.value)}
        />
        <label htmlFor="season-name">새 시즌 이름</label>
        <input
          id="season-name"
          maxLength={80}
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        <label htmlFor="publication-reason">변경 근거</label>
        <textarea
          id="publication-reason"
          maxLength={2000}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
        <label>
          <input
            type="checkbox"
            checked={confirmed}
            onChange={(e) => setConfirmed(e.target.checked)}
          />
          현재 시즌 종료와 미제출 Ticket 무효화를 이해했습니다.
        </label>
        <button
          disabled={!ready || !approved}
          onClick={() => void execute("publish")}
        >
          승인 후보를 새 시즌으로 게시
        </button>
        <label htmlFor="rollback-target">복원할 이전 시즌 설정</label>
        <select
          id="rollback-target"
          value={target}
          onChange={(e) => setTarget(e.target.value)}
        >
          <option value="">선택하세요</option>
          {history.map((p) => (
            <option key={p.id} value={p.previousSeasonId}>
              {p.previousSeasonId}
            </option>
          ))}
        </select>
        <button
          className="danger"
          disabled={!ready || !target}
          onClick={() => void execute("rollback")}
        >
          이전 설정으로 새 시즌 생성
        </button>
      </details>
      <details>
        <summary>게시·롤백 이력 ({history.length})</summary>
        {history.length ? (
          history.map((p) => (
            <article key={p.id}>
              <strong>
                {p.kind} · {new Date(p.createdAt).toLocaleString()}
              </strong>
              <p>{p.reason}</p>
              <p className="hash">
                {p.previousSeasonId} → {p.seasonId}
              </p>
            </article>
          ))
        ) : (
          <p>게시 이력이 없습니다.</p>
        )}
      </details>
    </section>
  );
}
