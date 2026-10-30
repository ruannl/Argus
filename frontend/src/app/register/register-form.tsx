"use client";

import { useActionState } from "react";

import {
  registerAccountAction,
  resendVerificationAction,
} from "@/lib/server/account-actions";

type FormState = {
  status: "idle" | "success" | "error";
  message: string;
  email: string;
};

const initialState: FormState = {
  status: "idle",
  message: "",
  email: "",
};

function stateFromActionResult(result: { ok: boolean; message: string; email?: string }): FormState {
  return {
    status: result.ok ? "success" : "error",
    message: result.message,
    email: result.email ?? "",
  };
}

export default function RegisterForm() {
  const [registerState, registerFormAction, registerPending] = useActionState(
    async (_previousState: FormState, formData: FormData) =>
      stateFromActionResult(await registerAccountAction(formData)),
    initialState,
  );
  const [resendState, resendFormAction, resendPending] = useActionState(
    async (_previousState: FormState, formData: FormData) =>
      stateFromActionResult(await resendVerificationAction(formData)),
    initialState,
  );

  if (registerState.status === "success") {
    return (
      <div className="mt-8 space-y-8">
        <p aria-live="polite" className="rounded-2xl bg-success-soft px-5 py-4 text-base leading-7 font-semibold text-success-ink">
          {registerState.message}
        </p>

        <form action={resendFormAction} className="space-y-5">
          <div>
            <label className="block text-sm font-semibold text-ink" htmlFor="resend-email">
              Email
            </label>
            <input
              autoComplete="email"
              className="mt-2 block w-full rounded-2xl border border-line bg-surface px-4 py-3 text-base text-ink outline-none transition placeholder:text-faint focus:border-accent focus:ring-4 focus:ring-accent-ring"
              defaultValue={resendState.email || registerState.email}
              id="resend-email"
              name="email"
              required
              type="email"
            />
          </div>
          {resendState.message ? (
            <p aria-live="polite" className="rounded-2xl bg-elevated px-5 py-4 text-base leading-7 font-semibold text-ink">
              {resendState.message}
            </p>
          ) : null}
          <button
            className="inline-flex w-full items-center justify-center rounded-full bg-ink px-6 py-3.5 text-base font-bold text-page transition hover:bg-ink-hover disabled:cursor-not-allowed disabled:opacity-60"
            disabled={resendPending}
            type="submit"
          >
            Resend verification email
          </button>
        </form>
      </div>
    );
  }

  return (
    <form action={registerFormAction} className="mt-8 space-y-5">
      <div>
        <label className="block text-sm font-semibold text-ink" htmlFor="register-email">
          Email
        </label>
        <input
          autoComplete="email"
          className="mt-2 block w-full rounded-2xl border border-line bg-surface px-4 py-3 text-base text-ink outline-none transition placeholder:text-faint focus:border-accent focus:ring-4 focus:ring-accent-ring"
          id="register-email"
          name="email"
          required
          type="email"
        />
      </div>

      <div>
        <label className="block text-sm font-semibold text-ink" htmlFor="register-password">
          Password
        </label>
        <input
          autoComplete="new-password"
          className="mt-2 block w-full rounded-2xl border border-line bg-surface px-4 py-3 text-base text-ink outline-none transition placeholder:text-faint focus:border-accent focus:ring-4 focus:ring-accent-ring"
          id="register-password"
          minLength={12}
          name="password"
          required
          type="password"
        />
        <p className="mt-2 text-sm leading-6 text-muted">Use at least 12 characters.</p>
      </div>

      {registerState.message ? (
        <p aria-live="polite" className="rounded-2xl bg-elevated px-5 py-4 text-base leading-7 font-semibold text-ink">
          {registerState.message}
        </p>
      ) : null}
      <button
        className="inline-flex w-full items-center justify-center rounded-full bg-accent px-6 py-3.5 text-base font-bold text-accent-ink transition hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-60"
        disabled={registerPending}
        type="submit"
      >
        Create account
      </button>
    </form>
  );
}
