import { render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  getCurrentAccount: vi.fn(),
  listFinancialAccounts: vi.fn(),
  listOpenImportReviews: vi.fn(),
  listImportHistory: vi.fn(),
}));

vi.mock("@/lib/server/account-actions", () => ({
  endSessionAction: vi.fn(),
}));

vi.mock("@/lib/server/finance-actions", () => ({
  createAccountAction: vi.fn(),
}));

import { redirect } from "next/navigation";

import ImportsPage from "@/app/imports/page";
import { auth } from "@/auth";
import {
  getCurrentAccount,
  listFinancialAccounts,
  listImportHistory,
  listOpenImportReviews,
} from "@/lib/server/backend-client";
import { endSessionAction } from "@/lib/server/account-actions";

const financialAccount = {
  id: "financial-account-id",
  display_name: "Cheque",
  institution: null,
  currency: "ZAR",
  is_archived: false,
  transaction_count: 0,
  created_at: "2026-01-01T00:00:00Z",
  updated_at: "2026-01-01T00:00:00Z",
};

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

describe("imports page", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(endSessionAction).mockReset();
    vi.mocked(redirect).mockReset();
    vi.mocked(listFinancialAccounts).mockReset();
    vi.mocked(listOpenImportReviews).mockReset();
    vi.mocked(listImportHistory).mockReset();
  });

  it("renders the upload workspace and honest first-run empty states", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue(account);
    vi.mocked(listFinancialAccounts).mockResolvedValue({
      ok: true,
      value: { accounts: [financialAccount] },
    });
    vi.mocked(listOpenImportReviews).mockResolvedValue({
      ok: true,
      value: { reviews: [] },
    });
    vi.mocked(listImportHistory).mockResolvedValue({
      ok: true,
      value: { items: [], next_cursor: null },
    });

    render(await ImportsPage());

    expect(screen.getByRole("heading", { level: 1, name: "Imports" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Choose a statement" })).toBeInTheDocument();
    expect(document.querySelector('input[type="file"]')).not.toBeNull();
    expect(screen.getByText("No open reviews.")).toBeInTheDocument();
    expect(screen.getByText("No statements imported yet.")).toBeInTheDocument();

    const nav = screen.getByRole("navigation", { name: "Workspace" });
    expect(within(nav).getByRole("link", { name: "Imports" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(redirect).not.toHaveBeenCalled();
  });

  it("renders the next history cursor link", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue(account);
    vi.mocked(listFinancialAccounts).mockResolvedValue({
      ok: true,
      value: { accounts: [financialAccount] },
    });
    vi.mocked(listOpenImportReviews).mockResolvedValue({
      ok: true,
      value: { reviews: [] },
    });
    vi.mocked(listImportHistory).mockResolvedValue({
      ok: true,
      value: {
        items: [
          {
            id: "statement-id",
            financial_account_id: "financial-account-id",
            source_type: "fnb_csv",
            statement_label: "Statement 2024-01-01 to 2024-01-31",
            start_date: "2024-01-01",
            end_date: "2024-01-31",
            total_rows: 1,
            imported_count: 1,
            suppressed_count: 0,
            skipped_count: 0,
            possible_duplicate_count: 0,
            exact_override_count: 0,
            statement_override_count: 0,
            is_hidden: false,
            version: 1,
            live_hidden_count: 0,
            created_at: "2024-02-01T00:00:00Z",
            updated_at: "2024-02-01T00:00:00Z",
          },
        ],
        next_cursor: "cursor-1",
      },
    });

    render(await ImportsPage());

    expect(screen.getByRole("link", { name: "Load older imports" })).toHaveAttribute(
      "href",
      "/imports?cursor=cursor-1",
    );
  });

  it("redirects to login when no backend session exists", async () => {
    vi.mocked(auth).mockResolvedValue(null as never);

    render(await ImportsPage());

    expect(redirect).toHaveBeenCalledWith("/login");
  });

  it("clears a dead backend session", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "dead-backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockRejectedValue(new Error("backend session expired"));

    render(await ImportsPage());

    expect(screen.getByText("Your session has expired. Please sign in again.")).toBeInTheDocument();
    await waitFor(() => expect(endSessionAction).toHaveBeenCalledTimes(1));
  });
});
