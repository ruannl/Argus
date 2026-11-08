import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("server-only", () => ({}));

import { PRODUCT_TOKEN } from "@/lib/brand";
import {
  abandonTransactionOperation,
  applyTransactionOperation,
  archiveCategory,
  archiveFinancialAccount,
  archiveMerchantTag,
  createCategory,
  createFinancialAccount,
  createMerchantTag,
  commitImportReview,
  deleteCategory,
  deleteImportStatement,
  inspectImportFile,
  deleteMerchantTag,
  getBackendHealth,
  getCurrentAccount,
  getTransaction,
  getTransactionOperation,
  listCategories,
  listFinancialAccounts,
  listImportHistory,
  listMerchantTags,
  listOpenImportReviews,
  loginAccount,
  logoutSession,
  patchTransaction,
  prepareImportReview,
  prepareTransactionOperation,
  queryTransactions,
  requestPasswordReset,
  registerAccount,
  resendVerification,
  resetPassword,
  restoreCategory,
  restoreFinancialAccount,
  restoreMerchantTag,
  updateCategory,
  updateFinancialAccount,
  updateMerchantTag,
  verifyEmail,
} from "@/lib/server/backend-client";

const APPLICATION_SECRET_HEADER = `X-${PRODUCT_TOKEN}-Application-Secret`;
const SESSION_HEADER = `X-${PRODUCT_TOKEN}-Session`;
const FRONTEND_ORIGIN_HEADER = `X-${PRODUCT_TOKEN}-Frontend-Origin`;
const REQUEST_ID_HEADER = "X-Request-Id";

const fetchMock = vi.fn<typeof fetch>();
const originalFetch = globalThis.fetch;

