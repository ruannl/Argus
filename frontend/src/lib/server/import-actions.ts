"use server";

import "server-only";

import { revalidatePath } from "next/cache";

import { auth } from "@/auth";
import {
  abandonImportReview,
  commitImportReview,
  deleteImportStatement,
  patchImportReview,
  refreshImportReview,
  setImportVisibility,
} from "@/lib/server/backend-client";
import type {
  ImportErrorKind,
  ImportRequestResult,
  ImportReviewDecision,
  ImportReviewSummary,
  ImportStatementSummary,
} from "@/lib/server/backend-client";

export type ImportActionResult = {
  ok: boolean;
  message: string;
  errorKind?: ImportErrorKind;
  existingImportIds?: string[];
  review?: ImportReviewSummary;
  statement?: ImportStatementSummary;
  statementId?: string;
  replayed?: boolean;
  deletedTransactionCount?: number;
};

const SESSION_EXPIRED_MESSAGE = "Your session has expired. Please sign in again.";

const ERROR_MESSAGES: Record<ImportErrorKind, string> = {
  invalid_request: "That request could not be accepted. Refresh the review and try again.",
  file_too_large: "That statement is larger than the 10 MB limit.",
  unsupported_format: "That file type is not supported. Use an FNB CSV/ZIP or a mapped CSV.",
  unsupported_encoding: "That CSV is not valid UTF-8. Re-export it as UTF-8 and try again.",
  unsafe_archive: "That archive is unsafe or contains something other than one root-level CSV.",
  encrypted_pdf: "That PDF is password-protected. Import a text-based PDF without encryption.",
  text_pdf_required: "That PDF has no text layer. Scanned statements are outside the MVP.",
  unsupported_layout: "That statement layout is not recognised. No records were changed.",
  processing_limit: "That statement exceeds a processing limit. No records were changed.",
  format_confirmation_required: "Confirm the format mapping and statement dates before continuing.",
  pdf_import_deferred: "PDF import is coming later. Use an FNB CSV/ZIP or a mapped CSV for now.",
  import_busy: "Another statement is being processed. Try again in a moment.",
  not_found: "That item no longer exists. Refresh the page and try again.",
  review_expired: "That review has expired. Upload the statement again to prepare a new review.",
  import_deleted: "That import was deleted. Its old operation cannot be replayed.",
  review_stale: "The account changed while you reviewed. Refresh the review before committing.",
  version_conflict: "The review changed in another tab. Refresh before saving.",
  statement_repeat:
    "This statement was already imported. Explicitly confirm the repeat to continue.",
  idempotency_conflict: "That operation was already used for different content.",
  account_archived: "That financial account is archived. Restore it in Settings before importing.",
  identity_conflict: "An identity check failed. No records were changed.",
  tag_unavailable: "A selected tag was deleted or archived. Choose it again or remove it.",
  category_unavailable:
    "A selected category was deleted, archived, or is not selectable. Choose it again or clear it.",
  no_included_rows:
    "Nothing is included. Skip invalid rows only after including at least one valid row.",
  invalid_rows: "Invalid rows must be marked as skipped before committing.",
  duplicate_decision_required: "Resolve every repeated-row group before committing.",
  rate_limited: "Too many import requests. Try again in a few minutes.",
  unavailable: "Something went wrong. Please try again later.",
};

function failure(kind: ImportErrorKind, existingImportIds?: string[]): ImportActionResult {
  return {
    ok: false,
    message: ERROR_MESSAGES[kind],
    errorKind: kind,
    existingImportIds,
  };
}

function failureFrom(result: ImportRequestResult<unknown>): ImportActionResult {
  return result.ok ? failure("unavailable") : failure(result.kind, result.existingImportIds);
}

