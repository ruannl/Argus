import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("server-only", () => ({}));

vi.mock("@/lib/server/backend-client", () => ({
  loginAccount: vi.fn(),
  logoutSession: vi.fn(),
  registerAccount: vi.fn(),
  requestPasswordReset: vi.fn(),
  resendVerification: vi.fn(),
  resetPassword: vi.fn(),
  verifyEmail: vi.fn(),
}));

vi.mock("@/auth", () => ({
  auth: vi.fn(),
  signIn: vi.fn(),
  signOut: vi.fn(),
}));

import {
  confirmVerification,
  endSessionAction,
  loginAction,
  registerAccountAction,
  requestPasswordResetAction,
  resendVerificationAction,
  resetPasswordAction,
} from "@/lib/server/account-actions";
import {
  loginAccount,
  logoutSession,
  registerAccount,
  requestPasswordReset,
  resendVerification,
  resetPassword,
  verifyEmail,
} from "@/lib/server/backend-client";
import { auth, signIn, signOut } from "@/auth";

const accepted = { ok: true as const, value: { status: "accepted" as const } };
const verified = { ok: true as const, value: { status: "verified" as const } };
const reset = { ok: true as const, value: { status: "reset" as const } };

function form(fields: Record<string, string>): FormData {
  const data = new FormData();
  for (const [name, value] of Object.entries(fields)) {
    data.set(name, value);
  }
  return data;
}

