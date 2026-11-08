"use server";

import "server-only";

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
import type {
  AccountRequestResult,
  AcceptedResponse,
  LoginResponse,
  PasswordResetResponse,
  VerifiedResponse,
} from "@/lib/server/backend-client";

const REGISTER_SUCCESS_MESSAGE = "Check your email to verify your account.";
const REGISTER_INVALID_REQUEST_MESSAGE = "That password or email could not be accepted.";
const RATE_LIMITED_MESSAGE = "Please try again later.";
const UNAVAILABLE_MESSAGE = "Something went wrong. Please try again later.";
const VERIFY_SUCCESS_MESSAGE = "Your email is verified. You can sign in.";
const LOGIN_INVALID_MESSAGE = "We could not sign you in with those details.";
const INVALID_TOKEN_MESSAGE = "This verification link is invalid or has expired.";
const REQUEST_PASSWORD_RESET_EMPTY_MESSAGE = "Enter your email.";
const REQUEST_PASSWORD_RESET_SUCCESS_MESSAGE =
  "If that email can receive a reset link, we sent one.";
const RESET_PASSWORD_SUCCESS_MESSAGE = "Your password has been reset. You can sign in.";
const RESET_PASSWORD_POLICY_MESSAGE = "That password could not be accepted.";
const RESET_PASSWORD_INVALID_TOKEN_MESSAGE = "This reset link is invalid or has expired.";
const TOKEN_PATTERN = /^[0-9a-f]{64}$/;

type AccountActionResult =
  { ok: true; message: string; email?: string } | { ok: false; message: string; email?: string };

type PasswordResetActionResult = { ok: true; message: string } | { ok: false; message: string };

function formString(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" ? value : null;
}

function unreachable(value: never): never {
  void value;
  throw new Error("unrecognized account result kind");
}

function registerMessage(result: AccountRequestResult<AcceptedResponse>): string {
  if (result.ok) {
    return REGISTER_SUCCESS_MESSAGE;
  }

  switch (result.kind) {
    case "invalid_request":
      return REGISTER_INVALID_REQUEST_MESSAGE;
    case "invalid_token":
      return UNAVAILABLE_MESSAGE;
    case "rate_limited":
      return RATE_LIMITED_MESSAGE;
    case "unavailable":
      return UNAVAILABLE_MESSAGE;
  }

  return unreachable(result.kind);
}

function verifyMessage(result: AccountRequestResult<VerifiedResponse>): string {
  if (result.ok) {
    return VERIFY_SUCCESS_MESSAGE;
  }

  switch (result.kind) {
    case "invalid_request":
      return UNAVAILABLE_MESSAGE;
    case "invalid_token":
      return INVALID_TOKEN_MESSAGE;
    case "rate_limited":
      return RATE_LIMITED_MESSAGE;
    case "unavailable":
      return UNAVAILABLE_MESSAGE;
  }

  return unreachable(result.kind);
}

function loginMessage(result: AccountRequestResult<LoginResponse>): string {
  if (result.ok) {
    return "";
  }

  switch (result.kind) {
    case "invalid_credentials":
    case "invalid_request":
      return LOGIN_INVALID_MESSAGE;
    case "rate_limited":
      return RATE_LIMITED_MESSAGE;
    case "invalid_token":
    case "unavailable":
      return UNAVAILABLE_MESSAGE;
  }

  return unreachable(result.kind);
}

function requestPasswordResetMessage(result: AccountRequestResult<AcceptedResponse>): string {
  if (result.ok) {
    return REQUEST_PASSWORD_RESET_SUCCESS_MESSAGE;
  }

  switch (result.kind) {
    case "invalid_request":
    case "invalid_token":
    case "unavailable":
      return UNAVAILABLE_MESSAGE;
    case "rate_limited":
      return RATE_LIMITED_MESSAGE;
  }

  return unreachable(result.kind);
}

type ResetPasswordMessageResult =
  AccountRequestResult<PasswordResetResponse> | { ok: false; kind: "invalid_credentials" };

function resetPasswordMessage(result: ResetPasswordMessageResult): string {
  if (result.ok) {
    return RESET_PASSWORD_SUCCESS_MESSAGE;
  }

  switch (result.kind) {
    case "invalid_request":
      return RESET_PASSWORD_POLICY_MESSAGE;
    case "invalid_token":
      return RESET_PASSWORD_INVALID_TOKEN_MESSAGE;
    case "invalid_credentials":
    case "unavailable":
      return UNAVAILABLE_MESSAGE;
    case "rate_limited":
      return RATE_LIMITED_MESSAGE;
  }

  return UNAVAILABLE_MESSAGE;
}

