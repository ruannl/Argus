import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/server/transaction-actions", () => ({
  queryTransactionsAction: vi.fn(),
  loadTransactionAction: vi.fn(),
  loadOperationAction: vi.fn(),
  saveTransactionAction: vi.fn(),
  prepareTransactionsAction: vi.fn(),
  applyOperationAction: vi.fn(),
  abandonOperationAction: vi.fn(),
}));

vi.mock("@/lib/server/account-actions", () => ({
  endSessionAction: vi.fn(),
}));

import TransactionsWorkspace from "@/app/transactions/transactions-workspace";
import {
  abandonOperationAction,
  applyOperationAction,
  loadOperationAction,
  loadTransactionAction,
  prepareTransactionsAction,
  queryTransactionsAction,
  saveTransactionAction,
} from "@/lib/server/transaction-actions";
import type {
  MerchantTag,
  TransactionDetail,
  TransactionOperation,
  TransactionPage,
} from "@/lib/server/backend-client";

const account = {
  id: "account-id",
  email: "customer@example.test",
  email_verified: true,
  subscriber_active: true,
  subscription_started_at: "2030-01-01T00:00:00Z",
  next_billing_at: null,
  lifetime: false,
  admin: false,
};

const category = {
  id: "cat-1",
  display_name: "Groceries",
  kind: "Expense" as const,
  is_system: false,
  is_default: false,
  is_archived: false,
  transaction_count: 1,
  created_at: "2024-01-01T00:00:00Z",
  updated_at: "2024-01-01T00:00:00Z",
};

const otherCategory = {
  ...category,
  id: "cat-2",
  display_name: "Travel",
};

const row = {
  id: "txn-1",
  version: 1,
  transaction_date: "2024-01-01",
  description: "Row one",
  amount: "-10.00",
  currency: "ZAR" as const,
  direction: "out" as const,
  financial_account: { id: "acc-1", display_name: "Cheque", is_archived: false },
  category: {
    id: "cat-1",
    display_name: "Groceries",
    kind: "Expense" as const,
    is_system: false,
    is_archived: false,
  },
  merchant_tags: [],
  statement_import: {
    id: "stmt-1",
    statement_label: "Statement",
    source_type: "fnb_csv",
    is_hidden: false,
  },
  is_hidden_by_user: false,
  effective_hidden: false,
  hidden_reason: "none" as const,
};

const pageData: TransactionPage = {
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
};

const prepared: TransactionOperation = {
  operation_id: "11111111-1111-1111-1111-111111111111",
  status: "Prepared",
  selection_mode: "explicit",
  captured_at: "2026-01-01T00:00:00Z",
  expires_at: "2026-01-01T00:15:00Z",
  selected_count: 1,
  changed_count: 1,
  unchanged_count: 0,
  confirmation_hash: "hash",
  applied_at: null,
  terminal_at: null,
  action: {
    category: {
      id: "cat-2",
      display_name: "Travel",
      kind: "Expense",
      is_system: false,
      is_archived: false,
    },
    add_tags: [],
    remove_tags: [],
    is_hidden_by_user: null,
    stale: false,
  },
};

const abandoned = {
  operation_id: prepared.operation_id,
  status: "Abandoned" as const,
  selected_count: 1,
  changed_count: 1,
  unchanged_count: 0,
  applied_at: null,
  replayed: false,
};

const initialQuery = {
  filters: { visibility: "visible" as const },
  page: 1,
  pageSize: 25,
  sortBy: "date" as const,
  sortDirection: "desc" as const,
};

const merchantTag: MerchantTag = {
  id: "tag-1",
  display_name: "Coffee",
  is_archived: false,
  transaction_count: 1,
  created_at: "2024-01-01T00:00:00Z",
  updated_at: "2024-01-01T00:00:00Z",
};

const secondTag: MerchantTag = {
  ...merchantTag,
  id: "tag-2",
  display_name: "Travel",
};

function detailFor(source: TransactionPage["transactions"][number]): TransactionDetail {
  return {
    transaction: source,
    suggested_tags: [],
    has_more_suggestions: false,
    source_page: null,
    source_row_number: 1,
  };
}

type WorkspaceOverrides = {
  initialOperationId?: string | null;
  initialPage?: TransactionPage | null;
  initialError?: string | null;
  inventoryFailed?: boolean;
  tags?: MerchantTag[];
};

function workspaceElement(overrides: WorkspaceOverrides = {}) {
  return (
    <TransactionsWorkspace
      account={account}
      accounts={[
        {
          id: "acc-1",
          display_name: "Cheque",
          institution: null,
          currency: "ZAR",
          is_archived: false,
          transaction_count: 1,
          created_at: "2024-01-01T00:00:00Z",
          updated_at: "2024-01-01T00:00:00Z",
        },
      ]}
      categories={[category, otherCategory]}
      initialError={overrides.initialError ?? null}
      initialOperationId={overrides.initialOperationId ?? null}
      initialPage={overrides.initialPage ?? pageData}
      initialQuery={initialQuery}
      inventoryFailed={overrides.inventoryFailed ?? false}
      tags={overrides.tags ?? []}
    />
  );
}

function renderWorkspace(overrides: WorkspaceOverrides = {}) {
  return render(workspaceElement(overrides));
}

