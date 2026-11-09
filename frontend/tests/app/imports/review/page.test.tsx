import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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

vi.mock("@/lib/server/finance-actions", () => ({
  createMerchantTagAction: vi.fn(),
}));

import { redirect } from "next/navigation";

import ReviewPage from "@/app/imports/review/[id]/page";
import { auth } from "@/auth";
import { testRouter } from "../../../../vitest.setup";

import { getCurrentAccount, getImportReview, listMerchantTags } from "@/lib/server/backend-client";
import { createMerchantTagAction } from "@/lib/server/finance-actions";
import { patchImportReviewAction } from "@/lib/server/import-actions";

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
    vi.mocked(createMerchantTagAction).mockReset();
    vi.mocked(patchImportReviewAction).mockReset();
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

    const nav = screen.getByRole("navigation", { name: "Workspace" });
    expect(within(nav).getByRole("link", { name: "Imports" })).toHaveAttribute(
      "aria-current",
      "page",
    );
  });

  it("creates a tag inline and assigns it to the row", async () => {
    const user = userEvent.setup();
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: { summary, page: { rows, total: 4, page: 1, page_size: 50 } },
    });
    vi.mocked(createMerchantTagAction).mockResolvedValue({
      ok: true,
      message: "Tag created.",
      value: {
        id: "tag-new",
        display_name: "Landlord",
        is_archived: false,
        transaction_count: 0,
        created_at: "2026-01-01T00:00:00Z",
        updated_at: "2026-01-01T00:00:00Z",
      },
    });
    vi.mocked(patchImportReviewAction).mockResolvedValue({
      ok: true,
      message: "Review updated.",
      review: { ...summary, version: 4 },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({}),
      }),
    );

    const row = screen.getAllByText("Woolworths groceries")[0].closest("tr");
    expect(row).not.toBeNull();
    await user.type(
      within(row as HTMLElement).getByLabelText("New tag name for row 1"),
      "Landlord",
    );
    await user.click(within(row as HTMLElement).getByRole("button", { name: /Create and add/ }));

    await waitFor(() => expect(createMerchantTagAction).toHaveBeenCalledTimes(1));
    await waitFor(() =>
      expect(patchImportReviewAction).toHaveBeenCalledWith(
        "review-id",
        3,
        [{ row_id: "row-1", selected_tag_ids: ["tag-new"] }],
        undefined,
      ),
    );
  });

  it("applies a skip decision without reloading the whole review", async () => {
    const user = userEvent.setup();
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: { summary, page: { rows, total: 4, page: 1, page_size: 50 } },
    });
    vi.mocked(patchImportReviewAction).mockResolvedValue({
      ok: true,
      message: "Review updated.",
      review: { ...summary, skipped_count: summary.skipped_count + 1, version: 4 },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({}),
      }),
    );

    const row = screen.getAllByText("Woolworths groceries")[0].closest("tr");
    expect(row).not.toBeNull();
    await user.click(within(row as HTMLElement).getByRole("checkbox", { name: "Skip row 1" }));

    await waitFor(() => expect(patchImportReviewAction).toHaveBeenCalledTimes(1));
    expect(within(row as HTMLElement).getByRole("checkbox", { name: "Skip row 1" })).toBeChecked();
    expect(testRouter.refresh).not.toHaveBeenCalled();
  });

  it("marks possible duplicates in the row status", async () => {
    const nearCandidate = {
      candidate_id: "candidate-id",
      source: "existing",
      import_id: "import-id",
      transaction_date: "2026-10-27",
      amount: "-1029.26",
      description: "AE GARFONTEIN 410586*6466 24 OCT",
      similarity: 0.91,
      reasons: ["near_description", "same_amount"],
    };
    const possibleRow = {
      ...rows[0],
      duplicate_state: "none",
      duplicate_reasons: ["near_description"],
      near_candidates: [nearCandidate],
      near_candidate_total: 1,
    };
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: { summary, page: { rows: [possibleRow, rows[1]], total: 2, page: 1, page_size: 50 } },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({}),
      }),
    );

    expect(screen.getByText("Possible duplicate")).toBeInTheDocument();
  });

  it("loads only possible duplicates when the filter is enabled", async () => {
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: { summary, page: { rows: [rows[0]], total: 1, page: 1, page_size: 50 } },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({ duplicates: "possible" }),
      }),
    );

    expect(getImportReview).toHaveBeenCalledWith(
      "backend-token",
      "review-id",
      1,
      50,
      "asc",
      true,
      false,
    );
    expect(screen.getByRole("checkbox", { name: /Possible duplicates only/ })).toBeChecked();
  });

  it("filters the review to rows with no tags", async () => {
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: { summary, page: { rows: [rows[0]], total: 1, page: 1, page_size: 50 } },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({ untagged: "true" }),
      }),
    );

    expect(getImportReview).toHaveBeenCalledWith(
      "backend-token",
      "review-id",
      1,
      50,
      "asc",
      false,
      true,
    );
    expect(screen.getByRole("checkbox", { name: "No tags only" })).toBeChecked();
  });

  it("expands and collapses the duplicate details for a row", async () => {
    const user = userEvent.setup();
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

    const row = screen.getAllByText("Existing coffee")[0].closest("tr");
    expect(row).not.toBeNull();
    expect(screen.getByText("Matches an existing occurrence")).not.toBeVisible();

    await user.click(
      within(row as HTMLElement).getByRole("button", { name: "Show details for row 3" }),
    );

    expect(
      within(row as HTMLElement).getByRole("button", { name: "Hide details for row 3" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Matches an existing occurrence")).toBeVisible();

    await user.click(
      within(row as HTMLElement).getByRole("button", { name: "Hide details for row 3" }),
    );
    expect(screen.getByText("Matches an existing occurrence")).not.toBeVisible();
  });

  it("replaces the visible rows when navigating to another page", async () => {
    vi.mocked(getImportReview)
      .mockResolvedValueOnce({
        ok: true,
        value: { summary, page: { rows: [rows[0]], total: 2, page: 1, page_size: 1 } },
      })
      .mockResolvedValueOnce({
        ok: true,
        value: { summary, page: { rows: [rows[1]], total: 2, page: 2, page_size: 1 } },
      });

    const first = render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({ page: "1", page_size: "25" }),
      }),
    );
    expect(screen.getAllByText("Woolworths groceries").length).toBeGreaterThan(0);

    first.rerender(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({ page: "2", page_size: "25" }),
      }),
    );

    expect(screen.getAllByText("Broken amount").length).toBeGreaterThan(0);
    expect(screen.queryByText("Woolworths groceries")).not.toBeInTheDocument();
  });

  it("loads the review page size requested in the query string", async () => {
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: { summary, page: { rows, total: 4, page: 1, page_size: 25 } },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({ page_size: "25" }),
      }),
    );

    expect(getImportReview).toHaveBeenCalledWith(
      "backend-token",
      "review-id",
      1,
      25,
      "asc",
      false,
      false,
    );
    expect(screen.getByLabelText("Rows per page")).toHaveValue("25");
  });

  it("loads the review in the requested date order", async () => {
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: { summary, page: { rows, total: 4, page: 1, page_size: 50 } },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({ sort_direction: "desc" }),
      }),
    );

    expect(getImportReview).toHaveBeenCalledWith(
      "backend-token",
      "review-id",
      1,
      50,
      "desc",
      false,
      false,
    );
    expect(screen.getByLabelText("Sort by date")).toHaveValue("desc");
  });

  it("falls back to 50 rows per page for an unsupported size", async () => {
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: { summary, page: { rows, total: 4, page: 1, page_size: 50 } },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({ page_size: "1000" }),
      }),
    );

    expect(getImportReview).toHaveBeenCalledWith(
      "backend-token",
      "review-id",
      1,
      50,
      "asc",
      false,
      false,
    );
    expect(screen.getByLabelText("Rows per page")).toHaveValue("50");
  });

  it("removes a row that stops matching the untagged filter", async () => {
    const user = userEvent.setup();
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: { summary, page: { rows: [rows[0]], total: 1, page: 1, page_size: 50 } },
    });
    vi.mocked(patchImportReviewAction).mockResolvedValue({
      ok: true,
      message: "Review updated.",
      review: { ...summary, version: 4 },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({ untagged: "true" }),
      }),
    );

    expect(screen.getAllByText("Woolworths groceries").length).toBeGreaterThan(0);
    await user.click(screen.getByRole("button", { name: "Add Woolworths" }));

    await waitFor(() => expect(patchImportReviewAction).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.queryAllByText("Woolworths groceries")).toHaveLength(0));
    await waitFor(() => expect(testRouter.refresh).toHaveBeenCalledTimes(1));
  });

  it("refreshes the possible-duplicates view after a group decision", async () => {
    const user = userEvent.setup();
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: { summary, page: { rows: [rows[3]], total: 1, page: 1, page_size: 50 } },
    });
    vi.mocked(patchImportReviewAction).mockResolvedValue({
      ok: true,
      message: "Review updated.",
      review: { ...summary, version: 4 },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({ duplicates: "possible" }),
      }),
    );

    await user.click(screen.getByRole("button", { name: "Show details for row 4" }));
    await user.click(screen.getByRole("button", { name: "Keep all in group" }));

    await waitFor(() =>
      expect(patchImportReviewAction).toHaveBeenCalledWith(
        "review-id",
        3,
        [{ row_id: "row-4", within_file_group: "group-1", within_file_action: "keep_all" }],
        undefined,
      ),
    );
    await waitFor(() => expect(testRouter.refresh).toHaveBeenCalledTimes(1));
  });

  it("explains that repeated-row groups block commit", async () => {
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: {
        summary: {
          ...summary,
          invalid_unskipped_count: 0,
          within_file_pending_count: 3,
          imported_count: 2,
          suppressed_count: 0,
          skipped_count: 2,
        },
        page: { rows, total: 4, page: 1, page_size: 50 },
      },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({}),
      }),
    );

    expect(screen.getByRole("button", { name: "Commit import" })).toBeDisabled();
    expect(screen.getByText(/3 repeated-row groups still need a decision/)).toBeInTheDocument();
  });

  it("explains when nothing would be imported", async () => {
    vi.mocked(getImportReview).mockResolvedValue({
      ok: true,
      value: {
        summary: {
          ...summary,
          invalid_unskipped_count: 0,
          within_file_pending_count: 0,
          imported_count: 0,
          suppressed_count: 0,
          skipped_count: 4,
        },
        page: { rows, total: 4, page: 1, page_size: 50 },
      },
    });

    render(
      await ReviewPage({
        params: Promise.resolve({ id: "review-id" }),
        searchParams: Promise.resolve({}),
      }),
    );

    expect(screen.getByRole("button", { name: "Commit import" })).toBeDisabled();
    expect(screen.getByText(/Nothing will be imported yet/)).toBeInTheDocument();
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
