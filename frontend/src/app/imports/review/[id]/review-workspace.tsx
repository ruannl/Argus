"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import {
  abandonImportReviewAction,
  commitImportReviewAction,
  patchImportReviewAction,
  refreshImportReviewAction,
} from "@/lib/server/import-actions";
import type { ImportActionResult } from "@/lib/server/import-actions";
import type {
  ImportReviewDecision,
  ImportReviewDetail,
  ImportReviewRow,
  MerchantTag,
} from "@/lib/server/backend-client";

type ReviewWorkspaceProps = {
  review: ImportReviewDetail;
  tags: MerchantTag[];
};

type Message = { tone: "status" | "error"; text: string };

const VALIDATION_MESSAGES: Record<string, string> = {
  invalid_date: "Date is invalid or unresolved.",
  date_outside_period: "Date is outside the confirmed statement period.",
  future_date: "Date is in the future.",
  century_required: "Choose the two-digit-year century.",
  invalid_amount: "Amount is invalid.",
  missing_amount: "No amount was found.",
  multiple_amounts: "More than one flow column is populated.",
  invalid_field_count: "Field count does not match the header.",
  empty_description: "Description is empty; the display fallback is not identity.",
  description_too_long: "Description is too long.",
  invalid_sign: "Amount sign conflicts with the selected direction.",
  identity_conflict: "Identity conflict.",
};

function suppressed(row: ImportReviewRow): boolean {
  return row.duplicate_state === "suppressed" || row.duplicate_state === "suppressed_within_file";
}

