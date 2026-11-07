import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/server/transaction-actions", () => ({
  saveTransactionAction: vi.fn(),
  loadTransactionAction: vi.fn(),
}));

import TransactionDetailView from "@/app/transactions/transaction-detail";
import {
  loadTransactionAction,
  saveTransactionAction,
} from "@/lib/server/transaction-actions";
import type { Category, MerchantTag, TransactionDetail } from "@/lib/server/backend-client";

const categories: Category[] = [
  {
    id: "cat-1",
    display_name: "Groceries",
    kind: "Expense",
    is_system: false,
    is_default: false,
    is_archived: false,
    transaction_count: 1,
    created_at: "2024-01-01T00:00:00Z",
    updated_at: "2024-01-01T00:00:00Z",
  },
  {
    id: "cat-2",
    display_name: "Travel",
    kind: "Expense",
    is_system: false,
    is_default: false,
    is_archived: false,
    transaction_count: 0,
    created_at: "2024-01-01T00:00:00Z",
    updated_at: "2024-01-01T00:00:00Z",
  },
];

const tags: MerchantTag[] = [
  {
    id: "tag-suggested",
    display_name: "Suggested Cafe",
    is_archived: false,
    transaction_count: 0,
    created_at: "2024-01-01T00:00:00Z",
    updated_at: "2024-01-01T00:00:00Z",
  },
  {
    id: "tag-assigned",
    display_name: "Assigned",
    is_archived: false,
    transaction_count: 1,
    created_at: "2024-01-01T00:00:00Z",
    updated_at: "2024-01-01T00:00:00Z",
  },
];

function makeDetail(overrides: Partial<TransactionDetail["transaction"]> = {}): TransactionDetail {
  return {
    transaction: {
      id: "txn-1",
      version: 3,
      transaction_date: "2024-01-01",
      description: "Imported row",
      amount: "-12.34",
      currency: "ZAR",
      direction: "out",
      financial_account: { id: "acc-1", display_name: "Cheque", is_archived: false },
      category: {
        id: "cat-1",
        display_name: "Groceries",
        kind: "Expense",
        is_system: false,
        is_archived: false,
      },
      merchant_tags: [
        { id: "tag-assigned", display_name: "Assigned", is_archived: false },
      ],
      statement_import: {
        id: "stmt-1",
        statement_label: "Statement",
        source_type: "fnb_csv",
        is_hidden: false,
      },
      is_hidden_by_user: false,
      effective_hidden: false,
      hidden_reason: "none",
      ...overrides,
    },
    suggested_tags: [
      { id: "tag-suggested", display_name: "Suggested Cafe", is_archived: false },
    ],
    has_more_suggestions: false,
    source_page: 2,
    source_row_number: 7,
  };
}

const savedRow = {
  ...makeDetail().transaction,
  version: 4,
};

