import "server-only";

import { PRODUCT_TOKEN } from "@/lib/brand";

const APPLICATION_SECRET_HEADER = `X-${PRODUCT_TOKEN}-Application-Secret`;
const SESSION_HEADER = `X-${PRODUCT_TOKEN}-Session`;
const FRONTEND_ORIGIN_HEADER = `X-${PRODUCT_TOKEN}-Frontend-Origin`;
const REQUEST_ID_HEADER = "X-Request-Id";

function newRequestId(): string {
  return crypto.randomUUID();
}

export type BackendHealth = {
  status: string;
};

export type CurrentAccount = {
  id: string;
  email: string;
  email_verified: boolean;
  subscriber_active: boolean;
  subscription_started_at: string | null;
  next_billing_at: string | null;
  lifetime: boolean;
  admin: boolean;
};

export type AccountReadErrorKind = "unauthorized" | "rate_limited" | "unavailable";

/** Sanitized /v1/me failure; never includes a backend body, URL or token. */
export class AccountReadError extends Error {
  readonly kind: AccountReadErrorKind;

  constructor(kind: AccountReadErrorKind) {
    super("Account check failed.");
    this.name = "AccountReadError";
    this.kind = kind;
  }
}

function isCurrentAccount(value: unknown): value is CurrentAccount {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return false;
  }
  const account = value as Record<string, unknown>;
  const keys = [
    "id",
    "email",
    "email_verified",
    "subscriber_active",
    "subscription_started_at",
    "next_billing_at",
    "lifetime",
    "admin",
  ];
  if (keys.some((key) => !Object.prototype.hasOwnProperty.call(account, key))) {
    return false;
  }
  return (
    typeof account.id === "string" &&
    account.id.length > 0 &&
    typeof account.email === "string" &&
    typeof account.email_verified === "boolean" &&
    typeof account.subscriber_active === "boolean" &&
    (account.subscription_started_at === null ||
      typeof account.subscription_started_at === "string") &&
    (account.next_billing_at === null || typeof account.next_billing_at === "string") &&
    typeof account.lifetime === "boolean" &&
    typeof account.admin === "boolean"
  );
}

export type AcceptedResponse = {
  status: "accepted";
};

export type VerifiedResponse = {
  status: "verified";
};

export type PasswordResetResponse = {
  status: "reset";
};

export type LoginResponse = {
  token: string;
  expires_at: string;
};

export type FinancialAccount = {
  id: string;
  display_name: string;
  institution: string | null;
  currency: string;
  is_archived: boolean;
  transaction_count: number;
  created_at: string;
  updated_at: string;
};

export type CategoryKind = "Expense" | "Income" | "Transfer" | "Excluded" | "Uncategorised";

export type UserCategoryKind = Exclude<CategoryKind, "Uncategorised">;

export type Category = {
  id: string;
  display_name: string;
  kind: CategoryKind;
  is_system: boolean;
  is_default: boolean;
  is_archived: boolean;
  transaction_count: number;
  created_at: string;
  updated_at: string;
};

export type MerchantTag = {
  id: string;
  display_name: string;
  is_archived: boolean;
  transaction_count: number;
  created_at: string;
  updated_at: string;
};

export type MerchantAlias = {
  id: string;
  display_alias: string;
  merchant_tag_id: string;
  tag_display_name: string;
  category_id: string | null;
  category_display_name: string | null;
  created_at: string;
  updated_at: string;
};

export type MerchantAliasInput = {
  display_alias: string;
  merchant_tag_id: string;
  category_id: string | null;
};

export type DeleteCategoryResult = {
  deleted: boolean;
  reassigned_transaction_count: number;
  uncategorised_category_id: string;
};

export type DeleteMerchantTagResult = {
  deleted: boolean;
  unassigned_transaction_count: number;
};

export type FinanceRequestErrorKind =
  | "invalid_request"
  | "duplicate_name"
  | "system_category_immutable"
  | "kind_change_confirmation_required"
  | "not_found"
  | "rate_limited"
  | "unavailable";

export type FinanceRequestResult<T> =
  { ok: true; value: T } | { ok: false; kind: FinanceRequestErrorKind };

export type FinancialAccountInput = {
  display_name: string;
  institution?: string | null;
};

export type CategoryInput = {
  display_name: string;
  kind: UserCategoryKind;
};

export type CategoryUpdateInput = CategoryInput & {
  confirm_kind_change?: boolean;
};

export type AccountRequestErrorKind =
  "invalid_request" | "invalid_token" | "invalid_credentials" | "rate_limited" | "unavailable";

type NonLoginAccountRequestErrorKind = Exclude<AccountRequestErrorKind, "invalid_credentials">;

export type AccountRequestResult<T> =
  | { ok: true; value: T }
  | {
      ok: false;
      kind: T extends LoginResponse ? AccountRequestErrorKind : NonLoginAccountRequestErrorKind;
    };

function requiredEnvironment(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`missing server environment: ${name}`);
  }
  return value;
}

function backendUrl(): string {
  return requiredEnvironment("ARGUS_BACKEND_URL").replace(/\/+$/, "");
}

async function readJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    throw new Error("backend request failed");
  }
  return (await response.json()) as T;
}

async function classifyAccountFailure(response: Response): Promise<AccountRequestErrorKind> {
  if (response.status === 429) {
    return "rate_limited";
  }

  if (response.status !== 400 && response.status !== 401) {
    return "unavailable";
  }

  try {
    const body: unknown = await response.json();
    if (typeof body === "object" && body !== null && "error" in body) {
      const error = (body as { error?: unknown }).error;
      if (response.status === 401 && error === "invalid_credentials") {
        return "invalid_credentials";
      }
      if (response.status === 400 && (error === "invalid_request" || error === "invalid_token")) {
        return error;
      }
    }
  } catch {
    // An unparseable failure body is intentionally indistinguishable from an unavailable backend.
  }

  return "unavailable";
}

async function accountRequest<T>(
  path: string,
  body: Record<string, string>,
  successStatus?: number,
): Promise<AccountRequestResult<T>> {
  try {
    const response = await fetch(`${backendUrl()}${path}`, {
      method: "POST",
      headers: trustedHeaders(),
      body: JSON.stringify(body),
      cache: "no-store",
    });

    if (successStatus !== undefined ? response.status !== successStatus : !response.ok) {
      return {
        ok: false,
        kind: await classifyAccountFailure(response),
      } as AccountRequestResult<T>;
    }

    return { ok: true, value: (await response.json()) as T };
  } catch {
    return { ok: false, kind: "unavailable" };
  }
}

function trustedHeaders(): Record<string, string> {
  return {
    "Content-Type": "application/json",
    [REQUEST_ID_HEADER]: newRequestId(),
    [APPLICATION_SECRET_HEADER]: requiredEnvironment("ARGUS_API_SECRET"),
    [FRONTEND_ORIGIN_HEADER]: requiredEnvironment("ARGUS_FRONTEND_URL"),
  };
}

export async function getBackendHealth(): Promise<BackendHealth> {
  const response = await fetch(`${backendUrl()}/health`, {
    method: "GET",
    headers: { [REQUEST_ID_HEADER]: newRequestId() },
    cache: "no-store",
  });
  return readJson<BackendHealth>(response);
}

