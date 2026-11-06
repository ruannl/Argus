import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("next/cache", () => ({
  revalidatePath: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  abandonImportReview: vi.fn(),
  commitImportReview: vi.fn(),
  deleteImportStatement: vi.fn(),
  patchImportReview: vi.fn(),
  refreshImportReview: vi.fn(),
  setImportVisibility: vi.fn(),
}));

import { auth } from "@/auth";
import { revalidatePath } from "next/cache";
import {
  abandonImportReview,
  commitImportReview,
  deleteImportStatement,
  patchImportReview,
  refreshImportReview,
  setImportVisibility,
} from "@/lib/server/backend-client";
import {
  abandonImportReviewAction,
  commitImportReviewAction,
  deleteImportStatementAction,
  patchImportReviewAction,
  refreshImportReviewAction,
  setImportVisibilityAction,
} from "@/lib/server/import-actions";

const summary = {
  id: "review-id",
  financial_account_id: "account-id",
  source_kind: "fnb_csv",
  start_date: "2024-01-01",
  end_date: "2024-01-31",
  version: 2,
  status: "Prepared",
  expires_at: "2030-01-02T00:00:00Z",
  total_rows: 3,
  imported_count: 3,
  suppressed_count: 0,
  skipped_count: 0,
  invalid_unskipped_count: 0,
  within_file_pending_count: 0,
  possible_duplicate_count: 0,
  exact_override_count: 0,
  statement_override: false,
  statement_override_count: 0,
  summary_hash: "hash",
};

const statement = {
  id: "statement-id",
  financial_account_id: "account-id",
  source_type: "fnb_csv",
  statement_label: "Statement",
  start_date: "2024-01-01",
  end_date: "2024-01-31",
  total_rows: 3,
  imported_count: 3,
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
};

describe("import server actions", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(revalidatePath).mockReset();
    vi.mocked(patchImportReview).mockReset();
    vi.mocked(refreshImportReview).mockReset();
    vi.mocked(commitImportReview).mockReset();
    vi.mocked(abandonImportReview).mockReset();
    vi.mocked(setImportVisibility).mockReset();
    vi.mocked(deleteImportStatement).mockReset();
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
  });

  it("patches a review and revalidates the workspace", async () => {
    vi.mocked(patchImportReview).mockResolvedValue({ ok: true, value: summary });

    const result = await patchImportReviewAction("review-id", 1, [], true);

    expect(result.ok).toBe(true);
    expect(result.review).toEqual(summary);
    expect(patchImportReview).toHaveBeenCalledWith(
      "backend-token", "review-id", 1, [], true,
    );
    expect(revalidatePath).toHaveBeenCalledWith("/imports");
  });

  it("refreshes a stale review and returns the new version", async () => {
    vi.mocked(refreshImportReview).mockResolvedValue({ ok: true, value: summary });

    const result = await refreshImportReviewAction("review-id", 1);

    expect(result.ok).toBe(true);
    expect(result.review?.version).toBe(2);
    expect(refreshImportReview).toHaveBeenCalledWith("backend-token", "review-id", 1);
  });

  it("commits and returns the statement id", async () => {
    vi.mocked(commitImportReview).mockResolvedValue({
      ok: true,
      value: { replayed: false, statement },
    });

    const result = await commitImportReviewAction("review-id", 2, "hash");

    expect(result.ok).toBe(true);
    expect(result.statementId).toBe("statement-id");
    expect(result.replayed).toBe(false);
    expect(revalidatePath).toHaveBeenCalledWith("/imports");
  });

  it("surfaces statement repeat ids without pretending success", async () => {
    vi.mocked(commitImportReview).mockResolvedValue({
      ok: false,
      kind: "statement_repeat",
      existingImportIds: ["existing-id"],
    });

    const result = await commitImportReviewAction("review-id", 2, "hash");

    expect(result.ok).toBe(false);
    expect(result.errorKind).toBe("statement_repeat");
    expect(result.existingImportIds).toEqual(["existing-id"]);
    expect(revalidatePath).not.toHaveBeenCalled();
  });

  it("maps terminal and lifecycle results", async () => {
    vi.mocked(abandonImportReview).mockResolvedValue({
      ok: true,
      value: { committed: false, statement: null, replayed: false },
    });
    vi.mocked(setImportVisibility).mockResolvedValue({ ok: true, value: { ...statement, is_hidden: true } });
    vi.mocked(deleteImportStatement).mockResolvedValue({
      ok: true,
      value: { deleted: true, deleted_transaction_count: 3 },
    });

    await expect(abandonImportReviewAction("review-id", 2)).resolves.toEqual(
      expect.objectContaining({ ok: true, message: expect.stringContaining("abandoned") }),
    );
    await expect(setImportVisibilityAction("statement-id", true, 1, 3)).resolves.toEqual(
      expect.objectContaining({ ok: true, statement: expect.objectContaining({ is_hidden: true }) }),
    );
    await expect(deleteImportStatementAction("statement-id", 1, 3)).resolves.toEqual(
      expect.objectContaining({ ok: true, deletedTransactionCount: 3 }),
    );
  });

  it("rejects without a backend session and never calls the backend", async () => {
    vi.mocked(auth).mockResolvedValue(null as never);

    const result = await patchImportReviewAction("review-id", 1, []);

    expect(result.ok).toBe(false);
    expect(result.message).toContain("session has expired");
    expect(patchImportReview).not.toHaveBeenCalled();
  });
});
