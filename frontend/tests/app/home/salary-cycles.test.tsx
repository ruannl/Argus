import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/server/transaction-actions", () => ({
  queryTransactionsAction: vi.fn(),
}));

vi.mock("@/lib/server/reporting-actions", () => ({
  loadSalaryCyclesAction: vi.fn(),
  previewSalaryCycleAction: vi.fn(),
  confirmSalaryCycleAction: vi.fn(),
  removeSalaryCycleAction: vi.fn(),
}));

import { testRouter } from "../../../vitest.setup";

import SalaryCycleWorkspace from "@/app/home/salary-cycles";
import {
  confirmSalaryCycleAction,
  loadSalaryCyclesAction,
  previewSalaryCycleAction,
  removeSalaryCycleAction,
} from "@/lib/server/reporting-actions";
import { queryTransactionsAction } from "@/lib/server/transaction-actions";
import type { SalaryCyclePreview, TransactionPage } from "@/lib/server/backend-client";

const startA = {
  id: "11111111-1111-1111-1111-111111111111",
  start_date: "2026-01-25",
  created_at: "2026-01-25T10:00:00Z",
};

const startB = {
  id: "22222222-2222-2222-2222-222222222222",
  start_date: "2026-02-24",
  created_at: "2026-02-24T10:00:00Z",
};

const row = {
  id: "33333333-3333-3333-3333-333333333333",
  version: 4,
  transaction_date: "2026-03-25",
  description: "Employer payment",
  amount: "10000.00",
  currency: "ZAR" as const,
  direction: "in" as const,
  financial_account: { id: "account", display_name: "Cheque", is_archived: false },
  category: {
    id: "category",
    display_name: "Salary",
    kind: "Income" as const,
    is_system: false,
    is_archived: false,
  },
  category_confirmed: true,
  merchant_tags: [],
  statement_import: {
    id: "statement",
    statement_label: "March",
    source_type: "fnb_csv",
    is_hidden: false,
  },
  is_hidden_by_user: false,
  effective_hidden: false,
  hidden_reason: "none" as const,
};

const page: TransactionPage = {
  transactions: [row],
  inventory_count: 1,
  matching_visible_count: 1,
  matching_hidden_count: 0,
  matching_total: 1,
  filtered_total: 1,
  page: 1,
  page_size: 25,
  sort_by: "date",
  sort_direction: "desc",
  money_totals: {
    money_in: "10000.00",
    money_out: "0.00",
    net: "10000.00",
    included_count: 1,
    transfers: { count: 0, money_in: "0.00", money_out: "0.00", absolute: "0.00" },
    excluded: { count: 0, money_in: "0.00", money_out: "0.00", absolute: "0.00" },
  },
};

const confirmPreview: SalaryCyclePreview = {
  action: "confirm",
  revision: "1",
  date: "2026-03-25",
  source: {
    id: row.id,
    version: 4,
    transaction_date: "2026-03-25",
    description: "Employer payment",
    amount: "10000.00",
    currency: "ZAR",
    financial_account: row.financial_account,
    category: row.category,
    category_confirmed: true,
  },
  proposed_interval: { start_date: "2026-03-25", end_date: "2026-11-15", label: "New cycle" },
  affected_intervals: [{ start_date: "2026-03-25", end_date: "2026-11-15", label: "New cycle" }],
  warnings: ["Adding a date changes the selected cycle and every later comparison."],
  retention_disclosure: true,
  already_confirmed: false,
};

const removePreview: SalaryCyclePreview = {
  action: "remove",
  revision: "1",
  date: "2026-01-25",
  source: null,
  proposed_interval: null,
  affected_intervals: [
    { start_date: "2026-01-25", end_date: "2026-02-23", label: "Cycle being removed" },
  ],
  warnings: ["Removing this date changes the affected cycle and every later comparison."],
  retention_disclosure: true,
  already_confirmed: false,
};

function renderWorkspace(overrides: Partial<Parameters<typeof SalaryCycleWorkspace>[0]> = {}) {
  return render(
    <SalaryCycleWorkspace
      accounts={[
        {
          id: "account",
          display_name: "Cheque",
          institution: null,
          currency: "ZAR",
          is_archived: false,
          transaction_count: 1,
          created_at: "2026-01-01T00:00:00Z",
          updated_at: "2026-01-01T00:00:00Z",
        },
      ]}
      initialRevision="1"
      initialStarts={[startA]}
      nextBeforeDate={null}
      today="2026-11-15"
      {...overrides}
    />,
  );
}

