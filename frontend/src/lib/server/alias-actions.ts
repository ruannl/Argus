"use server";

import "server-only";

import { revalidatePath } from "next/cache";

import { auth } from "@/auth";
import {
  createMerchantAlias,
  deleteMerchantAlias,
  updateMerchantAlias,
} from "@/lib/server/backend-client";
import type {
  FinanceRequestErrorKind,
  FinanceRequestResult,
  MerchantAlias,
  MerchantAliasInput,
} from "@/lib/server/backend-client";

export type AliasActionResult<T = unknown> = {
  ok: boolean;
  message: string;
  errorKind?: FinanceRequestErrorKind;
  value?: T;
};

const SESSION_EXPIRED_MESSAGE = "Your session has expired. Please sign in again.";

const ERROR_MESSAGES: Record<FinanceRequestErrorKind, string> = {
  invalid_request: "That alias could not be accepted. Choose a name and an active tag.",
  duplicate_name: "An alias with that name already exists.",
  system_category_immutable: "The Uncategorised system bucket cannot be changed.",
  kind_change_confirmation_required: "Confirm the category kind change before saving.",
  not_found: "That alias, tag, or category no longer exists. Refresh the page and try again.",
  rate_limited: "Too many requests. Please try again in a few minutes.",
  unavailable: "Something went wrong. Please try again later.",
};

function failure<T = unknown>(kind: FinanceRequestErrorKind): AliasActionResult<T> {
  return { ok: false, message: ERROR_MESSAGES[kind], errorKind: kind };
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

function aliasInput(formData: FormData): MerchantAliasInput | null {
  const displayAlias = formString(formData, "display_alias");
  const merchantTagId = formString(formData, "merchant_tag_id");
  const categoryId = formString(formData, "category_id");
  if (displayAlias === null || merchantTagId === null) {
    return null;
  }

  return {
    display_alias: displayAlias,
    merchant_tag_id: merchantTagId,
    category_id: categoryId === null || categoryId.trim() === "" ? null : categoryId,
  };
}

function revalidateSuggestions() {
  for (const path of ["/settings", "/transactions", "/imports"]) {
    revalidatePath(path);
  }
}

async function withAliasSession<T>(
  run: (token: string) => Promise<FinanceRequestResult<T>>,
  successMessage: string,
  includeValue = false,
): Promise<AliasActionResult<T>> {
  const token = await sessionToken();
  if (!token) {
    return { ok: false, message: SESSION_EXPIRED_MESSAGE, errorKind: "unavailable" };
  }

  try {
    const result = await run(token);
    if (!result.ok) {
      return failure<T>(result.kind);
    }
    revalidateSuggestions();
    return includeValue
      ? { ok: true, message: successMessage, value: result.value }
      : { ok: true, message: successMessage };
  } catch {
    return failure<T>("unavailable");
  }
}

export async function createAliasAction(
  formData: FormData,
): Promise<AliasActionResult<MerchantAlias>> {
  const input = aliasInput(formData);
  if (input === null) {
    return failure<MerchantAlias>("invalid_request");
  }

  return withAliasSession((token) => createMerchantAlias(token, input), "Alias created.", true);
}

export async function updateAliasAction(
  formData: FormData,
): Promise<AliasActionResult<MerchantAlias>> {
  const id = formString(formData, "id");
  const input = aliasInput(formData);
  if (!id || input === null) {
    return failure<MerchantAlias>("invalid_request");
  }

  return withAliasSession((token) => updateMerchantAlias(token, id, input), "Alias updated.", true);
}

export async function deleteAliasAction(formData: FormData): Promise<AliasActionResult> {
  const id = formString(formData, "id");
  if (!id) {
    return failure("invalid_request");
  }

  return withAliasSession((token) => deleteMerchantAlias(token, id), "Alias deleted.");
}
