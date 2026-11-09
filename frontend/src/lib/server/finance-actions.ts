"use server";

import "server-only";

import { revalidatePath } from "next/cache";

import { auth } from "@/auth";
import {
  archiveCategory,
  archiveFinancialAccount,
  archiveMerchantTag,
  createCategory,
  createFinancialAccount,
  createMerchantTag,
  deleteCategory,
  deleteMerchantTag,
  restoreCategory,
  restoreFinancialAccount,
  restoreMerchantTag,
  updateCategory,
  updateFinancialAccount,
  updateMerchantTag,
} from "@/lib/server/backend-client";
import type {
  FinanceRequestErrorKind,
  FinanceRequestResult,
  MerchantTag,
  UserCategoryKind,
} from "@/lib/server/backend-client";

export type FinanceActionResult<T = unknown> = {
  ok: boolean;
  message: string;
  errorKind?: FinanceRequestErrorKind;
  value?: T;
};

const SESSION_EXPIRED_MESSAGE = "Your session has expired. Please sign in again.";

const ERROR_MESSAGES: Record<FinanceRequestErrorKind, string> = {
  invalid_request: "That name or value could not be accepted.",
  duplicate_name: "A category or tag with that name already exists.",
  system_category_immutable: "The Uncategorised system bucket cannot be changed.",
  kind_change_confirmation_required: "Confirm the category kind change before saving.",
  not_found: "That item no longer exists. Refresh the page and try again.",
  rate_limited: "Too many requests. Please try again in a few minutes.",
  unavailable: "Something went wrong. Please try again later.",
};

function failure<T = unknown>(kind: FinanceRequestErrorKind): FinanceActionResult<T> {
  return { ok: false, message: ERROR_MESSAGES[kind], errorKind: kind };
}

function success(message: string): FinanceActionResult {
  return { ok: true, message };
}

function revalidateWorkspace() {
  for (const path of ["/settings", "/home", "/transactions", "/imports"]) {
    revalidatePath(path);
  }
}

async function sessionToken(): Promise<string | null> {
  try {
    return (await auth())?.user?.backendToken ?? null;
  } catch {
    return null;
  }
}

function formString(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" ? value : null;
}

function formChecked(formData: FormData, name: string): boolean {
  const value = formData.get(name);
  return value === "true" || value === "on" || value === "1";
}

function isUserCategoryKind(value: string | null): value is UserCategoryKind {
  return value === "Expense" || value === "Income" || value === "Transfer" || value === "Excluded";
}

async function withSession<T>(
  run: (token: string) => Promise<FinanceRequestResult<T>>,
  successMessage: string,
  includeValue = false,
): Promise<FinanceActionResult<T>> {
  const token = await sessionToken();
  if (!token) {
    return { ok: false, message: SESSION_EXPIRED_MESSAGE, errorKind: "unavailable" };
  }

  try {
    const result = await run(token);
    if (!result.ok) {
      return failure<T>(result.kind);
    }
    revalidateWorkspace();
    return includeValue
      ? { ok: true, message: successMessage, value: result.value }
      : { ok: true, message: successMessage };
  } catch {
    return failure<T>("unavailable");
  }
}

// --- Financial accounts ----------------------------------------------------

export async function createAccountAction(formData: FormData): Promise<FinanceActionResult> {
  const displayName = formString(formData, "display_name");
  if (displayName === null) {
    return failure("invalid_request");
  }

  const institution = formString(formData, "institution");
  const input =
    institution === null || institution.trim() === ""
      ? { display_name: displayName }
      : { display_name: displayName, institution };

  return withSession((token) => createFinancialAccount(token, input), "Financial account created.");
}

export async function renameAccountAction(formData: FormData): Promise<FinanceActionResult> {
  const id = formString(formData, "id");
  const displayName = formString(formData, "display_name");
  if (!id || displayName === null) {
    return failure("invalid_request");
  }

  // Institution is deliberately omitted so the stored value is preserved.
  return withSession(
    (token) => updateFinancialAccount(token, id, { display_name: displayName }),
    "Financial account renamed.",
  );
}

export async function updateAccountInstitutionAction(
  formData: FormData,
): Promise<FinanceActionResult> {
  const id = formString(formData, "id");
  const displayName = formString(formData, "display_name");
  const institution = formString(formData, "institution");
  if (!id || displayName === null || institution === null) {
    return failure("invalid_request");
  }

  // An empty institution field is an explicit clear.
  const value = institution.trim() === "" ? null : institution;

  return withSession(
    (token) => updateFinancialAccount(token, id, { display_name: displayName, institution: value }),
    "Institution updated.",
  );
}

