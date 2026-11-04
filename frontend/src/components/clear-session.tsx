"use client";

import { useEffect, useRef } from "react";

import { endSessionAction } from "@/lib/server/account-actions";

export default function ClearSession() {
  const started = useRef(false);

  useEffect(() => {
    if (started.current) {
      return;
    }

    started.current = true;
    void Promise.resolve(endSessionAction()).catch(() => undefined);
  }, []);

  return (
    <p
      aria-live="polite"
      className="rounded-2xl bg-warning-soft px-5 py-4 text-base leading-7 font-semibold text-warning-ink"
    >
      Your session has expired. Please sign in again.
    </p>
  );
}