export async function getCurrentAccount(sessionToken: string): Promise<CurrentAccount> {
  if (!sessionToken) {
    throw new Error("session token is required");
  }

  let response: Response;
  try {
    response = await fetch(`${backendUrl()}/v1/me`, {
      method: "GET",
      headers: {
        [REQUEST_ID_HEADER]: newRequestId(),
        [APPLICATION_SECRET_HEADER]: requiredEnvironment("ARGUS_API_SECRET"),
        [SESSION_HEADER]: sessionToken,
        [FRONTEND_ORIGIN_HEADER]: requiredEnvironment("ARGUS_FRONTEND_URL"),
      },
      cache: "no-store",
    });
  } catch {
    throw new AccountReadError("unavailable");
  }

  if (response.status === 401) {
    let confirmedDenial = false;
    try {
      const body: unknown = await response.json();
      confirmedDenial =
        typeof body === "object" &&
        body !== null &&
        "error" in body &&
        body.error === "unauthorized";
    } catch {
      // An unreadable denial does not establish which response the backend sent.
    }
    throw new AccountReadError(confirmedDenial ? "unauthorized" : "unavailable");
  }
  if (response.status !== 200) {
    throw new AccountReadError(response.status === 429 ? "rate_limited" : "unavailable");
  }

  try {
    const body: unknown = await response.json();
    if (isCurrentAccount(body)) {
      return body;
    }
  } catch {
    // A malformed success response does not establish account authority.
  }
  throw new AccountReadError("unavailable");
}

export async function registerAccount(
  email: string,
  password: string,
): Promise<AccountRequestResult<AcceptedResponse>> {
  return accountRequest<AcceptedResponse>("/v1/register", { email, password });
}

export async function verifyEmail(token: string): Promise<AccountRequestResult<VerifiedResponse>> {
  return accountRequest<VerifiedResponse>("/v1/verify", { token });
}

export async function resendVerification(
  email: string,
): Promise<AccountRequestResult<AcceptedResponse>> {
  return accountRequest<AcceptedResponse>("/v1/verification/resend", { email });
}

export async function requestPasswordReset(
  email: string,
): Promise<AccountRequestResult<AcceptedResponse>> {
  return accountRequest<AcceptedResponse>("/v1/password/reset/request", { email });
}

export async function resetPassword(
  token: string,
  password: string,
): Promise<AccountRequestResult<PasswordResetResponse>> {
  return accountRequest<PasswordResetResponse>("/v1/password/reset", { token, password }, 200);
}

export async function loginAccount(
  email: string,
  password: string,
): Promise<AccountRequestResult<LoginResponse>> {
  return accountRequest<LoginResponse>("/v1/login", { email, password }, 200);
}

export async function logoutSession(sessionToken: string): Promise<AccountRequestResult<null>> {
  if (!sessionToken) {
    throw new Error("session token is required");
  }

  try {
    const response = await fetch(`${backendUrl()}/v1/logout`, {
      method: "POST",
      headers: {
        ...trustedHeaders(),
        [SESSION_HEADER]: sessionToken,
      },
      cache: "no-store",
    });
    if (response.status === 204) {
      return { ok: true, value: null };
    }
    return { ok: false, kind: "unavailable" };
  } catch {
    return { ok: false, kind: "unavailable" };
  }
}

// --- Finance workspace -----------------------------------------------------

function financeHeaders(sessionToken: string, hasBody: boolean): Record<string, string> {
  const headers: Record<string, string> = {
    [REQUEST_ID_HEADER]: newRequestId(),
    [APPLICATION_SECRET_HEADER]: requiredEnvironment("ARGUS_API_SECRET"),
    [SESSION_HEADER]: sessionToken,
    [FRONTEND_ORIGIN_HEADER]: requiredEnvironment("ARGUS_FRONTEND_URL"),
  };
  if (hasBody) {
    headers["Content-Type"] = "application/json";
  }
  return headers;
}

async function classifyFinanceFailure(response: Response): Promise<FinanceRequestErrorKind> {
  if (response.status === 429) {
    return "rate_limited";
  }
  if (response.status === 404) {
    return "not_found";
  }

  if (response.status !== 400 && response.status !== 409) {
    return "unavailable";
  }

  try {
    const body: unknown = await response.json();
    if (typeof body === "object" && body !== null && "error" in body) {
      const error = (body as { error?: unknown }).error;
      if (response.status === 409 && error === "duplicate_name") {
        return "duplicate_name";
      }
      if (response.status === 400) {
        if (error === "invalid_request") {
          return "invalid_request";
        }
        if (error === "system_category_immutable") {
          return "system_category_immutable";
        }
        if (error === "kind_change_confirmation_required") {
          return "kind_change_confirmation_required";
        }
      }
    }
  } catch {
    // An unparseable failure body is indistinguishable from an unavailable backend.
  }

  return "unavailable";
}

async function financeRequest<T>(
  sessionToken: string,
  method: "GET" | "POST" | "PATCH" | "DELETE",
  path: string,
  body?: Record<string, unknown>,
): Promise<FinanceRequestResult<T>> {
  if (!sessionToken) {
    return { ok: false, kind: "unavailable" };
  }

  try {
    const init: RequestInit = {
      method,
      headers: financeHeaders(sessionToken, body !== undefined),
      cache: "no-store",
    };
    if (body !== undefined) {
      init.body = JSON.stringify(body);
    }

    const response = await fetch(`${backendUrl()}${path}`, init);
    if (!response.ok) {
      return { ok: false, kind: await classifyFinanceFailure(response) };
    }
    return { ok: true, value: (await response.json()) as T };
  } catch {
    return { ok: false, kind: "unavailable" };
  }
}

export async function listFinancialAccounts(
  sessionToken: string,
): Promise<FinanceRequestResult<{ accounts: FinancialAccount[] }>> {
  return financeRequest<{ accounts: FinancialAccount[] }>(
    sessionToken,
    "GET",
    "/v1/finance/accounts",
  );
}

export async function createFinancialAccount(
  sessionToken: string,
  input: FinancialAccountInput,
): Promise<FinanceRequestResult<FinancialAccount>> {
  const body: Record<string, unknown> = { display_name: input.display_name };
  if ("institution" in input) {
    body.institution = input.institution;
  }
  return financeRequest<FinancialAccount>(sessionToken, "POST", "/v1/finance/accounts", body);
}

export async function updateFinancialAccount(
  sessionToken: string,
  id: string,
  input: FinancialAccountInput,
): Promise<FinanceRequestResult<FinancialAccount>> {
  const body: Record<string, unknown> = { display_name: input.display_name };
  if ("institution" in input) {
    body.institution = input.institution;
  }
  return financeRequest<FinancialAccount>(
    sessionToken,
    "PATCH",
    `/v1/finance/accounts/${encodeURIComponent(id)}`,
    body,
  );
}

export async function archiveFinancialAccount(
  sessionToken: string,
  id: string,
): Promise<FinanceRequestResult<FinancialAccount>> {
  return financeRequest<FinancialAccount>(
    sessionToken,
    "POST",
    `/v1/finance/accounts/${encodeURIComponent(id)}/archive`,
  );
}

export async function restoreFinancialAccount(
  sessionToken: string,
  id: string,
): Promise<FinanceRequestResult<FinancialAccount>> {
  return financeRequest<FinancialAccount>(
    sessionToken,
    "POST",
    `/v1/finance/accounts/${encodeURIComponent(id)}/restore`,
  );
}

export async function listCategories(
  sessionToken: string,
): Promise<FinanceRequestResult<{ categories: Category[] }>> {
  return financeRequest<{ categories: Category[] }>(sessionToken, "GET", "/v1/finance/categories");
}

export async function createCategory(
  sessionToken: string,
  input: CategoryInput,
): Promise<FinanceRequestResult<Category>> {
  return financeRequest<Category>(sessionToken, "POST", "/v1/finance/categories", {
    display_name: input.display_name,
    kind: input.kind,
  });
}

export async function updateCategory(
  sessionToken: string,
  id: string,
  input: CategoryUpdateInput,
): Promise<FinanceRequestResult<Category>> {
  const body: Record<string, unknown> = {
    display_name: input.display_name,
    kind: input.kind,
  };
  if (input.confirm_kind_change !== undefined) {
    body.confirm_kind_change = input.confirm_kind_change;
  }
  return financeRequest<Category>(
    sessionToken,
    "PATCH",
    `/v1/finance/categories/${encodeURIComponent(id)}`,
    body,
  );
}