function revalidateWorkspace() {
  for (const path of ["/imports", "/home", "/transactions", "/settings"]) {
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

export async function patchImportReviewAction(
  reviewId: string,
  expectedReviewVersion: number,
  decisions: ImportReviewDecision[],
  statementOverride?: boolean,
): Promise<ImportActionResult> {
  const token = await sessionToken();
  if (!token) {
    return { ...failure("unavailable"), message: SESSION_EXPIRED_MESSAGE };
  }
  try {
    const result = await patchImportReview(
      token,
      reviewId,
      expectedReviewVersion,
      decisions,
      statementOverride,
    );
    if (!result.ok) return failureFrom(result);
    revalidateWorkspace();
    return { ok: true, message: "Review updated.", review: result.value };
  } catch {
    return failure("unavailable");
  }
}

export async function refreshImportReviewAction(
  reviewId: string,
  expectedReviewVersion: number,
): Promise<ImportActionResult> {
  const token = await sessionToken();
  if (!token) {
    return { ...failure("unavailable"), message: SESSION_EXPIRED_MESSAGE };
  }
  try {
    const result = await refreshImportReview(token, reviewId, expectedReviewVersion);
    if (!result.ok) return failureFrom(result);
    revalidateWorkspace();
    return {
      ok: true,
      message: "Duplicates refreshed. Confirm the updated review before committing.",
      review: result.value,
    };
  } catch {
    return failure("unavailable");
  }
}

export async function commitImportReviewAction(
  reviewId: string,
  expectedReviewVersion: number,
  confirmedSummaryHash: string,
): Promise<ImportActionResult> {
  const token = await sessionToken();
  if (!token) {
    return { ...failure("unavailable"), message: SESSION_EXPIRED_MESSAGE };
  }
  try {
    const result = await commitImportReview(
      token,
      reviewId,
      expectedReviewVersion,
      confirmedSummaryHash,
    );
    if (!result.ok) return failureFrom(result);
    revalidateWorkspace();
    return {
      ok: true,
      message: result.value.replayed
        ? "This import was already committed. Showing the existing statement."
        : "Statement imported.",
      statementId: result.value.statement.id,
      statement: result.value.statement,
      replayed: result.value.replayed,
    };
  } catch {
    return failure("unavailable");
  }
}

export async function abandonImportReviewAction(
  reviewId: string,
  expectedReviewVersion: number,
): Promise<ImportActionResult> {
  const token = await sessionToken();
  if (!token) {
    return { ...failure("unavailable"), message: SESSION_EXPIRED_MESSAGE };
  }
  try {
    const result = await abandonImportReview(token, reviewId, expectedReviewVersion);
    if (!result.ok) return failureFrom(result);
    revalidateWorkspace();
    return {
      ok: true,
      message: result.value.committed
        ? "This review was already committed. Showing the existing statement."
        : "Review abandoned. No import was created.",
      statementId: result.value.statement?.id,
      statement: result.value.statement ?? undefined,
    };
  } catch {
    return failure("unavailable");
  }
}

export async function setImportVisibilityAction(
  statementId: string,
  hidden: boolean,
  expectedVersion: number,
  expectedTransactionCount: number,
): Promise<ImportActionResult> {
  const token = await sessionToken();
  if (!token) {
    return { ...failure("unavailable"), message: SESSION_EXPIRED_MESSAGE };
  }
  try {
    const result = await setImportVisibility(
      token,
      statementId,
      hidden,
      expectedVersion,
      expectedTransactionCount,
    );
    if (!result.ok) return failureFrom(result);
    revalidateWorkspace();
    return {
      ok: true,
      message: hidden ? "Statement hidden." : "Statement restored.",
      statement: result.value,
    };
  } catch {
    return failure("unavailable");
  }
}

export async function deleteImportStatementAction(
  statementId: string,
  expectedVersion: number,
  expectedTransactionCount: number,
): Promise<ImportActionResult> {
  const token = await sessionToken();
  if (!token) {
    return { ...failure("unavailable"), message: SESSION_EXPIRED_MESSAGE };
  }
  try {
    const result = await deleteImportStatement(
      token,
      statementId,
      expectedVersion,
      expectedTransactionCount,
    );
    if (!result.ok) return failureFrom(result);
    revalidateWorkspace();
    return {
      ok: true,
      message: `Statement deleted with ${result.value.deleted_transaction_count} transaction${
        result.value.deleted_transaction_count === 1 ? "" : "s"
      }.`,
      statementId,
      deletedTransactionCount: result.value.deleted_transaction_count,
    };
  } catch {
    return failure("unavailable");
  }
}
