"use client";

import { useRouter } from "next/navigation";

type AccountReadUnavailableProps = {
  kind: "rate_limited" | "unavailable";
  view: "list" | "detail";
};

/** Route-local retry without inventing account authority or clearing a session. */
export default function AccountReadUnavailable({ kind, view }: AccountReadUnavailableProps) {
  const router = useRouter();

  return (
    <main className="min-h-screen px-5 py-8 sm:px-8 lg:px-12">
      <section className="mx-auto max-w-3xl rounded-[2rem] border border-warning-line bg-warning-soft p-6 sm:p-10">
        <h1 className="text-2xl font-semibold text-warning-ink">
          {view === "list" ? "Transactions unavailable" : "Transaction detail unavailable"}
        </h1>
        <p className="mt-3 text-sm leading-6 text-warning-ink" role="alert">
          {kind === "rate_limited"
            ? "Too many account checks right now. Wait a few minutes, then retry. We did not sign you out."
            : "Your account could not be checked right now. That does not prove your session expired. We did not sign you out."}
        </p>
        <button
          className="mt-4 rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink focus-visible:ring-2 focus-visible:ring-accent-text"
          onClick={() => router.refresh()}
          type="button"
        >
          Retry account check
        </button>
      </section>
    </main>
  );
}
