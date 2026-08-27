import { afterEach, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import LiveOps from "./LiveOps";
import { Api } from "./api";
import type { Detail } from "./contracts";
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});
const detail = {
  id: "experiment",
  planHash: "plan",
  decision: {
    conclusion: "approved_candidate",
    resultDigest: "digest",
    selectedVariantId: "uniform",
  },
} as Detail;
function mock() {
  return vi.fn(
    async (url: string, options?: RequestInit) =>
      new Response(
        JSON.stringify(
          options?.method === "POST"
            ? {}
            : url.endsWith("/active")
              ? {
                  seasonId: "season",
                  name: "Fixture",
                  configChecksum: "config",
                }
              : [],
        ),
      ),
  );
}
it("LIVE-UI-001 no approval means no publication; follow-up is explicit", async () => {
  const fetch = mock();
  vi.stubGlobal("fetch", fetch);
  const followup = vi.fn();
  render(
    <LiveOps api={new Api("admin")} detail={null} onFollowup={followup} />,
  );
  await screen.findByText("Fixture · season");
  expect(
    (
      screen.getByRole("button", {
        name: "승인 후보를 새 시즌으로 게시",
        hidden: true,
      }) as HTMLButtonElement
    ).disabled,
  ).toBe(true);
  fireEvent.click(
    screen.getByRole("button", { name: "현재 게시 설정으로 후속 실험 초안" }),
  );
  expect(followup).toHaveBeenCalledWith("season");
  expect(
    fetch.mock.calls.every(([, options]) => options?.method === "GET"),
  ).toBe(true);
});
it("LIVE-UI-002 separate approver key, reason and closure confirmation required", async () => {
  const fetch = mock();
  vi.stubGlobal("fetch", fetch);
  render(
    <LiveOps api={new Api("admin")} detail={detail} onFollowup={() => {}} />,
  );
  await screen.findByText("Fixture · season");
  fireEvent.click(screen.getByText("운영 변경 · 명시적 확인 필요"));
  fireEvent.change(screen.getByLabelText("게시 승인자 키 (탭 메모리만 사용)"), {
    target: { value: "approver" },
  });
  fireEvent.change(screen.getByLabelText("새 시즌 이름"), {
    target: { value: "Next" },
  });
  fireEvent.change(screen.getByLabelText("변경 근거"), {
    target: { value: "Reason" },
  });
  const button = screen.getByRole("button", {
    name: "승인 후보를 새 시즌으로 게시",
  }) as HTMLButtonElement;
  expect(button.disabled).toBe(true);
  fireEvent.click(screen.getByRole("checkbox"));
  fireEvent.click(button);
  await waitFor(() =>
    expect(
      fetch.mock.calls.some(([, options]) => options?.method === "POST"),
    ).toBe(true),
  );
  const [url, options] = fetch.mock.calls.find(
    ([, options]) => options?.method === "POST",
  )!;
  expect(url).toContain("/liveops/publish");
  expect(url).not.toContain("approver");
  expect(
    (options!.headers as Record<string, string>)["X-SimOps-Approver-Key"],
  ).toBe("approver");
  expect(JSON.parse(options!.body as string).expectedSeasonId).toBe("season");
});

it("LIVE-UI-003 changing experiment invalidates prior closure confirmation", async () => {
  vi.stubGlobal("fetch", mock());
  const api = new Api("admin");
  const view = render(
    <LiveOps api={api} detail={detail} onFollowup={() => {}} />,
  );
  await screen.findByText("Fixture · season");
  fireEvent.click(screen.getByText("운영 변경 · 명시적 확인 필요"));
  fireEvent.click(screen.getByRole("checkbox"));
  expect((screen.getByRole("checkbox") as HTMLInputElement).checked).toBe(true);
  view.rerender(
    <LiveOps
      api={api}
      detail={{ ...detail, id: "another" }}
      onFollowup={() => {}}
    />,
  );
  expect((screen.getByRole("checkbox") as HTMLInputElement).checked).toBe(
    false,
  );
});