export async function archiveCategory(
  sessionToken: string,
  id: string,
): Promise<FinanceRequestResult<Category>> {
  return financeRequest<Category>(
    sessionToken,
    "POST",
    `/v1/finance/categories/${encodeURIComponent(id)}/archive`,
  );
}

export async function restoreCategory(
  sessionToken: string,
  id: string,
): Promise<FinanceRequestResult<Category>> {
  return financeRequest<Category>(
    sessionToken,
    "POST",
    `/v1/finance/categories/${encodeURIComponent(id)}/restore`,
  );
}

export async function deleteCategory(
  sessionToken: string,
  id: string,
): Promise<FinanceRequestResult<DeleteCategoryResult>> {
  return financeRequest<DeleteCategoryResult>(
    sessionToken,
    "DELETE",
    `/v1/finance/categories/${encodeURIComponent(id)}`,
  );
}

export async function listMerchantTags(
  sessionToken: string,
): Promise<FinanceRequestResult<{ merchant_tags: MerchantTag[] }>> {
  return financeRequest<{ merchant_tags: MerchantTag[] }>(
    sessionToken,
    "GET",
    "/v1/finance/merchant-tags",
  );
}

export async function createMerchantTag(
  sessionToken: string,
  displayName: string,
): Promise<FinanceRequestResult<MerchantTag>> {
  return financeRequest<MerchantTag>(sessionToken, "POST", "/v1/finance/merchant-tags", {
    display_name: displayName,
  });
}

export async function updateMerchantTag(
  sessionToken: string,
  id: string,
  displayName: string,
): Promise<FinanceRequestResult<MerchantTag>> {
  return financeRequest<MerchantTag>(
    sessionToken,
    "PATCH",
    `/v1/finance/merchant-tags/${encodeURIComponent(id)}`,
    { display_name: displayName },
  );
}

export async function archiveMerchantTag(
  sessionToken: string,
  id: string,
): Promise<FinanceRequestResult<MerchantTag>> {
  return financeRequest<MerchantTag>(
    sessionToken,
    "POST",
    `/v1/finance/merchant-tags/${encodeURIComponent(id)}/archive`,
  );
}

export async function restoreMerchantTag(
  sessionToken: string,
  id: string,
): Promise<FinanceRequestResult<MerchantTag>> {
  return financeRequest<MerchantTag>(
    sessionToken,
    "POST",
    `/v1/finance/merchant-tags/${encodeURIComponent(id)}/restore`,
  );
}

export async function deleteMerchantTag(
  sessionToken: string,
  id: string,
): Promise<FinanceRequestResult<DeleteMerchantTagResult>> {
  return financeRequest<DeleteMerchantTagResult>(
    sessionToken,
    "DELETE",
    `/v1/finance/merchant-tags/${encodeURIComponent(id)}`,
  );
}

// --- Merchant aliases ------------------------------------------------------

export async function listMerchantAliases(
  sessionToken: string,
): Promise<FinanceRequestResult<{ merchant_aliases: MerchantAlias[] }>> {
  return financeRequest<{ merchant_aliases: MerchantAlias[] }>(
    sessionToken,
    "GET",
    "/v1/finance/merchant-aliases",
  );
}

export async function createMerchantAlias(
  sessionToken: string,
  input: MerchantAliasInput,
): Promise<FinanceRequestResult<MerchantAlias>> {
  return financeRequest<MerchantAlias>(sessionToken, "POST", "/v1/finance/merchant-aliases", input);
}

export async function updateMerchantAlias(
  sessionToken: string,
  id: string,
  input: MerchantAliasInput,
): Promise<FinanceRequestResult<MerchantAlias>> {
  return financeRequest<MerchantAlias>(
    sessionToken,
    "PATCH",
    `/v1/finance/merchant-aliases/${encodeURIComponent(id)}`,
    input,
  );
}

export async function deleteMerchantAlias(
  sessionToken: string,
  id: string,
): Promise<FinanceRequestResult<{ deleted: boolean }>> {
  return financeRequest<{ deleted: boolean }>(
    sessionToken,
    "DELETE",
    `/v1/finance/merchant-aliases/${encodeURIComponent(id)}`,
  );
}

// --- Transaction workspace -------------------------------------------------

export type TransactionVisibility = "visible" | "hidden" | "all";

export type TransactionFilters = {
  search?: string;
  start_date?: string;
  end_date?: string;
  financial_account_id?: string;
  statement_import_id?: string;
  category_id?: string;
  merchant_tag?: string;
  category_confirmed?: boolean;
  category_kind?: CategoryKind;
  direction?: "in" | "out" | "zero";
  visibility?: TransactionVisibility;
  calculation_scope?: "included";
  excluded_category_ids?: string[];
};

export type TransactionQuery = {
  filters?: TransactionFilters;
  page?: number;
  page_size?: number;
  sort_by?: "date" | "amount" | "description";
  sort_direction?: "asc" | "desc";
};

export type TransactionAccountRef = {
  id: string;
  display_name: string;
  is_archived: boolean;
};

export type TransactionCategoryRef = {
  id: string;
  display_name: string;
  kind: CategoryKind;
  is_system: boolean;
  is_archived: boolean;
};

export type TransactionTagRef = {
  id: string;
  display_name: string;
  is_archived: boolean;
};

export type TransactionStatementRef = {
  id: string;
  statement_label: string;
  source_type: string;
  is_hidden: boolean;
};

export type TransactionRow = {
  id: string;
  version: number;
  transaction_date: string;
  description: string;
  amount: string;
  currency: "ZAR";
  direction: "in" | "out" | "zero";
  financial_account: TransactionAccountRef;
  category: TransactionCategoryRef;
  category_confirmed: boolean;
  merchant_tags: TransactionTagRef[];
  statement_import: TransactionStatementRef;
  is_hidden_by_user: boolean;
  effective_hidden: boolean;
  hidden_reason: "none" | "individual" | "statement" | "both";
};

export type TransactionPage = {
  transactions: TransactionRow[];
  inventory_count: number;
  matching_visible_count: number;
  matching_hidden_count: number;
  matching_total: number;
  filtered_total: number;
  page: number;
  page_size: number;
  sort_by: string;
  sort_direction: string;
  money_totals: TransactionMoneyTotals;
};

export type TransactionMoneyDiagnostic = {
  count: number;
  money_in: string;
  money_out: string;
  absolute: string;
};

export type TransactionMoneyTotals = {
  money_in: string;
  money_out: string;
  net: string;
  included_count: number;
  transfers: TransactionMoneyDiagnostic;
  excluded: TransactionMoneyDiagnostic;
};

export type TransactionDetail = {
  transaction: TransactionRow;
  suggested_tags: TransactionTagRef[];
  suggested_category?: TransactionCategoryRef | null;
  has_more_suggestions: boolean;
  source_page: number | null;
  source_row_number: number;
};

export type TransactionChanges = {
  category_id?: string;
  add_tag_ids?: string[];
  remove_tag_ids?: string[];
  is_hidden_by_user?: boolean;
  confirm_category?: boolean;
};

export type TransactionPatchInput = {
  expected_version: number;
  changes: TransactionChanges;
  confirm?: boolean;
};

export type TransactionSelection =
  | {
      mode: "explicit";
      transactions: { id: string; expected_version: number }[];
    }
  | {
      mode: "all_matching";
      filters: TransactionFilters;
    };

export type TransactionPrepareInput = {
  operation_id: string;
  selection: TransactionSelection;
  changes: TransactionChanges;
};

export type TransactionOperationAction = {
  category: TransactionCategoryRef | null;
  add_tags: TransactionTagRef[];
  remove_tags: TransactionTagRef[];
  is_hidden_by_user: boolean | null;
  confirm_category: boolean | null;
  stale: boolean;
};

export type TransactionOperationStatus = "Prepared" | "Applied" | "Abandoned" | "Expired";

