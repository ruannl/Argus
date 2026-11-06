/** @vitest-environment node */

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.unmock("next/server");

const runtime = vi.hoisted(() => {
  process.env.ARGUS_AUTH_SECRET = "test-only-auth-secret-placeholder-32-bytes";
  process.env.ARGUS_AUTH_TRUST_HOST = "true";
  process.env.AUTH_URL = "http://127.0.0.1:3000/api/auth";
  return { headers: new Headers() };
});

vi.mock("next/headers", () => ({
  headers: async () => runtime.headers,
  cookies: async () => ({
    get: () => undefined,
    set: () => undefined,
    delete: () => undefined,
  }),
}));

import { encode } from "next-auth/jwt";
import { NextRequest } from "next/server";

import { auth, handlers } from "@/auth";
import { PRODUCT_SLUG, PRODUCT_TOKEN } from "@/lib/brand";
import { getCurrentAccount } from "@/lib/server/backend-client";

const cookieName = `${PRODUCT_SLUG}-authjs.session-token`;
const fetchMock = vi.fn<typeof fetch>();
const originalFetch = globalThis.fetch;

async function sessionCookie(): Promise<string> {
  const secret = process.env.ARGUS_AUTH_SECRET;
  if (!secret) throw new Error("missing test auth secret");
  const jwt = await encode({
    token: {
      backendToken: "backend-token",
      expiresAt: "2030-01-01T00:00:00Z",
      sub: "backend-session",
    },
    secret,
    salt: cookieName,
    maxAge: 12 * 60 * 60,
  });
  return `${cookieName}=${jwt}`;
}

describe("pinned Auth.js composition", () => {
  beforeEach(() => {
    fetchMock.mockReset();
    globalThis.fetch = fetchMock;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    runtime.headers = new Headers();
  });

  it("reaches the server session and supplies the token to a server client call", async () => {
    runtime.headers = new Headers({
      cookie: await sessionCookie(),
      "x-forwarded-proto": "http",
      host: "127.0.0.1:3000",
    });
    vi.stubEnv("ARGUS_BACKEND_URL", "http://backend.test/");
    vi.stubEnv("ARGUS_API_SECRET", "application-secret");
    vi.stubEnv("ARGUS_FRONTEND_URL", "http://frontend.test");
    fetchMock.mockResolvedValue(new Response(JSON.stringify({
      id: "account-id",
      email: "customer@example.test",
      email_verified: true,
      subscriber_active: true,
      subscription_started_at: "2030-01-01T00:00:00Z",
      next_billing_at: null,
      lifetime: false,
      admin: false,
    }), { status: 200 }));

    const session = await auth();
    expect(session?.user?.backendToken).toBe("backend-token");

    await getCurrentAccount(session!.user!.backendToken!);
    const headers = fetchMock.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers[`X-${PRODUCT_TOKEN}-Session`]).toBe("backend-token");

    vi.unstubAllEnvs();
  });

  it("does not leak backend credentials through the public session handler", async () => {
    const response = await handlers.GET(new NextRequest(
      "http://127.0.0.1:3000/api/auth/session",
      { headers: { cookie: await sessionCookie() } },
    ));
    const text = await response.text();

    expect(text).not.toContain("backend-token");
    expect(text).not.toContain("expiresAt");
  });
});
