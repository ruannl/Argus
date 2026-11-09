import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("next/cache", () => ({
  revalidatePath: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  archiveCategory: vi.fn(),
  archiveFinancialAccount: vi.fn(),
  archiveMerchantTag: vi.fn(),
  createCategory: vi.fn(),
  createFinancialAccount: vi.fn(),
  createMerchantTag: vi.fn(),
  deleteCategory: vi.fn(),
  deleteMerchantTag: vi.fn(),
  restoreCategory: vi.fn(),
  restoreFinancialAccount: vi.fn(),
  restoreMerchantTag: vi.fn(),
  updateCategory: vi.fn(),
  updateFinancialAccount: vi.fn(),
  updateMerchantTag: vi.fn(),
}));

import { revalidatePath } from "next/cache";

import { auth } from "@/auth";
import {
  archiveAccountAction,
  createAccountAction,
  createCategoryAction,
  deleteCategoryAction,
  deleteMerchantTagAction,
  renameAccountAction,
  restoreAccountAction,
  updateAccountInstitutionAction,
  updateCategoryAction,
} from "@/lib/server/finance-actions";
import {
  archiveFinancialAccount,
  createCategory,
  createFinancialAccount,
  deleteCategory,
  deleteMerchantTag,
  restoreFinancialAccount,
  updateCategory,
  updateFinancialAccount,
} from "@/lib/server/backend-client";

const account = {
  id: "account-id",
  display_name: "Cheque",
  institution: "FNB",
  currency: "ZAR",
  is_archived: false,
  transaction_count: 0,
  created_at: "2026-01-01T00:00:00.0000000Z",
  updated_at: "2026-01-01T00:00:00.0000000Z",
};

const category = {
  id: "category-id",
  display_name: "Groceries",
  kind: "Expense" as const,
  is_system: false,
  is_default: true,
  is_archived: false,
  transaction_count: 0,
  created_at: "2026-01-01T00:00:00.0000000Z",
  updated_at: "2026-01-01T00:00:00.0000000Z",
};

function form(entries: Record<string, string>): FormData {
  const data = new FormData();
  for (const [name, value] of Object.entries(entries)) {
    data.set(name, value);
  }
  return data;
}

function signedIn() {
  vi.mocked(auth).mockResolvedValue({
    expires: "2030-01-01T00:00:00.000Z",
    authenticated: true,
    user: { backendToken: "backend-token" },
  } as never);
}

