import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("server-only", () => ({}));

import { PRODUCT_TOKEN } from "@/lib/brand";
import {
  getBackendHealth,
  getCurrentAccount,
  loginAccount,
  logoutSession,
  requestPasswordReset,
  registerAccount,
  resendVerification,
  resetPassword,
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
