"use client";

import { useRef, useState } from "react";
import Link from "next/link";

import { confirmVerification } from "@/lib/server/account-actions";

const UNAVAILABLE_MESSAGE = "Something went wrong. Please try again later.";

type ConfirmationResult = {
  ok: boolean;
  message: string;
};

export default function ConfirmControl() {
  const [pending, setPending] = useState(false);
  const [terminalMessage, setTerminalMessage] = useState<string | null>(null);
  const [successful, setSuccessful] = useState(false);
  const inFlight = useRef(false);
  const settled = useRef(false);

  if (terminalMessage !== null) {
    return (
      <div className="mt-6 space-y-4">
        <p aria-live="polite" className="rounded-2xl bg-success-soft px-5 py-4 text-base leading-7 font-semibold text-success-ink">
          {terminalMessage}
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

  function handleClick() {
    if (inFlight.current || settled.current) {
      return;
    }

    inFlight.current = true;
    setPending(true);
    const token = new URL(window.location.href).searchParams.get("token") ?? "";

    void confirmVerification(token)
      .then((result: ConfirmationResult) => {
        if (settled.current) {
          return;
        }

        settled.current = true;
        setSuccessful(result.ok);
        setTerminalMessage(result.message);
        setPending(false);
        if (result.ok) {
          window.history.replaceState(null, "", "/verify");
        }
      })
      .catch(() => {
        if (settled.current) {
          return;
        }

        settled.current = true;
        setTerminalMessage(UNAVAILABLE_MESSAGE);
        setPending(false);
      });
  }

  return (
    <button
      aria-busy={pending}
      className="mt-6 inline-flex w-full items-center justify-center rounded-full bg-accent px-6 py-3.5 text-base font-bold text-accent-ink transition hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-60"
      disabled={pending}
      onClick={handleClick}
      type="button"
    >
      Verify email
    </button>
  );
}
