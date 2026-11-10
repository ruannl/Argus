"use server";

import "server-only";

import { revalidatePath } from "next/cache";

import { auth } from "@/auth";
import {
  abandonTransactionOperation,
  applyTransactionOperation,
  getTransaction,
  getTransactionOperation,
  patchTransaction,
  prepareTransactionOperation,
  queryTransactions,
} from "@/lib/server/backend-client";
import type {
  TransactionApplyResult,
  TransactionChanges,
  TransactionDetail,
  TransactionErrorDetails,
  TransactionErrorKind,
  TransactionFilters,
  TransactionMutationResult,
  TransactionOperation,
  TransactionPage,
  TransactionPrepareInput,
  TransactionPrepareResult,
  TransactionQuery,
  TransactionRequestResult,
  TransactionRow,
} from "@/lib/server/backend-client";

export type TransactionActionResult<T> =
  | { ok: true; value: T }
  | {
      ok: false;
      kind: TransactionErrorKind;
      message: string;
      details?: TransactionErrorDetails;
    };

const SESSION_EXPIRED_MESSAGE = "Your session has expired. Please sign in again.";

const ERROR_MESSAGES: Record<TransactionErrorKind, string> = {
  invalid_request: "That request could not be accepted. Check the values and try again.",
  empty_selection: "No transactions match that selection.",
  category_unavailable: "That category is archived or no longer available.",
  tag_unavailable: "That tag is archived or no longer available for adding.",
  tag_limit_exceeded: "A transaction cannot have more than 100 tags.",
  selection_too_large:
    "Too many transactions match. Narrow the filters before selecting all matches.",
  request_too_large: "That request was too large. Narrow the change and try again.",
  version_conflict:
    "This transaction changed since you loaded it. Review the latest values before saving again.",
  selection_stale:
    "The selected transactions changed since confirmation. Nothing was applied; refresh and prepare again.",
  classification_changed:
    "A chosen category or tag changed since preparation. Nothing was applied; prepare again.",
  parent_hidden:
    "The parent statement is hidden. Restore it in Imports before restoring these transactions.",
  idempotency_conflict:
    "That confirmation no longer matches its saved operation. Start a new preparation.",
  operation_busy: "The workspace is busy. Retry the same operation in a moment.",
  operation_expired: "This confirmation expired. Nothing was applied.",
  operation_abandoned: "This confirmation was cancelled. Nothing was applied.",
  operation_quota:
    "Too many confirmations are waiting. Cancel one or wait for it to expire before preparing another.",
  not_found: "That transaction no longer exists in your workspace.",
  rate_limited: "Too many requests. Please try again in a few minutes.",
  unauthorized: SESSION_EXPIRED_MESSAGE,
  unavailable: "Something went wrong. Please try again later.",
};

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

function isUuid(value: unknown): value is string {
  return typeof value === "string" && UUID_PATTERN.test(value);
}

function isDate(value: unknown): value is string {
  if (typeof value !== "string" || !DATE_PATTERN.test(value)) {
    return false;
  }
  const parsed = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}

function validFilters(value: unknown): value is TransactionFilters {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const filters = value as Record<string, unknown>;
  const allowed = new Set([
    "search",
    "start_date",
    "end_date",
    "financial_account_id",
    "statement_import_id",
    "category_id",
    "merchant_tag",
    "category_confirmed",
    "category_kind",
    "direction",
    "visibility",
  ]);
  if (Object.keys(filters).some((key) => !allowed.has(key))) {
    return false;
  }
  if (filters.search !== undefined) {
    if (typeof filters.search !== "string") {
      return false;
    }
    const trimmed = filters.search.trim();
    if (Array.from(trimmed).length > 200) {
      return false;
    }
  }
  if (filters.start_date !== undefined && !isDate(filters.start_date)) {
    return false;
  }
  if (filters.end_date !== undefined && !isDate(filters.end_date)) {
    return false;
  }
  if (
    typeof filters.start_date === "string" &&
    typeof filters.end_date === "string" &&
    filters.start_date > filters.end_date
  ) {
    return false;
  }
  for (const key of ["financial_account_id", "statement_import_id", "category_id"]) {
    if (filters[key] !== undefined && !isUuid(filters[key])) {
      return false;
    }
  }
  if (
    filters.merchant_tag !== undefined &&
    filters.merchant_tag !== "untagged" &&
    !isUuid(filters.merchant_tag)
  ) {
    return false;
  }
  if (
    filters.category_kind !== undefined &&
    !["Income", "Expense", "Transfer", "Excluded", "Uncategorised"].includes(
      filters.category_kind as string,
    )
  ) {
    return false;
  }
  if (
    filters.direction !== undefined &&
    !["in", "out", "zero"].includes(filters.direction as string)
  ) {
    return false;
  }
  if (filters.category_confirmed !== undefined && typeof filters.category_confirmed !== "boolean") {
    return false;
  }
  if (
    filters.visibility !== undefined &&
    !["visible", "hidden", "all"].includes(filters.visibility as string)
  ) {
    return false;
  }
  return true;
}