export async function loginAction(formData: FormData): Promise<AccountActionResult> {
  const email = formString(formData, "email");
  const password = formString(formData, "password");

  if (email === null || password === null || email.length === 0 || password.length === 0) {
    return { ok: false, message: LOGIN_INVALID_MESSAGE, email: email ?? "" };
  }

  try {
    const result = await loginAccount(email, password);
    if (!result.ok) {
      return { ok: false, message: loginMessage(result), email };
    }

    await signIn("credentials", {
      backendToken: result.value.token,
      expiresAt: result.value.expires_at,
      redirect: false,
    });
    return { ok: true, message: "", email };
  } catch {
    return { ok: false, message: UNAVAILABLE_MESSAGE, email };
  }
}

export async function endSessionAction(): Promise<void> {
  let backendToken: string | undefined;
  try {
    backendToken = (await auth())?.user?.backendToken;
  } catch {
    backendToken = undefined;
  }

  if (backendToken) {
    try {
      await logoutSession(backendToken);
    } catch {
      // The frontend session must still be cleared when the backend is unavailable.
    }
  }

  await signOut({ redirectTo: "/login" });
}

export async function registerAccountAction(formData: FormData): Promise<AccountActionResult> {
  const email = formString(formData, "email");
  const password = formString(formData, "password");

  if (email === null || password === null || email.length === 0 || password.length === 0) {
    return { ok: false, message: REGISTER_INVALID_REQUEST_MESSAGE, email: email ?? "" };
  }

  try {
    const result = await registerAccount(email, password);
    if (result.ok) {
      return { ok: true, message: registerMessage(result), email };
    }
    return { ok: false, message: registerMessage(result), email };
  } catch {
    return { ok: false, message: UNAVAILABLE_MESSAGE, email };
  }
}

export async function resendVerificationAction(formData: FormData): Promise<AccountActionResult> {
  const email = formString(formData, "email");

  if (email === null || email.length === 0) {
    return { ok: false, message: REGISTER_INVALID_REQUEST_MESSAGE, email: email ?? "" };
  }

  try {
    const result = await resendVerification(email);
    if (result.ok) {
      return { ok: true, message: registerMessage(result), email };
    }
    return { ok: false, message: registerMessage(result), email };
  } catch {
    return { ok: false, message: UNAVAILABLE_MESSAGE, email };
  }
}

export async function confirmVerification(token: string): Promise<AccountActionResult> {
  if (!TOKEN_PATTERN.test(token)) {
    return { ok: false, message: INVALID_TOKEN_MESSAGE };
  }

  try {
    const result = await verifyEmail(token);
    if (result.ok) {
      return { ok: true, message: verifyMessage(result) };
    }
    return { ok: false, message: verifyMessage(result) };
  } catch {
    return { ok: false, message: UNAVAILABLE_MESSAGE };
  }
}

export async function requestPasswordResetAction(formData: FormData): Promise<AccountActionResult> {
  const email = formString(formData, "email");

  if (email === null || email.length === 0) {
    return {
      ok: false,
      message: REQUEST_PASSWORD_RESET_EMPTY_MESSAGE,
      email: email ?? "",
    };
  }

  try {
    const result = await requestPasswordReset(email);
    return {
      ok: result.ok,
      message: requestPasswordResetMessage(result),
      email,
    };
  } catch {
    return { ok: false, message: UNAVAILABLE_MESSAGE, email };
  }
}

export async function resetPasswordAction(
  token: string,
  password: string,
): Promise<PasswordResetActionResult> {
  if (typeof token !== "string" || !TOKEN_PATTERN.test(token)) {
    return { ok: false, message: RESET_PASSWORD_INVALID_TOKEN_MESSAGE };
  }

  if (typeof password !== "string" || password.length === 0) {
    return { ok: false, message: RESET_PASSWORD_POLICY_MESSAGE };
  }

  try {
    const result = await resetPassword(token, password);
    return { ok: result.ok, message: resetPasswordMessage(result) };
  } catch {
    return { ok: false, message: UNAVAILABLE_MESSAGE };
  }
}
