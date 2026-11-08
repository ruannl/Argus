"use client";

import type { FormEvent } from "react";
import Link from "next/link";
import { useRef, useState } from "react";

import { resetPasswordAction } from "@/lib/server/account-actions";

const POLICY_MESSAGE = "That password could not be accepted.";
const RATE_LIMITED_MESSAGE = "Please try again later.";
const UNAVAILABLE_MESSAGE = "Something went wrong. Please try again later.";
const SUCCESS_MESSAGE = "Your password has been reset. You can sign in.";

type ResetActionResult = {
  ok: boolean;
  message: string;
};

function isRetryableMessage(message: string): boolean {
  return message === POLICY_MESSAGE || message === RATE_LIMITED_MESSAGE;
}

export default function ResetPasswordForm() {
  const [pending, setPending] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [successful, setSuccessful] = useState(false);
  const [terminal, setTerminal] = useState(false);
  const inFlight = useRef(false);
  const settled = useRef(false);

  if (message !== null && terminal) {
    return (
      <div className="mt-6 space-y-4">
        <p
          aria-live="polite"
          className="rounded-2xl bg-success-soft px-5 py-4 text-base leading-7 font-semibold text-success-ink"
        >
          {message}
        </p>
        {successful ? (
          <Link
            className="inline-flex w-full items-center justify-center rounded-full bg-ink px-6 py-3.5 text-base font-bold text-page transition hover:bg-ink-hover"
            href="/login"
          >
            Sign in
          </Link>
        ) : null}
      </div>
    );
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (inFlight.current || settled.current) {
      return;
    }

    const formData = new FormData(event.currentTarget);
    const passwordValue = formData.get("password");
    const password = typeof passwordValue === "string" ? passwordValue : "";

    if (password.length === 0) {
      setMessage(POLICY_MESSAGE);
      return;
    }

    inFlight.current = true;
    setPending(true);
    const token = new URL(window.location.href).searchParams.get("token") ?? "";

    void resetPasswordAction(token, password)
      .then((result: ResetActionResult) => {
        if (settled.current) {
          return;
        }

        inFlight.current = false;
        setPending(false);

        if (!result.ok && isRetryableMessage(result.message)) {
          setMessage(result.message);
          return;
        }

        settled.current = true;
        setTerminal(true);
        setSuccessful(result.ok && result.message === SUCCESS_MESSAGE);
        setMessage(result.message);
        if (result.ok) {
          window.history.replaceState(null, "", "/reset");
        }
      })
      .catch(() => {
        if (settled.current) {
          return;
        }

        inFlight.current = false;
        settled.current = true;
        setTerminal(true);
        setSuccessful(false);
        setMessage(UNAVAILABLE_MESSAGE);
        setPending(false);
      });
  }

  return (
    <form className="mt-6 space-y-5" onSubmit={handleSubmit}>
      <div>
        <label className="block text-sm font-semibold text-ink" htmlFor="reset-password">
          New password
        </label>
        <input
          autoComplete="new-password"
          className="mt-2 block w-full rounded-2xl border border-line bg-surface px-4 py-3 text-base text-ink outline-none transition placeholder:text-faint focus:border-accent focus:ring-4 focus:ring-accent-ring"
          id="reset-password"
          minLength={12}
          name="password"
          required
          type="password"
        />
        <p className="mt-2 text-sm leading-6 text-muted">Use at least 12 characters.</p>
      </div>

      {message !== null ? (
        <p
          aria-live="polite"
          className="rounded-2xl bg-elevated px-5 py-4 text-base leading-7 font-semibold text-ink"
        >
          {message}
        </p>
      ) : null}
      <button
        aria-busy={pending}
        className="inline-flex w-full items-center justify-center rounded-full bg-accent px-6 py-3.5 text-base font-bold text-accent-ink transition hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-60"
        disabled={pending}
        type="submit"
      >
        Reset password
      </button>
    </form>
  );
}