function validQuery(value: unknown): value is TransactionQuery {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const query = value as Record<string, unknown>;
  const allowed = new Set(["filters", "page", "page_size", "sort_by", "sort_direction"]);
  if (Object.keys(query).some((key) => !allowed.has(key))) {
    return false;
  }
  if (query.filters !== undefined && !validFilters(query.filters)) {
    return false;
  }
  if (query.page !== undefined && (!Number.isInteger(query.page) || (query.page as number) < 1)) {
    return false;
  }
  if (query.page_size !== undefined && ![25, 50, 100].includes(query.page_size as number)) {
    return false;
  }
  if (
    query.sort_by !== undefined &&
    !["date", "amount", "description"].includes(query.sort_by as string)
  ) {
    return false;
  }
  if (
    query.sort_direction !== undefined &&
    !["asc", "desc"].includes(query.sort_direction as string)
  ) {
    return false;
  }
  return true;
}

function validChanges(value: unknown): value is TransactionChanges {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const changes = value as Record<string, unknown>;
  const allowed = new Set([
    "category_id",
    "add_tag_ids",
    "remove_tag_ids",
    "is_hidden_by_user",
    "confirm_category",
  ]);
  if (Object.keys(changes).some((key) => !allowed.has(key))) {
    return false;
  }
  if (changes.category_id !== undefined && !isUuid(changes.category_id)) {
    return false;
  }
  if (changes.is_hidden_by_user !== undefined && typeof changes.is_hidden_by_user !== "boolean") {
    return false;
  }
  if (changes.confirm_category !== undefined && changes.confirm_category !== true) {
    return false;
  }
  if (changes.confirm_category === true && changes.category_id !== undefined) {
    return false;
  }
  const add = changes.add_tag_ids;
  const remove = changes.remove_tag_ids;
  for (const valueArray of [add, remove]) {
    if (valueArray === undefined) {
      continue;
    }
    if (!Array.isArray(valueArray) || valueArray.length > 100) {
      return false;
    }
    if (!valueArray.every(isUuid) || new Set(valueArray).size !== valueArray.length) {
      return false;
    }
  }
  if (Array.isArray(add) && Array.isArray(remove)) {
    const removeSet = new Set(remove);
    if (add.some((id) => removeSet.has(id))) {
      return false;
    }
  }
  return (
    changes.category_id !== undefined ||
    changes.is_hidden_by_user !== undefined ||
    changes.confirm_category === true ||
    (Array.isArray(add) && add.length > 0) ||
    (Array.isArray(remove) && remove.length > 0)
  );
}

function failure(
  kind: TransactionErrorKind,
  details?: TransactionErrorDetails,
): { ok: false; kind: TransactionErrorKind; message: string; details?: TransactionErrorDetails } {
  return { ok: false, kind, message: ERROR_MESSAGES[kind], details };
}

function fromFailure<T>(result: Extract<TransactionRequestResult<T>, { ok: false }>) {
  return failure(result.kind, result.details);
}

async function sessionToken(): Promise<string | null> {
  try {
    return (await auth())?.user?.backendToken ?? null;
  } catch {
    return null;
  }
}

function revalidateWorkspace(transactionId?: string) {
  for (const path of ["/transactions", "/imports", "/settings", "/home"]) {
    revalidatePath(path);
  }
  if (transactionId) {
    revalidatePath(`/transactions/${transactionId}`);
  }
}

// --- Reads -----------------------------------------------------------------

export async function queryTransactionsAction(
  query: TransactionQuery,
): Promise<TransactionActionResult<TransactionPage>> {
  if (!validQuery(query)) {
    return failure("invalid_request");
  }
  const token = await sessionToken();
  if (!token) {
    return failure("unauthorized");
  }
  try {
    const result = await queryTransactions(token, query);
    return result.ok ? result : fromFailure(result);
  } catch {
    return failure("unavailable");
  }
}