export type TransactionOperation = {
  operation_id: string;
  status: TransactionOperationStatus;
  selection_mode: "explicit" | "all_matching";
  captured_at: string;
  expires_at: string;
  selected_count: number;
  changed_count: number;
  unchanged_count: number;
  confirmation_hash: string | null;
  applied_at: string | null;
  terminal_at: string | null;
  action: TransactionOperationAction | null;
};

export type TransactionPrepareResult = {
  operation: TransactionOperation;
  created: boolean;
};

export type TransactionApplyResult = {
  operation_id: string;
  status: TransactionOperationStatus;
  selected_count: number;
  changed_count: number;
  unchanged_count: number;
  applied_at: string | null;
  replayed: boolean;
};

export type TransactionMutationResult = {
  transaction: TransactionRow;
  selected_count: number;
  changed_count: number;
  unchanged_count: number;
};

export type TransactionErrorKind =
  | "invalid_request"
  | "empty_selection"
  | "category_unavailable"
  | "tag_unavailable"
  | "tag_limit_exceeded"
  | "selection_too_large"
  | "request_too_large"
  | "version_conflict"
  | "selection_stale"
  | "classification_changed"
  | "parent_hidden"
  | "idempotency_conflict"
  | "operation_busy"
  | "operation_expired"
  | "operation_abandoned"
  | "operation_quota"
  | "not_found"
  | "rate_limited"
  | "unauthorized"
  | "unavailable";

export type TransactionErrorDetails = {
  statements?: { id: string; statement_label: string }[];
  stale_count?: number;
  missing_count?: number;
};

export type TransactionRequestResult<T> =
  | { ok: true; value: T }
  | { ok: false; kind: TransactionErrorKind; details?: TransactionErrorDetails };

const TRANSACTION_ERROR_KINDS = new Set<string>([
  "invalid_request",
  "empty_selection",
  "category_unavailable",
  "tag_unavailable",
  "tag_limit_exceeded",
  "selection_too_large",
  "request_too_large",
  "version_conflict",
  "selection_stale",
  "classification_changed",
  "parent_hidden",
  "idempotency_conflict",
  "operation_busy",
  "operation_expired",
  "operation_abandoned",
  "operation_quota",
  "not_found",
  "rate_limited",
]);

async function classifyTransactionFailure(
  response: Response,
): Promise<{ kind: TransactionErrorKind; details?: TransactionErrorDetails }> {
  if (response.status === 401) {
    return { kind: "unauthorized" };
  }
  if (response.status === 429) {
    return { kind: "rate_limited" };
  }
  if (response.status === 404) {
    return { kind: "not_found" };
  }
  if (response.status === 413) {
    return { kind: "request_too_large" };
  }
  if (response.status === 410) {
    try {
      const body: unknown = await response.json();
      const error =
        typeof body === "object" && body !== null && "error" in body
          ? (body as { error?: unknown }).error
          : null;
      if (error === "operation_abandoned" || error === "operation_expired") {
        return { kind: error };
      }
    } catch {
      // An unparseable 410 remains a terminal operation response.
    }
    return { kind: "operation_expired" };
  }

  try {
    const body: unknown = await response.json();
    if (typeof body === "object" && body !== null && "error" in body) {
      const error = (body as { error?: unknown }).error;
      if (typeof error === "string" && TRANSACTION_ERROR_KINDS.has(error)) {
        return {
          kind: error as TransactionErrorKind,
          details: transactionErrorDetails(body),
        };
      }
    }
  } catch {
    // Fall through to unavailable.
  }

  return { kind: "unavailable" };
}

function transactionErrorDetails(body: object): TransactionErrorDetails | undefined {
  const source = body as {
    statements?: unknown;
    stale_count?: unknown;
    missing_count?: unknown;
  };
  const details: TransactionErrorDetails = {};
  if (Array.isArray(source.statements)) {
    details.statements = source.statements
      .filter(
        (value): value is { id: string; statement_label: string } =>
          typeof value === "object" &&
          value !== null &&
          typeof (value as { id?: unknown }).id === "string" &&
          typeof (value as { statement_label?: unknown }).statement_label === "string",
      )
      .slice(0, 5)
      .map((value) => ({ id: value.id, statement_label: value.statement_label }));
  }
  if (typeof source.stale_count === "number") {
    details.stale_count = source.stale_count;
  }
  if (typeof source.missing_count === "number") {
    details.missing_count = source.missing_count;
  }
  return Object.keys(details).length === 0 ? undefined : details;
}

async function transactionRequest<T>(
  sessionToken: string,
  method: "GET" | "POST" | "PATCH",
  path: string,
  body?: Record<string, unknown>,
): Promise<TransactionRequestResult<T>> {
  if (!sessionToken) {
    return { ok: false, kind: "unavailable" };
  }

  try {
    const init: RequestInit = {
      method,
      headers: financeHeaders(sessionToken, body !== undefined),
      cache: "no-store",
    };
    if (body !== undefined) {
      init.body = JSON.stringify(body);
    }

    const response = await fetch(`${backendUrl()}${path}`, init);
    if (!response.ok) {
      const failure = await classifyTransactionFailure(response);
      return { ok: false, ...failure };
    }
    return { ok: true, value: (await response.json()) as T };
  } catch {
    return { ok: false, kind: "unavailable" };
  }
}

export async function queryTransactions(
  sessionToken: string,
  query: TransactionQuery,
): Promise<TransactionRequestResult<TransactionPage>> {
  return transactionRequest<TransactionPage>(
    sessionToken,
    "POST",
    "/v1/finance/transactions/query",
    query as Record<string, unknown>,
  );
}

export async function getTransaction(
  sessionToken: string,
  id: string,
): Promise<TransactionRequestResult<TransactionDetail>> {
  return transactionRequest<TransactionDetail>(
    sessionToken,
    "GET",
    `/v1/finance/transactions/${encodeURIComponent(id)}`,
  );
}

export async function patchTransaction(
  sessionToken: string,
  id: string,
  input: TransactionPatchInput,
): Promise<TransactionRequestResult<TransactionMutationResult>> {
  return transactionRequest<TransactionMutationResult>(
    sessionToken,
    "PATCH",
    `/v1/finance/transactions/${encodeURIComponent(id)}`,
    input as unknown as Record<string, unknown>,
  );
}

export async function prepareTransactionOperation(
  sessionToken: string,
  input: TransactionPrepareInput,
): Promise<TransactionRequestResult<TransactionPrepareResult>> {
  return transactionRequest<TransactionPrepareResult>(
    sessionToken,
    "POST",
    "/v1/finance/transactions/operations/prepare",
    input as unknown as Record<string, unknown>,
  );
}

export async function getTransactionOperation(
  sessionToken: string,
  operationId: string,
): Promise<TransactionRequestResult<TransactionOperation>> {
  return transactionRequest<TransactionOperation>(
    sessionToken,
    "GET",
    `/v1/finance/transactions/operations/${encodeURIComponent(operationId)}`,
  );
}

export async function applyTransactionOperation(
  sessionToken: string,
  operationId: string,
  confirmationHash: string,
  expectedSelectedCount: number,
): Promise<TransactionRequestResult<TransactionApplyResult>> {
  return transactionRequest<TransactionApplyResult>(
    sessionToken,
    "POST",
    `/v1/finance/transactions/operations/${encodeURIComponent(operationId)}/apply`,
    {
      confirmation_hash: confirmationHash,
      expected_selected_count: expectedSelectedCount,
      confirm: true,
    },
  );
}

export async function abandonTransactionOperation(
  sessionToken: string,
  operationId: string,
): Promise<TransactionRequestResult<TransactionApplyResult>> {
  return transactionRequest<TransactionApplyResult>(
    sessionToken,
    "POST",
    `/v1/finance/transactions/operations/${encodeURIComponent(operationId)}/abandon`,
    {},
  );
}

// --- Statement import -------------------------------------------------------