describe("finance server actions", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(revalidatePath).mockReset();
    vi.mocked(archiveFinancialAccount).mockReset();
    vi.mocked(createCategory).mockReset();
    vi.mocked(createFinancialAccount).mockReset();
    vi.mocked(deleteCategory).mockReset();
    vi.mocked(deleteMerchantTag).mockReset();
    vi.mocked(restoreFinancialAccount).mockReset();
    vi.mocked(updateCategory).mockReset();
    vi.mocked(updateFinancialAccount).mockReset();
  });

  it("creates an account and revalidates the workspace", async () => {
    signedIn();
    vi.mocked(createFinancialAccount).mockResolvedValue({ ok: true, value: account });

    const result = await createAccountAction(form({ display_name: "Cheque", institution: "FNB" }));

    expect(createFinancialAccount).toHaveBeenCalledWith("backend-token", {
      display_name: "Cheque",
      institution: "FNB",
    });
    expect(result).toEqual({ ok: true, message: "Financial account created." });
    expect(revalidatePath).toHaveBeenCalledWith("/settings");
    expect(revalidatePath).toHaveBeenCalledWith("/home");
    expect(revalidatePath).toHaveBeenCalledWith("/transactions");
    expect(revalidatePath).toHaveBeenCalledWith("/imports");
  });

  it("omits an empty institution when creating an account", async () => {
    signedIn();
    vi.mocked(createFinancialAccount).mockResolvedValue({ ok: true, value: account });

    await createAccountAction(form({ display_name: "Cheque", institution: "" }));

    const input = vi.mocked(createFinancialAccount).mock.calls[0][1];
    expect(input).toEqual({ display_name: "Cheque" });
    expect(Object.hasOwn(input, "institution")).toBe(false);
  });

  it("omits institution on a rename-only update", async () => {
    signedIn();
    vi.mocked(updateFinancialAccount).mockResolvedValue({ ok: true, value: account });

    const result = await renameAccountAction(
      form({ id: "account-id", display_name: "Main cheque" }),
    );

    const input = vi.mocked(updateFinancialAccount).mock.calls[0][2];
    expect(input).toEqual({ display_name: "Main cheque" });
    expect(Object.hasOwn(input, "institution")).toBe(false);
    expect(result).toEqual({ ok: true, message: "Financial account renamed." });
  });

  it("sends null when the institution is cleared and a value when set", async () => {
    signedIn();
    vi.mocked(updateFinancialAccount).mockResolvedValue({ ok: true, value: account });

    await updateAccountInstitutionAction(
      form({ id: "account-id", display_name: "Cheque", institution: "  " }),
    );
    expect(vi.mocked(updateFinancialAccount).mock.calls[0][2]).toEqual({
      display_name: "Cheque",
      institution: null,
    });

    await updateAccountInstitutionAction(
      form({ id: "account-id", display_name: "Cheque", institution: "Capitec" }),
    );
    expect(vi.mocked(updateFinancialAccount).mock.calls[1][2]).toEqual({
      display_name: "Cheque",
      institution: "Capitec",
    });
  });

  it("archives and restores accounts", async () => {
    signedIn();
    vi.mocked(archiveFinancialAccount).mockResolvedValue({ ok: true, value: account });
    vi.mocked(restoreFinancialAccount).mockResolvedValue({ ok: true, value: account });

    await expect(archiveAccountAction(form({ id: "account-id" }))).resolves.toEqual({
      ok: true,
      message: "Financial account archived.",
    });
    await expect(restoreAccountAction(form({ id: "account-id" }))).resolves.toEqual({
      ok: true,
      message: "Financial account restored.",
    });
    expect(archiveFinancialAccount).toHaveBeenCalledWith("backend-token", "account-id");
    expect(restoreFinancialAccount).toHaveBeenCalledWith("backend-token", "account-id");
  });

  it("maps duplicate names to a plain-language failure without revalidating", async () => {
    signedIn();
    vi.mocked(createFinancialAccount).mockResolvedValue({
      ok: false,
      kind: "duplicate_name",
    });

    const result = await createAccountAction(form({ display_name: "Cheque" }));

    expect(result).toEqual({
      ok: false,
      message: "A category or tag with that name already exists.",
      errorKind: "duplicate_name",
    });
    expect(revalidatePath).not.toHaveBeenCalled();
  });

  it("returns a session-expired failure when there is no backend token", async () => {
    vi.mocked(auth).mockResolvedValue(null as never);

    const result = await createAccountAction(form({ display_name: "Cheque" }));

    expect(result).toEqual({
      ok: false,
      message: "Your session has expired. Please sign in again.",
      errorKind: "unavailable",
    });
    expect(createFinancialAccount).not.toHaveBeenCalled();
  });

  it("passes the kind confirmation checkbox through", async () => {
    signedIn();
    vi.mocked(updateCategory).mockResolvedValue({ ok: true, value: category });

    await updateCategoryAction(
      form({
        id: "category-id",
        display_name: "Groceries",
        kind: "Income",
        confirm_kind_change: "true",
      }),
    );
    expect(vi.mocked(updateCategory).mock.calls[0][2]).toEqual({
      display_name: "Groceries",
      kind: "Income",
      confirm_kind_change: true,
    });

    await updateCategoryAction(
      form({ id: "category-id", display_name: "Groceries", kind: "Income" }),
    );
    expect(vi.mocked(updateCategory).mock.calls[1][2]).toEqual({
      display_name: "Groceries",
      kind: "Income",
      confirm_kind_change: false,
    });
  });

  it("rejects an invalid category kind without calling the backend", async () => {
    signedIn();

    const result = await createCategoryAction(form({ display_name: "Coffee", kind: "Bogus" }));

    expect(result).toEqual({
      ok: false,
      message: "That name or value could not be accepted.",
      errorKind: "invalid_request",
    });
    expect(createCategory).not.toHaveBeenCalled();
  });

  it("reports how many transactions a category delete reassigns", async () => {
    signedIn();
    vi.mocked(deleteCategory).mockResolvedValue({
      ok: true,
      value: {
        deleted: true,
        reassigned_transaction_count: 2,
        uncategorised_category_id: "uncategorised-id",
      },
    });

    const result = await deleteCategoryAction(form({ id: "category-id" }));

    expect(result).toEqual({
      ok: true,
      message: "2 transactions moved to Uncategorised.",
    });
    expect(revalidatePath).toHaveBeenCalledWith("/settings");
  });

  it("reports how many transactions a tag delete unassigns", async () => {
    signedIn();
    vi.mocked(deleteMerchantTag).mockResolvedValue({
      ok: true,
      value: { deleted: true, unassigned_transaction_count: 3 },
    });

    const result = await deleteMerchantTagAction(form({ id: "tag-id" }));

    expect(result).toEqual({
      ok: true,
      message: "Tag removed from 3 transactions.",
    });
  });
});
