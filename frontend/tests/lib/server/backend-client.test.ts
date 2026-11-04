import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("server-only", () => ({}));

import { PRODUCT_TOKEN } from "@/lib/brand";
import {
  archiveCategory,
  archiveFinancialAccount,
  archiveMerchantTag,
  createCategory,
  createFinancialAccount,
  createMerchantTag,
  deleteCategory,
  deleteMerchantTag,
  getBackendHealth,
  getCurrentAccount,
  listCategories,
  listFinancialAccounts,
  listMerchantTags,
  loginAccount,
  logoutSession,
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
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ error: "unauthorized" }), { status: 401 }));
    await expect(getCurrentAccount("bad-token")).rejects.toThrow("backend request failed");
    await expect(getCurrentAccount("")).rejects.toThrow("session token is required");
    expect(fetchMock).toHaveBeenCalledTimes(1);
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