export type ImportErrorKind =
  | "invalid_request"
  | "file_too_large"
  | "unsupported_format"
  | "unsupported_encoding"
  | "unsafe_archive"
  | "encrypted_pdf"
  | "text_pdf_required"
  | "unsupported_layout"
  | "processing_limit"
  | "format_confirmation_required"
  | "pdf_import_deferred"
  | "import_busy"
  | "not_found"
  | "review_expired"
  | "import_deleted"
  | "review_stale"
  | "version_conflict"
  | "statement_repeat"
  | "idempotency_conflict"
  | "account_archived"
  | "identity_conflict"
  | "tag_unavailable"
  | "category_unavailable"
  | "no_included_rows"
  | "invalid_rows"
  | "duplicate_decision_required"
  | "rate_limited"
  | "unavailable";

export type ImportRequestResult<T> =
  { ok: true; value: T } | { ok: false; kind: ImportErrorKind; existingImportIds?: string[] };

export type ImportParserConfig = {
  format?: "fnb" | "standard_bank" | "generic" | null;
  header_line?: number | null;
  delimiter?: string | null;
  date_format?: string | null;
  decimal_separator?: string | null;
  thousands_separator?: string | null;
  amount_mode?: "signed" | "debit_credit" | null;
  date_column?: number | null;
  description_column?: number | null;
  amount_column?: number | null;
  debit_column?: number | null;
  credit_column?: number | null;
  direction?: "normal" | "inverted" | null;
  negative_style?: string | null;
  confirm_empty_description?: boolean;
  stable_id_column?: number | null;
  source_namespace?: string | null;
  century?: number | null;
};

export type ImportParseOptions = {
  financial_account_id: string;
  source_kind: "fnb" | "generic_csv" | "standard_bank_pdf";
  parser_config?: ImportParserConfig | null;
  start_date?: string | null;
  end_date?: string | null;
  currency?: "ZAR" | null;
  confirm_zar?: boolean | null;
  statement_override?: boolean | null;
};

export type ImportLimits = {
  max_file_bytes: number;
  max_data_rows: number;
  max_pdf_pages: number;
  max_description_scalars: number;
};

export type ImportPreviewRow = {
  row_id: string;
  source_line: number;
  source_page: number | null;
  source_row_number: number;
  raw_values: Record<string, string | null>;
  transaction_date: string | null;
  description: string;
  amount: string | null;
  validation_codes: string[];
  warnings: string[];
};

export type ImportInspectResult = {
  detected_source_kind: string;
  limits: ImportLimits;
  proposed_start_date: string | null;
  proposed_end_date: string | null;
  mapping: Record<string, string>;
  required_confirmations: string[];
  warnings: string[];
  preview: ImportPreviewRow[];
  preview_truncated: boolean;
  can_prepare: boolean;
};

export type ImportNearCandidate = {
  candidate_id: string;
  source: string;
  import_id: string | null;
  transaction_date: string;
  amount: string;
  description: string;
  similarity: number;
  reasons: string[];
};

export type ImportReviewRow = {
  row_id: string;
  source_line: number;
  source_page: number | null;
  source_row_number: number;
  raw_values: Record<string, string | null>;
  transaction_date: string | null;
  description: string;
  amount: string | null;
  validation_codes: string[];
  warnings: string[];
  duplicate_state: string;
  duplicate_reasons: string[];
  matching_import_ids: string[];
  near_candidates: ImportNearCandidate[];
  near_candidate_total: number;
  within_file_group: string | null;
  within_file_state: string;
  skip: boolean;
  override: boolean;
  suggested_tag_ids: string[];
  selected_tag_ids: string[];
  suggested_category_id: string | null;
  selected_category_id: string | null;
  category_decided: boolean;
};

export type ImportReviewSummary = {
  id: string;
  financial_account_id: string;
  source_kind: string;
  start_date: string | null;
  end_date: string | null;
  version: number;
  status: string;
  expires_at: string;
  total_rows: number;
  imported_count: number;
  suppressed_count: number;
  skipped_count: number;
  invalid_unskipped_count: number;
  within_file_pending_count: number;
  possible_duplicate_count: number;
  exact_override_count: number;
  categorised_count: number;
  statement_override: boolean;
  statement_override_count: number;
  summary_hash: string;
};

export type ImportReviewPage = {
  rows: ImportReviewRow[];
  total: number;
  page: number;
  page_size: number;
};

export type ImportReviewDetail = {
  summary: ImportReviewSummary;
  page: ImportReviewPage;
};

export type ImportOpenReview = {
  id: string;
  financial_account_id: string;
  source_kind: string;
  start_date: string | null;
  end_date: string | null;
  version: number;
  expires_at: string;
  total_rows: number;
  imported_count: number;
  suppressed_count: number;
  skipped_count: number;
  created_at: string;
};

export type ImportReviewDecision = {
  row_id: string;
  skip?: boolean;
  override?: boolean;
  within_file_state?: "keep" | "skip";
  within_file_group?: string;
  within_file_action?: "keep_all" | "skip_all";
  selected_tag_ids?: string[];
  selected_category_id?: string | null;
  reset_category_to_suggestion?: boolean;
};

export type ImportStatementSummary = {
  id: string;
  financial_account_id: string;
  source_type: string;
  statement_label: string;
  start_date: string;
  end_date: string;
  total_rows: number;
  imported_count: number;
  suppressed_count: number;
  skipped_count: number;
  possible_duplicate_count: number;
  exact_override_count: number;
  statement_override_count: number;
  is_hidden: boolean;
  version: number;
  live_hidden_count: number;
  created_at: string;
  updated_at: string;
};

export type ImportStatementRow = {
  id: string;
  source_row_number: number;
  source_page: number | null;
  transaction_date: string;
  description: string;
  amount: string;
  is_hidden_by_user: boolean;
  effective_hidden: boolean;
  tags: { id: string; display_name: string }[];
};

export type ImportStatementDetail = {
  statement: ImportStatementSummary;
  rows: ImportStatementRow[];
  page: number;
  page_size: number;
  expected_transaction_count: number;
};

export type ImportHistoryPage = {
  items: ImportStatementSummary[];
  next_cursor: string | null;
};

export type ImportCommitResult = {
  replayed: boolean;
  statement: ImportStatementSummary;
};

export type ImportAbandonResult = {
  committed: boolean;
  statement: ImportStatementSummary | null;
  replayed: boolean;
};

export type ImportDeleteResult = {
  deleted: boolean;
  deleted_transaction_count: number;
};

const IMPORT_ERROR_KINDS = new Set<string>([
  "invalid_request",
  "file_too_large",
  "unsupported_format",
  "unsupported_encoding",
  "unsafe_archive",
  "encrypted_pdf",
  "text_pdf_required",
  "unsupported_layout",
  "processing_limit",
  "format_confirmation_required",
  "pdf_import_deferred",
  "import_busy",
  "not_found",
  "review_expired",
  "import_deleted",
  "review_stale",
  "version_conflict",
  "statement_repeat",
  "idempotency_conflict",
  "account_archived",
  "identity_conflict",
  "tag_unavailable",
  "category_unavailable",
  "no_included_rows",
  "invalid_rows",
  "duplicate_decision_required",
  "rate_limited",
]);

function importHeaders(sessionToken: string, hasJsonBody: boolean): Record<string, string> {
  const headers: Record<string, string> = {
    [REQUEST_ID_HEADER]: newRequestId(),
    [APPLICATION_SECRET_HEADER]: requiredEnvironment("ARGUS_API_SECRET"),
    [SESSION_HEADER]: sessionToken,
    [FRONTEND_ORIGIN_HEADER]: requiredEnvironment("ARGUS_FRONTEND_URL"),
  };
  if (hasJsonBody) {
    headers["Content-Type"] = "application/json";
  }
  return headers;
}