describe("server backend client", () => {
  beforeEach(() => {
    vi.stubEnv("ARGUS_BACKEND_URL", "http://backend.test/");
    vi.stubEnv("ARGUS_API_SECRET", "application-secret");
    vi.stubEnv("ARGUS_FRONTEND_URL", "http://frontend.test");
    fetchMock.mockReset();
    globalThis.fetch = fetchMock;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.unstubAllEnvs();
  });

  it("requests public health without customer credentials", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ status: "ok" }), { status: 200 }));

    await expect(getBackendHealth()).resolves.toEqual({ status: "ok" });
    expect(fetchMock).toHaveBeenCalledWith("http://backend.test/health", {
      method: "GET",
      headers: {
        [REQUEST_ID_HEADER]: expect.any(String),
      },
      cache: "no-store",
    });
  });

  it("sends the application, session, and expected-origin headers to /v1/me", async () => {
    const account = {
      id: "00000000-0000-0000-0000-000000000001",
      email: "customer@example.test",
      email_verified: false,
      subscriber_active: false,
      subscription_started_at: null,
      next_billing_at: null,
      lifetime: false,
      admin: false,
    };
    fetchMock.mockResolvedValue(new Response(JSON.stringify(account), { status: 200 }));

    await expect(getCurrentAccount("opaque-session-token")).resolves.toEqual(account);
    expect(fetchMock).toHaveBeenCalledWith("http://backend.test/v1/me", {
      method: "GET",
      headers: {
        [REQUEST_ID_HEADER]: expect.any(String),
        [APPLICATION_SECRET_HEADER]: "application-secret",
        [SESSION_HEADER]: "opaque-session-token",
        [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
      },
      cache: "no-store",
    });
  });

  it("does not expose backend failure bodies and rejects empty session tokens", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ error: "unauthorized", detail: "PRIVATE-CANARY" }), { status: 401 }));
    await expect(getCurrentAccount("bad-token")).rejects.toMatchObject({
      kind: "unauthorized",
      message: "Account check failed.",
    });
    await expect(getCurrentAccount("")).rejects.toThrow("session token is required");
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("preserves successful account responses with additional fields", async () => {
    const account = {
      id: "00000000-0000-0000-0000-000000000001",
      email: "customer@example.test",
      email_verified: true,
      subscriber_active: true,
      subscription_started_at: null,
      next_billing_at: null,
      lifetime: false,
      admin: false,
      extra_field: "additional backend data",
    };
    fetchMock.mockResolvedValue(new Response(JSON.stringify(account), { status: 200 }));

    await expect(getCurrentAccount("opaque-session-token")).resolves.toEqual(account);
  });

  it.each([
    [401, "unauthorized"],
    [429, "rate_limited"],
    [403, "unavailable"],
    [500, "unavailable"],
    [204, "unavailable"],
  ] as const)("normalizes /v1/me status %i to %s without returning its body", async (status, kind) => {
    const body = status === 401
      ? { error: "unauthorized", detail: "PRIVATE-CANARY" }
      : { error: "PRIVATE-CANARY" };
    fetchMock.mockResolvedValue(new Response(
      status === 204 || status === 403 ? null : JSON.stringify(body), { status },
    ));
    await expect(getCurrentAccount("opaque-session-token")).rejects.toMatchObject({
      kind,
      message: "Account check failed.",
    });
  });

  it.each([
    JSON.stringify({ error: "unexpected", detail: "PRIVATE-CANARY" }),
    "not-json PRIVATE-CANARY",
    "",
  ])("does not treat a noncanonical 401 body as confirmed denial", async (body) => {
    fetchMock.mockResolvedValue(new Response(body, { status: 401 }));
    await expect(getCurrentAccount("opaque-session-token")).rejects.toMatchObject({
      kind: "unavailable",
      message: "Account check failed.",
    });
  });

  it("treats malformed, unexpected and unreachable /v1/me bodies as unavailable", async () => {
    fetchMock.mockResolvedValueOnce(new Response("not-json", { status: 200 }));
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ id: "unexpected-only" }), { status: 200 }));
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({
      id: "account-id",
      email: "customer@example.test",
      email_verified: "true",
      subscriber_active: true,
      subscription_started_at: null,
      next_billing_at: null,
      lifetime: false,
      admin: false,
    }), { status: 200 }));
    fetchMock.mockRejectedValueOnce(new Error("PRIVATE-CANARY backend origin"));
    for (let index = 0; index < 4; index += 1) {
      await expect(getCurrentAccount("opaque-session-token")).rejects.toMatchObject({
        kind: "unavailable",
        message: "Account check failed.",
      });
    }
  });

  it("registers accounts through the trusted backend route", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ status: "accepted" }), { status: 202 }));

    await expect(registerAccount("customer@example.test", "a-compliant-password"))
      .resolves.toEqual({ ok: true, value: { status: "accepted" } });
    expect(fetchMock).toHaveBeenCalledWith("http://backend.test/v1/register", {
      method: "POST",
      headers: {
        [REQUEST_ID_HEADER]: expect.any(String),
        "Content-Type": "application/json",
        [APPLICATION_SECRET_HEADER]: "application-secret",
        [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
      },
      body: JSON.stringify({ email: "customer@example.test", password: "a-compliant-password" }),
      cache: "no-store",
    });
  });

  it("verifies email tokens through the trusted backend route", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ status: "verified" }), { status: 200 }));

    await expect(verifyEmail("a".repeat(64))).resolves.toEqual({
      ok: true,
      value: { status: "verified" },
    });
    expect(fetchMock).toHaveBeenCalledWith("http://backend.test/v1/verify", {
      method: "POST",
      headers: {
        [REQUEST_ID_HEADER]: expect.any(String),
        "Content-Type": "application/json",
        [APPLICATION_SECRET_HEADER]: "application-secret",
        [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
      },
      body: JSON.stringify({ token: "a".repeat(64) }),
      cache: "no-store",
    });
  });

  it("resends verification through the trusted backend route", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ status: "accepted" }), { status: 202 }));

    await expect(resendVerification("customer@example.test"))
      .resolves.toEqual({ ok: true, value: { status: "accepted" } });
    expect(fetchMock).toHaveBeenCalledWith("http://backend.test/v1/verification/resend", {
      method: "POST",
      headers: {
        [REQUEST_ID_HEADER]: expect.any(String),
        "Content-Type": "application/json",
        [APPLICATION_SECRET_HEADER]: "application-secret",
        [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
      },
      body: JSON.stringify({ email: "customer@example.test" }),
      cache: "no-store",
    });
  });

  it("requests a password reset through the trusted backend route", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ status: "accepted" }), { status: 202 }));

    await expect(requestPasswordReset("customer@example.test")).resolves.toEqual({
      ok: true,
      value: { status: "accepted" },
    });
    expect(fetchMock).toHaveBeenCalledWith("http://backend.test/v1/password/reset/request", {
      method: "POST",
      headers: {
        [REQUEST_ID_HEADER]: expect.any(String),
        "Content-Type": "application/json",
        [APPLICATION_SECRET_HEADER]: "application-secret",
        [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
      },
      body: JSON.stringify({ email: "customer@example.test" }),
      cache: "no-store",
    });
  });

  it("consumes a password reset through the trusted backend route", async () => {
    const response = { status: "reset" as const };
    fetchMock.mockResolvedValue(new Response(JSON.stringify(response), { status: 200 }));

    await expect(resetPassword("a".repeat(64), "a compliant reset passphrase")).resolves.toEqual({
      ok: true,
      value: response,
    });
    expect(fetchMock).toHaveBeenCalledWith("http://backend.test/v1/password/reset", {
      method: "POST",
      headers: {
        [REQUEST_ID_HEADER]: expect.any(String),
        "Content-Type": "application/json",
        [APPLICATION_SECRET_HEADER]: "application-secret",
        [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
      },
      body: JSON.stringify({
        token: "a".repeat(64),
        password: "a compliant reset passphrase",
      }),
      cache: "no-store",
    });
  });

  it("classifies password reset request and consume failures by backend error kind", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "invalid_request" }), { status: 400 }),
    );
    await expect(requestPasswordReset("not-an-email")).resolves.toEqual({
      ok: false,
      kind: "invalid_request",
    });

    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "invalid_token" }), { status: 400 }),
    );
    await expect(resetPassword("b".repeat(64), "a compliant reset passphrase")).resolves.toEqual({
      ok: false,
      kind: "invalid_token",
    });

    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ error: "rate_limited" }), { status: 429 }));
    await expect(requestPasswordReset("customer@example.test")).resolves.toEqual({
      ok: false,
      kind: "rate_limited",
    });
  });

  it("logs in through the trusted backend route and returns the pinned shape", async () => {
    const response = {
      token: "opaque-session-token",
      expires_at: "2026-08-23T05:32:00Z",
    };
    fetchMock.mockResolvedValue(new Response(JSON.stringify(response), { status: 200 }));

    await expect(loginAccount("customer@example.test", "a-compliant-password"))
      .resolves.toEqual({ ok: true, value: response });
    expect(fetchMock).toHaveBeenCalledWith("http://backend.test/v1/login", {
      method: "POST",
      headers: {
        [REQUEST_ID_HEADER]: expect.any(String),
        "Content-Type": "application/json",
        [APPLICATION_SECRET_HEADER]: "application-secret",
        [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
      },
      body: JSON.stringify({ email: "customer@example.test", password: "a-compliant-password" }),
      cache: "no-store",
    });
  });

  it("classifies login credential failures without exposing backend details", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ error: "invalid_credentials" }), { status: 401 }),
    );

    await expect(loginAccount("customer@example.test", "wrong-password"))
      .resolves.toEqual({ ok: false, kind: "invalid_credentials" });
  });

  it("classifies login structural failures and rate limits", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "invalid_request" }), { status: 400 }),
    );
    await expect(loginAccount("customer@example.test", "password"))
      .resolves.toEqual({ ok: false, kind: "invalid_request" });

    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "not exposed" }), { status: 429 }),
    );
    await expect(loginAccount("customer@example.test", "password"))
      .resolves.toEqual({ ok: false, kind: "rate_limited" });
  });

  it("collapses other login 401 responses to unavailable", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ error: "unauthorized" }), { status: 401 }),
    );

    await expect(loginAccount("customer@example.test", "password"))
      .resolves.toEqual({ ok: false, kind: "unavailable" });
  });

  it("logs out with the session header and no JSON body", async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));

    await expect(logoutSession("opaque-session-token")).resolves.toEqual({
      ok: true,
      value: null,
    });
    expect(fetchMock).toHaveBeenCalledWith("http://backend.test/v1/logout", {
      method: "POST",
      headers: {
        [REQUEST_ID_HEADER]: expect.any(String),
        "Content-Type": "application/json",
        [APPLICATION_SECRET_HEADER]: "application-secret",
        [SESSION_HEADER]: "opaque-session-token",
        [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
      },
      cache: "no-store",
    });
  });

  it("collapses every non-204 logout response to unavailable", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ error: "unauthorized" }), { status: 401 }));

    await expect(logoutSession("opaque-session-token")).resolves.toEqual({
      ok: false,
      kind: "unavailable",
    });
  });

  it("classifies a 400 invalid request from the backend", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ error: "invalid_request" }), { status: 400 }),
    );

    await expect(registerAccount("not-an-email", "short"))
      .resolves.toEqual({ ok: false, kind: "invalid_request" });
  });

  it("classifies a 400 invalid token from the backend", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ error: "invalid_token" }), { status: 400 }),
    );

    await expect(verifyEmail("a".repeat(64)))
      .resolves.toEqual({ ok: false, kind: "invalid_token" });
  });

  it("classifies rate limits before inspecting the response body", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ error: "backend detail" }), { status: 429 }),
    );

    await expect(resendVerification("customer@example.test"))
      .resolves.toEqual({ ok: false, kind: "rate_limited" });
  });

  it("collapses unknown 400 bodies and other HTTP failures to unavailable", async () => {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ error: "other" }), { status: 400 }));
    await expect(registerAccount("customer@example.test", "password"))
      .resolves.toEqual({ ok: false, kind: "unavailable" });

    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "sensitive backend detail" }), { status: 500 }),
    );
    await expect(resendVerification("customer@example.test"))
      .resolves.toEqual({ ok: false, kind: "unavailable" });

    fetchMock.mockResolvedValueOnce(new Response("not json", { status: 400 }));
    await expect(verifyEmail("a".repeat(64)))
      .resolves.toEqual({ ok: false, kind: "unavailable" });
  });

  it("returns unavailable instead of throwing for missing environment", async () => {
    vi.stubEnv("ARGUS_BACKEND_URL", "");

    await expect(registerAccount("customer@example.test", "password"))
      .resolves.toEqual({ ok: false, kind: "unavailable" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("returns unavailable instead of throwing when fetch rejects", async () => {
    fetchMock.mockRejectedValue(new Error("network failure with secret details"));

    await expect(verifyEmail("a".repeat(64)))
      .resolves.toEqual({ ok: false, kind: "unavailable" });
  });
});

describe("server finance client", () => {
  function postNoBodyInit() {
    return {
      method: "POST",
      headers: {
        [REQUEST_ID_HEADER]: expect.any(String),
        [APPLICATION_SECRET_HEADER]: "application-secret",
        [SESSION_HEADER]: "opaque-session-token",
        [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
      },
      cache: "no-store",
    };
  }

  const account = {
    id: "00000000-0000-0000-0000-0000000000aa",
    display_name: "Cheque",
    institution: "FNB",
    currency: "ZAR",
    is_archived: false,
    transaction_count: 0,
    created_at: "2026-01-01T00:00:00.0000000Z",
    updated_at: "2026-01-01T00:00:00.0000000Z",
  };

  const category = {
    id: "00000000-0000-0000-0000-0000000000bb",
    display_name: "Groceries",
    kind: "Expense",
    is_system: false,
    is_default: true,
    is_archived: false,
    transaction_count: 0,
    created_at: "2026-01-01T00:00:00.0000000Z",
    updated_at: "2026-01-01T00:00:00.0000000Z",
  };

  const tag = {
    id: "00000000-0000-0000-0000-0000000000cc",
    display_name: "Woolworths",
    is_archived: false,
    transaction_count: 0,
    created_at: "2026-01-01T00:00:00.0000000Z",
    updated_at: "2026-01-01T00:00:00.0000000Z",
  };

  function getInit() {
    return {
      method: "GET",
      headers: {
        [REQUEST_ID_HEADER]: expect.any(String),
        [APPLICATION_SECRET_HEADER]: "application-secret",
        [SESSION_HEADER]: "opaque-session-token",
        [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
      },
      cache: "no-store",
    };
  }

  function writeInit(method: string, body: unknown) {
    const init: Record<string, unknown> = {
      method,
      headers: {
        [REQUEST_ID_HEADER]: expect.any(String),
        [APPLICATION_SECRET_HEADER]: "application-secret",
        [SESSION_HEADER]: "opaque-session-token",
        [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
        "Content-Type": "application/json",
      },
      cache: "no-store",
    };
    if (body !== undefined) {
      init.body = JSON.stringify(body);
    }
    return init;
  }

  beforeEach(() => {
    vi.stubEnv("ARGUS_BACKEND_URL", "http://backend.test/");
    vi.stubEnv("ARGUS_API_SECRET", "application-secret");
    vi.stubEnv("ARGUS_FRONTEND_URL", "http://frontend.test");
    fetchMock.mockReset();
    globalThis.fetch = fetchMock;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.unstubAllEnvs();
  });

  it("lists financial accounts with the session headers and no body", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ accounts: [account] }), { status: 200 }),
    );

    await expect(listFinancialAccounts("opaque-session-token")).resolves.toEqual({
      ok: true,
      value: { accounts: [account] },
    });
    expect(fetchMock).toHaveBeenCalledWith(
      "http://backend.test/v1/finance/accounts",
      getInit(),
    );
  });

  it("creates an account with an institution and omits it when absent", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(account), { status: 201 }));

    await createFinancialAccount("opaque-session-token", {
      display_name: "Cheque",
      institution: "FNB",
    });
    expect(fetchMock).toHaveBeenLastCalledWith(
      "http://backend.test/v1/finance/accounts",
      writeInit("POST", { display_name: "Cheque", institution: "FNB" }),
    );

    await createFinancialAccount("opaque-session-token", { display_name: "Cheque" });
    expect(fetchMock).toHaveBeenLastCalledWith(
      "http://backend.test/v1/finance/accounts",
      writeInit("POST", { display_name: "Cheque" }),
    );
  });

  it("preserves institution on a rename-only update and clears it when null", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(account), { status: 200 }));

    await updateFinancialAccount("opaque-session-token", account.id, {
      display_name: "Main cheque",
    });
    expect(fetchMock).toHaveBeenLastCalledWith(
      `http://backend.test/v1/finance/accounts/${account.id}`,
      writeInit("PATCH", { display_name: "Main cheque" }),
    );

    await updateFinancialAccount("opaque-session-token", account.id, {
      display_name: "Main cheque",
      institution: null,
    });
    expect(fetchMock).toHaveBeenLastCalledWith(
      `http://backend.test/v1/finance/accounts/${account.id}`,
      writeInit("PATCH", { display_name: "Main cheque", institution: null }),
    );
  });

  it("archives and restores accounts", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(account), { status: 200 }));

    await archiveFinancialAccount("opaque-session-token", account.id);
    expect(fetchMock).toHaveBeenLastCalledWith(
      `http://backend.test/v1/finance/accounts/${account.id}/archive`,
      postNoBodyInit(),
    );

    await restoreFinancialAccount("opaque-session-token", account.id);
    expect(fetchMock).toHaveBeenLastCalledWith(
      `http://backend.test/v1/finance/accounts/${account.id}/restore`,
      postNoBodyInit(),
    );
  });

  it("creates and updates categories with an explicit kind confirmation", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(category), { status: 201 }));

    await createCategory("opaque-session-token", {
      display_name: "Groceries",
      kind: "Expense",
    });
    expect(fetchMock).toHaveBeenLastCalledWith(
      "http://backend.test/v1/finance/categories",
      writeInit("POST", { display_name: "Groceries", kind: "Expense" }),
    );

    await updateCategory("opaque-session-token", category.id, {
      display_name: "Groceries",
      kind: "Income",
      confirm_kind_change: true,
    });
    expect(fetchMock).toHaveBeenLastCalledWith(
      `http://backend.test/v1/finance/categories/${category.id}`,
      writeInit("PATCH", {
        display_name: "Groceries",
        kind: "Income",
        confirm_kind_change: true,
      }),
    );
  });

  it("lists, archives, restores, and deletes categories", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ categories: [category] }), { status: 200 }),
    );
    await listCategories("opaque-session-token");
    expect(fetchMock).toHaveBeenLastCalledWith(
      "http://backend.test/v1/finance/categories",
      getInit(),
    );

    fetchMock.mockResolvedValue(new Response(JSON.stringify(category), { status: 200 }));
    await archiveCategory("opaque-session-token", category.id);
    expect(fetchMock).toHaveBeenLastCalledWith(
      `http://backend.test/v1/finance/categories/${category.id}/archive`,
      postNoBodyInit(),
    );

    await restoreCategory("opaque-session-token", category.id);
    expect(fetchMock).toHaveBeenLastCalledWith(
      `http://backend.test/v1/finance/categories/${category.id}/restore`,
      postNoBodyInit(),
    );

    fetchMock.mockResolvedValue(
      new Response(
        JSON.stringify({
          deleted: true,
          reassigned_transaction_count: 2,
          uncategorised_category_id: "00000000-0000-0000-0000-0000000000dd",
        }),
        { status: 200 },
      ),
    );
    await expect(deleteCategory("opaque-session-token", category.id)).resolves.toEqual({
      ok: true,
      value: {
        deleted: true,
        reassigned_transaction_count: 2,
        uncategorised_category_id: "00000000-0000-0000-0000-0000000000dd",
      },
    });
    expect(fetchMock).toHaveBeenLastCalledWith(
      `http://backend.test/v1/finance/categories/${category.id}`,
      {
        method: "DELETE",
        headers: {
          [REQUEST_ID_HEADER]: expect.any(String),
          [APPLICATION_SECRET_HEADER]: "application-secret",
          [SESSION_HEADER]: "opaque-session-token",
          [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
        },
        cache: "no-store",
      },
    );
  });

  it("lists, creates, updates, archives, restores, and deletes merchant tags", async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ merchant_tags: [tag] }), { status: 200 }),
    );
    await listMerchantTags("opaque-session-token");
    expect(fetchMock).toHaveBeenLastCalledWith(
      "http://backend.test/v1/finance/merchant-tags",
      getInit(),
    );

    fetchMock.mockResolvedValue(new Response(JSON.stringify(tag), { status: 201 }));
    await createMerchantTag("opaque-session-token", "Woolworths");
    expect(fetchMock).toHaveBeenLastCalledWith(
      "http://backend.test/v1/finance/merchant-tags",
      writeInit("POST", { display_name: "Woolworths" }),
    );

    fetchMock.mockResolvedValue(new Response(JSON.stringify(tag), { status: 200 }));
    await updateMerchantTag("opaque-session-token", tag.id, "Woolworths");
    expect(fetchMock).toHaveBeenLastCalledWith(
      `http://backend.test/v1/finance/merchant-tags/${tag.id}`,
      writeInit("PATCH", { display_name: "Woolworths" }),
    );

    await archiveMerchantTag("opaque-session-token", tag.id);
    expect(fetchMock).toHaveBeenLastCalledWith(
      `http://backend.test/v1/finance/merchant-tags/${tag.id}/archive`,
      postNoBodyInit(),
    );

    await restoreMerchantTag("opaque-session-token", tag.id);
    expect(fetchMock).toHaveBeenLastCalledWith(
      `http://backend.test/v1/finance/merchant-tags/${tag.id}/restore`,
      postNoBodyInit(),
    );

    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ deleted: true, unassigned_transaction_count: 3 }), {
        status: 200,
      }),
    );
    await expect(deleteMerchantTag("opaque-session-token", tag.id)).resolves.toEqual({
      ok: true,
      value: { deleted: true, unassigned_transaction_count: 3 },
    });
    expect(fetchMock).toHaveBeenLastCalledWith(
      `http://backend.test/v1/finance/merchant-tags/${tag.id}`,
      {
        method: "DELETE",
        headers: {
          [REQUEST_ID_HEADER]: expect.any(String),
          [APPLICATION_SECRET_HEADER]: "application-secret",
          [SESSION_HEADER]: "opaque-session-token",
          [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
        },
        cache: "no-store",
      },
    );
  });

  it("classifies finance failures by backend error kind", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "duplicate_name" }), { status: 409 }),
    );
    await expect(createMerchantTag("opaque-session-token", "Woolworths")).resolves.toEqual({
      ok: false,
      kind: "duplicate_name",
    });

    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "system_category_immutable" }), { status: 400 }),
    );
    await expect(
      createCategory("opaque-session-token", { display_name: "X", kind: "Expense" }),
    ).resolves.toEqual({ ok: false, kind: "system_category_immutable" });

    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "kind_change_confirmation_required" }), {
        status: 400,
      }),
    );
    await expect(
      updateCategory("opaque-session-token", category.id, {
        display_name: "Groceries",
        kind: "Income",
      }),
    ).resolves.toEqual({ ok: false, kind: "kind_change_confirmation_required" });

    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "invalid_request" }), { status: 400 }),
    );
    await expect(
      createCategory("opaque-session-token", { display_name: "", kind: "Expense" }),
    ).resolves.toEqual({ ok: false, kind: "invalid_request" });

    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "not_found" }), { status: 404 }),
    );
    await expect(deleteCategory("opaque-session-token", category.id)).resolves.toEqual({
      ok: false,
      kind: "not_found",
    });

    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "rate_limited" }), { status: 429 }),
    );
    await expect(listFinancialAccounts("opaque-session-token")).resolves.toEqual({
      ok: false,
      kind: "rate_limited",
    });

    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "unauthorized" }), { status: 401 }),
    );
    await expect(listFinancialAccounts("opaque-session-token")).resolves.toEqual({
      ok: false,
      kind: "unavailable",
    });

    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "sensitive backend detail" }), { status: 500 }),
    );
    await expect(listCategories("opaque-session-token")).resolves.toEqual({
      ok: false,
      kind: "unavailable",
    });
  });

  it("returns unavailable without fetching when the session token is empty", async () => {
    await expect(listFinancialAccounts("")).resolves.toEqual({
      ok: false,
      kind: "unavailable",
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("server import client", () => {
  beforeEach(() => {
    vi.stubEnv("ARGUS_BACKEND_URL", "http://backend.test/");
    vi.stubEnv("ARGUS_API_SECRET", "application-secret");
    vi.stubEnv("ARGUS_FRONTEND_URL", "http://frontend.test");
    fetchMock.mockReset();
    globalThis.fetch = fetchMock;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.unstubAllEnvs();
  });

  const review = {
    summary: {
      id: "00000000-0000-0000-0000-000000000001",
      financial_account_id: "00000000-0000-0000-0000-000000000002",
      source_kind: "fnb_csv",
      start_date: "2024-01-01",
      end_date: "2024-01-31",
      version: 1,
      status: "Prepared",
      expires_at: "2030-01-02T00:00:00.0000000Z",
      total_rows: 2,
      imported_count: 2,
      suppressed_count: 0,
      skipped_count: 0,
      possible_duplicate_count: 0,
      exact_override_count: 0,
      statement_override: false,
      statement_override_count: 0,
      summary_hash: "hash",
    },
    page: { rows: [], total: 0, page: 1, page_size: 100 },
  };

  it("uploads inspect multipart with trust headers and no JSON content type", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({
        detected_source_kind: "fnb_csv",
        limits: { max_file_bytes: 1, max_data_rows: 1, max_pdf_pages: 1, max_description_scalars: 1 },
        proposed_start_date: null,
        proposed_end_date: null,
        mapping: {},
        required_confirmations: [],
        warnings: [],
        preview: [],
        preview_truncated: false,
        can_prepare: true,
      }), { status: 200 }),
    );

    await expect(inspectImportFile(
      "opaque-session-token",
      new Uint8Array([1, 2, 3]),
      { financial_account_id: "account", source_kind: "fnb" },
    )).resolves.toEqual(expect.objectContaining({ ok: true }));

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://backend.test/v1/finance/imports/inspect");
    expect(init?.method).toBe("POST");
    expect(init?.headers).toEqual(expect.objectContaining({
      [APPLICATION_SECRET_HEADER]: "application-secret",
      [SESSION_HEADER]: "opaque-session-token",
      [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
    }));
    expect((init?.headers as Record<string, string>)["Content-Type"]).toBeUndefined();
    expect(init?.body).toBeInstanceOf(FormData);
    const form = init?.body as FormData;
    expect(JSON.parse(String(form.get("options")))).toEqual({
      financial_account_id: "account",
      source_kind: "fnb",
    });
    expect(form.get("file")).toBeInstanceOf(Blob);
  });

  it("prepares a review through the multipart route", async () => {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(review), { status: 201 }));

    await expect(prepareImportReview(
      "opaque-session-token",
      new Uint8Array([1]),
      { financial_account_id: "account", source_kind: "fnb" },
    )).resolves.toEqual({ ok: true, value: review });
    expect(fetchMock.mock.calls[0][0]).toBe("http://backend.test/v1/finance/imports/reviews");
  });

  it("classifies a deferred PDF upload as a specific error", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: "pdf_import_deferred" }), { status: 400 }),
    );

    await expect(inspectImportFile(
      "opaque-session-token",
      new Uint8Array([1]),
      { financial_account_id: "account", source_kind: "standard_bank_pdf" },
    )).resolves.toEqual({ ok: false, kind: "pdf_import_deferred" });
  });

  it("returns statement repeat IDs from the commit error body", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({
        error: "statement_repeat",
        existing_import_ids: ["00000000-0000-0000-0000-000000000010"],
      }), { status: 409 }),
    );

    await expect(commitImportReview("opaque-session-token", "review", 2, "hash")).resolves.toEqual({
      ok: false,
      kind: "statement_repeat",
      existingImportIds: ["00000000-0000-0000-0000-000000000010"],
    });
  });

  it("maps a replayed commit and sends the confirmation body", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ replayed: true, statement: {} }), { status: 200 }),
    );

    await expect(commitImportReview("opaque-session-token", "review", 3, "hash")).resolves.toEqual({
      ok: true,
      value: { replayed: true, statement: {} },
    });
    expect(fetchMock.mock.calls[0][1]?.body).toBe(
      JSON.stringify({ expected_review_version: 3, confirmed_summary_hash: "hash" }),
    );
  });

  it("lists history with account, state, cursor, and limit query values", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ items: [], next_cursor: null }), { status: 200 }),
    );

    await listImportHistory("opaque-session-token", {
      accountId: "account",
      state: "hidden",
      cursor: "next",
      limit: 25,
    });
    expect(fetchMock.mock.calls[0][0]).toBe(
      "http://backend.test/v1/finance/imports?account_id=account&state=hidden&cursor=next&limit=25",
    );
  });

  it("sends DELETE with the confirmation JSON body", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ deleted: true, deleted_transaction_count: 4 }), { status: 200 }),
    );

    await expect(deleteImportStatement("opaque-session-token", "statement", 2, 4)).resolves.toEqual({
      ok: true,
      value: { deleted: true, deleted_transaction_count: 4 },
    });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://backend.test/v1/finance/imports/statement");
    expect(init?.method).toBe("DELETE");
    expect(init?.body).toBe(JSON.stringify({
      expected_version: 2,
      expected_transaction_count: 4,
      confirm: true,
    }));
  });

  it("returns unavailable without fetching when the session token is empty", async () => {
    await expect(listOpenImportReviews("")).resolves.toEqual({
      ok: false,
      kind: "unavailable",
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("server transaction client", () => {
  beforeEach(() => {
    vi.stubEnv("ARGUS_BACKEND_URL", "http://backend.test/");
    vi.stubEnv("ARGUS_API_SECRET", "application-secret");
    vi.stubEnv("ARGUS_FRONTEND_URL", "http://frontend.test");
    fetchMock.mockReset();
    globalThis.fetch = fetchMock;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.unstubAllEnvs();
  });

  const page = {
    transactions: [],
    inventory_count: 0,
    matching_visible_count: 0,
    matching_hidden_count: 0,
    matching_total: 0,
    filtered_total: 0,
    page: 1,
    page_size: 25,
    sort_by: "date",
    sort_direction: "desc",
  };

  it("queries through a body-only POST with the trusted headers", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(page), { status: 200 }));

    await expect(queryTransactions("opaque-session-token", {
      filters: { search: "private merchant", visibility: "hidden" },
      page: 2,
      page_size: 50,
      sort_by: "amount",
      sort_direction: "asc",
    })).resolves.toEqual({ ok: true, value: page });

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://backend.test/v1/finance/transactions/query");
    expect(init?.method).toBe("POST");
    expect(init?.headers).toEqual({
      [REQUEST_ID_HEADER]: expect.any(String),
      "Content-Type": "application/json",
      [APPLICATION_SECRET_HEADER]: "application-secret",
      [SESSION_HEADER]: "opaque-session-token",
      [FRONTEND_ORIGIN_HEADER]: "http://frontend.test",
    });
    expect(init?.body).toBe(JSON.stringify({
      filters: { search: "private merchant", visibility: "hidden" },
      page: 2,
      page_size: 50,
      sort_by: "amount",
      sort_direction: "asc",
    }));
    expect(init?.cache).toBe("no-store");
  });

  it("loads one owned transaction detail by encoded id", async () => {
    const detail = {
      transaction: { id: "transaction-id" },
      suggested_tags: [],
      has_more_suggestions: false,
      source_page: null,
      source_row_number: 4,
    };
    fetchMock.mockResolvedValue(new Response(JSON.stringify(detail), { status: 200 }));

    await expect(getTransaction("opaque-session-token", "transaction/id")).resolves.toEqual({
      ok: true,
      value: detail,
    });
    expect(fetchMock.mock.calls[0][0]).toBe(
      "http://backend.test/v1/finance/transactions/transaction%2Fid",
    );
  });

  it("patches with expected version and exact action fields", async () => {
    const result = { transaction: {}, selected_count: 1, changed_count: 1, unchanged_count: 0 };
    fetchMock.mockResolvedValue(new Response(JSON.stringify(result), { status: 200 }));

    await expect(patchTransaction("opaque-session-token", "transaction-id", {
      expected_version: 3,
      changes: { category_id: "category-id", add_tag_ids: ["tag-a"], remove_tag_ids: ["tag-b"] },
    })).resolves.toEqual({ ok: true, value: result });
    expect(fetchMock.mock.calls[0][1]?.method).toBe("PATCH");
    expect(fetchMock.mock.calls[0][1]?.body).toBe(JSON.stringify({
      expected_version: 3,
      changes: { category_id: "category-id", add_tag_ids: ["tag-a"], remove_tag_ids: ["tag-b"] },
    }));
  });

  it("prepares, applies, and abandons operations", async () => {
    const operation = {
      operation_id: "operation-id",
      status: "Prepared",
      selection_mode: "all_matching",
      captured_at: "2026-01-01T00:00:00Z",
      expires_at: "2026-01-01T00:15:00Z",
      selected_count: 2,
      changed_count: 1,
      unchanged_count: 1,
      confirmation_hash: "hash",
      applied_at: null,
      terminal_at: null,
      action: null,
    };
    fetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify({ operation, created: true }), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        operation_id: "operation-id",
        status: "Applied",
        selected_count: 2,
        changed_count: 1,
        unchanged_count: 1,
        applied_at: "2026-01-01T00:01:00Z",
        replayed: false,
      }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...operation, status: "Abandoned" }), { status: 200 }));

    await prepareTransactionOperation("opaque-session-token", {
      operation_id: "operation-id",
      selection: { mode: "all_matching", filters: { category_id: "category-id" } },
      changes: { is_hidden_by_user: true },
    });
    expect(fetchMock.mock.calls[0][0]).toBe(
      "http://backend.test/v1/finance/transactions/operations/prepare",
    );

    await applyTransactionOperation("opaque-session-token", "operation-id", "hash", 2);
    expect(fetchMock.mock.calls[1][0]).toBe(
      "http://backend.test/v1/finance/transactions/operations/operation-id/apply",
    );
    expect(fetchMock.mock.calls[1][1]?.body).toBe(JSON.stringify({
      confirmation_hash: "hash",
      expected_selected_count: 2,
      confirm: true,
    }));

    await abandonTransactionOperation("opaque-session-token", "operation-id");
    expect(fetchMock.mock.calls[2][0]).toBe(
      "http://backend.test/v1/finance/transactions/operations/operation-id/abandon",
    );
    expect(fetchMock.mock.calls[2][1]?.body).toBe("{}");
  });

  it("maps fixed error codes without leaking bodies", async () => {
    const cases: [number, unknown, string][] = [
      [401, { error: "unauthorized" }, "unauthorized"],
      [404, { error: "not_found" }, "not_found"],
      [409, { error: "version_conflict" }, "version_conflict"],
      [409, { error: "selection_stale" }, "selection_stale"],
      [409, { error: "classification_changed" }, "classification_changed"],
      [409, { error: "parent_hidden" }, "parent_hidden"],
      [409, { error: "idempotency_conflict" }, "idempotency_conflict"],
      [409, { error: "operation_busy" }, "operation_busy"],
      [410, { error: "operation_expired" }, "operation_expired"],
      [410, { error: "operation_abandoned" }, "operation_abandoned"],
      [413, { error: "request_too_large" }, "request_too_large"],
      [429, { error: "operation_quota" }, "rate_limited"],
      [400, { error: "unknown_error" }, "unavailable"],
      [400, { something: "else" }, "unavailable"],
    ];

    for (const [status, body, kind] of cases) {
      fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(body), { status }));
      await expect(queryTransactions("opaque-session-token", {})).resolves.toEqual({
        ok: false,
        kind,
      });
    }
  });

  it("maps 410 and empty session tokens safely", async () => {
    fetchMock.mockResolvedValueOnce(new Response("not json", { status: 410 }));
    await expect(getTransactionOperation("opaque-session-token", "operation-id")).resolves.toEqual({
      ok: false,
      kind: "operation_expired",
    });

    fetchMock.mockRejectedValueOnce(new Error("network failure with secret details"));
    await expect(getTransaction("opaque-session-token", "transaction-id")).resolves.toEqual({
      ok: false,
      kind: "unavailable",
    });

    await expect(queryTransactions("", {})).resolves.toEqual({
      ok: false,
      kind: "unavailable",
    });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
