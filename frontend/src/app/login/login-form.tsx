"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useActionState, useEffect } from "react";

import { loginAction } from "@/lib/server/account-actions";

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

export default function LoginForm() {
  const router = useRouter();
  const [state, formAction, pending] = useActionState(
    async (_previousState: FormState, formData: FormData) => {
      const result = await loginAction(formData);
      return {
        status: result.ok ? ("success" as const) : ("error" as const),
        message: result.message,
        email: result.email ?? "",
      };
    },
    initialState,
  );

  useEffect(() => {
    if (state.status === "success") {
      router.replace("/home");
    }
  }, [router, state.status]);

  return (
    <form action={formAction} className="mt-8 space-y-5">
      <div>
        <label className="block text-sm font-semibold text-ink" htmlFor="login-email">
          Email
        </label>
        <input
          autoComplete="email"
          className="mt-2 block w-full rounded-2xl border border-line bg-surface px-4 py-3 text-base text-ink outline-none transition placeholder:text-faint focus:border-accent focus:ring-4 focus:ring-accent-ring"
          defaultValue={state.email}
          id="login-email"
          name="email"
          required
          type="email"
        />
      </div>

      <div>
        <label className="block text-sm font-semibold text-ink" htmlFor="login-password">
          Password
        </label>
        <input
          autoComplete="current-password"
          className="mt-2 block w-full rounded-2xl border border-line bg-surface px-4 py-3 text-base text-ink outline-none transition placeholder:text-faint focus:border-accent focus:ring-4 focus:ring-accent-ring"
          id="login-password"
          name="password"
          required
          type="password"
        />
      </div>

      {state.message ? (
        <p aria-live="polite" className="rounded-2xl bg-elevated px-5 py-4 text-base leading-7 font-semibold text-ink">
          {state.message}
        </p>
      ) : null}
      <button
        className="inline-flex w-full items-center justify-center rounded-full bg-accent px-6 py-3.5 text-base font-bold text-accent-ink transition hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-60"
        disabled={pending}
        type="submit"
      >
        Sign in
      </button>
      <p className="text-center text-sm leading-6 text-muted">
        <Link className="font-semibold text-ink underline underline-offset-4" href="/forgot-password">
          Forgot password?
        </Link>
      </p>
      <p className="text-center text-sm leading-6 text-muted">
        Need an account?{" "}
        <Link className="font-semibold text-ink underline underline-offset-4" href="/register">
          Create an account
        </Link>
      </p>
    </form>
  );
}
