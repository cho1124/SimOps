import { afterEach, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import Analysis, { AnalysisResult } from "./Analysis";
import { Api } from "./api";
import type { AnalysisJob, Report } from "./contracts";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});
const job: AnalysisJob = {
  id: "analysis",
  status: "succeeded",
  attempts: 1,
  lastError: null,
  createdAt: "2026-08-27T00:00:00Z",
  snapshotHash: "snapshot",
  snapshot: {
    experimentId: "fixture",
    planHash: "plan",
    resultDigest: "digest",
    metrics: [
      { key: "clear", value: 0.5, unit: "ratio", definitionKey: "clear-rate" },
    ],
  },
  report: {
    provider: "offline",
    model: "rule-based-demo-not-llm",
    modelDigest: "offline-v1",
    promptVersion: "v1",
    validationVersion: "v1",
    snapshotHash: "snapshot",
    outputHash: "output",
    conclusionHash: "conclusion",
    output: {
      assessment: "no_candidates",
      observations: [{ metricKey: "clear", value: 999 }],
      hypotheses: [{ code: "policy_sensitivity", metricKeys: ["clear"] }],
      nextExperiments: [{ code: "replicate_seeds", metricKeys: ["clear"] }],
    },
  },
};
const result = {
  experimentId: "fixture",
  planHash: "plan",
  resultDigest: "digest",
} as Report;

it("AI-UI-001 labels offline and hypotheses; numbers come from trusted snapshot", () => {
  render(<AnalysisResult job={job} />);
  expect(screen.getByText("규칙 기반 데모 · LLM 아님")).toBeTruthy();
  expect(screen.getByText("미검증 가설")).toBeTruthy();
  expect(screen.getAllByText("50.0%").length).toBeGreaterThan(0);
  expect(screen.queryByText("999")).toBeNull();
  expect(screen.queryByRole("button", { name: /승인|배포/ })).toBeNull();
});
it("AI-UI-002 rejects mismatched snapshot evidence", () => {
  render(<AnalysisResult job={{ ...job, snapshotHash: "changed" }} />);
  expect(screen.getByRole("alert").textContent).toContain("일치하지");
  expect(screen.queryByText("미검증 가설")).toBeNull();
});
it("AI-UI-003 requests only async analysis and preserves idempotency on transport retry", async () => {
  let posts = 0;
  const fetcher = vi.fn(async (_url: string, options?: RequestInit) => {
    if (options?.method === "POST") {
      posts++;
      if (posts === 1) throw new Error("offline");
      return new Response(JSON.stringify({ jobId: "new" }));
    }
    return new Response("[]");
  });
  vi.stubGlobal("fetch", fetcher);
  render(<Analysis api={new Api("test")} result={result} />);
  await waitFor(() => expect(fetcher).toHaveBeenCalled());
  fireEvent.click(screen.getByRole("button", { name: "분석 요청" }));
  await waitFor(() =>
    expect(screen.getByRole("alert").textContent).toContain("offline"),
  );
  fireEvent.click(screen.getByRole("button", { name: "분석 요청" }));
  await waitFor(() => expect(posts).toBe(2));
  const requests = fetcher.mock.calls.filter(
    ([, options]) => options?.method === "POST",
  );
  expect(requests[0][1]!.body).toBe(requests[1][1]!.body);
  expect(requests.every(([url]) => url.endsWith("/analyses"))).toBe(true);
});
it("AI-UI-004 failed analyses show no invented fallback report", async () => {
  vi.stubGlobal(
    "fetch",
    vi.fn(
      async () =>
        new Response(
          JSON.stringify([
            {
              ...job,
              status: "failed",
              report: null,
              lastError: "ANALYSIS_SCHEMA_INVALID",
            },
          ]),
        ),
    ),
  );
  render(<Analysis api={new Api("test")} result={result} />);
  expect(await screen.findByText(/데모로 자동 대체하지/)).toBeTruthy();
  expect(screen.queryByText("미검증 가설")).toBeNull();
});