export async function archiveAccountAction(formData: FormData): Promise<FinanceActionResult> {
  const id = formString(formData, "id");
  if (!id) {
    return failure("invalid_request");
  }
  return withSession((token) => archiveFinancialAccount(token, id), "Financial account archived.");
}

export async function restoreAccountAction(formData: FormData): Promise<FinanceActionResult> {
  const id = formString(formData, "id");
  if (!id) {
    return failure("invalid_request");
  }
  return withSession((token) => restoreFinancialAccount(token, id), "Financial account restored.");
}

// --- Categories ------------------------------------------------------------

export async function createCategoryAction(formData: FormData): Promise<FinanceActionResult> {
  const displayName = formString(formData, "display_name");
  const kind = formString(formData, "kind");
  if (displayName === null || !isUserCategoryKind(kind)) {
    return failure("invalid_request");
  }

  return withSession(
    (token) => createCategory(token, { display_name: displayName, kind }),
    "Category created.",
  );
}

export async function updateCategoryAction(formData: FormData): Promise<FinanceActionResult> {
  const id = formString(formData, "id");
  const displayName = formString(formData, "display_name");
  const kind = formString(formData, "kind");
  if (!id || displayName === null || !isUserCategoryKind(kind)) {
    return failure("invalid_request");
  }

  return withSession(
    (token) =>
      updateCategory(token, id, {
        display_name: displayName,
        kind,
        confirm_kind_change: formChecked(formData, "confirm_kind_change"),
      }),
    "Category updated.",
  );
}

export async function archiveCategoryAction(formData: FormData): Promise<FinanceActionResult> {
  const id = formString(formData, "id");
  if (!id) {
    return failure("invalid_request");
  }
  return withSession((token) => archiveCategory(token, id), "Category archived.");
}

export async function restoreCategoryAction(formData: FormData): Promise<FinanceActionResult> {
  const id = formString(formData, "id");
  if (!id) {
    return failure("invalid_request");
  }
  return withSession((token) => restoreCategory(token, id), "Category restored.");
}

export async function deleteCategoryAction(formData: FormData): Promise<FinanceActionResult> {
  const id = formString(formData, "id");
  if (!id) {
    return failure("invalid_request");
  }

  const token = await sessionToken();
  if (!token) {
    return { ok: false, message: SESSION_EXPIRED_MESSAGE, errorKind: "unavailable" };
  }

  try {
    const result = await deleteCategory(token, id);
    if (!result.ok) {
      return failure(result.kind);
    }
    revalidateWorkspace();
    const count = result.value.reassigned_transaction_count;
    return success(
      `${count} ${count === 1 ? "transaction" : "transactions"} moved to Uncategorised.`,
    );
  } catch {
    return failure("unavailable");
  }
}

// --- Merchant tags ---------------------------------------------------------

export async function createMerchantTagAction(
  formData: FormData,
): Promise<FinanceActionResult<MerchantTag>> {
  const displayName = formString(formData, "display_name");
  if (displayName === null) {
    return failure<MerchantTag>("invalid_request");
  }
  return withSession((token) => createMerchantTag(token, displayName), "Tag created.", true);
}

export async function updateMerchantTagAction(formData: FormData): Promise<FinanceActionResult> {
  const id = formString(formData, "id");
  const displayName = formString(formData, "display_name");
  if (!id || displayName === null) {
    return failure("invalid_request");
  }
  return withSession((token) => updateMerchantTag(token, id, displayName), "Merchant tag renamed.");
}

export async function archiveMerchantTagAction(formData: FormData): Promise<FinanceActionResult> {
  const id = formString(formData, "id");
  if (!id) {
    return failure("invalid_request");
  }
  return withSession((token) => archiveMerchantTag(token, id), "Merchant tag archived.");
}

export async function restoreMerchantTagAction(formData: FormData): Promise<FinanceActionResult> {
  const id = formString(formData, "id");
  if (!id) {
    return failure("invalid_request");
  }
  return withSession((token) => restoreMerchantTag(token, id), "Merchant tag restored.");
}

export async function deleteMerchantTagAction(formData: FormData): Promise<FinanceActionResult> {
  const id = formString(formData, "id");
  if (!id) {
    return failure("invalid_request");
  }

  const token = await sessionToken();
  if (!token) {
    return { ok: false, message: SESSION_EXPIRED_MESSAGE, errorKind: "unavailable" };
  }

  try {
    const result = await deleteMerchantTag(token, id);
    if (!result.ok) {
      return failure(result.kind);
    }
    revalidateWorkspace();
    const count = result.value.unassigned_transaction_count;
    return success(`Tag removed from ${count} ${count === 1 ? "transaction" : "transactions"}.`);
  } catch {
    return failure("unavailable");
  }
}