describe("transaction detail", () => {
  beforeEach(() => {
    vi.mocked(saveTransactionAction).mockReset();
    vi.mocked(loadTransactionAction).mockReset();
    vi.mocked(loadTransactionAction).mockResolvedValue({ ok: true, value: makeDetail() });
  });

  it("keeps imported source read-only and requires explicit suggestion acceptance", async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    vi.mocked(saveTransactionAction).mockResolvedValue({
      ok: true,
      value: {
        transaction: savedRow,
        selected_count: 1,
        changed_count: 1,
        unchanged_count: 0,
      },
    });
    render(
      <TransactionDetailView
        categories={categories}
        initialDetail={makeDetail()}
        onSaved={onSaved}
        tags={tags}
      />,
    );

    expect(screen.getByText("Imported row")).toBeInTheDocument();
    expect(screen.getByText("-12.34 ZAR")).toBeInTheDocument();
    expect(screen.getByText(/Row 7 · page 2/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Description")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Suggested: Suggested Cafe — add/ }));
    expect(saveTransactionAction).toHaveBeenCalledWith({
      id: "txn-1",
      expected_version: 3,
      changes: { add_tag_ids: ["tag-suggested"] },
      confirm: undefined,
    });
    expect(onSaved).toHaveBeenCalled();
  });

  it("sends the displayed version for a category change", async () => {
    const user = userEvent.setup();
    vi.mocked(saveTransactionAction).mockResolvedValue({
      ok: true,
      value: {
        transaction: { ...savedRow, category: { ...savedRow.category, id: "cat-2" } },
        selected_count: 1,
        changed_count: 1,
        unchanged_count: 0,
      },
    });
    render(
      <TransactionDetailView categories={categories} initialDetail={makeDetail()} tags={tags} />,
    );

    await user.selectOptions(screen.getByLabelText("Category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Save classification" }));

    expect(saveTransactionAction).toHaveBeenCalledWith({
      id: "txn-1",
      expected_version: 3,
      changes: { category_id: "cat-2" },
      confirm: undefined,
    });
  });

  it("disables classification when inventory loading failed", () => {
    render(
      <TransactionDetailView
        categories={categories}
        classificationDisabled
        initialDetail={makeDetail()}
        tags={tags}
      />,
    );

    expect(screen.getByText(/Categories or tags did not load/i)).toBeInTheDocument();
    expect(screen.getByLabelText("Category")).toBeDisabled();
    expect(
      screen.getByRole("button", { name: /Suggested: Suggested Cafe — add/ }),
    ).toBeDisabled();
    expect(screen.getByRole("button", { name: "Save classification" })).toBeDisabled();
  });

  it("keeps a pending tag addition visible and removable before saving", async () => {
    const user = userEvent.setup();
    render(
      <TransactionDetailView categories={categories} initialDetail={makeDetail()} tags={tags} />,
    );

    await user.click(screen.getByRole("button", { name: "Add Suggested Cafe" }));
    expect(
      screen.getByRole("button", { name: "Remove pending tag Suggested Cafe" }),
    ).toBeInTheDocument();

    await user.click(
      screen.getByRole("button", { name: "Remove pending tag Suggested Cafe" }),
    );
    expect(
      screen.queryByRole("button", { name: "Remove pending tag Suggested Cafe" }),
    ).not.toBeInTheDocument();
    expect(saveTransactionAction).not.toHaveBeenCalled();
  });

  it("does not claim a refresh when a conflict cannot reload the row", async () => {
    const user = userEvent.setup();
    vi.mocked(saveTransactionAction).mockResolvedValue({
      ok: false,
      kind: "version_conflict",
      message: "This transaction changed since you loaded it.",
    });
    vi.mocked(loadTransactionAction).mockResolvedValue({
      ok: false,
      kind: "unavailable",
      message: "Unavailable.",
    });
    render(
      <TransactionDetailView categories={categories} initialDetail={makeDetail()} tags={tags} />,
    );

    await user.selectOptions(screen.getByLabelText("Category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Save classification" }));

    expect(
      await screen.findByText(/^The latest values could not be reloaded\./),
    ).toBeInTheDocument();
    expect(screen.queryByText(/latest values have been refreshed/i)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save classification" })).toBeDisabled();

    vi.mocked(loadTransactionAction).mockResolvedValue({ ok: true, value: makeDetail() });
    await user.click(screen.getByRole("button", { name: "Reload transaction" }));
    expect(await screen.findByText("Latest transaction values loaded.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save classification" })).toBeEnabled();
  });

  it("retires a standalone detail when a save reports deletion", async () => {
    const user = userEvent.setup();
    vi.mocked(saveTransactionAction).mockResolvedValue({
      ok: false,
      kind: "not_found",
      message: "Not found.",
    });
    render(
      <TransactionDetailView categories={categories} initialDetail={makeDetail()} tags={tags} />,
    );

    await user.selectOptions(screen.getByLabelText("Category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Save classification" }));

    expect(
      await screen.findByRole("heading", { name: "Transaction no longer available" }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save classification" })).not.toBeInTheDocument();
  });

  it("reports terminal unavailability when a save targets a deleted row", async () => {
    const user = userEvent.setup();
    const onUnavailable = vi.fn();
    vi.mocked(saveTransactionAction).mockResolvedValue({
      ok: false,
      kind: "not_found",
      message: "Not found.",
    });
    render(
      <TransactionDetailView
        categories={categories}
        initialDetail={makeDetail()}
        onUnavailable={onUnavailable}
        tags={tags}
      />,
    );

    await user.selectOptions(screen.getByLabelText("Category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Save classification" }));

    await waitFor(() => expect(onUnavailable).toHaveBeenCalledTimes(1));
    expect(saveTransactionAction).toHaveBeenCalledTimes(1);
  });

  it("keeps the authoritative saved row when the detail refresh fails", async () => {
    const user = userEvent.setup();
    const saved = {
      ...savedRow,
      category: { ...savedRow.category, id: "cat-2", display_name: "Travel" },
    };
    vi.mocked(saveTransactionAction).mockResolvedValue({
      ok: true,
      value: {
        transaction: saved,
        selected_count: 1,
        changed_count: 1,
        unchanged_count: 0,
      },
    });
    vi.mocked(loadTransactionAction).mockResolvedValue({
      ok: false,
      kind: "unavailable",
      message: "Unavailable.",
    });
    render(
      <TransactionDetailView categories={categories} initialDetail={makeDetail()} tags={tags} />,
    );

    await user.selectOptions(screen.getByLabelText("Category"), "cat-2");
    await user.click(screen.getByRole("button", { name: "Save classification" }));

    expect(await screen.findByText(/could not be refreshed/i)).toBeInTheDocument();
    expect(screen.getByText(/version 4/)).toBeInTheDocument();
    expect((screen.getByLabelText("Category") as HTMLSelectElement).value).toBe("cat-2");
    expect(screen.getByRole("button", { name: "Save classification" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Reload transaction" })).toBeInTheDocument();
  });

  it("explains a hidden parent and disables individual restore", () => {
    render(
      <TransactionDetailView
        categories={categories}
        initialDetail={makeDetail({
          statement_import: {
            id: "stmt-1",
            statement_label: "Statement",
            source_type: "fnb_csv",
            is_hidden: true,
          },
          is_hidden_by_user: true,
          effective_hidden: true,
          hidden_reason: "both",
        })}
        tags={tags}
      />,
    );

    expect(screen.getByText(/This statement is hidden/)).toBeInTheDocument();
    expect(
      screen.getAllByRole("link", { name: "Statement" }).some(
        (link) => link.getAttribute("href") === "/imports/stmt-1",
      ),
    ).toBe(true);
    expect(screen.getByRole("button", { name: "Restore transaction…" })).toBeDisabled();
  });

  it("surfaces owned statement links when a restore is refused", async () => {
    const user = userEvent.setup();
    vi.mocked(saveTransactionAction).mockResolvedValue({
      ok: false,
      kind: "parent_hidden",
      message: "Restore the parent statement first.",
      details: { statements: [{ id: "stmt-1", statement_label: "Statement" }] },
    });
    render(
      <TransactionDetailView
        categories={categories}
        initialDetail={makeDetail({ is_hidden_by_user: true, effective_hidden: true, hidden_reason: "individual" })}
        tags={tags}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Restore transaction…" }));
    await user.click(screen.getByRole("button", { name: "Restore transaction" }));

    expect(await screen.findByText("Restore the parent statement first.")).toBeInTheDocument();
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Restore the parent statement first.");
    expect(within(alert).getByRole("link", { name: "Statement" })).toHaveAttribute(
      "href",
      "/imports/stmt-1",
    );
  });
});