async function classifyImportFailure(
  response: Response,
): Promise<{ kind: ImportErrorKind; existingImportIds?: string[] }> {
  if (response.status === 429) {
    return { kind: "rate_limited" };
  }
  if (response.status === 404) {
    return { kind: "not_found" };
  }
  if (response.status === 413) {
    return { kind: "file_too_large" };
  }
  if (response.status === 415) {
    return { kind: "unsupported_format" };
  }
  if (response.status === 410) {
    try {
      const body: unknown = await response.json();
      const error =
        typeof body === "object" && body !== null && "error" in body
          ? (body as { error?: unknown }).error
          : null;
      if (error === "import_deleted" || error === "review_expired") {
        return { kind: error };
      }
    } catch {
      // An unparseable 410 is still a terminal review/import response.
    }
    return { kind: "review_expired" };
  }

  try {
    const body: unknown = await response.json();
    if (typeof body === "object" && body !== null && "error" in body) {
      const error = (body as { error?: unknown }).error;
      if (typeof error === "string" && IMPORT_ERROR_KINDS.has(error)) {
        const existing = (body as { existing_import_ids?: unknown }).existing_import_ids;
        return {
          kind: error as ImportErrorKind,
          existingImportIds: Array.isArray(existing)
            ? existing.filter((value): value is string => typeof value === "string")
            : undefined,
        };
      }
    }
  } catch {
    // Fall through to unavailable.
  }

  return { kind: "unavailable" };
}

async function importRequest<T>(
  sessionToken: string,
  method: "GET" | "POST" | "PATCH" | "DELETE",
  path: string,
  body?: Record<string, unknown>,
): Promise<ImportRequestResult<T>> {
  if (!sessionToken) {
    return { ok: false, kind: "unavailable" };
  }

  try {
    const init: RequestInit = {
      method,
      headers: importHeaders(sessionToken, body !== undefined),
      cache: "no-store",
    };
    if (body !== undefined) {
      init.body = JSON.stringify(body);
    }
    const response = await fetch(`${backendUrl()}${path}`, init);
    if (!response.ok) {
      const failure = await classifyImportFailure(response);
      return { ok: false, ...failure };
    }
    return { ok: true, value: (await response.json()) as T };
  } catch {
    return { ok: false, kind: "unavailable" };
  }
}

async function uploadImport<T>(
  sessionToken: string,
  path: string,
  fileBytes: Uint8Array,
  filename: string,
  optionsJson: string,
  signal?: AbortSignal,
): Promise<ImportRequestResult<T>> {
  if (!sessionToken) {
    return { ok: false, kind: "unavailable" };
  }

  try {
    const form = new FormData();
    form.append("options", optionsJson);
    const copy = new Uint8Array(fileBytes.byteLength);
    copy.set(fileBytes);
    form.append(
      "file",
      new Blob([copy.buffer as ArrayBuffer], { type: "application/octet-stream" }),
      filename,
    );
    const response = await fetch(`${backendUrl()}${path}`, {
      method: "POST",
      headers: importHeaders(sessionToken, false),
      body: form,
      cache: "no-store",
      signal,
    });
    if (!response.ok) {
      const failure = await classifyImportFailure(response);
      return { ok: false, ...failure };
    }
    return { ok: true, value: (await response.json()) as T };
  } catch {
    return { ok: false, kind: "unavailable" };
  }
}

export async function inspectImportFile(
  sessionToken: string,
  fileBytes: Uint8Array,
  options: ImportParseOptions,
  filename = "statement.bin",
  signal?: AbortSignal,
): Promise<ImportRequestResult<ImportInspectResult>> {
  return uploadImport<ImportInspectResult>(
    sessionToken,
    "/v1/finance/imports/inspect",
    fileBytes,
    filename,
    JSON.stringify(options),
    signal,
  );
}

export async function prepareImportReview(
  sessionToken: string,
  fileBytes: Uint8Array,
  options: ImportParseOptions,
  filename = "statement.bin",
  signal?: AbortSignal,
): Promise<ImportRequestResult<ImportReviewDetail>> {
  return uploadImport<ImportReviewDetail>(
    sessionToken,
    "/v1/finance/imports/reviews",
    fileBytes,
    filename,
    JSON.stringify(options),
    signal,
  );
}

export async function listOpenImportReviews(
  sessionToken: string,
): Promise<ImportRequestResult<{ reviews: ImportOpenReview[] }>> {
  return importRequest<{ reviews: ImportOpenReview[] }>(
    sessionToken,
    "GET",
    "/v1/finance/imports/reviews",
  );
}

export async function getImportReview(
  sessionToken: string,
  reviewId: string,
  page = 1,
  pageSize = 100,
  sortDirection: "asc" | "desc" = "desc",
  possibleDuplicatesOnly = false,
  untaggedOnly = false,
  repeatsInFileOnly = false,
  excludePossibleDuplicates = false,
): Promise<ImportRequestResult<ImportReviewDetail>> {
  const filters = [
    possibleDuplicatesOnly ? "&duplicates=possible" : "",
    excludePossibleDuplicates ? "&duplicates=exclude" : "",
    repeatsInFileOnly ? "&repeats=file" : "",
    untaggedOnly ? "&untagged=true" : "",
  ].join("");
  return importRequest<ImportReviewDetail>(
    sessionToken,
    "GET",
    `/v1/finance/imports/reviews/${encodeURIComponent(reviewId)}?page=${page}&page_size=${pageSize}&sort_direction=${sortDirection}${filters}`,
  );
}

export async function patchImportReview(
  sessionToken: string,
  reviewId: string,
  expectedReviewVersion: number,
  decisions: ImportReviewDecision[],
  statementOverride?: boolean,
): Promise<ImportRequestResult<ImportReviewSummary>> {
  const body: Record<string, unknown> = {
    expected_review_version: expectedReviewVersion,
    decisions,
  };
  if (statementOverride !== undefined) {
    body.statement_override = statementOverride;
  }
  return importRequest<ImportReviewSummary>(
    sessionToken,
    "PATCH",
    `/v1/finance/imports/reviews/${encodeURIComponent(reviewId)}`,
    body,
  );
}

export async function refreshImportReview(
  sessionToken: string,
  reviewId: string,
  expectedReviewVersion: number,
): Promise<ImportRequestResult<ImportReviewSummary>> {
  return importRequest<ImportReviewSummary>(
    sessionToken,
    "POST",
    `/v1/finance/imports/reviews/${encodeURIComponent(reviewId)}/refresh`,
    { expected_review_version: expectedReviewVersion },
  );
}

export async function commitImportReview(
  sessionToken: string,
  reviewId: string,
  expectedReviewVersion: number,
  confirmedSummaryHash: string,
): Promise<ImportRequestResult<ImportCommitResult>> {
  return importRequest<ImportCommitResult>(
    sessionToken,
    "POST",
    `/v1/finance/imports/reviews/${encodeURIComponent(reviewId)}/commit`,
    {
      expected_review_version: expectedReviewVersion,
      confirmed_summary_hash: confirmedSummaryHash,
    },
  );
}

export async function abandonImportReview(
  sessionToken: string,
  reviewId: string,
  expectedReviewVersion: number,
): Promise<ImportRequestResult<ImportAbandonResult>> {
  return importRequest<ImportAbandonResult>(
    sessionToken,
    "POST",
    `/v1/finance/imports/reviews/${encodeURIComponent(reviewId)}/abandon`,
    { expected_review_version: expectedReviewVersion },
  );
}

export async function listImportHistory(
  sessionToken: string,
  options?: {
    accountId?: string;
    state?: "all" | "visible" | "hidden";
    cursor?: string;
    limit?: number;
  },
): Promise<ImportRequestResult<ImportHistoryPage>> {
  const params = new URLSearchParams();
  if (options?.accountId) params.set("account_id", options.accountId);
  if (options?.state) params.set("state", options.state);
  if (options?.cursor) params.set("cursor", options.cursor);
  if (options?.limit) params.set("limit", String(options.limit));
  const query = params.toString();
  return importRequest<ImportHistoryPage>(
    sessionToken,
    "GET",
    `/v1/finance/imports${query ? `?${query}` : ""}`,
  );
}

