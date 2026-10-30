"use client";

import Link from "next/link";
import { useActionState } from "react";

import { requestPasswordResetAction } from "@/lib/server/account-actions";

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

export default function ForgotPasswordForm() {
  const [state, formAction, pending] = useActionState(
    async (_previousState: FormState, formData: FormData) => {
      const result = await requestPasswordResetAction(formData);
      return {
        status: result.ok ? ("success" as const) : ("error" as const),
        message: result.message,
        email: result.email ?? "",
      };
    },
    initialState,
  );

  if (state.status === "success") {
    return (
      <div className="mt-8 space-y-6">
        <p
          aria-live="polite"
          className="rounded-2xl bg-success-soft px-5 py-4 text-base leading-7 font-semibold text-success-ink"
        >
          {state.message}
        </p>
        <Link
          className="inline-flex w-full items-center justify-center rounded-full bg-ink px-6 py-3.5 text-base font-bold text-page transition hover:bg-ink-hover"
          href="/login"
        >
          Sign in
        </Link>
      </div>
    );
  }

  return (
    <form action={formAction} className="mt-8 space-y-5">
      <div>
        <label
          className="block text-sm font-semibold text-ink"
          htmlFor="forgot-password-email"
        >
          Email
        </label>
        <input
          autoComplete="email"
          className="mt-2 block w-full rounded-2xl border border-line bg-surface px-4 py-3 text-base text-ink outline-none transition placeholder:text-faint focus:border-accent focus:ring-4 focus:ring-accent-ring"
          defaultValue={state.email}
          id="forgot-password-email"
          name="email"
          required
          type="email"
        />
      </div>

      {state.message ? (
        <p
          aria-live="polite"
          className="rounded-2xl bg-elevated px-5 py-4 text-base leading-7 font-semibold text-ink"
        >
          {state.message}
        </p>
      ) : null}
      <button
        className="inline-flex w-full items-center justify-center rounded-full bg-accent px-6 py-3.5 text-base font-bold text-accent-ink transition hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-60"
        disabled={pending}
        type="submit"
      >
        Request password reset
      </button>
      <p className="text-center text-sm leading-6 text-muted">
        Remember your password?{" "}
        <Link className="font-semibold text-ink underline underline-offset-4" href="/login">
          Sign in
        </Link>
      </p>
    </form>
  );
}
