import { afterEach, describe, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import App from "./App";
import Results from "./Results";
import { Api, percent } from "./api";
import type { Detail, Report } from "./contracts";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});
const detail: Detail = {
  id: "fixture",
  status: "analyzing",
  revision: 1,
  planHash: "plan",
  decision: null,
  definition: {
    experimentId: "fixture",
    hypothesis: "Test fixture, not production data.",
    gameVersion: "0.1.0",
    agentVersion: "1.0.0",
    runsPerCell: 10,
    firstSeed: "0",
    bootstrapReplicates: 100,
    primaryMetric: "mae",
    targetCumulativeFailureRates: [0, 0.02, 0.05, 0.1, 0.2, 0.3],
    agentIds: ["novice", "greedy"],
    variants: ["control", "uniform", "ramped"].map((id, i) => ({
      id,
      role: i ? "treatment" : "control",
      attackPercentByStage: [100, 100, 100, 100, 100, 100],
    })),
  },
  batch: {
    id: "batch",
    status: "completed",
    completedCells: 6,
    expectedCells: 6,
    completedRuns: 60,
    expectedRuns: 60,
    resultDigest: "digest",
    jobs: [],
  },
};
const report: Report = {
  experimentId: "fixture",
  planHash: "plan",
  calculatorVersion: "fixture",
  resultDigest: "digest",
  completedRuns: 60,
  replayCheckedRuns: 60,
  replayMismatchCount: 0,
  reviewCandidateIds: [],
  publicationState: "not_published",
  cells: detail.definition.variants.flatMap((v) =>
    ["novice", "greedy"].map((agentId) => ({
      variantId: v.id,
      agentId,
      agentVersion: "1.0.0",
      validRuns: 10,
      clearRate: v.id === "control" ? 1 : 0.5,
      curveTargetMae: 0.1,
      stages: [1, 2, 3, 4, 5, 6].map((stage) => ({
        stage,
        entries: 10,
        clears: 5,
        failures: 5,
        conditionalPassRate: 0.5,
        cumulativeFailureRate: 0.5,
        undefinedReason: null,
      })),
      configChecksum: "config",
      sampleHash: "sample",
      turns: { mean: 10, median: 10, p90: 12 },
      rewardEntropy: 0.8,
      examples: [],
    })),
  ),
  comparisons: ["uniform", "ramped"].map((variantId) => ({
    variantId,
    eligibleForHumanReview: false,
    noviceMaeDifference: {
      difference: -0.01,
      lower95: -0.02,
      upper95: 0.01,
      pairs: 10,
    },
    checks: [
      {
        key: "novice.adjacent_failure_jump",
        passed: false,
        observed: 0.3,
        requirement: "<= 0.15",
      },
    ],
  })),
  treatmentMaeDifference: {
    difference: 0.01,
    lower95: 0,
    upper95: 0.02,
    pairs: 10,
  },
  treatmentMaeDifferenceDirection: "ramped minus uniform",
};
function mockApi(result: Report = report) {
  return vi.fn(async (url: string, options?: RequestInit) => {
    if (url.endsWith("/publications")) return new Response("[]");
    if (url.endsWith("/seasons/active"))
      return new Response(
        JSON.stringify({
          seasonId: "season",
          name: "Fixture",
          configChecksum: "config",
        }),
      );
    if (url.endsWith("/analyses")) return new Response("[]");
    if (url.endsWith("/decision"))
      return new Response(
        JSON.stringify({
          ...detail,
          decision: JSON.parse(options!.body as string),
        }),
      );
    if (url.includes("/results")) return new Response(JSON.stringify(result));
    if (url.endsWith("/experiments/fixture"))
      return new Response(JSON.stringify(detail));
    return new Response(
      JSON.stringify([
        {
          id: "fixture",
          status: "analyzing",
          revision: 1,
          planHash: "plan",
          createdAt: "",
        },
      ]),
    );
  });
}
async function login() {
  fireEvent.change(screen.getByLabelText("운영자 키"), {
    target: { value: "test-only-key" },
  });
  fireEvent.click(screen.getByRole("button", { name: "실험실 연결" }));
}