export async function getImportStatement(
  sessionToken: string,
  statementId: string,
  page = 1,
  pageSize = 100,
): Promise<ImportRequestResult<ImportStatementDetail>> {
  return importRequest<ImportStatementDetail>(
    sessionToken,
    "GET",
    `/v1/finance/imports/${encodeURIComponent(statementId)}?page=${page}&page_size=${pageSize}`,
  );
}

export async function setImportVisibility(
  sessionToken: string,
  statementId: string,
  hidden: boolean,
  expectedVersion: number,
  expectedTransactionCount: number,
): Promise<ImportRequestResult<ImportStatementSummary>> {
  return importRequest<ImportStatementSummary>(
    sessionToken,
    "POST",
    `/v1/finance/imports/${encodeURIComponent(statementId)}/${hidden ? "hide" : "restore"}`,
    {
      expected_version: expectedVersion,
      expected_transaction_count: expectedTransactionCount,
      confirm: true,
    },
  );
}

export async function deleteImportStatement(
  sessionToken: string,
  statementId: string,
  expectedVersion: number,
  expectedTransactionCount: number,
): Promise<ImportRequestResult<ImportDeleteResult>> {
  return importRequest<ImportDeleteResult>(
    sessionToken,
    "DELETE",
    `/v1/finance/imports/${encodeURIComponent(statementId)}`,
    {
      expected_version: expectedVersion,
      expected_transaction_count: expectedTransactionCount,
      confirm: true,
    },
  );
}

// --- Reporting and salary cycles -------------------------------------------

export type ReportErrorKind =
  | "invalid_request"
  | "not_found"
  | "request_too_large"
  | "unauthorized"
  | "rate_limited"
  | "receipt_ineligible"
  | "version_conflict"
  | "cycle_sequence_changed"
  | "report_busy"
  | "unavailable";

export type ReportRequestResult<T> = { ok: true; value: T } | { ok: false; kind: ReportErrorKind };

const REPORT_ERROR_KINDS = new Set<string>([
  "invalid_request",
  "not_found",
  "request_too_large",
  "unauthorized",
  "rate_limited",
  "receipt_ineligible",
  "version_conflict",
  "cycle_sequence_changed",
  "report_busy",
]);

export type ReportPeriodInput = {
  mode?: "salary_cycle" | "calendar" | "custom";
  preset?: "month_to_date" | "previous_month" | "last_three_months" | "last_six_months" | "month";
  month?: string;
  start_date?: string;
  end_date?: string;
  cycle_start_id?: string;
};

export type ReportFilters = {
  financial_account_id?: string;
  category_id?: string;
  merchant_tag?: string;
  direction?: "in" | "out" | "zero";
};

export type ReportQuery = {
  view?: "overview" | "category" | "merchant_tag" | "comparison";
  period?: ReportPeriodInput;
  filters?: ReportFilters;
  tag_in_page?: number;
  tag_out_page?: number;
  transactions_page?: number;
  coverage_page?: number;
  comparison_coverage_page?: number;
  category_changes_page?: number;
};

export type ReportFilterDescriptor = {
  start_date: string;
  end_date: string;
  financial_account_id: string | null;
  category_id: string | null;
  merchant_tag: string | null;
  category_kind: string | null;
  direction: "in" | "out" | "zero" | null;
  visibility: "visible" | "hidden" | "all";
  calculation_scope: "included" | null;
  excluded_category_ids: string[] | null;
  category_confirmed?: boolean | null;
};

export type ReportPeriod = {
  mode: "salary_cycle" | "calendar" | "custom";
  preset: string | null;
  label: string;
  start_date: string;
  end_date: string;
  is_partial: boolean;
  is_future: boolean;
  is_open_salary_cycle: boolean;
  cycle_start_id: string | null;
  note: string | null;
};

export type ReportComparison = {
  period: ReportPeriod | null;
  label: string;
  shorter_prior: boolean;
  unavailable_reason: string | null;
  has_included_data: boolean;
};

export type ReportMoney = {
  money_in: string;
  money_out: string;
  net: string;
  included_count: number;
  zero_count: number;
};

export type ReportDiagnostic = {
  count: number;
  money_in: string;
  money_out: string;
  absolute: string;
};

export type ReportGap = { start_date: string; end_date: string; days: number };

export type ReportGapPage = {
  items: ReportGap[];
  total: number;
  page: number;
  page_size: number;
  has_more: boolean;
  next_page: number | null;
  gap_days: number;
};

export type ReportCoverage = {
  total_days: number;
  covered_days: number;
  covered_percentage: string | null;
  partial: boolean;
  gaps: ReportGapPage;
};

export type ReportCoveragePair = {
  current: ReportCoverage;
  prior: ReportCoverage | null;
};

export type ReportCategoryRank = {
  id: string;
  display_name: string;
  kind: CategoryKind;
  is_archived: boolean;
  amount: string;
  count: number;
  share: string | null;
  prior_amount: string | null;
  change_percentage: string | null;
  descriptor: ReportFilterDescriptor;
};

export type ReportOtherCategories = {
  amount: string;
  count: number;
  excluded_category_ids: string[];
  prior_amount: string | null;
  change_percentage: string | null;
  descriptor: ReportFilterDescriptor;
};

export type ReportCategoryDirection = {
  ranks: ReportCategoryRank[];
  uncategorised: ReportCategoryRank | null;
  other: ReportOtherCategories;
  other_ranks?: ReportCategoryRank[];
  all_ranks?: ReportCategoryRank[];
};

export type ReportCategoryBreakdown = {
  money_in: ReportCategoryDirection;
  money_out: ReportCategoryDirection;
};

export type ReportGroupTotals = {
  kind: string;
  money_in: string;
  money_out: string;
  net: string;
  count: number;
};

export type ReportUntagged = {
  amount: string;
  count: number;
  descriptor: ReportFilterDescriptor;
};

export type ReportTagRank = {
  id: string;
  display_name: string;
  is_archived: boolean;
  amount: string;
  count: number;
  share: string | null;
  prior_amount: string | null;
  change_percentage: string | null;
  descriptor: ReportFilterDescriptor;
};

export type ReportOtherTags = {
  direction: "in" | "out";
  items: ReportTagRank[];
  total: number;
  page: number;
  page_size: number;
  has_more: boolean;
  next_page: number | null;
};

export type ReportTagDirection = {
  ranks: ReportTagRank[];
  untagged: ReportUntagged;
  other: ReportOtherTags;
  all_ranks?: ReportTagRank[];
};

export type ReportTagBreakdown = {
  money_in: ReportTagDirection;
  money_out: ReportTagDirection;
  overlap_disclosure: boolean;
};

export type ReportTotals = {
  money: ReportMoney;
  money_in_descriptor: ReportFilterDescriptor;
  money_out_descriptor: ReportFilterDescriptor;
  net_descriptor: ReportFilterDescriptor;
  uncategorised_in_descriptor: ReportFilterDescriptor;
  uncategorised_out_descriptor: ReportFilterDescriptor;
  unconfirmed_descriptor: ReportFilterDescriptor;
  uncategorised_count: number;
  uncategorised_money_in: string;
  uncategorised_money_out: string;
  categorised_coverage_percentage: string | null;
  unconfirmed_category_count: number;
  tagged_count: number;
  untagged_count: number;
  untagged_money_in: string;
  untagged_money_out: string;
  tag_coverage_percentage: string | null;
  transfers: ReportDiagnostic;
  excluded: ReportDiagnostic;
  hidden: ReportDiagnostic;
  hidden_descriptor: ReportFilterDescriptor;
  transfers_descriptor: ReportFilterDescriptor;
  excluded_descriptor: ReportFilterDescriptor;
};

