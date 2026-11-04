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
  | { ok: true; value: T }
  | { ok: false; kind: FinanceRequestErrorKind };

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
  | "invalid_request"
  | "invalid_token"
  | "invalid_credentials"
  | "rate_limited"
  | "unavailable";

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
      if (
        response.status === 400 &&
        (error === "invalid_request" || error === "invalid_token")
      ) {
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

  const response = await fetch(`${backendUrl()}/v1/me`, {
    method: "GET",
    headers: {
      [REQUEST_ID_HEADER]: newRequestId(),
      [APPLICATION_SECRET_HEADER]: requiredEnvironment("ARGUS_API_SECRET"),
      [SESSION_HEADER]: sessionToken,
      [FRONTEND_ORIGIN_HEADER]: requiredEnvironment("ARGUS_FRONTEND_URL"),
    },
    cache: "no-store",
  });
  return readJson<CurrentAccount>(response);
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
  return accountRequest<PasswordResetResponse>(
    "/v1/password/reset",
    { token, password },
    200,
  );
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