describe("salary cycle management", () => {
  beforeEach(() => {
    vi.mocked(queryTransactionsAction).mockReset();
    vi.mocked(loadSalaryCyclesAction).mockReset();
    vi.mocked(previewSalaryCycleAction).mockReset();
    vi.mocked(confirmSalaryCycleAction).mockReset();
    vi.mocked(removeSalaryCycleAction).mockReset();
  });

  it("reads an eligible receipt through the private body-only search", async () => {
    const user = userEvent.setup();
    vi.mocked(queryTransactionsAction).mockResolvedValue({ ok: true, value: page });
    renderWorkspace();

    await user.selectOptions(screen.getByLabelText("Account"), "account");
    await user.type(screen.getByLabelText("Private description search"), "  employer  ");
    await user.click(screen.getByRole("button", { name: "Find receipts" }));

    await waitFor(() =>
      expect(queryTransactionsAction).toHaveBeenCalledWith({
        filters: {
          visibility: "visible",
          direction: "in",
          end_date: "2026-11-15",
          financial_account_id: "account",
          search: "employer",
        },
        page: 1,
        page_size: 25,
        sort_by: "date",
        sort_direction: "desc",
      }),
    );
    expect(screen.getByText(/Employer payment/)).toBeInTheDocument();
  });

  it("previews a new date with retention and impact warnings before confirmation", async () => {
    const user = userEvent.setup();
    vi.mocked(queryTransactionsAction).mockResolvedValue({ ok: true, value: page });
    vi.mocked(previewSalaryCycleAction).mockResolvedValue({ ok: true, value: confirmPreview });
    renderWorkspace();

    await user.click(screen.getByRole("button", { name: "Find receipts" }));
    await user.click(await screen.findByRole("button", { name: "Use as salary start" }));

    expect(previewSalaryCycleAction).toHaveBeenCalledWith({
      action: "confirm",
      transaction_id: row.id,
      expected_transaction_version: 4,
    });
    expect(screen.getByText("Confirm this reporting date")).toBeInTheDocument();
    expect(screen.getByText(/Retention policy/)).toBeInTheDocument();
    expect(screen.getByText(/Adding a date changes/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirm start date" })).toBeInTheDocument();
  });

  it("reports an already-confirmed same-date receipt with no confirm action", async () => {
    const user = userEvent.setup();
    vi.mocked(queryTransactionsAction).mockResolvedValue({ ok: true, value: page });
    vi.mocked(previewSalaryCycleAction).mockResolvedValue({
      ok: true,
      value: { ...confirmPreview, already_confirmed: true, affected_intervals: [] },
    });
    renderWorkspace();

    await user.click(screen.getByRole("button", { name: "Find receipts" }));
    await user.click(await screen.findByRole("button", { name: "Use as salary start" }));

    expect(
      screen.getByText(
        /This date is already confirmed\. Selecting another receipt on the same date/,
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Confirm start date" })).not.toBeInTheDocument();
  });

  it("confirms a start, refreshes the sequence and the Overview report", async () => {
    const user = userEvent.setup();
    vi.mocked(queryTransactionsAction).mockResolvedValue({ ok: true, value: page });
    vi.mocked(previewSalaryCycleAction).mockResolvedValue({ ok: true, value: confirmPreview });
    vi.mocked(confirmSalaryCycleAction).mockResolvedValue({
      ok: true,
      value: { id: startB.id, start_date: "2026-03-25", revision: "2", status: "created" },
    });
    vi.mocked(loadSalaryCyclesAction).mockResolvedValue({
      ok: true,
      value: { revision: "2", starts: [startB, startA], next_before_date: null },
    });
    renderWorkspace();

    await user.click(screen.getByRole("button", { name: "Find receipts" }));
    await user.click(await screen.findByRole("button", { name: "Use as salary start" }));
    await user.click(screen.getByRole("button", { name: "Confirm start date" }));

    await waitFor(() =>
      expect(confirmSalaryCycleAction).toHaveBeenCalledWith({
        transaction_id: row.id,
        expected_transaction_version: 4,
        expected_revision: "1",
        confirm: true,
      }),
    );
    await waitFor(() => expect(loadSalaryCyclesAction).toHaveBeenCalledWith(null));
    expect(testRouter.refresh).toHaveBeenCalled();
    expect(screen.getByText(/Confirmed 2026-03-25/)).toBeInTheDocument();
  });

  it("discards accumulated pages when a page revision differs from the displayed sequence", async () => {
    const user = userEvent.setup();
    vi.mocked(loadSalaryCyclesAction)
      .mockResolvedValueOnce({
        ok: true,
        value: { revision: "2", starts: [startB], next_before_date: null },
      })
      .mockResolvedValueOnce({
        ok: true,
        value: { revision: "2", starts: [startB], next_before_date: null },
      });
    renderWorkspace({ initialStarts: [startA], nextBeforeDate: "2026-01-01" });

    await user.click(screen.getByRole("button", { name: "More confirmed dates" }));

    await waitFor(() =>
      expect(
        screen.getByText(/The salary-cycle sequence changed while paging/),
      ).toBeInTheDocument(),
    );
    expect(screen.getByText("2026-02-24 · latest open cycle")).toBeInTheDocument();
    expect(screen.queryByText(/2026-01-25/)).not.toBeInTheDocument();
    expect(loadSalaryCyclesAction).toHaveBeenCalledTimes(2);
  });

  it("requires an impact preview before removing a boundary", async () => {
    const user = userEvent.setup();
    vi.mocked(previewSalaryCycleAction).mockResolvedValue({ ok: true, value: removePreview });
    vi.mocked(removeSalaryCycleAction).mockResolvedValue({
      ok: true,
      value: { removed_id: startA.id, removed_date: "2026-01-25", revision: "2" },
    });
    vi.mocked(loadSalaryCyclesAction).mockResolvedValue({
      ok: true,
      value: { revision: "2", starts: [], next_before_date: null },
    });
    renderWorkspace();

    await user.click(screen.getByRole("button", { name: "Remove date" }));
    expect(screen.getByRole("heading", { name: "Confirm removal" })).toBeInTheDocument();
    expect(screen.getByText(/Removing this date changes/)).toBeInTheDocument();
    expect(removeSalaryCycleAction).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Confirm removal" }));

    await waitFor(() =>
      expect(removeSalaryCycleAction).toHaveBeenCalledWith({
        start_id: startA.id,
        expected_revision: "1",
      }),
    );
    expect(screen.getByText(/No salary-cycle start is confirmed/)).toBeInTheDocument();
    expect(testRouter.refresh).toHaveBeenCalled();
  });

  it("does not automatically repeat removal after an uncertain result and retires the preview", async () => {
    const user = userEvent.setup();
    vi.mocked(previewSalaryCycleAction).mockResolvedValue({ ok: true, value: removePreview });
    vi.mocked(removeSalaryCycleAction).mockResolvedValue({
      ok: false,
      kind: "unavailable",
      message: "The report could not be loaded. Retry the view.",
    });
    vi.mocked(loadSalaryCyclesAction).mockResolvedValue({
      ok: true,
      value: { revision: "1", starts: [startA], next_before_date: null },
    });
    renderWorkspace();

    await user.click(screen.getByRole("button", { name: "Remove date" }));
    await user.click(screen.getByRole("button", { name: "Confirm removal" }));

    await waitFor(() => expect(removeSalaryCycleAction).toHaveBeenCalledTimes(1));
    expect(screen.getByText(/The report could not be loaded/)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Confirm removal" })).not.toBeInTheDocument();
    expect(loadSalaryCyclesAction).toHaveBeenCalledWith(null);
  });

  it("never rebinds a stale removal preview to a re-added boundary with a newer revision", async () => {
    const user = userEvent.setup();
    const replacement = {
      id: "99999999-9999-9999-9999-999999999999",
      start_date: "2026-01-25",
      created_at: "2026-02-01T10:00:00Z",
    };
    vi.mocked(previewSalaryCycleAction)
      .mockResolvedValueOnce({ ok: true, value: removePreview })
      .mockResolvedValueOnce({
        ok: true,
        value: { ...removePreview, revision: "3", date: "2026-01-25" },
      });
    vi.mocked(removeSalaryCycleAction)
      .mockResolvedValueOnce({
        ok: false,
        kind: "cycle_sequence_changed",
        message: "The salary-cycle sequence changed since you previewed it.",
      })
      .mockResolvedValueOnce({
        ok: true,
        value: { removed_id: replacement.id, removed_date: "2026-01-25", revision: "4" },
      });
    vi.mocked(loadSalaryCyclesAction)
      .mockResolvedValueOnce({
        ok: true,
        value: { revision: "3", starts: [replacement], next_before_date: null },
      })
      .mockResolvedValueOnce({
        ok: true,
        value: { revision: "4", starts: [], next_before_date: null },
      });
    renderWorkspace();

    await user.click(screen.getByRole("button", { name: "Remove date" }));
    await user.click(screen.getByRole("button", { name: "Confirm removal" }));

    await waitFor(() => expect(removeSalaryCycleAction).toHaveBeenCalledTimes(1));
    expect(removeSalaryCycleAction).toHaveBeenNthCalledWith(1, {
      start_id: startA.id,
      expected_revision: "1",
    });
    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Confirm removal" })).not.toBeInTheDocument(),
    );
    expect(screen.getByText("2026-01-25 · latest open cycle")).toBeInTheDocument();

    // A fresh explicit preview is required before the replacement can be removed.
    await user.click(screen.getByRole("button", { name: "Remove date" }));
    expect(previewSalaryCycleAction).toHaveBeenNthCalledWith(2, {
      action: "remove",
      start_id: replacement.id,
    });
    await user.click(screen.getByRole("button", { name: "Confirm removal" }));

    await waitFor(() => expect(removeSalaryCycleAction).toHaveBeenCalledTimes(2));
    expect(removeSalaryCycleAction).toHaveBeenNthCalledWith(2, {
      start_id: replacement.id,
      expected_revision: "3",
    });
  });
});
