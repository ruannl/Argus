import { describe, expect, it, vi } from "vitest";

vi.hoisted(() => {
  process.env.ARGUS_AUTH_SECRET = "test-only-auth-secret-placeholder-32-bytes";
});

vi.mock("next-auth", () => ({
  default: (config: unknown) => ({
    auth: vi.fn(),
    handlers: { GET: vi.fn(), POST: vi.fn() },
    signIn: vi.fn(),
    signOut: vi.fn(),
    config,
  }),
}));

vi.mock("next-auth/providers/credentials", () => ({
  default: (config: Record<string, unknown>) => ({
    id: "credentials",
    type: "credentials",
    ...config,
  }),
}));

import { authConfig } from "@/auth";

describe("Auth.js configuration", () => {
  it("keeps backend credentials out of the client session callback", async () => {
    const callback = authConfig.callbacks?.session;
    expect(callback).toBeDefined();

    const result = await callback?.({
      session: {
        expires: "2030-01-01T00:00:00.000Z",
        authenticated: true,
        user: {
          id: "backend-session",
          backendToken: "backend-token",
          expiresAt: "2030-01-01T00:00:00Z",
        },
      },
      token: {
        backendToken: "backend-token",
        expiresAt: "2030-01-01T00:00:00Z",
      },
      user: {
        id: "backend-session",
        backendToken: "backend-token",
        expiresAt: "2030-01-01T00:00:00Z",
      },
      trigger: "signIn",
    } as never);

    expect(result).toEqual({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
    });
    expect(result).not.toHaveProperty("backendToken");
    expect(result).not.toHaveProperty("expiresAt");
  });

  it("accepts only bounded backend tokens and uses a stable non-identifying id", async () => {
    const provider = authConfig.providers[0];
    if (!("authorize" in provider) || !provider.authorize) {
      throw new Error("credentials provider is not configured");
    }

    await expect(
      provider.authorize(
        {
          backendToken: "backend-token",
          expiresAt: "2030-01-01T00:00:00Z",
        },
        new Request("http://127.0.0.1/login"),
      ),
    ).resolves.toMatchObject({
      id: "backend-session",
      backendToken: "backend-token",
      expiresAt: "2030-01-01T00:00:00Z",
    });

    await expect(
      provider.authorize(
        { backendToken: "x".repeat(1025) },
        new Request("http://127.0.0.1/login"),
      ),
    ).resolves.toBeNull();
  });

  it("pins the JWT cookie policy and nominal lifetime", () => {
    expect(authConfig.session).toEqual({ strategy: "jwt", maxAge: 43_200 });
    // Host trust is env-driven and fail-closed: this test file never sets
    // ARGUS_AUTH_TRUST_HOST, so the default must be false.
    expect(authConfig.trustHost).toBe(false);
    expect(authConfig.cookies?.sessionToken).toMatchObject({
      options: {
        httpOnly: true,
        sameSite: "lax",
        path: "/",
        secure: false,
      },
    });
  });

  it("enables host trust only for the literal true", async () => {
    vi.resetModules();
    process.env.ARGUS_AUTH_TRUST_HOST = "TRUE";
    const wrongCase = await import("@/auth");
    expect(wrongCase.authConfig.trustHost).toBe(false);

    vi.resetModules();
    process.env.ARGUS_AUTH_TRUST_HOST = "true";
    const enabled = await import("@/auth");
    expect(enabled.authConfig.trustHost).toBe(true);

    delete process.env.ARGUS_AUTH_TRUST_HOST;
    vi.resetModules();
  });
});