export default function ReviewWorkspace({ review, tags }: ReviewWorkspaceProps) {
  const router = useRouter();
  const [version, setVersion] = useState(review.summary.version);
  const [hash, setHash] = useState(review.summary.summary_hash);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<Message | null>(null);
  const [repeat, setRepeat] = useState<string[] | null>(null);
  const [confirmAbandon, setConfirmAbandon] = useState(false);
  const tagsById = new Map(tags.map((tag) => [tag.id, tag]));
  const rows = review.page.rows;
  const totalPages = Math.max(1, Math.ceil(review.page.total / review.page.page_size));
  const invalidUnskipped = review.summary.invalid_unskipped_count > 0;
  const pendingGroups = review.summary.within_file_pending_count > 0;
  const hasReviewedOutcome =
    review.summary.imported_count > 0 || review.summary.suppressed_count > 0;
  const includedCount = review.summary.imported_count;
  const canCommit =
    !busy && !invalidUnskipped && !pendingGroups && hasReviewedOutcome;

  async function run(
    action: () => Promise<ImportActionResult>,
    onSuccess?: (result: ImportActionResult) => void,
  ): Promise<ImportActionResult | null> {
    setBusy(true);
    setMessage(null);
    try {
      const result = await action();
      setMessage({ tone: result.ok ? "status" : "error", text: result.message });
      if (result.review) {
        setVersion(result.review.version);
        setHash(result.review.summary_hash);
      }
      if (!result.ok && result.errorKind === "statement_repeat") {
        setRepeat(result.existingImportIds ?? []);
      }
      if (result.ok) {
        onSuccess?.(result);
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

  function patch(decision: ImportReviewDecision, statementOverride?: boolean) {
    void run(() =>
      patchImportReviewAction(review.summary.id, version, [decision], statementOverride),
    );
  }

  function toggleSkip(row: ImportReviewRow) {
    const next = !row.skip;
    const decision: ImportReviewDecision = {
      row_id: row.row_id,
      skip: next,
      override: false,
    };
    if (row.within_file_group) {
      decision.within_file_state = next ? "skip" : "keep";
    }
    patch(decision);
  }

  function toggleOverride(row: ImportReviewRow) {
    patch({ row_id: row.row_id, override: !row.override });
  }

  function groupDecision(row: ImportReviewRow, action: "keep_all" | "skip_all") {
    if (!row.within_file_group) return;
    patch({
      row_id: row.row_id,
      within_file_group: row.within_file_group,
      within_file_action: action,
    });
  }

  function addTag(row: ImportReviewRow, tagId: string) {
    if (row.selected_tag_ids.includes(tagId)) return;
    patch({ row_id: row.row_id, selected_tag_ids: [...row.selected_tag_ids, tagId] });
  }

  function removeTag(row: ImportReviewRow, tagId: string) {
    patch({
      row_id: row.row_id,
      selected_tag_ids: row.selected_tag_ids.filter((id) => id !== tagId),
    });
  }

  async function commit() {
    setBusy(true);
    setMessage(null);
    try {
      const result = await commitImportReviewAction(
        review.summary.id,
        version,
        hash,
      );
      if (result.ok && result.statementId) {
        router.push(`/imports/${result.statementId}`);
        return;
      }
      setMessage({ tone: "error", text: result.message });
      if (result.errorKind === "statement_repeat") {
        setRepeat(result.existingImportIds ?? []);
      }
    } finally {
      setBusy(false);
    }
  }

  async function confirmRepeat() {
    setBusy(true);
    setMessage(null);
    try {
      const patched = await patchImportReviewAction(
        review.summary.id,
        version,
        [],
        true,
      );
      if (!patched.ok || !patched.review) {
        setMessage({ tone: "error", text: patched.message });
        return;
      }
      setVersion(patched.review.version);
      setHash(patched.review.summary_hash);
      const committed = await commitImportReviewAction(
        review.summary.id,
        patched.review.version,
        patched.review.summary_hash,
      );
      if (committed.ok && committed.statementId) {
        router.push(`/imports/${committed.statementId}`);
        return;
      }
      setMessage({ tone: "error", text: committed.message });
    } finally {
      setBusy(false);
    }
  }

  async function abandon() {
    setConfirmAbandon(false);
    const result = await run(() => abandonImportReviewAction(review.summary.id, version));
    if (result?.ok) {
      router.push("/imports");
    }
  }

  return (
    <div className="mt-8 space-y-6">
      <section className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {[
          ["Total rows", review.summary.total_rows],
          ["Included", review.summary.imported_count],
          ["Suppressed", review.summary.suppressed_count],
          ["Skipped", review.summary.skipped_count],
        ].map(([label, value]) => (
          <div className="rounded-2xl border border-line bg-surface p-4" key={String(label)}>
            <p className="text-xs font-semibold tracking-[0.12em] text-muted uppercase">
              {label}
            </p>
            <p className="mt-2 font-mono text-2xl font-semibold tabular-nums text-ink">
              {value}
            </p>
          </div>
        ))}
      </section>

      <section className="rounded-2xl border border-line bg-surface p-4 text-sm text-muted">
        <p className="font-semibold text-ink">
          {review.summary.start_date ?? "Unknown start"} to {review.summary.end_date ?? "Unknown end"} ·{" "}
          {review.summary.source_kind}
        </p>
        <p className="mt-1">
          Review version {version} · {review.summary.possible_duplicate_count} possible duplicate
          {review.summary.possible_duplicate_count === 1 ? "" : "s"} ·{" "}
          {review.summary.exact_override_count} exact override
          {review.summary.exact_override_count === 1 ? "" : "s"} · expires{" "}
          {new Date(review.summary.expires_at).toLocaleString()}
        </p>
      </section>

      {message ? (
        <p
          className={
            message.tone === "error"
              ? "rounded-xl border border-warning-line bg-warning-soft p-3 text-sm font-semibold text-warning-ink"
              : "rounded-xl border border-line bg-elevated p-3 text-sm font-semibold text-ink"
          }
          role={message.tone === "error" ? "alert" : "status"}
        >
          {message.text}
        </p>
      ) : null}

      {review.summary.statement_override ? (
        <p className="rounded-xl border border-warning-line bg-warning-soft p-3 text-sm font-semibold text-warning-ink">
          A repeat-statement override is active for this review.
        </p>
      ) : null}

      {invalidUnskipped ? (
        <p className="rounded-xl border border-warning-line bg-warning-soft p-3 text-sm font-semibold text-warning-ink">
          Invalid rows must be marked as skipped before committing.
        </p>
      ) : null}
      {pendingGroups ? (
        <p className="rounded-xl border border-warning-line bg-warning-soft p-3 text-sm font-semibold text-warning-ink">
          Every repeated-row group needs a keep-all, skip-all, or per-row decision.
        </p>
      ) : null}

      <section className="space-y-4">
        {rows.map((row) => {
          const suggestions = row.suggested_tag_ids
            .filter((id) => !row.selected_tag_ids.includes(id))
            .map((id) => tagsById.get(id))
            .filter((tag): tag is MerchantTag => tag !== undefined);
          const selected = row.selected_tag_ids.map((id) => ({
            id,
            tag: tagsById.get(id),
          }));
          const missingSelected = selected.filter((entry) => entry.tag === undefined);
          return (
            <article
              className="rounded-[1.5rem] border border-line bg-surface p-4 sm:p-5"
              key={row.row_id}
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <p className="text-xs font-semibold tracking-[0.12em] text-muted uppercase">
                    Row {row.source_row_number}
                    {row.source_page !== null ? ` · page ${row.source_page}` : ""}
                  </p>
                  <p className="mt-1 text-base font-semibold text-ink">
                    {row.transaction_date ?? "Date unresolved"}
                  </p>
                  <p className="mt-1 max-w-2xl break-words text-sm leading-6 text-muted">
                    {row.description}
                  </p>
                  <details className="mt-2 text-xs text-muted">
                    <summary className="cursor-pointer font-semibold text-ink">
                      Source values · physical line {row.source_line}
                    </summary>
                    <dl className="mt-1 grid gap-1 sm:grid-cols-2">
                      {Object.entries(row.raw_values)
                        .filter(([, value]) => value !== null && value !== "")
                        .map(([name, value]) => (
                          <div className="break-words" key={name}>
                            <dt className="inline font-semibold">{name}: </dt>
                            <dd className="inline font-mono">{value}</dd>
                          </div>
                        ))}
                    </dl>
                  </details>
                </div>
                <p className="font-mono text-lg font-semibold tabular-nums text-ink">
                  {row.amount ?? "—"}
                </p>
              </div>

              {row.validation_codes.length > 0 || row.warnings.length > 0 ? (
                <ul className="mt-3 flex flex-wrap gap-2 text-xs font-semibold">
                  {row.validation_codes.map((code) => (
                    <li
                      className="rounded-full border border-warning-line bg-warning-soft px-2 py-1 text-warning-ink"
                      key={code}
                      title={code}
                    >
                      {VALIDATION_MESSAGES[code] ?? code}
                    </li>
                  ))}
                  {row.warnings.map((warning) => (
                    <li
                      className="rounded-full border border-line bg-elevated px-2 py-1 text-muted"
                      key={warning}
                      title={warning}
                    >
                      {VALIDATION_MESSAGES[warning] ?? warning}
                    </li>
                  ))}
                </ul>
              ) : null}

              {row.duplicate_reasons.length > 0 ? (
                <div className="mt-3 rounded-xl border border-line bg-page p-3 text-sm">
                  <p className="font-semibold text-ink">
                    {suppressed(row)
                      ? "Matches an existing occurrence"
                      : "Possible duplicate warning"}
                  </p>
                  <p className="mt-1 text-muted">{row.duplicate_reasons.join(" · ")}</p>
                  {row.matching_import_ids.length > 0 ? (
                    <p className="mt-1 flex flex-wrap gap-2 text-xs">
                      {row.matching_import_ids.map((id) => (
                        <Link
                          className="font-semibold text-accent-text underline underline-offset-4"
                          href={`/imports/${id}`}
                          key={id}
                        >
                          matching import
                        </Link>
                      ))}
                    </p>
                  ) : null}
                  {row.near_candidates.length > 0 ? (
                    <ul className="mt-2 space-y-1 text-xs text-muted">
                      {row.near_candidates.map((candidate) => (
                        <li key={`${candidate.candidate_id}-${candidate.source}`}>
                          {candidate.transaction_date} · {candidate.amount} ·{" "}
                          {candidate.description} · {candidate.reasons.join(", ")} ·{" "}
                          {Math.round(candidate.similarity * 100)}%
                          {candidate.import_id ? (
                            <>
                              {" "}
                              ·{" "}
                              <Link
                                className="font-semibold text-accent-text underline underline-offset-4"
                                href={`/imports/${candidate.import_id}`}
                              >
                                view import
                              </Link>
                            </>
                          ) : null}
                        </li>
                      ))}
                      {row.near_candidate_total > row.near_candidates.length ? (
                        <li>{row.near_candidate_total - row.near_candidates.length} more candidate(s)</li>
                      ) : null}
                    </ul>
                  ) : null}
                </div>
              ) : null}

              {row.within_file_group && row.within_file_state === "pending" ? (
                <div className="mt-3 rounded-xl border border-warning-line bg-warning-soft p-3 text-sm">
                  <p className="font-semibold text-warning-ink">
                    This row repeats within the file. Decide the group before committing.
                  </p>
                  <div className="mt-2 flex flex-wrap gap-2">
                    <button
                      className="rounded-full border border-line bg-surface px-3 py-1.5 text-xs font-bold text-ink"
                      disabled={busy}
                      onClick={() => groupDecision(row, "keep_all")}
                      type="button"
                    >
                      Keep all in group
                    </button>
                    <button
                      className="rounded-full border border-line bg-surface px-3 py-1.5 text-xs font-bold text-ink"
                      disabled={busy}
                      onClick={() => groupDecision(row, "skip_all")}
                      type="button"
                    >
                      Skip all in group
                    </button>
                    <button
                      className="rounded-full border border-line bg-surface px-3 py-1.5 text-xs font-bold text-ink"
                      disabled={busy}
                      onClick={() => toggleSkip(row)}
                      type="button"
                    >
                      Skip this row only
                    </button>
                  </div>
                </div>
              ) : null}

              <div className="mt-3 flex flex-wrap items-center gap-3">
                <label className="inline-flex items-center gap-2 text-sm font-semibold text-ink">
                  <input
                    checked={row.skip}
                    disabled={busy}
                    onChange={() => toggleSkip(row)}
                    type="checkbox"
                  />
                  Skip row
                </label>
                {suppressed(row) ? (
                  <button
                    className={
                      row.override
                        ? "rounded-full border border-warning-line bg-warning-soft px-3 py-1.5 text-xs font-bold text-warning-ink"
                        : "rounded-full border border-line px-3 py-1.5 text-xs font-bold text-ink"
                    }
                    disabled={busy || row.skip}
                    onClick={() => toggleOverride(row)}
                    type="button"
                  >
                    {row.override ? "Override active" : "Include anyway"}
                  </button>
                ) : null}
              </div>

              <div className="mt-3 border-t border-line pt-3">
                <p className="text-xs font-semibold tracking-[0.12em] text-muted uppercase">
                  Merchant tags
                </p>
                <div className="mt-2 flex flex-wrap gap-2">
                  {selected.map(({ id, tag }) => (
                    <span
                      className="inline-flex items-center gap-2 rounded-full border border-line bg-page px-3 py-1 text-xs font-semibold text-ink"
                      key={id}
                    >
                      {tag ? tag.display_name : `Unavailable tag ${id.slice(0, 8)}`}
                      {tag?.is_archived ? " (archived)" : ""}
                      <button
                        className="text-muted"
                        disabled={busy}
                        onClick={() => removeTag(row, id)}
                        type="button"
                      >
                        Remove
                      </button>
                    </span>
                  ))}
                  {selected.length === 0 ? (
                    <span className="text-xs text-muted">No tags selected.</span>
                  ) : null}
                </div>
                {suggestions.length > 0 ? (
                  <div className="mt-2 flex flex-wrap items-center gap-2">
                    <span className="text-xs text-muted">Suggestions:</span>
                    {suggestions.map((tag) => (
                      <button
                        className="rounded-full border border-line bg-page px-3 py-1 text-xs font-semibold text-ink"
                        disabled={busy}
                        key={tag.id}
                        onClick={() => addTag(row, tag.id)}
                        type="button"
                      >
                        Add {tag.display_name}
                      </button>
                    ))}
                  </div>
                ) : null}
                {missingSelected.length > 0 ? (
                  <p className="mt-2 text-xs font-semibold text-warning-ink">
                    A selected tag is no longer available. Remove it or choose another before
                    committing.
                  </p>
                ) : null}
                <label className="mt-2 grid gap-1 text-xs font-semibold text-ink sm:max-w-sm">
                  Choose another tag
                  <select
                    className="rounded-lg border border-line bg-page px-2 py-1.5 text-sm font-normal text-ink"
                    disabled={busy}
                    onChange={(event) => {
                      if (event.target.value) addTag(row, event.target.value);
                      event.target.value = "";
                    }}
                    value=""
                  >
                    <option value="">Select a tag…</option>
                    {tags
                      .filter((tag) => !tag.is_archived && !row.selected_tag_ids.includes(tag.id))
                      .map((tag) => (
                        <option key={tag.id} value={tag.id}>
                          {tag.display_name}
                        </option>
                      ))}
                  </select>
                </label>
              </div>
            </article>
          );
        })}
      </section>

      <nav aria-label="Review pages" className="flex flex-wrap items-center gap-2">
        {Array.from({ length: totalPages }, (_, index) => index + 1).map((pageNumber) => (
          <Link
            aria-current={pageNumber === review.page.page ? "page" : undefined}
            className={
              pageNumber === review.page.page
                ? "rounded-full bg-ink px-3 py-1.5 text-xs font-bold text-page"
                : "rounded-full border border-line px-3 py-1.5 text-xs font-bold text-ink"
            }
            href={`/imports/review/${review.summary.id}?page=${pageNumber}`}
            key={pageNumber}
          >
            {pageNumber}
          </Link>
        ))}
        <span className="text-xs text-muted">
          {includedCount} row{includedCount === 1 ? "" : "s"} currently included across the review.
        </span>
      </nav>

      {repeat ? (
        <section className="rounded-2xl border border-warning-line bg-warning-soft p-4">
          <h2 className="text-lg font-semibold text-warning-ink">
            This statement was already imported
          </h2>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-warning-ink">
            The duplicate rows are protected. Only confirm a repeat if you deliberately want a
            second statement with fresh occurrence slots.
          </p>
          {repeat.length > 0 ? (
            <p className="mt-2 flex flex-wrap gap-2 text-xs">
              {repeat.map((id) => (
                <Link
                  className="font-semibold text-warning-ink underline underline-offset-4"
                  href={`/imports/${id}`}
                  key={id}
                >
                  existing import
                </Link>
              ))}
            </p>
          ) : null}
          <div className="mt-3 flex flex-wrap gap-2">
            <button
              className="rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink"
              disabled={busy}
              onClick={confirmRepeat}
              type="button"
            >
              Confirm repeat and import
            </button>
            <button
              className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
              disabled={busy}
              onClick={() => setRepeat(null)}
              type="button"
            >
              Cancel
            </button>
          </div>
        </section>
      ) : null}

      <section className="sticky bottom-0 rounded-2xl border border-line bg-surface p-4 card-shadow">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="text-sm text-muted">
            <p className="font-semibold text-ink">
              {review.summary.imported_count} included · {review.summary.skipped_count} skipped ·{" "}
              {review.summary.suppressed_count} suppressed
            </p>
            <p className="mt-1">
              Invalid rows and repeated groups must be resolved across every page.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <button
              className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
              disabled={busy}
              onClick={() => run(() => refreshImportReviewAction(review.summary.id, version))}
              type="button"
            >
              Refresh duplicates
            </button>
            <button
              className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
              disabled={busy}
              onClick={() => setConfirmAbandon(true)}
              type="button"
            >
              Abandon review
            </button>
            <button
              className="primary-link rounded-full px-5 py-2 text-sm font-bold disabled:opacity-60"
              disabled={!canCommit}
              onClick={commit}
              type="button"
            >
              {busy ? "Working…" : "Commit import"}
            </button>
          </div>
        </div>
      </section>

      {confirmAbandon ? (
        <section className="rounded-2xl border border-warning-line bg-warning-soft p-4">
          <h2 className="text-lg font-semibold text-warning-ink">Abandon this review?</h2>
          <p className="mt-2 text-sm leading-6 text-warning-ink">
            Prepared payloads are purged. No import is created and the action cannot be undone.
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            <button
              className="rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink"
              disabled={busy}
              onClick={abandon}
              type="button"
            >
              Abandon review
            </button>
            <button
              className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
              disabled={busy}
              onClick={() => setConfirmAbandon(false)}
              type="button"
            >
              Keep review
            </button>
          </div>
        </section>
      ) : null}
    </div>
  );
}
