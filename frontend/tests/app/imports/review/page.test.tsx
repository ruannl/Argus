import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  getCurrentAccount: vi.fn(),
  getImportReview: vi.fn(),
  listMerchantTags: vi.fn(),
}));

vi.mock("@/lib/server/account-actions", () => ({
  endSessionAction: vi.fn(),
}));

vi.mock("@/lib/server/import-actions", () => ({
  abandonImportReviewAction: vi.fn(),
  commitImportReviewAction: vi.fn(),
  patchImportReviewAction: vi.fn(),
  refreshImportReviewAction: vi.fn(),
}));

import { redirect } from "next/navigation";

import ReviewPage from "@/app/imports/review/[id]/page";
import { auth } from "@/auth";
import { getCurrentAccount, getImportReview, listMerchantTags } from "@/lib/server/backend-client";

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

const summary = {
  id: "review-id",
  financial_account_id: "financial-account-id",
  source_kind: "fnb_csv",
  start_date: "2024-01-01",
  end_date: "2024-01-31",
  version: 3,
  status: "Prepared",
  expires_at: "2030-01-02T00:00:00.0000000Z",
  total_rows: 4,
  imported_count: 2,
  suppressed_count: 1,
  skipped_count: 1,
  invalid_unskipped_count: 1,
  within_file_pending_count: 1,
  possible_duplicate_count: 1,
  exact_override_count: 0,
  statement_override: false,
  statement_override_count: 0,
  summary_hash: "hash",
};

const rows = [
  {
    row_id: "row-1",
    source_line: 8,
    source_page: null,
    source_row_number: 1,
    raw_values: { date: "2024/01/02", amount: "-10.00", description: "Woolworths groceries" },
    transaction_date: "2024-01-02",
    description: "Woolworths groceries",
    amount: "-10.00",
    validation_codes: [],
    warnings: [],
    duplicate_state: "none",
    duplicate_reasons: [],
    matching_import_ids: [],
    near_candidates: [],
    near_candidate_total: 0,
    within_file_group: null,
    within_file_state: "none",
    skip: false,
    override: false,
    suggested_tag_ids: ["tag-id"],
    selected_tag_ids: [],
  },
  {
    row_id: "row-2",
    source_line: 9,
    source_page: null,
    source_row_number: 2,
    raw_values: { date: "2024/01/03", amount: "bad", description: "Broken amount" },
    transaction_date: "2024-01-03",
    description: "Broken amount",
    amount: null,
    validation_codes: ["invalid_amount"],
    warnings: [],
    duplicate_state: "none",
    duplicate_reasons: [],
    matching_import_ids: [],
    near_candidates: [],
    near_candidate_total: 0,
    within_file_group: null,
    within_file_state: "none",
    skip: false,
    override: false,
    suggested_tag_ids: [],
    selected_tag_ids: [],
  },
  {
    row_id: "row-3",
    source_line: 10,
    source_page: null,
    source_row_number: 3,
    raw_values: { date: "2024/01/04", amount: "-11.00", description: "Existing coffee" },
    transaction_date: "2024-01-04",
    description: "Existing coffee",
    amount: "-11.00",
    validation_codes: [],
    warnings: [],
    duplicate_state: "suppressed",
    duplicate_reasons: ["existing_occurrence"],
    matching_import_ids: ["import-id"],
    near_candidates: [],
    near_candidate_total: 0,
    within_file_group: null,
    within_file_state: "none",
    skip: false,
    override: false,
    suggested_tag_ids: [],
    selected_tag_ids: ["tag-id"],
  },
  {
    row_id: "row-4",
    source_line: 11,
    source_page: null,
    source_row_number: 4,
    raw_values: { date: "2024/01/05", amount: "-12.00", description: "Same coffee" },
    transaction_date: "2024-01-05",
    description: "Same coffee",
    amount: "-12.00",
    validation_codes: [],
    warnings: [],
    duplicate_state: "within_file",
    duplicate_reasons: ["within_file"],
    matching_import_ids: [],
    near_candidates: [],
    near_candidate_total: 0,
    within_file_group: "group-1",
    within_file_state: "pending",
    skip: false,
    override: false,
    suggested_tag_ids: [],
    selected_tag_ids: [],
  },
];

const tag = {
  id: "tag-id",
  display_name: "Woolworths",
  is_archived: false,
  transaction_count: 0,
  created_at: "2026-01-01T00:00:00Z",
  updated_at: "2026-01-01T00:00:00Z",
};

describe("import review page", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(getImportReview).mockReset();
    vi.mocked(listMerchantTags).mockReset();
    vi.mocked(redirect).mockReset();
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue(account);
    vi.mocked(listMerchantTags).mockResolvedValue({
      ok: true,
      value: { merchant_tags: [tag] },
    });
  });

  it("renders read-only rows, blockers, suggestions, and duplicate controls", async () => {
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: { summary, page: { rows, total: 4, page: 1, page_size: 50 } },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({}),
      }),
    );

    expect(screen.getByRole("heading", { level: 1, name: "Import review" })).toBeInTheDocument();
    expect(screen.getAllByText("Woolworths groceries").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Broken amount").length).toBeGreaterThan(0);
    expect(screen.getByText("Matches an existing occurrence")).toBeInTheDocument();
    expect(screen.getByText("Keep all in group")).toBeInTheDocument();
    expect(screen.getByText("Add Woolworths")).toBeInTheDocument();
    expect(
      screen.getByText("Invalid rows must be marked as skipped before committing."),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Commit import" })).toBeDisabled();
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();

    const nav = screen.getByRole("navigation", { name: "Workspace" });
    expect(within(nav).getByRole("link", { name: "Imports" })).toHaveAttribute(
      "aria-current",
      "page",
    );
  });

  it("uses global summary blockers even when the current page has no invalid rows", async () => {
    const pageRows = [rows[0], rows[2]];
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: {
        summary: {
          ...summary,
          total_rows: 2,
          imported_count: 1,
          suppressed_count: 1,
          skipped_count: 0,
          invalid_unskipped_count: 1,
          within_file_pending_count: 0,
        },
        page: { rows: pageRows, total: 2, page: 1, page_size: 50 },
      },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({}),
      }),
    );

    expect(
      screen.getByText("Invalid rows must be marked as skipped before committing."),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Commit import" })).toBeDisabled();
  });

  it("shows a plain expired-review state", async () => {
    vi.mocked(getImportReview).mockResolvedValue({ ok: false, kind: "review_expired" });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({}),
      }),
    );

    expect(screen.getByRole("heading", { name: "Review unavailable" })).toBeInTheDocument();
    expect(screen.getByText(/expired/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to imports" })).toHaveAttribute(
      "href",
      "/imports",
    );
  });
});