export async function loadTransactionAction(
  id: string,
): Promise<TransactionActionResult<TransactionDetail>> {
  if (!isUuid(id)) {
    return failure("not_found");
  }
  const token = await sessionToken();
  if (!token) {
    return failure("unauthorized");
  }
  try {
    const result = await getTransaction(token, id);
    return result.ok ? result : fromFailure(result);
  } catch {
    return failure("unavailable");
  }
}

export async function loadOperationAction(
  operationId: string,
): Promise<TransactionActionResult<TransactionOperation>> {
  if (!isUuid(operationId)) {
    return failure("invalid_request");
  }
  const token = await sessionToken();
  if (!token) {
    return failure("unauthorized");
  }
  try {
    const result = await getTransactionOperation(token, operationId);
    return result.ok ? result : fromFailure(result);
  } catch {
    return failure("unavailable");
  }
}

// --- Writes ----------------------------------------------------------------

export async function saveTransactionAction(input: {
  id: string;
  expected_version: number;
  changes: TransactionChanges;
  confirm?: boolean;
}): Promise<TransactionActionResult<TransactionMutationResult>> {
  if (
    typeof input !== "object" ||
    input === null ||
    !isUuid(input.id) ||
    !Number.isInteger(input.expected_version) ||
    input.expected_version < 1 ||
    !validChanges(input.changes) ||
    (input.changes.is_hidden_by_user !== undefined && input.confirm !== true)
  ) {
    return failure("invalid_request");
  }
  const token = await sessionToken();
  if (!token) {
    return failure("unauthorized");
  }
  try {
    const result = await patchTransaction(token, input.id, {
      expected_version: input.expected_version,
      changes: input.changes,
      confirm: input.confirm,
    });
    if (!result.ok) {
      return fromFailure(result);
    }
    revalidateWorkspace(input.id);
    return result;
  } catch {
    return failure("unavailable");
  }
}

export async function prepareTransactionsAction(
  input: TransactionPrepareInput,
): Promise<TransactionActionResult<TransactionPrepareResult>> {
  if (
    typeof input !== "object" ||
    input === null ||
    !isUuid(input.operation_id) ||
    !validChanges(input.changes) ||
    typeof input.selection !== "object" ||
    input.selection === null
  ) {
    return failure("invalid_request");
  }

  const selection = input.selection;
  if (selection.mode === "explicit") {
    if (
      !Array.isArray(selection.transactions) ||
      selection.transactions.length === 0 ||
      selection.transactions.length > 1000 ||
      selection.transactions.some(
        (member) =>
          !isUuid(member.id) ||
          !Number.isInteger(member.expected_version) ||
          member.expected_version < 1,
      ) ||
      new Set(selection.transactions.map((member) => member.id)).size !==
        selection.transactions.length
    ) {
      return failure("invalid_request");
    }
  } else if (selection.mode === "all_matching") {
    if (!validFilters(selection.filters)) {
      return failure("invalid_request");
    }
  } else {
    return failure("invalid_request");
  }

  const token = await sessionToken();
  if (!token) {
    return failure("unauthorized");
  }
  try {
    const result = await prepareTransactionOperation(token, input);
    return result.ok ? result : fromFailure(result);
  } catch {
    return failure("unavailable");
  }
}

export async function applyOperationAction(
  operationId: string,
  confirmationHash: string,
  expectedSelectedCount: number,
): Promise<TransactionActionResult<TransactionApplyResult>> {
  if (
    !isUuid(operationId) ||
    typeof confirmationHash !== "string" ||
    confirmationHash.length === 0 ||
    confirmationHash.length > 256 ||
    !Number.isInteger(expectedSelectedCount) ||
    expectedSelectedCount < 1
  ) {
    return failure("invalid_request");
  }
  const token = await sessionToken();
  if (!token) {
    return failure("unauthorized");
  }
  try {
    const result = await applyTransactionOperation(
      token,
      operationId,
      confirmationHash,
      expectedSelectedCount,
    );
    if (!result.ok) {
      return fromFailure(result);
    }
    revalidateWorkspace();
    return result;
  } catch {
    return failure("unavailable");
  }
}

export async function abandonOperationAction(
  operationId: string,
): Promise<TransactionActionResult<TransactionApplyResult>> {
  if (!isUuid(operationId)) {
    return failure("invalid_request");
  }
  const token = await sessionToken();
  if (!token) {
    return failure("unauthorized");
  }
  try {
    const result = await abandonTransactionOperation(token, operationId);
    return result.ok ? result : fromFailure(result);
  } catch {
    return failure("unavailable");
  }
}

// Exported for the workspace's typed helpers.
export type {
  TransactionChanges,
  TransactionDetail,
  TransactionOperation,
  TransactionPage,
  TransactionRow,
};
