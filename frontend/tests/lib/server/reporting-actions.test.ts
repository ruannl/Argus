import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("next/cache", () => ({
  revalidatePath: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  queryReport: vi.fn(),
  listSalaryCycles: vi.fn(),
  previewSalaryCycle: vi.fn(),
  confirmSalaryCycle: vi.fn(),
  removeSalaryCycle: vi.fn(),
}));

vi.mock("@/lib/server/reporting", () => ({
  loadReportForState: vi.fn(),
}));

import { revalidatePath } from "next/cache";

import { auth } from "@/auth";
import {
  confirmSalaryCycle,
  listSalaryCycles,
  previewSalaryCycle,
  removeSalaryCycle,
  type ReportResponse,
} from "@/lib/server/backend-client";
import { loadReportForState } from "@/lib/server/reporting";
import {
  confirmSalaryCycleAction,
  loadReportAction,
  loadSalaryCyclesAction,
  previewSalaryCycleAction,
  removeSalaryCycleAction,
} from "@/lib/server/reporting-actions";

function signedIn() {
  vi.mocked(auth).mockResolvedValue({
    expires: "2030-01-01T00:00:00.000Z",
    authenticated: true,
    user: { backendToken: "backend-token" },
  } as never);
}

const report: ReportResponse = {
  status: "ready",
  as_of: "2026-11-15T10:00:00Z",
  today: "2026-11-15",
  timezone: "Africa/Johannesburg",
  sequence_revision: "0",
  view: "overview",
  period_mode: "salary_cycle",
  period: null,
  comparison: null,
  focus: {
    kind: "overview",
    category_id: null,
    category_name: null,
    category_kind: null,
    category_archived: null,
    merchant_tag_id: null,
    merchant_tag_name: null,
    merchant_tag_archived: null,
    untagged: false,
    financial_account_id: null,
    financial_account_name: null,
    financial_account_archived: null,
    direction: null,
  },
  current: null,
  prior: null,
  categories: null,
  tags: null,
  trend: null,
  category_trend: null,
  coverage: null,
  focus_group: null,
  transactions: null,
  category_changes: null,
  setup: null,
  policy_labels: [],
};

describe("reporting server actions", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(revalidatePath).mockReset();
    vi.mocked(listSalaryCycles).mockReset();
    vi.mocked(previewSalaryCycle).mockReset();
    vi.mocked(confirmSalaryCycle).mockReset();
    vi.mocked(removeSalaryCycle).mockReset();
    vi.mocked(loadReportForState).mockReset();
  });

  it("requires an authenticated session before any report read", async () => {
    vi.mocked(auth).mockResolvedValue(null as never);

    await expect(
      loadReportAction({ view: "summary", mode: "salary_cycle" }),
    ).resolves.toMatchObject({
      ok: false,
      kind: "unauthorized",
    });
    expect(loadReportForState).not.toHaveBeenCalled();
  });

  it("reconstructs only allowlisted state and forwards page overrides", async () => {
    signedIn();
    vi.mocked(loadReportForState).mockResolvedValue({ ok: true, value: report });

    const result = await loadReportAction({
      view: "charts",
      mode: "custom",
      start: "2026-01-01",
      end: "2026-01-31",
      account: "11111111-1111-1111-1111-111111111111",
    });

    expect(result.ok).toBe(true);
    expect(result).toMatchObject({ value: { category_trend: null } });
    expect(loadReportForState).toHaveBeenCalledWith(
      "backend-token",
      {
        view: "charts",
        mode: "custom",
        start: "2026-01-01",
        end: "2026-01-31",
        account: "11111111-1111-1111-1111-111111111111",
      },
      {},
    );
  });

  it("rejects a broadening or malformed state instead of defaulting", async () => {
    signedIn();

    await expect(
      loadReportAction({
        view: "summary",
        mode: "calendar",
        preset: "month_to_date",
        start: "2026-01-01",
      }),
    ).resolves.toMatchObject({ ok: false, kind: "invalid_request" });
    expect(loadReportForState).not.toHaveBeenCalled();
  });

  it("maps backend report failures without leaking a body", async () => {
    signedIn();
    vi.mocked(loadReportForState).mockResolvedValue({ ok: false, kind: "rate_limited" });

    await expect(loadReportAction({ view: "summary", mode: "salary_cycle" })).resolves.toEqual({
      ok: false,
      kind: "rate_limited",
      message: "Too many requests. Wait a few minutes before retrying.",
    });
  });

  it("validates salary paging and reports a sequence-conflict result", async () => {
    signedIn();
    await expect(loadSalaryCyclesAction("not-a-date")).resolves.toMatchObject({
      ok: false,
      kind: "invalid_request",
    });
    expect(listSalaryCycles).not.toHaveBeenCalled();

    vi.mocked(listSalaryCycles).mockResolvedValue({ ok: false, kind: "report_busy" });
    await expect(loadSalaryCyclesAction("2026-01-01")).resolves.toMatchObject({
      ok: false,
      kind: "report_busy",
    });
  });

  it("previews without a write and maps receipt ineligibility", async () => {
    signedIn();
    vi.mocked(previewSalaryCycle).mockResolvedValue({ ok: false, kind: "receipt_ineligible" });

    await expect(
      previewSalaryCycleAction({
        action: "confirm",
        transaction_id: "transaction",
        expected_transaction_version: 1,
      }),
    ).resolves.toMatchObject({ ok: false, kind: "receipt_ineligible" });
    expect(previewSalaryCycle).toHaveBeenCalledWith("backend-token", {
      action: "confirm",
      transaction_id: "transaction",
      expected_transaction_version: 1,
    });
  });

  it("revalidates Overview layout after confirmed or removed dates", async () => {
    signedIn();
    vi.mocked(confirmSalaryCycle).mockResolvedValue({
      ok: true,
      value: { id: "start", start_date: "2026-01-25", revision: "2", status: "created" },
    });
    vi.mocked(removeSalaryCycle).mockResolvedValue({
      ok: true,
      value: { removed_id: "start", removed_date: "2026-01-25", revision: "3" },
    });

    await expect(
      confirmSalaryCycleAction({
        transaction_id: "transaction",
        expected_transaction_version: 1,
        expected_revision: "1",
        confirm: true,
      }),
    ).resolves.toMatchObject({ ok: true });
    expect(revalidatePath).toHaveBeenCalledWith("/home", "layout");

    await expect(
      removeSalaryCycleAction({ start_id: "start", expected_revision: "2" }),
    ).resolves.toMatchObject({ ok: true });
    expect(removeSalaryCycle).toHaveBeenCalledWith("backend-token", "start", {
      expected_revision: "2",
      confirm: true,
    });
    expect(revalidatePath).toHaveBeenCalledTimes(2);
  });

  it("rejects confirmation without explicit confirmation and expected versions", async () => {
    signedIn();

    await expect(
      confirmSalaryCycleAction({
        transaction_id: "transaction",
        expected_transaction_version: 0,
        expected_revision: "0",
        confirm: true,
      }),
    ).resolves.toMatchObject({ ok: false, kind: "invalid_request" });
    expect(confirmSalaryCycle).not.toHaveBeenCalled();
  });
});