describe("operator console", () => {
  it("UI-001 rejects unauthorized access and never persists the key", async () => {
    const storage = vi.spyOn(Storage.prototype, "setItem");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        async () =>
          new Response(
            JSON.stringify({
              code: "UNAUTHORIZED",
              message: "Invalid operator key",
            }),
            { status: 401 },
          ),
      ),
    );
    render(<App />);
    await login();
    expect((await screen.findByRole("alert")).textContent).toContain(
      "운영자 키",
    );
    expect(storage).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "연결 해제" })).toBeNull();
  });
  it("UI-002 renders persisted no-candidate results and persona selection", async () => {
    vi.stubGlobal("fetch", mockApi());
    render(<App />);
    await login();
    expect(await screen.findByText("후보 없음")).toBeTruthy();
    expect(
      (
        screen.getByRole("option", {
          name: "후보 승인 (게시 아님)",
        }) as HTMLOptionElement
      ).disabled,
    ).toBe(true);
    fireEvent.change(screen.getByLabelText("상세 성향"), {
      target: { value: "greedy" },
    });
    expect(
      screen.getByRole("heading", { name: "greedy · 실패가 발생하는 구간" }),
    ).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "연결 해제" }));
    expect(screen.queryByText("후보 없음")).toBeNull();
  });
  it("UI-003 decisions require a reason and send exact plan/result provenance", async () => {
    const fetch = mockApi();
    vi.stubGlobal("fetch", fetch);
    const command = async (action: () => Promise<unknown>) => {
      await action();
    };
    render(
      <Results
        report={report}
        detail={detail}
        api={new Api("key")}
        busy={false}
        command={command}
      />,
    );
    expect(
      (
        screen.getByRole("button", {
          name: "검토 결과 확정",
        }) as HTMLButtonElement
      ).disabled,
    ).toBe(true);
    fireEvent.change(screen.getByLabelText("판단 근거"), {
      target: { value: "Boss failure jump violates the registered guardrail." },
    });
    fireEvent.click(screen.getByRole("button", { name: "검토 결과 확정" }));
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
    const body = JSON.parse(fetch.mock.calls[0][1]!.body as string);
    expect(body).toMatchObject({
      planHash: "plan",
      resultDigest: "digest",
      conclusion: "rejected",
      selectedVariantId: null,
    });
    expect(fetch.mock.calls[0][0]).not.toContain("publish");
  });
  it("UI-004 mismatched result snapshots are not displayed", async () => {
    vi.stubGlobal("fetch", mockApi({ ...report, planHash: "different" }));
    render(<App />);
    await login();
    expect((await screen.findByRole("alert")).textContent).toContain(
      "계획과 결과",
    );
    expect(screen.queryByText("후보 없음")).toBeNull();
  });
  it("UI-005 credentials go only in headers and status errors retain their code", async () => {
    const fetch = vi.fn(
      async () =>
        new Response(
          JSON.stringify({
            code: "SIMULATION_CAPACITY",
            message: "Full",
            correlationId: "trace",
          }),
          { status: 429 },
        ),
    );
    vi.stubGlobal("fetch", fetch);
    await expect(new Api("private-key").list()).rejects.toMatchObject({
      status: 429,
      code: "SIMULATION_CAPACITY",
      message: "Full",
      correlationId: "trace",
      name: "Error",
    });
    const args = fetch.mock.calls as unknown as [string, RequestInit][];
    expect(args[0][0]).not.toContain("private-key");
    expect(args[0][1].headers).toMatchObject({
      "X-SimOps-Admin-Key": "private-key",
    });
    expect(args[0][1].redirect).toBe("error");
  });
  it("UI-006 undefined rates do not render as zero", () => {
    expect(percent(null)).toBe("관측 없음");
    expect(percent(0)).toBe("0.0%");
  });
});