describe("account server actions", () => {
  beforeEach(() => {
    vi.mocked(loginAccount).mockReset();
    vi.mocked(logoutSession).mockReset();
    vi.mocked(registerAccount).mockReset();
    vi.mocked(requestPasswordReset).mockReset();
    vi.mocked(resendVerification).mockReset();
    vi.mocked(resetPassword).mockReset();
    vi.mocked(verifyEmail).mockReset();
    vi.mocked(auth).mockReset();
    vi.mocked(signIn).mockReset();
    vi.mocked(signOut).mockReset();
  });

  it("uses the same register success copy for new and existing emails", async () => {
    vi.mocked(registerAccount).mockResolvedValue(accepted);

    const newAccount = await registerAccountAction(
      form({ email: "new@example.test", password: "a compliant password" }),
    );
    const existingAccount = await registerAccountAction(
      form({ email: "existing@example.test", password: "a compliant password" }),
    );

    expect(newAccount).toMatchObject({
      ok: true,
      message: "Check your email to verify your account.",
    });
    expect(existingAccount).toMatchObject({
      ok: true,
      message: "Check your email to verify your account.",
    });
  });

  it.each([
    ["invalid_request", "That password or email could not be accepted."],
    ["invalid_token", "Something went wrong. Please try again later."],
    ["rate_limited", "Please try again later."],
    ["unavailable", "Something went wrong. Please try again later."],
  ] as const)("maps register %s to its closed copy", async (kind, message) => {
    vi.mocked(registerAccount).mockResolvedValue({ ok: false, kind });

    await expect(
      registerAccountAction(
        form({ email: "customer@example.test", password: "a compliant password" }),
      ),
    ).resolves.toMatchObject({ ok: false, message });
  });

  it("maps resend success and unreachable invalid-token failures without enumeration", async () => {
    vi.mocked(resendVerification)
      .mockResolvedValueOnce(accepted)
      .mockResolvedValueOnce({ ok: false, kind: "invalid_token" });

    const success = await resendVerificationAction(form({ email: "known@example.test" }));
    const collapsed = await resendVerificationAction(form({ email: "unknown@example.test" }));

    expect(success).toMatchObject({
      ok: true,
      message: "Check your email to verify your account.",
    });
    expect(collapsed).toMatchObject({
      ok: false,
      message: "Something went wrong. Please try again later.",
    });
  });

  it("uses one request success copy and echoes only the submitted email", async () => {
    vi.mocked(requestPasswordReset).mockResolvedValue(accepted);

    await expect(
      requestPasswordResetAction(form({ email: "known@example.test" })),
    ).resolves.toEqual({
      ok: true,
      message: "If that email can receive a reset link, we sent one.",
      email: "known@example.test",
    });
    await expect(
      requestPasswordResetAction(form({ email: "unknown@example.test" })),
    ).resolves.toEqual({
      ok: true,
      message: "If that email can receive a reset link, we sent one.",
      email: "unknown@example.test",
    });
  });

  it.each([
    ["invalid_request", "Something went wrong. Please try again later."],
    ["invalid_token", "Something went wrong. Please try again later."],
    ["rate_limited", "Please try again later."],
    ["unavailable", "Something went wrong. Please try again later."],
  ] as const)("maps reset request %s to its closed copy", async (kind, message) => {
    vi.mocked(requestPasswordReset).mockResolvedValue({ ok: false, kind });

    await expect(
      requestPasswordResetAction(form({ email: "customer@example.test" })),
    ).resolves.toEqual({
      ok: false,
      message,
      email: "customer@example.test",
    });
  });

  it("rejects an empty reset request before calling the backend", async () => {
    await expect(requestPasswordResetAction(form({ email: "" }))).resolves.toEqual({
      ok: false,
      message: "Enter your email.",
      email: "",
    });
    await expect(requestPasswordResetAction(new FormData())).resolves.toEqual({
      ok: false,
      message: "Enter your email.",
      email: "",
    });
    expect(requestPasswordReset).not.toHaveBeenCalled();
  });

  it("maps every verify result and rejects malformed tokens before the backend", async () => {
    vi.mocked(verifyEmail)
      .mockResolvedValueOnce(verified)
      .mockResolvedValueOnce({ ok: false, kind: "invalid_token" })
      .mockResolvedValueOnce({ ok: false, kind: "rate_limited" })
      .mockResolvedValueOnce({ ok: false, kind: "unavailable" })
      .mockResolvedValueOnce({ ok: false, kind: "invalid_request" });

    const token = "a".repeat(64);
    await expect(confirmVerification(token)).resolves.toMatchObject({
      ok: true,
      message: "Your email is verified. You can sign in.",
    });
    await expect(confirmVerification(token)).resolves.toMatchObject({
      ok: false,
      message: "This verification link is invalid or has expired.",
    });
    await expect(confirmVerification(token)).resolves.toMatchObject({
      ok: false,
      message: "Please try again later.",
    });
    await expect(confirmVerification(token)).resolves.toMatchObject({
      ok: false,
      message: "Something went wrong. Please try again later.",
    });
    await expect(confirmVerification(token)).resolves.toMatchObject({
      ok: false,
      message: "Something went wrong. Please try again later.",
    });

    await expect(confirmVerification("abc")).resolves.toEqual({
      ok: false,
      message: "This verification link is invalid or has expired.",
    });
    expect(verifyEmail).toHaveBeenCalledTimes(5);
  });

  it.each([
    ["invalid_request", "That password could not be accepted."],
    ["invalid_token", "This reset link is invalid or has expired."],
    ["invalid_credentials", "Something went wrong. Please try again later."],
    ["rate_limited", "Please try again later."],
    ["unavailable", "Something went wrong. Please try again later."],
  ] as const)("maps password reset %s to its closed copy", async (kind, message) => {
    vi.mocked(resetPassword).mockResolvedValue({ ok: false, kind } as never);

    await expect(
      resetPasswordAction("a".repeat(64), "a compliant password"),
    ).resolves.toEqual({ ok: false, message });
    expect(resetPassword).toHaveBeenCalledWith("a".repeat(64), "a compliant password");
  });

  it("returns reset success without returning the password or token", async () => {
    vi.mocked(resetPassword).mockResolvedValue(reset);

    await expect(
      resetPasswordAction("b".repeat(64), "a different compliant password"),
    ).resolves.toEqual({
      ok: true,
      message: "Your password has been reset. You can sign in.",
    });
  });

  it.each(["", "abc", "A".repeat(64), "a".repeat(63)])(
    "rejects malformed reset token %s before the backend",
    async (token) => {
      await expect(resetPasswordAction(token, "a compliant password")).resolves.toEqual({
        ok: false,
        message: "This reset link is invalid or has expired.",
      });
      expect(resetPassword).not.toHaveBeenCalled();
    },
  );

  it("rejects an empty reset password before the backend", async () => {
    await expect(resetPasswordAction("a".repeat(64), "")).resolves.toEqual({
      ok: false,
      message: "That password could not be accepted.",
    });
    await expect(resetPasswordAction("a".repeat(64), undefined as never)).resolves.toEqual({
      ok: false,
      message: "That password could not be accepted.",
    });
    expect(resetPassword).not.toHaveBeenCalled();
  });

  it("does not mutate Auth.js while resetting a password", async () => {
    vi.mocked(resetPassword).mockResolvedValue(reset);

    await resetPasswordAction("a".repeat(64), "a compliant password");

    expect(signIn).not.toHaveBeenCalled();
    expect(signOut).not.toHaveBeenCalled();
  });

  it("turns an unexpected action dependency rejection into unavailable copy", async () => {
    vi.mocked(registerAccount).mockRejectedValue(new Error("network failure"));

    await expect(
      registerAccountAction(
        form({ email: "customer@example.test", password: "a compliant password" }),
      ),
    ).resolves.toMatchObject({
      ok: false,
      message: "Something went wrong. Please try again later.",
    });
  });

  it.each([
    ["invalid_credentials", "We could not sign you in with those details."],
    ["invalid_request", "We could not sign you in with those details."],
    ["rate_limited", "Please try again later."],
    ["unavailable", "Something went wrong. Please try again later."],
  ] as const)("maps login %s without revealing account details", async (kind, message) => {
    vi.mocked(loginAccount).mockResolvedValue({ ok: false, kind });

    await expect(
      loginAction(form({ email: "customer@example.test", password: "a password" })),
    ).resolves.toMatchObject({ ok: false, message });
    expect(signIn).not.toHaveBeenCalled();
  });

  it("uses the same closed login copy for empty fields without calling the backend", async () => {
    await expect(loginAction(form({ email: "", password: "" }))).resolves.toEqual({
      ok: false,
      message: "We could not sign you in with those details.",
      email: "",
    });
    expect(loginAccount).not.toHaveBeenCalled();
  });

  it("mints one frontend session from a successful backend login", async () => {
    vi.mocked(loginAccount).mockResolvedValue({
      ok: true,
      value: { token: "backend-token", expires_at: "2030-01-01T00:00:00Z" },
    });

    await expect(
      loginAction(form({ email: "customer@example.test", password: "a password" })),
    ).resolves.toMatchObject({ ok: true });
    expect(loginAccount).toHaveBeenCalledTimes(1);
    expect(signIn).toHaveBeenCalledWith("credentials", {
      backendToken: "backend-token",
      expiresAt: "2030-01-01T00:00:00Z",
      redirect: false,
    });
  });

  it("revokes the backend session before clearing the Auth.js session", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(logoutSession).mockResolvedValue({ ok: false, kind: "unavailable" });
    vi.mocked(signOut).mockResolvedValue(undefined);

    await endSessionAction();

    expect(logoutSession).toHaveBeenCalledWith("backend-token");
    expect(signOut).toHaveBeenCalledWith({ redirectTo: "/login" });
    expect(vi.mocked(logoutSession).mock.invocationCallOrder[0]).toBeLessThan(
      vi.mocked(signOut).mock.invocationCallOrder[0],
    );
  });
});
