"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import {
  deleteImportStatementAction,
  setImportVisibilityAction,
} from "@/lib/server/import-actions";
import type { ImportActionResult } from "@/lib/server/import-actions";
import type { ImportStatementDetail } from "@/lib/server/backend-client";

type StatementWorkspaceProps = {
  detail: ImportStatementDetail;
};

type Message = { tone: "status" | "error"; text: string };

export default function StatementWorkspace({ detail }: StatementWorkspaceProps) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<Message | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [confirmVisibility, setConfirmVisibility] = useState(false);
  const summary = detail.statement;
  const totalPages = Math.max(1, Math.ceil(detail.expected_transaction_count / detail.page_size));

  async function run(action: () => Promise<ImportActionResult>) {
    setBusy(true);
    setMessage(null);
    try {
      const result = await action();
      setMessage({ tone: result.ok ? "status" : "error", text: result.message });
      if (result.ok) {
        router.refresh();
      }
      return result;
    } catch {
      setMessage({ tone: "error", text: "Something went wrong. Please try again." });
      return null;
    } finally {
      setBusy(false);
    }
  }

  async function deleteStatement() {
    setConfirmDelete(false);
    const result = await run(() =>
      deleteImportStatementAction(
        summary.id,
        summary.version,
        detail.expected_transaction_count,
      ),
    );
    if (result?.ok) {
      router.push("/imports");
    }
  }

  return (
    <div className="mt-8 space-y-6">
      <section className="rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-8">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold tracking-[0.14em] text-accent-text uppercase">
              Committed import
            </p>
            <h2 className="mt-2 text-2xl font-semibold tracking-[-0.03em] text-ink">
              {summary.statement_label}
            </h2>
            <p className="mt-2 text-sm text-muted">
              {summary.start_date} to {summary.end_date} · {summary.source_type} · version{" "}
              {summary.version}
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            {summary.is_hidden ? (
              <span className="rounded-full border border-warning-line bg-warning-soft px-3 py-1 text-xs font-bold text-warning-ink">
                Hidden statement
              </span>
            ) : null}
            <span className="rounded-full border border-line px-3 py-1 text-xs font-semibold text-muted">
              {summary.live_hidden_count} hidden transaction
              {summary.live_hidden_count === 1 ? "" : "s"}
            </span>
          </div>
        </div>

        <dl className="mt-6 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          {[
            ["Total rows", summary.total_rows],
            ["Imported", summary.imported_count],
            ["Suppressed", summary.suppressed_count],
            ["Skipped", summary.skipped_count],
            ["Possible duplicates", summary.possible_duplicate_count],
            ["Exact overrides", summary.exact_override_count],
            ["Statement overrides", summary.statement_override_count],
            ["Owned transactions", detail.expected_transaction_count],
          ].map(([label, value]) => (
            <div className="rounded-xl border border-line bg-page p-3" key={String(label)}>
              <dt className="text-xs font-semibold tracking-[0.1em] text-muted uppercase">
                {label}
              </dt>
              <dd className="mt-1 font-mono text-xl font-semibold tabular-nums text-ink">
                {value}
              </dd>
            </div>
          ))}
        </dl>

        {message ? (
          <p
            className={
              message.tone === "error"
                ? "mt-4 rounded-xl border border-warning-line bg-warning-soft p-3 text-sm font-semibold text-warning-ink"
                : "mt-4 rounded-xl border border-line bg-elevated p-3 text-sm font-semibold text-ink"
            }
            role={message.tone === "error" ? "alert" : "status"}
          >
            {message.text}
          </p>
        ) : null}

        <div className="mt-5 flex flex-wrap gap-2">
          <button
            className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink disabled:opacity-60"
            disabled={busy}
            onClick={() => setConfirmVisibility(true)}
            type="button"
          >
            {summary.is_hidden ? "Restore statement" : "Hide statement"}
          </button>
          <button
            className="rounded-full border border-warning-line px-4 py-2 text-sm font-bold text-warning-ink disabled:opacity-60"
            disabled={busy}
            onClick={() => setConfirmDelete(true)}
            type="button"
          >
            Delete statement…
          </button>
          <Link
            className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            href="/imports"
          >
            Back to imports
          </Link>
        </div>
      </section>

      {confirmVisibility ? (
        <section className="rounded-2xl border border-line bg-elevated p-4">
          <h2 className="text-lg font-semibold text-ink">
            {summary.is_hidden ? "Restore this statement?" : "Hide this statement?"}
          </h2>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-muted">
            {summary.statement_label} currently owns {detail.expected_transaction_count}{" "}
            transaction{detail.expected_transaction_count === 1 ? "" : "s"}. This changes only
            the statement visibility layer; individual transaction hide flags are preserved.
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            <button
              className="rounded-full border border-line bg-surface px-4 py-2 text-sm font-bold text-ink"
              disabled={busy}
              onClick={() => {
                setConfirmVisibility(false);
                void run(() =>
                  setImportVisibilityAction(
                    summary.id,
                    !summary.is_hidden,
                    summary.version,
                    detail.expected_transaction_count,
                  ),
                );
              }}
              type="button"
            >
              {summary.is_hidden ? "Restore statement" : "Hide statement"}
            </button>
            <button
              className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
              disabled={busy}
              onClick={() => setConfirmVisibility(false)}
              type="button"
            >
              Cancel
            </button>
          </div>
        </section>
      ) : null}

      {confirmDelete ? (
        <section className="rounded-2xl border border-warning-line bg-warning-soft p-4">
          <h2 className="text-lg font-semibold text-warning-ink">Delete this statement?</h2>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-warning-ink">
            This permanently deletes {summary.statement_label} and exactly its{" "}
            {detail.expected_transaction_count} owned transaction
            {detail.expected_transaction_count === 1 ? "" : "s"}. Other imports and their
            transactions are untouched. A clean re-import becomes possible.
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            <button
              className="rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink"
              disabled={busy}
              onClick={deleteStatement}
              type="button"
            >
              Delete permanently
            </button>
            <button
              className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
              disabled={busy}
              onClick={() => setConfirmDelete(false)}
              type="button"
            >
              Keep statement
            </button>
          </div>
        </section>
      ) : null}

      <section className="space-y-3">
        <div className="flex flex-wrap items-end justify-between gap-2">
          <h2 className="text-xl font-semibold tracking-[-0.02em] text-ink">
            Transactions created by this statement
          </h2>
          <span className="text-xs text-muted">
            Read-only. Individual transaction editing belongs to the Transactions workspace.
          </span>
        </div>
        {detail.rows.length === 0 ? (
          <p className="rounded-2xl border border-line bg-surface p-4 text-sm text-muted">
            This statement created no new transactions. Its rows were suppressed as existing
            occurrences.
          </p>
        ) : (
          detail.rows.map((row) => (
            <article
              className="rounded-2xl border border-line bg-surface p-4"
              key={row.id}
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <p className="text-xs font-semibold tracking-[0.1em] text-muted uppercase">
                    Row {row.source_row_number}
                    {row.source_page !== null ? ` · page ${row.source_page}` : ""}
                  </p>
                  <p className="mt-1 text-sm font-semibold text-ink">{row.transaction_date}</p>
                  <p className="mt-1 max-w-2xl break-words text-sm leading-6 text-muted">
                    {row.description}
                  </p>
                </div>
                <p className="font-mono text-lg font-semibold tabular-nums text-ink">
                  {row.amount}
                </p>
              </div>
              <div className="mt-2 flex flex-wrap gap-2 text-xs">
                {row.effective_hidden ? (
                  <span className="rounded-full border border-warning-line bg-warning-soft px-2 py-1 font-semibold text-warning-ink">
                    Hidden
                  </span>
                ) : null}
                {row.is_hidden_by_user ? (
                  <span className="rounded-full border border-line bg-elevated px-2 py-1 font-semibold text-muted">
                    Individually hidden
                  </span>
                ) : null}
                {row.tags.map((tag) => (
                  <span
                    className="rounded-full border border-line bg-page px-2 py-1 font-semibold text-ink"
                    key={tag.id}
                  >
                    {tag.display_name}
                  </span>
                ))}
              </div>
            </article>
          ))
        )}
      </section>

      <nav aria-label="Statement pages" className="flex flex-wrap items-center gap-2">
        {Array.from({ length: totalPages }, (_, index) => index + 1).map((pageNumber) => (
          <Link
            aria-current={pageNumber === detail.page ? "page" : undefined}
            className={
              pageNumber === detail.page
                ? "rounded-full bg-ink px-3 py-1.5 text-xs font-bold text-page"
                : "rounded-full border border-line px-3 py-1.5 text-xs font-bold text-ink"
            }
            href={`/imports/${summary.id}?page=${pageNumber}`}
            key={pageNumber}
          >
            {pageNumber}
          </Link>
        ))}
      </nav>
    </div>
  );
}