export type ReportBucket = {
  start_date: string;
  end_date: string;
  label: string;
  granularity: "week" | "month";
  money_in: string;
  money_out: string;
  money_in_share: string | null;
  money_out_share: string | null;
  count: number;
  coverage: "covered" | "partial" | "uncovered";
  money_in_descriptor: ReportFilterDescriptor;
  money_out_descriptor: ReportFilterDescriptor;
};

export type ReportFocus = {
  kind: string;
  category_id: string | null;
  category_name: string | null;
  category_kind: string | null;
  category_archived: boolean | null;
  merchant_tag_id: string | null;
  merchant_tag_name: string | null;
  merchant_tag_archived: boolean | null;
  untagged: boolean;
  financial_account_id: string | null;
  financial_account_name: string | null;
  financial_account_archived: boolean | null;
  direction: "in" | "out" | "zero" | null;
};

export type ReportTransactionPage = {
  items: TransactionRow[];
  total: number;
  page: number;
  page_size: number;
  has_more: boolean;
  next_page: number | null;
};

export type ReportCategoryChange = {
  id: string;
  display_name: string;
  kind: CategoryKind;
  is_archived: boolean;
  current_amount: string;
  prior_amount: string;
  delta: string;
  delta_percentage: string | null;
  current_descriptor: ReportFilterDescriptor;
  prior_descriptor: ReportFilterDescriptor;
};

export type ReportCategoryChangePage = {
  items: ReportCategoryChange[];
  total: number;
  page: number;
  page_size: number;
  has_more: boolean;
  next_page: number | null;
};

export type ReportCategoryChanges = {
  money_in: ReportCategoryChangePage;
  money_out: ReportCategoryChangePage;
};

export type ReportSetup = {
  reason: string;
  inventory_empty: boolean;
  message: string;
  alternatives: string[];
};

export type ReportResponse = {
  status: "setup_required" | "no_data" | "no_matches" | "all_hidden" | "excluded_only" | "ready";
  as_of: string;
  today: string;
  timezone: string;
  sequence_revision: string;
  view: "overview" | "category" | "merchant_tag" | "comparison";
  period_mode: "salary_cycle" | "calendar" | "custom";
  period: ReportPeriod | null;
  comparison: ReportComparison | null;
  focus: ReportFocus;
  current: ReportTotals | null;
  prior: ReportTotals | null;
  categories: ReportCategoryBreakdown | null;
  tags: ReportTagBreakdown | null;
  trend: ReportBucket[] | null;
  coverage: ReportCoveragePair | null;
  focus_group: ReportGroupTotals | null;
  transactions: ReportTransactionPage | null;
  category_changes: ReportCategoryChanges | null;
  setup: ReportSetup | null;
  policy_labels: string[];
};

export type SalaryCycleBoundary = {
  id: string;
  start_date: string;
  created_at: string;
};

export type SalaryCycleList = {
  revision: string;
  starts: SalaryCycleBoundary[];
  next_before_date: string | null;
};

export type SalaryCycleSource = {
  id: string;
  version: number;
  transaction_date: string;
  description: string;
  amount: string;
  currency: "ZAR";
  financial_account: TransactionAccountRef;
  category: TransactionCategoryRef;
  category_confirmed: boolean;
};

export type SalaryCycleInterval = {
  start_date: string;
  end_date: string;
  label: string;
};

export type SalaryCyclePreview = {
  action: "confirm" | "remove";
  revision: string;
  date: string | null;
  source: SalaryCycleSource | null;
  proposed_interval: SalaryCycleInterval | null;
  affected_intervals: SalaryCycleInterval[];
  warnings: string[];
  retention_disclosure: boolean;
  already_confirmed: boolean;
};

export type SalaryCycleConfirm = {
  id: string;
  start_date: string;
  revision: string;
  status: "created" | "already_confirmed";
};

export type SalaryCycleRemove = {
  removed_id: string;
  removed_date: string;
  revision: string;
};

async function reportRequest<T>(
  sessionToken: string,
  method: "GET" | "POST" | "DELETE",
  path: string,
  body?: Record<string, unknown>,
): Promise<ReportRequestResult<T>> {
  if (!sessionToken) {
    return { ok: false, kind: "unauthorized" };
  }

  try {
    const init: RequestInit = {
      method,
      headers: financeHeaders(sessionToken, body !== undefined),
      cache: "no-store",
    };
    if (body !== undefined) {
      init.body = JSON.stringify(body);
    }

    const response = await fetch(`${backendUrl()}${path}`, init);
    if (response.ok) {
      return { ok: true, value: (await response.json()) as T };
    }

    if (response.status === 401) {
      try {
        const denial: unknown = await response.json();
        if (
          typeof denial === "object" &&
          denial !== null &&
          "error" in denial &&
          (denial as { error?: unknown }).error === "unauthorized"
        ) {
          return { ok: false, kind: "unauthorized" };
        }
      } catch {
        // A malformed upstream 401 does not establish that the session expired.
      }
      return { ok: false, kind: "unavailable" };
    }
    if (response.status === 413) {
      return { ok: false, kind: "request_too_large" };
    }
    if (response.status === 429) {
      return { ok: false, kind: "rate_limited" };
    }
    if (response.status === 404) {
      return { ok: false, kind: "not_found" };
    }

    try {
      const failure: unknown = await response.json();
      if (typeof failure === "object" && failure !== null && "error" in failure) {
        const error = (failure as { error?: unknown }).error;
        if (typeof error === "string" && REPORT_ERROR_KINDS.has(error)) {
          return { ok: false, kind: error as ReportErrorKind };
        }
      }
    } catch {
      // An unreadable failure is indistinguishable from an unavailable backend.
    }

    return { ok: false, kind: response.status === 422 ? "receipt_ineligible" : "unavailable" };
  } catch {
    return { ok: false, kind: "unavailable" };
  }
}

export async function queryReport(
  sessionToken: string,
  query: ReportQuery,
): Promise<ReportRequestResult<ReportResponse>> {
  return reportRequest<ReportResponse>(
    sessionToken,
    "POST",
    "/v1/finance/reports/query",
    query as Record<string, unknown>,
  );
}

export async function listSalaryCycles(
  sessionToken: string,
  beforeDate?: string | null,
): Promise<ReportRequestResult<SalaryCycleList>> {
  const query = beforeDate ? `?before_date=${encodeURIComponent(beforeDate)}` : "";
  return reportRequest<SalaryCycleList>(sessionToken, "GET", `/v1/finance/salary-cycles${query}`);
}

export async function previewSalaryCycle(
  sessionToken: string,
  input: {
    action: "confirm" | "remove";
    transaction_id?: string;
    expected_transaction_version?: number;
    start_id?: string;
  },
): Promise<ReportRequestResult<SalaryCyclePreview>> {
  return reportRequest<SalaryCyclePreview>(
    sessionToken,
    "POST",
    "/v1/finance/salary-cycles/preview",
    input as unknown as Record<string, unknown>,
  );
}

export async function confirmSalaryCycle(
  sessionToken: string,
  input: {
    transaction_id: string;
    expected_transaction_version: number;
    expected_revision: string;
    confirm: true;
  },
): Promise<ReportRequestResult<SalaryCycleConfirm>> {
  return reportRequest<SalaryCycleConfirm>(
    sessionToken,
    "POST",
    "/v1/finance/salary-cycles",
    input as unknown as Record<string, unknown>,
  );
}

export async function removeSalaryCycle(
  sessionToken: string,
  id: string,
  input: { expected_revision: string; confirm: true },
): Promise<ReportRequestResult<SalaryCycleRemove>> {
  return reportRequest<SalaryCycleRemove>(
    sessionToken,
    "DELETE",
    `/v1/finance/salary-cycles/${encodeURIComponent(id)}`,
    input,
  );
}
