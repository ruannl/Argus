import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { redirect } from "next/navigation";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("@/lib/server/account-actions", () => ({
  endSessionAction: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  AccountReadError: class AccountReadError extends Error {
    readonly kind: string;
    constructor(kind: string) {
      super("Account check failed.");
      this.kind = kind;
    }
  },
  getCurrentAccount: vi.fn(),
  getTransaction: vi.fn(),
  listCategories: vi.fn(),
  listMerchantTags: vi.fn(),
}));

vi.mock("@/lib/server/transaction-actions", () => ({
  abandonOperationAction: vi.fn(),
  applyOperationAction: vi.fn(),
  loadOperationAction: vi.fn(),
  loadTransactionAction: vi.fn(),
  prepareTransactionsAction: vi.fn(),
  queryTransactionsAction: vi.fn(),
  saveTransactionAction: vi.fn(),
}));

import TransactionDetailPage from "@/app/transactions/[id]/page";
import { auth } from "@/auth";
import { testRouter } from "../../../vitest.setup";
import {
  AccountReadError,
  getCurrentAccount,
  getTransaction,
  listCategories,
  listMerchantTags,
} from "@/lib/server/backend-client";
import type { TransactionDetail } from "@/lib/server/backend-client";
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

const transactionId = "22222222-2222-2222-2222-222222222222";

const detail: TransactionDetail = {
  transaction: {
    id: transactionId,
    version: 1,
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
    merchant_tags: [],
    statement_import: {
      id: "stmt-1",
      statement_label: "Statement",
      source_type: "fnb_csv",
      is_hidden: false,
    },
    is_hidden_by_user: false,
    effective_hidden: false,
    hidden_reason: "none",
  },
  suggested_tags: [],
  has_more_suggestions: false,
  source_page: null,
  source_row_number: 4,
};

function renderDetailPage() {
  return TransactionDetailPage({
    params: Promise.resolve({ id: transactionId }),
  });
}

describe("standalone transaction detail page", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", `/transactions/${transactionId}`);
    vi.mocked(auth).mockReset();
    vi.mocked(endSessionAction).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(getTransaction).mockReset();
    vi.mocked(listCategories).mockReset();
    vi.mocked(listMerchantTags).mockReset();
  });

  function signedIn() {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue(account);
    vi.mocked(listCategories).mockResolvedValue({ ok: true, value: { categories: [] } });
    vi.mocked(listMerchantTags).mockResolvedValue({
      ok: true,
      value: { merchant_tags: [] },
    });
  }

  it("separates an unavailable service from a missing transaction", async () => {
    signedIn();
    vi.mocked(getTransaction).mockResolvedValue({ ok: false, kind: "unavailable" });

    render(await renderDetailPage());

    expect(
      screen.getByRole("heading", { name: "Transaction detail could not load" }),
    ).toBeInTheDocument();
    expect(screen.getByText(/not proof that the transaction was deleted/i)).toBeInTheDocument();
    expect(screen.queryByText(/no longer exists/i)).not.toBeInTheDocument();
  });

  it("reports a missing transaction without blaming the service", async () => {
    signedIn();
    vi.mocked(getTransaction).mockResolvedValue({ ok: false, kind: "not_found" });

    render(await renderDetailPage());

    expect(screen.getByRole("heading", { name: "Transaction not found" })).toBeInTheDocument();
    expect(screen.getByText(/no longer exists/i)).toBeInTheDocument();
    expect(screen.queryByText(/service is unavailable/i)).not.toBeInTheDocument();
  });

  it("retains the downstream detail rate-limit state after account preflight", async () => {
    signedIn();
    vi.mocked(getTransaction).mockResolvedValue({ ok: false, kind: "rate_limited" });

    render(await renderDetailPage());

    expect(screen.getByText(/Too many requests right now/)).toBeInTheDocument();
    expect(endSessionAction).not.toHaveBeenCalled();
  });

  it("still clears the session on downstream detail denial", async () => {
    signedIn();
    vi.mocked(getTransaction).mockResolvedValue({ ok: false, kind: "unauthorized" });

    render(await renderDetailPage());

    expect(screen.getByText("Your session has expired. Please sign in again.")).toBeInTheDocument();
    await waitFor(() => expect(endSessionAction).toHaveBeenCalledTimes(1));
  });

  it("disables classification when taxonomy loading fails but the row loads", async () => {
    signedIn();
    vi.mocked(getTransaction).mockResolvedValue({ ok: true, value: detail });
    vi.mocked(listCategories).mockResolvedValue({ ok: false, kind: "unavailable" });

    render(await renderDetailPage());

    expect(screen.getByText(/Some categories or tags could not be loaded/i)).toBeInTheDocument();
    expect(screen.getByLabelText("Category")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Save classification" })).toBeDisabled();
  });

  it.each(["unavailable", "rate_limited"] as const)(
    "does not revoke the session or load protected detail on a %s /v1/me read",
    async (kind) => {
      const user = userEvent.setup();
      signedIn();
      vi.mocked(getCurrentAccount).mockRejectedValue(new AccountReadError(kind));

      render(await renderDetailPage());

      expect(
        screen.getByRole("heading", { name: "Transaction detail unavailable" }),
      ).toBeInTheDocument();
      expect(screen.getByRole("alert")).toHaveTextContent(
        kind === "rate_limited" ? /Wait a few minutes/ : /does not prove your session expired/,
      );
      expect(endSessionAction).not.toHaveBeenCalled();
      expect(getTransaction).not.toHaveBeenCalled();
      expect(listCategories).not.toHaveBeenCalled();
      expect(listMerchantTags).not.toHaveBeenCalled();

      await user.click(screen.getByRole("button", { name: "Retry account check" }));
      expect(testRouter.refresh).toHaveBeenCalledTimes(1);
      expect(window.location.pathname).toBe(`/transactions/${transactionId}`);
    },
  );

  it("clears only a confirmed unauthorized account session", async () => {
    signedIn();
    vi.mocked(getCurrentAccount).mockRejectedValue(new AccountReadError("unauthorized"));

    render(await renderDetailPage());

    expect(screen.getByText("Your session has expired. Please sign in again.")).toBeInTheDocument();
    await waitFor(() => expect(endSessionAction).toHaveBeenCalledTimes(1));
    expect(getTransaction).not.toHaveBeenCalled();
  });

  it("uses safe unavailable copy on untyped preflight failures", async () => {
    signedIn();
    vi.mocked(getCurrentAccount).mockRejectedValue(new Error("PRIVATE-CANARY backend origin"));

    render(await renderDetailPage());

    expect(
      screen.getByRole("heading", { name: "Transaction detail unavailable" }),
    ).toBeInTheDocument();
    expect(screen.queryByText(/PRIVATE-CANARY/)).not.toBeInTheDocument();
    expect(endSessionAction).not.toHaveBeenCalled();
    expect(getTransaction).not.toHaveBeenCalled();
  });

  it("redirects before an account check when no server session exists", async () => {
    vi.mocked(auth).mockResolvedValue(null as never);
    vi.mocked(redirect).mockImplementation(() => {
      throw new Error("NEXT_REDIRECT");
    });

    await expect(renderDetailPage()).rejects.toThrow("NEXT_REDIRECT");
    expect(redirect).toHaveBeenCalledWith("/login");
    expect(getCurrentAccount).not.toHaveBeenCalled();
    expect(getTransaction).not.toHaveBeenCalled();
  });
});
