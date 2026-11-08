import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  getCurrentAccount: vi.fn(),
  getImportStatement: vi.fn(),
}));

vi.mock("@/lib/server/account-actions", () => ({
  endSessionAction: vi.fn(),
}));

vi.mock("@/lib/server/import-actions", () => ({
  deleteImportStatementAction: vi.fn(),
  setImportVisibilityAction: vi.fn(),
}));

import { redirect } from "next/navigation";

import StatementPage from "@/app/imports/[id]/page";
import { auth } from "@/auth";
import { getCurrentAccount, getImportStatement } from "@/lib/server/backend-client";

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

const detail = {
  statement: {
    id: "statement-id",
    financial_account_id: "financial-account-id",
    source_type: "fnb_csv",
    statement_label: "Statement 2024-01-01 to 2024-01-31",
    start_date: "2024-01-01",
    end_date: "2024-01-31",
    total_rows: 2,
    imported_count: 2,
    suppressed_count: 0,
    skipped_count: 0,
    possible_duplicate_count: 0,
    exact_override_count: 0,
    statement_override_count: 0,
    is_hidden: false,
    version: 1,
    live_hidden_count: 1,
    created_at: "2024-02-01T00:00:00Z",
    updated_at: "2024-02-01T00:00:00Z",
  },
  rows: [
    {
      id: "transaction-id",
      source_row_number: 1,
      source_page: null,
      transaction_date: "2024-01-02",
      description: "Committed row",
      amount: "-10.00",
      is_hidden_by_user: true,
      effective_hidden: true,
      tags: [{ id: "tag-id", display_name: "Woolworths" }],
    },
  ],
  page: 1,
  page_size: 50,
  expected_transaction_count: 2,
};

describe("statement detail page", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(getImportStatement).mockReset();
    vi.mocked(redirect).mockReset();
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue(account);
  });

  it("renders the committed summary, read-only rows, and lifecycle controls", async () => {
    vi.mocked(getImportStatement).mockResolvedValue({ ok: true, value: detail });

    render(
      await StatementPage({
        params: Promise.resolve({ id: "statement-id" }),
        searchParams: Promise.resolve({}),
      }),
    );

    expect(screen.getByRole("heading", { level: 1, name: "Statement detail" })).toBeInTheDocument();
    expect(screen.getByText("Statement 2024-01-01 to 2024-01-31")).toBeInTheDocument();
    expect(screen.getByText("Committed row")).toBeInTheDocument();
    expect(screen.getByText("Individually hidden")).toBeInTheDocument();
    expect(screen.getByText("Woolworths")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Hide statement" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Delete statement…" })).toBeInTheDocument();

    const nav = screen.getByRole("navigation", { name: "Workspace" });
    expect(within(nav).getByRole("link", { name: "Imports" })).toHaveAttribute(
      "aria-current",
      "page",
    );
  });

  it("shows a plain missing-statement state", async () => {
    vi.mocked(getImportStatement).mockResolvedValue({ ok: false, kind: "not_found" });

    render(
      await StatementPage({
        params: Promise.resolve({ id: "missing" }),
        searchParams: Promise.resolve({}),
      }),
    );

    expect(screen.getByRole("heading", { name: "Statement unavailable" })).toBeInTheDocument();
    expect(screen.getByText("That statement no longer exists.")).toBeInTheDocument();
  });
});