describe("transactions workspace", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/transactions");
    vi.spyOn(globalThis.crypto, "randomUUID").mockReturnValue(prepared.operation_id as never);
    vi.mocked(queryTransactionsAction).mockReset();
    vi.mocked(loadTransactionAction).mockReset();
    vi.mocked(loadOperationAction).mockReset();
    vi.mocked(saveTransactionAction).mockReset();
    vi.mocked(prepareTransactionsAction).mockReset();
    vi.mocked(applyOperationAction).mockReset();
    vi.mocked(abandonOperationAction).mockReset();
    vi.mocked(queryTransactionsAction).mockResolvedValue({ ok: true, value: pageData });
  });

  afterEach(() => vi.restoreAllMocks());

  it("renders truthful counts, exact amounts and hidden-count copy", () => {
    renderWorkspace();

    expect(screen.getByText(/1 visible and 0 hidden match these filters/)).toBeInTheDocument();
    expect(screen.getByText("Row one")).toBeInTheDocument();
    expect(screen.getByText("-10.00 ZAR")).toBeInTheDocument();
    expect(screen.queryByText("No transactions yet")).not.toBeInTheDocument();
  });

  it("preserves explicit selection across page navigation", async () => {
    const user = userEvent.setup();
    const firstOfTwo = {
      ...pageData,
      inventory_count: 50,
      matching_visible_count: 50,
      matching_total: 50,
      filtered_total: 50,
    };
    renderWorkspace({ initialPage: firstOfTwo });

    await user.click(
      screen.getByRole("checkbox", {
        name: /Select transaction Row one on 2024-01-01 for -10.00 ZAR/,
      }),
    );
    expect(screen.getByText(/1 transaction selected\./)).toBeInTheDocument();

    vi.mocked(queryTransactionsAction).mockResolvedValue({
      ok: true,
      value: { ...pageData, transactions: [], page: 2, filtered_total: 1 },
    });
    await user.click(screen.getByRole("button", { name: "Next" }));

    await waitFor(() =>
      expect(screen.getByText(/1 transaction selected\./)).toBeInTheDocument(),
    );
  });

  it("prepares and applies an explicit classification change with exact scope", async () => {
    const user = userEvent.setup();
    vi.mocked(prepareTransactionsAction).mockResolvedValue({
      ok: true,
      value: { operation: prepared, created: true },
    });
    vi.mocked(applyOperationAction).mockResolvedValue({
      ok: true,
      value: {
        operation_id: prepared.operation_id,
        status: "Applied",
        selected_count: 1,
        changed_count: 1,
        unchanged_count: 0,
        applied_at: "2026-01-01T00:01:00Z",
        replayed: false,
      },
    });
    renderWorkspace();

    await user.click(
      screen.getByRole("checkbox", {
        name: /Select transaction Row one on 2024-01-01 for -10.00 ZAR/,
      }),
    );
    await user.selectOptions(screen.getByLabelText("Set category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Review change" }));

    expect(await screen.findByRole("heading", { name: "Confirm this change" })).toBeInTheDocument();
    expect(screen.getByText(/1 will change and 0 already match/)).toBeInTheDocument();
    expect(prepareTransactionsAction).toHaveBeenCalledWith({
      operation_id: expect.any(String),
      selection: {
        mode: "explicit",
        transactions: [{ id: "txn-1", expected_version: 1 }],
      },
      changes: { category_id: "cat-2" },
    });

    await user.click(screen.getByRole("button", { name: "Apply change" }));
    expect(await screen.findByRole("heading", { name: "Applied receipt" })).toBeInTheDocument();
    expect(applyOperationAction).toHaveBeenCalledWith(prepared.operation_id, "hash", 1);
    expect(screen.getByText(/1 changed, 0 already matched, 1 in the saved scope/)).toBeInTheDocument();
  });

  it("retains a late preparation for safe abandonment and releases busy state", async () => {
    const user = userEvent.setup();
    let resolvePrepare: (value: Awaited<ReturnType<typeof prepareTransactionsAction>>) => void;
    const deferred = new Promise<Awaited<ReturnType<typeof prepareTransactionsAction>>>(
      (resolve) => {
        resolvePrepare = resolve;
      },
    );
    vi.mocked(prepareTransactionsAction).mockReturnValue(deferred);
    vi.mocked(abandonOperationAction).mockResolvedValue({ ok: true, value: abandoned });
    renderWorkspace();

    await user.click(
      screen.getByRole("checkbox", {
        name: /Select transaction Row one on 2024-01-01 for -10.00 ZAR/,
      }),
    );
    await user.selectOptions(screen.getByLabelText("Set category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Review change" }));

    await user.click(screen.getByRole("button", { name: "Hidden" }));
    await act(async () => {
      resolvePrepare!({ ok: true, value: { operation: prepared, created: true } });
    });

    expect(await screen.findByRole("heading", { name: "Recovered confirmation" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Confirm this change" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Abandon and prepare again" })).toBeEnabled();
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(prepared.operation_id);
    await user.click(screen.getByRole("button", { name: "Abandon and prepare again" }));
    expect(abandonOperationAction).toHaveBeenCalledWith(prepared.operation_id);
    await screen.findByRole("heading", { name: "Operation abandoned" });
    await user.click(screen.getByRole("button", { name: "Acknowledge outcome" }));
    expect(new URL(window.location.href).searchParams.has("operation_id")).toBe(false);
    expect(screen.getByRole("button", { name: "Select all matching filters" })).toBeEnabled();
  });

  it("retains the original operation id and input when preparation has an uncertain outcome", async () => {
    const user = userEvent.setup();
    let resolvePrepare!: (value: Awaited<ReturnType<typeof prepareTransactionsAction>>) => void;
    vi.mocked(prepareTransactionsAction).mockReturnValueOnce(
      new Promise((resolve) => { resolvePrepare = resolve; }),
    );
    renderWorkspace();

    await user.click(screen.getByRole("checkbox", { name: /Select transaction Row one/ }));
    await user.selectOptions(screen.getByLabelText("Set category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Review change" }));
    const originalInput = vi.mocked(prepareTransactionsAction).mock.calls[0][0];
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(originalInput.operation_id);

    await user.click(screen.getByRole("button", { name: "Hidden" }));
    await act(async () => {
      resolvePrepare({ ok: false, kind: "unavailable", message: "Unavailable" });
    });
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(originalInput.operation_id);
    vi.mocked(prepareTransactionsAction).mockResolvedValueOnce({
      ok: true,
      value: { operation: prepared, created: false },
    });
    await user.click(await screen.findByRole("button", { name: "Retry same preparation" }));
    expect(prepareTransactionsAction).toHaveBeenLastCalledWith(originalInput);
    expect(await screen.findByRole("heading", { name: "Recovered confirmation" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Apply change" })).not.toBeInTheDocument();
  });

  it("does not claim cancellation when abandon returns an applied receipt", async () => {
    const user = userEvent.setup();
    vi.mocked(prepareTransactionsAction).mockResolvedValue({
      ok: true,
      value: { operation: prepared, created: true },
    });
    vi.mocked(abandonOperationAction).mockResolvedValue({
      ok: true,
      value: { ...abandoned, status: "Applied", applied_at: "2026-01-01T00:01:00Z" },
    });
    renderWorkspace();
    await user.click(screen.getByRole("checkbox", { name: /Select transaction Row one/ }));
    await user.selectOptions(screen.getByLabelText("Set category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Review change" }));
    await screen.findByRole("heading", { name: "Confirm this change" });
    await user.click(screen.getByRole("button", { name: "Cancel and abandon" }));

    expect(await screen.findByRole("heading", { name: "Applied receipt" })).toBeInTheDocument();
    expect(screen.queryByText(/Confirmation cancelled\. No transaction data changed/)).not.toBeInTheDocument();
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(prepared.operation_id);
    await user.click(screen.getByRole("button", { name: "Dismiss receipt" }));
    expect(new URL(window.location.href).searchParams.has("operation_id")).toBe(false);
  });

  it("keeps the recovery id when reload status is unavailable", async () => {
    window.history.replaceState(null, "", `/transactions?operation_id=${prepared.operation_id}`);
    vi.mocked(loadOperationAction).mockResolvedValue({
      ok: false, kind: "unavailable", message: "Status is unavailable.",
    });
    renderWorkspace({ initialOperationId: prepared.operation_id });

    expect(await screen.findByText("Status is unavailable.")).toBeInTheDocument();
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(prepared.operation_id);
    expect(screen.getByRole("button", { name: "Check operation status" })).toBeInTheDocument();
  });

  it("preserves recovery across an in-flight apply, view change, lost response and reload", async () => {
    const user = userEvent.setup();
    let resolveApply!: (value: Awaited<ReturnType<typeof applyOperationAction>>) => void;
    vi.mocked(prepareTransactionsAction).mockResolvedValue({
      ok: true, value: { operation: prepared, created: true },
    });
    vi.mocked(applyOperationAction).mockReturnValue(new Promise((resolve) => { resolveApply = resolve; }));
    vi.mocked(loadOperationAction).mockResolvedValue({
      ok: false, kind: "unavailable", message: "Status is unavailable.",
    });
    const view = renderWorkspace();
    await user.click(screen.getByRole("checkbox", { name: /Select transaction Row one/ }));
    await user.selectOptions(screen.getByLabelText("Set category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Review change" }));
    await screen.findByRole("heading", { name: "Confirm this change" });
    await user.click(screen.getByRole("button", { name: "Apply change" }));
    await user.click(screen.getByRole("button", { name: "Hidden" }));
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(prepared.operation_id);
    expect(new URL(window.location.href).searchParams.get("visibility")).toBe("hidden");

    await act(async () => {
      resolveApply({ ok: false, kind: "unavailable", message: "Apply unavailable." });
    });
    expect(await screen.findByText(/apply outcome is uncertain/i)).toBeInTheDocument();
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(prepared.operation_id);
    expect(new URL(window.location.href).searchParams.get("visibility")).toBe("hidden");
    expect(vi.mocked(queryTransactionsAction).mock.calls.at(-1)?.[0].filters?.visibility).toBe("hidden");

    view.unmount();
    renderWorkspace({ initialOperationId: prepared.operation_id });
    expect(await screen.findByText("Status is unavailable.")).toBeInTheDocument();
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(prepared.operation_id);
  });

  it("keeps the current filters when an older apply succeeds late", async () => {
    const user = userEvent.setup();
    let resolveApply!: (value: Awaited<ReturnType<typeof applyOperationAction>>) => void;
    vi.mocked(prepareTransactionsAction).mockResolvedValue({
      ok: true, value: { operation: prepared, created: true },
    });
    vi.mocked(applyOperationAction).mockReturnValue(new Promise((resolve) => { resolveApply = resolve; }));
    renderWorkspace();
    await user.click(screen.getByRole("checkbox", { name: /Select transaction Row one/ }));
    await user.selectOptions(screen.getByLabelText("Set category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Review change" }));
    await screen.findByRole("heading", { name: "Confirm this change" });
    await user.click(screen.getByRole("button", { name: "Apply change" }));
    await user.click(screen.getByRole("button", { name: "Hidden" }));

    await act(async () => {
      resolveApply({
        ok: true,
        value: { ...abandoned, status: "Applied", applied_at: "2026-01-01T00:01:00Z" },
      });
    });
    expect(await screen.findByRole("heading", { name: "Applied receipt" })).toBeInTheDocument();
    expect(new URL(window.location.href).searchParams.get("visibility")).toBe("hidden");
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(prepared.operation_id);
    expect(vi.mocked(queryTransactionsAction).mock.calls.at(-1)?.[0].filters?.visibility).toBe("hidden");
  });

  it("does not treat a missing recovery status as proof of cancellation", async () => {
    window.history.replaceState(null, "", `/transactions?operation_id=${prepared.operation_id}`);
    vi.mocked(loadOperationAction).mockResolvedValue({
      ok: false, kind: "not_found", message: "Not found.",
    });
    renderWorkspace({ initialOperationId: prepared.operation_id });

    expect(await screen.findByText(/not found yet.*unresolved/i)).toBeInTheDocument();
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(prepared.operation_id);
    expect(abandonOperationAction).not.toHaveBeenCalled();
  });

  it("reconciles the all-matching banner when capture changes the count", async () => {
    const user = userEvent.setup();
    vi.mocked(prepareTransactionsAction).mockResolvedValue({
      ok: true,
      value: {
        operation: { ...prepared, selection_mode: "all_matching", selected_count: 3, changed_count: 2, unchanged_count: 1 },
        created: true,
      },
    });
    renderWorkspace();
    await user.click(screen.getByRole("button", { name: "Select all matching filters" }));
    await user.selectOptions(screen.getByLabelText("Set category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Review change" }));

    expect(await screen.findByText(/3 transactions captured for this confirmation/)).toBeInTheDocument();
    expect(screen.getByText(/earlier list showed 1.*saved scope now contains 3/i)).toBeInTheDocument();
    expect(screen.getByText(/Captured filters: visibility: visible/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Apply change" })).toBeInTheDocument();
  });

  it("does not claim cancellation or discard recovery when abandon is unavailable", async () => {
    const user = userEvent.setup();
    vi.mocked(prepareTransactionsAction).mockResolvedValue({
      ok: true, value: { operation: prepared, created: true },
    });
    vi.mocked(abandonOperationAction).mockResolvedValue({
      ok: false, kind: "unavailable", message: "Abandon unavailable.",
    });
    renderWorkspace();
    await user.click(screen.getByRole("checkbox", { name: /Select transaction Row one/ }));
    await user.selectOptions(screen.getByLabelText("Set category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Review change" }));
    await screen.findByRole("heading", { name: "Confirm this change" });
    await user.click(screen.getByRole("button", { name: "Cancel and abandon" }));

    expect(await screen.findByText("Abandon unavailable.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Confirm this change" })).toBeInTheDocument();
    expect(screen.queryByText(/Confirmation cancelled\. No transaction data changed/)).not.toBeInTheDocument();
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(prepared.operation_id);
  });

  it.each(["Abandoned", "Expired"] as const)(
    "retains a confirmed %s outcome until acknowledgement",
    async (status) => {
      const user = userEvent.setup();
      vi.mocked(prepareTransactionsAction).mockResolvedValue({
        ok: true, value: { operation: prepared, created: true },
      });
      vi.mocked(abandonOperationAction).mockResolvedValue({
        ok: true, value: { ...abandoned, status },
      });
      renderWorkspace();
      await user.click(screen.getByRole("checkbox", { name: /Select transaction Row one/ }));
      await user.selectOptions(screen.getByLabelText("Set category"), "cat-2");
      await user.click(screen.getByRole("button", { name: "Review change" }));
      await screen.findByRole("heading", { name: "Confirm this change" });
      await user.click(screen.getByRole("button", { name: "Cancel and abandon" }));

      expect(await screen.findByRole("heading", { name: `Operation ${status.toLowerCase()}` })).toBeInTheDocument();
      expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(prepared.operation_id);
      await user.click(screen.getByRole("button", { name: "Acknowledge outcome" }));
      expect(new URL(window.location.href).searchParams.has("operation_id")).toBe(false);
    },
  );

  it("recovers a prepared operation without applying it automatically", async () => {
    const recovered = { ...prepared, status: "Prepared" as const };
    vi.mocked(loadOperationAction).mockResolvedValue({ ok: true, value: recovered });
    renderWorkspace({ initialOperationId: recovered.operation_id });

    expect(
      await screen.findByRole("heading", { name: "Recovered confirmation" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Abandon and prepare again" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Apply change" })).not.toBeInTheDocument();
    expect(applyOperationAction).not.toHaveBeenCalled();
  });

  it("saves an inline category change with the displayed version", async () => {
    const user = userEvent.setup();
    vi.mocked(saveTransactionAction).mockResolvedValue({
      ok: true,
      value: {
        transaction: { ...row, version: 2, category: { ...row.category, id: "cat-2" } },
        selected_count: 1,
        changed_count: 1,
        unchanged_count: 0,
      },
    });
    renderWorkspace();

    await user.click(screen.getByRole("checkbox", { name: /Select transaction Row one/ }));
    expect(screen.getByText(/1 transaction selected\./)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Edit inline" }));
    await user.selectOptions(
      screen.getByLabelText("Inline category for Row one"),
      "cat-2",
    );
    await user.click(screen.getByRole("button", { name: "Save inline changes" }));

    expect(saveTransactionAction).toHaveBeenCalledWith({
      id: "txn-1",
      expected_version: 1,
      changes: { category_id: "cat-2" },
    });
    expect(await screen.findByText("Inline change saved.")).toBeInTheDocument();
    expect(queryTransactionsAction).toHaveBeenCalled();
    expect(await screen.findByText(/0 transactions selected\./)).toBeInTheDocument();
  });

  it("binds an inline draft to its opening version and keeps chosen options visible", async () => {
    const user = userEvent.setup();
    vi.mocked(queryTransactionsAction).mockResolvedValue({
      ok: true,
      value: { ...pageData, transactions: [{ ...row, version: 2 }] },
    });
    renderWorkspace({ tags: [merchantTag, secondTag] });

    await user.click(screen.getByRole("button", { name: "Edit inline" }));
    const addSelect = screen.getByLabelText("Add tags inline for Row one");
    await user.selectOptions(addSelect, [merchantTag.id, secondTag.id]);

    const coffeeOption = within(addSelect).getByRole("option", {
      name: merchantTag.display_name,
    }) as HTMLOptionElement;
    expect(coffeeOption.selected).toBe(true);
    expect(coffeeOption.isConnected).toBe(true);

    await user.selectOptions(
      screen.getByLabelText("Inline category for Row one"),
      "cat-2",
    );
    await user.click(screen.getByRole("button", { name: "Hidden" }));
    await screen.findByText(/version 2/);

    expect(screen.getByRole("button", { name: "Save inline changes" })).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent(/changed elsewhere while you were editing/i);
    expect(saveTransactionAction).not.toHaveBeenCalled();
  });

  it("preserves a drawer category when the stale inline draft is reopened", async () => {
    const user = userEvent.setup();
    const rowTwo = {
      ...row,
      version: 2,
      category: { ...row.category, id: "cat-2", display_name: "Travel" },
    };
    vi.mocked(loadTransactionAction)
      .mockResolvedValueOnce({ ok: true, value: detailFor(row) })
      .mockResolvedValue({ ok: true, value: detailFor(rowTwo) });
    vi.mocked(saveTransactionAction)
      .mockResolvedValueOnce({
        ok: true,
        value: {
          transaction: rowTwo,
          selected_count: 1,
          changed_count: 1,
          unchanged_count: 0,
        },
      })
      .mockResolvedValueOnce({
        ok: true,
        value: {
          transaction: { ...rowTwo, version: 3 },
          selected_count: 1,
          changed_count: 1,
          unchanged_count: 0,
        },
      });
    vi.mocked(queryTransactionsAction).mockResolvedValue({
      ok: true,
      value: { ...pageData, transactions: [rowTwo] },
    });
    renderWorkspace({ tags: [merchantTag] });

    await user.click(screen.getByRole("button", { name: "Edit inline" }));
    await user.click(screen.getByRole("button", { name: /Row one/ }));
    const dialog = await screen.findByRole("dialog");
    await user.selectOptions(within(dialog).getByLabelText("Category"), "cat-2");
    await user.click(within(dialog).getByRole("button", { name: "Save classification" }));

    await waitFor(() =>
      expect(saveTransactionAction).toHaveBeenCalledWith({
        id: "txn-1",
        expected_version: 1,
        changes: { category_id: "cat-2" },
        confirm: undefined,
      }),
    );
    await screen.findAllByText(/version 2/);

    // The still-mounted inline draft is bound to version 1 and must not send
    // its old category with the refreshed row.
    expect(screen.getByRole("alert")).toHaveTextContent(/changed elsewhere while you were editing/i);
    const callsAfterDrawerSave = vi.mocked(saveTransactionAction).mock.calls.length;

    await user.click(within(dialog).getByRole("button", { name: "Close detail" }));
    await user.click(screen.getByRole("button", { name: "Close inline editor" }));
    await user.click(screen.getByRole("button", { name: "Edit inline" }));

    const reopenedCategory = screen.getByLabelText("Inline category for Row one") as HTMLSelectElement;
    expect(reopenedCategory.value).toBe("cat-2");
    await user.selectOptions(screen.getByLabelText("Add tags inline for Row one"), merchantTag.id);
    await user.click(screen.getByRole("button", { name: "Save inline changes" }));

    await waitFor(() =>
      expect(vi.mocked(saveTransactionAction).mock.calls.at(-1)?.[0]).toMatchObject({
        id: "txn-1",
        expected_version: 2,
        changes: { add_tag_ids: [merchantTag.id] },
      }),
    );
    expect(vi.mocked(saveTransactionAction).mock.calls.length).toBe(callsAfterDrawerSave + 1);
  });

  it("retires a stale inline draft and refreshes the row", async () => {
    const user = userEvent.setup();
    vi.mocked(saveTransactionAction).mockResolvedValue({
      ok: false,
      kind: "version_conflict",
      message: "This transaction changed since you loaded it.",
    });
    vi.mocked(queryTransactionsAction).mockResolvedValue({
      ok: true,
      value: { ...pageData, transactions: [{ ...row, version: 2 }] },
    });
    renderWorkspace({ tags: [merchantTag] });

    await user.click(screen.getByRole("button", { name: "Edit inline" }));
    await user.selectOptions(screen.getByLabelText("Inline category for Row one"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Save inline changes" }));

    expect(
      await screen.findByText(/changed while you were editing.*reopen it after the list refreshes/i),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText("Inline category for Row one")).not.toBeInTheDocument();
    await waitFor(() => expect(queryTransactionsAction).toHaveBeenCalled());
    expect(saveTransactionAction).toHaveBeenCalledTimes(1);
  });

  it("keeps sequential inline tag selections and saves the deliberate set", async () => {
    const user = userEvent.setup();
    vi.mocked(saveTransactionAction).mockResolvedValue({
      ok: true,
      value: {
        transaction: { ...row, version: 2 },
        selected_count: 1,
        changed_count: 1,
        unchanged_count: 0,
      },
    });
    renderWorkspace({ tags: [merchantTag, secondTag] });

    await user.click(screen.getByRole("button", { name: "Edit inline" }));
    const addSelect = screen.getByLabelText("Add tags inline for Row one");
    await user.selectOptions(addSelect, [merchantTag.id, secondTag.id]);
    await user.deselectOptions(addSelect, merchantTag.id);
    await user.click(screen.getByRole("button", { name: "Save inline changes" }));

    expect(saveTransactionAction).toHaveBeenCalledWith({
      id: "txn-1",
      expected_version: 1,
      changes: { add_tag_ids: [secondTag.id] },
    });
  });

  it("ignores a stale detail response after another row is opened", async () => {
    const user = userEvent.setup();
    const rowTwo = { ...row, id: "txn-2", description: "Row two" };
    let resolveFirst!: (value: Awaited<ReturnType<typeof loadTransactionAction>>) => void;
    vi.mocked(loadTransactionAction)
      .mockReturnValueOnce(
        new Promise((resolve) => {
          resolveFirst = resolve;
        }),
      )
      .mockResolvedValueOnce({ ok: true, value: detailFor(rowTwo) })
      .mockResolvedValue({ ok: true, value: detailFor(rowTwo) });
    vi.mocked(saveTransactionAction).mockResolvedValue({
      ok: true,
      value: {
        transaction: { ...rowTwo, version: 2, category: { ...rowTwo.category, id: "cat-2" } },
        selected_count: 1,
        changed_count: 1,
        unchanged_count: 0,
      },
    });
    renderWorkspace({
      initialPage: { ...pageData, transactions: [row, rowTwo] },
    });

    await user.click(screen.getByRole("button", { name: /Row one/ }));
    await user.click(screen.getByRole("button", { name: "Close transaction detail" }));
    await user.click(screen.getByRole("button", { name: /Row two/ }));

    const dialog = await screen.findByRole("dialog");
    expect(
      await within(dialog).findByRole("heading", { name: "Row two" }),
    ).toBeInTheDocument();

    await act(async () => {
      resolveFirst({ ok: true, value: detailFor(row) });
    });

    expect(
      within(screen.getByRole("dialog")).getByRole("heading", { name: "Row two" }),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole("dialog")).queryByRole("heading", { name: "Row one" }),
    ).not.toBeInTheDocument();

    await user.selectOptions(within(dialog).getByLabelText("Category"), "cat-2");
    await user.click(within(dialog).getByRole("button", { name: "Save classification" }));
    await waitFor(() =>
      expect(saveTransactionAction).toHaveBeenCalledWith({
        id: "txn-2",
        expected_version: 1,
        changes: { category_id: "cat-2" },
        confirm: undefined,
      }),
    );
  });

  it("keeps keyboard focus inside the detail drawer", async () => {
    const user = userEvent.setup();
    vi.mocked(loadTransactionAction).mockResolvedValue({
      ok: true,
      value: detailFor(row),
    });
    renderWorkspace();

    await user.click(screen.getByRole("button", { name: /Row one/ }));
    const dialog = await screen.findByRole("dialog");
    await waitFor(() =>
      expect(within(dialog).getByRole("button", { name: "Close detail" })).toHaveFocus(),
    );

    const focusable = Array.from(
      dialog.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
      ),
    );
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    expect(first).toBe(within(dialog).getByRole("button", { name: "Close detail" }));

    last.focus();
    await user.tab();
    expect(first).toHaveFocus();

    await user.tab({ shift: true });
    expect(last).toHaveFocus();
  });

  it("focuses the loading drawer and closes it with Escape", async () => {
    const user = userEvent.setup();
    vi.mocked(loadTransactionAction).mockReturnValue(new Promise(() => {}));
    renderWorkspace();

    const trigger = screen.getByRole("button", { name: /Row one/ });
    await user.click(trigger);
    const dialog = await screen.findByRole("dialog");
    await waitFor(() => expect(dialog).toHaveFocus());

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it("closes an error drawer with Escape and returns focus", async () => {
    const user = userEvent.setup();
    vi.mocked(loadTransactionAction).mockResolvedValue({
      ok: false,
      kind: "unavailable",
      message: "Detail unavailable.",
    });
    renderWorkspace();

    const trigger = screen.getByRole("button", { name: /Row one/ });
    await user.click(trigger);
    expect(await screen.findByText("Detail unavailable.")).toBeInTheDocument();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it("disables classification entry points when inventory is incomplete", () => {
    renderWorkspace({ inventoryFailed: true });

    expect(screen.getByRole("button", { name: "Edit inline" })).toBeDisabled();
    expect(screen.getByLabelText("Set category")).toBeDisabled();
  });

  it("clears an executable confirmation on filter change while retaining recovery", async () => {
    const user = userEvent.setup();
    vi.mocked(prepareTransactionsAction).mockResolvedValue({
      ok: true,
      value: { operation: prepared, created: true },
    });
    vi.mocked(abandonOperationAction).mockResolvedValue({ ok: true, value: abandoned });
    renderWorkspace();

    await user.click(
      screen.getByRole("checkbox", {
        name: /Select transaction Row one on 2024-01-01 for -10.00 ZAR/,
      }),
    );
    await user.selectOptions(screen.getByLabelText("Set category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Review change" }));
    await screen.findByRole("heading", { name: "Confirm this change" });

    await user.click(screen.getByRole("button", { name: "Hidden" }));

    expect(await screen.findByText(/0 transactions selected\./)).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Recovered confirmation" })).toBeInTheDocument();
    expect(abandonOperationAction).not.toHaveBeenCalled();
    expect(
      screen.queryByRole("heading", { name: "Confirm this change" }),
    ).not.toBeInTheDocument();
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(prepared.operation_id);
    await user.click(screen.getByRole("button", { name: "Abandon and prepare again" }));
    expect(abandonOperationAction).toHaveBeenCalledWith(prepared.operation_id);
  });

  it("does not claim a completed list refresh when the inline reload fails", async () => {
    const user = userEvent.setup();
    vi.mocked(saveTransactionAction).mockResolvedValue({
      ok: false,
      kind: "version_conflict",
      message: "This transaction changed since you loaded it.",
    });
    vi.mocked(queryTransactionsAction).mockResolvedValue({
      ok: false,
      kind: "unavailable",
      message: "Query unavailable.",
    });
    renderWorkspace({ tags: [merchantTag] });

    await user.click(screen.getByRole("button", { name: "Edit inline" }));
    await user.selectOptions(screen.getByLabelText("Inline category for Row one"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Save inline changes" }));

    expect(
      await screen.findByText(/changed while you were editing.*reopen it after the list refreshes/i),
    ).toBeInTheDocument();
    expect(await screen.findByText("Query unavailable.")).toBeInTheDocument();
    expect(screen.queryByText(/has been refreshed/i)).not.toBeInTheDocument();
  });

  it("disables an already-open inline editor when inventory becomes incomplete", async () => {
    const user = userEvent.setup();
    const view = renderWorkspace({ tags: [merchantTag] });
    await user.click(screen.getByRole("button", { name: "Edit inline" }));

    view.rerender(workspaceElement({ inventoryFailed: true, tags: [merchantTag] }));

    expect(
      screen.getByText(/did not load, so this editor is disabled/i),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/Inline category for/)).toBeDisabled();
    expect(screen.getByRole("button", { name: "Save inline changes" })).toBeDisabled();
  });

  it("applies a late inline save refresh to the current view", async () => {
    const user = userEvent.setup();
    let resolveSave!: (value: Awaited<ReturnType<typeof saveTransactionAction>>) => void;
    vi.mocked(saveTransactionAction).mockReturnValue(
      new Promise((resolve) => {
        resolveSave = resolve;
      }),
    );
    vi.mocked(queryTransactionsAction).mockResolvedValue({
      ok: true,
      value: {
        ...pageData,
        transactions: [],
        matching_visible_count: 0,
        matching_hidden_count: 0,
        matching_total: 0,
        filtered_total: 0,
      },
    });
    renderWorkspace();

    await user.click(screen.getByRole("button", { name: "Edit inline" }));
    await user.selectOptions(screen.getByLabelText("Inline category for Row one"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Save inline changes" }));
    await user.click(screen.getByRole("button", { name: "Hidden" }));

    await waitFor(() =>
      expect(
        vi.mocked(queryTransactionsAction).mock.calls.at(-1)?.[0].filters?.visibility,
      ).toBe("hidden"),
    );

    await act(async () => {
      resolveSave({
        ok: true,
        value: {
          transaction: { ...row, version: 2, category: { ...row.category, id: "cat-2" } },
          selected_count: 1,
          changed_count: 1,
          unchanged_count: 0,
        },
      });
    });

    await waitFor(() =>
      expect(
        vi.mocked(queryTransactionsAction).mock.calls.at(-1)?.[0].filters?.visibility,
      ).toBe("hidden"),
    );
  });

  it("applies a late drawer save refresh to the current view", async () => {
    const user = userEvent.setup();
    let resolveSave!: (value: Awaited<ReturnType<typeof saveTransactionAction>>) => void;
    vi.mocked(saveTransactionAction).mockReturnValue(
      new Promise((resolve) => {
        resolveSave = resolve;
      }),
    );
    vi.mocked(loadTransactionAction).mockResolvedValue({
      ok: true,
      value: detailFor(row),
    });
    vi.mocked(queryTransactionsAction).mockResolvedValue({
      ok: true,
      value: {
        ...pageData,
        transactions: [],
        matching_visible_count: 0,
        matching_hidden_count: 0,
        matching_total: 0,
        filtered_total: 0,
      },
    });
    renderWorkspace();

    await user.click(screen.getByRole("button", { name: /Row one/ }));
    const dialog = await screen.findByRole("dialog");
    await user.selectOptions(within(dialog).getByLabelText("Category"), "cat-2");
    await user.click(within(dialog).getByRole("button", { name: "Save classification" }));
    await user.click(within(dialog).getByRole("button", { name: "Close detail" }));
    await user.click(screen.getByRole("button", { name: "Hidden" }));

    await waitFor(() =>
      expect(
        vi.mocked(queryTransactionsAction).mock.calls.at(-1)?.[0].filters?.visibility,
      ).toBe("hidden"),
    );

    await act(async () => {
      resolveSave({
        ok: true,
        value: {
          transaction: { ...row, version: 2, category: { ...row.category, id: "cat-2" } },
          selected_count: 1,
          changed_count: 1,
          unchanged_count: 0,
        },
      });
    });

    await waitFor(() =>
      expect(
        vi.mocked(queryTransactionsAction).mock.calls.at(-1)?.[0].filters?.visibility,
      ).toBe("hidden"),
    );
  });

  it("does not let a late status result replace a newer operation's recovery", async () => {
    const user = userEvent.setup();
    const operationA = prepared;
    const operationB = {
      ...prepared,
      operation_id: "22222222-2222-4222-8222-222222222222",
    };
    let resolveStatusA!: (value: Awaited<ReturnType<typeof loadOperationAction>>) => void;
    vi.mocked(loadOperationAction)
      .mockResolvedValueOnce({ ok: true, value: operationA })
      .mockReturnValueOnce(
        new Promise((resolve) => {
          resolveStatusA = resolve;
        }),
      )
      .mockResolvedValueOnce({
        ok: false,
        kind: "unavailable",
        message: "Status unavailable.",
      })
      .mockResolvedValue({
        ok: false,
        kind: "unavailable",
        message: "Status unavailable.",
      });
    vi.mocked(abandonOperationAction)
      .mockResolvedValueOnce({ ok: true, value: abandoned })
      .mockResolvedValueOnce({
        ok: true,
        value: {
          operation_id: operationB.operation_id,
          status: "Applied",
          selected_count: 1,
          changed_count: 1,
          unchanged_count: 0,
          applied_at: "2026-01-01T00:01:00Z",
          replayed: false,
        },
      });
    vi.mocked(prepareTransactionsAction).mockResolvedValue({
      ok: true,
      value: { operation: operationB, created: true },
    });
    vi.mocked(applyOperationAction).mockResolvedValue({
      ok: false,
      kind: "unavailable",
      message: "Apply unavailable.",
    });
    vi.spyOn(globalThis.crypto, "randomUUID").mockReturnValue(operationB.operation_id as never);

    renderWorkspace({ initialOperationId: operationA.operation_id });
    await screen.findByRole("heading", { name: "Recovered confirmation" });

    await user.click(screen.getByRole("button", { name: "Check operation status" }));
    await user.click(screen.getByRole("button", { name: "Abandon and prepare again" }));
    await screen.findByRole("heading", { name: "Operation abandoned" });
    await user.click(screen.getByRole("button", { name: "Acknowledge outcome" }));
    expect(new URL(window.location.href).searchParams.has("operation_id")).toBe(false);

    await user.click(screen.getByRole("checkbox", { name: /Select transaction Row one/ }));
    await user.selectOptions(screen.getByLabelText("Set category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Review change" }));
    await screen.findByRole("heading", { name: "Confirm this change" });
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(
      operationB.operation_id,
    );

    await user.click(screen.getByRole("button", { name: "Apply change" }));
    expect(await screen.findByText(/apply outcome is uncertain/i)).toBeInTheDocument();
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(
      operationB.operation_id,
    );

    await act(async () => {
      resolveStatusA({ ok: true, value: operationA });
    });

    expect(screen.queryByRole("heading", { name: "Recovered confirmation" })).not.toBeInTheDocument();
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(
      operationB.operation_id,
    );

    await user.click(screen.getByRole("button", { name: "Cancel and abandon" }));
    expect(await screen.findByRole("heading", { name: "Applied receipt" })).toBeInTheDocument();
    expect(new URL(window.location.href).searchParams.get("operation_id")).toBe(
      operationB.operation_id,
    );
    await user.click(screen.getByRole("button", { name: "Dismiss receipt" }));
    expect(new URL(window.location.href).searchParams.has("operation_id")).toBe(false);
  });
});
