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
