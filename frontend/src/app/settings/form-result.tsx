"use client";

import type { FinanceActionResult } from "@/lib/server/finance-actions";

/** Inline, announced feedback for a finance form submission. */
export default function FormResult({ result }: { result: FinanceActionResult | null }) {
  if (!result) {
    return null;
  }

  return (
    <p
      aria-live="polite"
      className={
        result.ok
          ? "mt-3 rounded-2xl bg-success-soft px-4 py-3 text-sm font-semibold text-success-ink"
          : "mt-3 rounded-2xl bg-warning-soft px-4 py-3 text-sm font-semibold text-warning-ink"
      }
      role="status"
    >
      {result.message}
    </p>
  );
}
