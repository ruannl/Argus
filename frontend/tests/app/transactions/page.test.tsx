import { render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  getCurrentAccount: vi.fn(),
  listFinancialAccounts: vi.fn(),
  listCategories: vi.fn(),
  listMerchantTags: vi.fn(),
  queryTransactions: vi.fn(),
}));

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

import { redirect } from "next/navigation";

import TransactionsPage from "@/app/transactions/page";
import { auth } from "@/auth";
import {
  getCurrentAccount,
  listCategories,
  listFinancialAccounts,
  listMerchantTags,
  queryTransactions,
} from "@/lib/server/backend-client";
import { endSessionAction } from "@/lib/server/account-actions";

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

const emptyPage = {
  transactions: [],
  inventory_count: 0,
  matching_visible_count: 0,
  matching_hidden_count: 0,
  matching_total: 0,
  filtered_total: 0,
  page: 1,
  page_size: 25,
  sort_by: "date",
  sort_direction: "desc",
};

function renderPage(searchParams: Record<string, string | string[]> = {}) {
  return TransactionsPage({ searchParams: Promise.resolve(searchParams) });
}

describe("transactions page", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(listFinancialAccounts).mockReset();
    vi.mocked(listCategories).mockReset();
    vi.mocked(listMerchantTags).mockReset();
    vi.mocked(queryTransactions).mockReset();
    vi.mocked(endSessionAction).mockReset();
    vi.mocked(redirect).mockReset();
  });

  function signedIn() {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue(account);
    vi.mocked(listFinancialAccounts).mockResolvedValue({ ok: true, value: { accounts: [] } });
    vi.mocked(listCategories).mockResolvedValue({ ok: true, value: { categories: [] } });
    vi.mocked(listMerchantTags).mockResolvedValue({ ok: true, value: { merchant_tags: [] } });
  }

  it("renders the workspace with an honest first-run empty state", async () => {
    signedIn();
    vi.mocked(queryTransactions).mockResolvedValue({ ok: true, value: emptyPage });

    render(await renderPage());

    expect(screen.getByRole("heading", { level: 1, name: "Transactions" })).toBeInTheDocument();
    expect(screen.getByText("No transactions yet")).toBeInTheDocument();
    expect(queryTransactions).toHaveBeenCalledWith("backend-token", {
      filters: { visibility: "visible" },
      page: 1,
      page_size: 25,
      sort_by: "date",
      sort_direction: "desc",
    });

    const nav = screen.getByRole("navigation", { name: "Workspace" });
    expect(within(nav).getByRole("link", { name: "Transactions" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(redirect).not.toHaveBeenCalled();
  });

  it("passes validated nonsensitive filters to the initial query", async () => {
    signedIn();
    vi.mocked(queryTransactions).mockResolvedValue({ ok: true, value: emptyPage });

    render(
      await renderPage({
        statement_import_id: "11111111-1111-1111-1111-111111111111",
        visibility: "all",
        page: "2",
        page_size: "50",
        sort_by: "amount",
        sort_direction: "asc",
      }),
    );

    expect(queryTransactions).toHaveBeenCalledWith("backend-token", {
      filters: {
        visibility: "all",
        statement_import_id: "11111111-1111-1111-1111-111111111111",
      },
      page: 2,
      page_size: 50,
      sort_by: "amount",
      sort_direction: "asc",
    });
  });

  it("shows an explicit error for malformed address filters instead of broadening", async () => {
    signedIn();

    render(await renderPage({ page_size: "30" }));

    expect(screen.getByText(/Some transaction filters in the address were invalid/)).toBeInTheDocument();
    expect(queryTransactions).not.toHaveBeenCalled();
  });

  it("redirects to login when no backend session exists", async () => {
    vi.mocked(auth).mockResolvedValue(null as never);

    render(await renderPage());

    expect(redirect).toHaveBeenCalledWith("/login");
  });

  it("clears a dead backend session", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "dead-backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockRejectedValue(new Error("backend session expired"));

    render(await renderPage());

    expect(screen.getByText("Your session has expired. Please sign in again.")).toBeInTheDocument();
    await waitFor(() => expect(endSessionAction).toHaveBeenCalledTimes(1));
  });
});
