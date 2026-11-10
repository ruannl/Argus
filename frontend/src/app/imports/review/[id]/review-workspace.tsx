"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { Fragment, useState } from "react";

import {
  EDITOR_ROW_CLASS,
  STATUS_PILL_CLASS,
  TABLE_CLASS,
  TABLE_HEAD_CLASS,
  TABLE_ROW_CLASS,
  TABLE_WRAPPER_CLASS,
  TD_CLASS,
  TH_CLASS,
} from "@/components/table-styles";
import { createMerchantTagAction } from "@/lib/server/finance-actions";
import {
  abandonImportReviewAction,
  commitImportReviewAction,
  patchImportReviewAction,
  refreshImportReviewAction,
} from "@/lib/server/import-actions";
import type { ImportActionResult } from "@/lib/server/import-actions";
import type {
  Category,
  ImportReviewDecision,
  ImportReviewDetail,
  ImportReviewRow,
  MerchantTag,
} from "@/lib/server/backend-client";

import { sortByDisplayName } from "@/lib/display-order";

type ReviewWorkspaceProps = {
  review: ImportReviewDetail;
  categories: Category[];
  categoriesUnavailable: boolean;
  tags: MerchantTag[];
  pageSize: number;
  possibleOnly: boolean;
  untaggedOnly: boolean;
  sortDirection: "asc" | "desc";
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

function possibleDuplicate(row: ImportReviewRow): boolean {
  return (
    !row.skip &&
    (row.near_candidate_total > 0 || row.duplicate_reasons.includes("stable_id_changed"))
  );
}

function reviewHref(
  reviewId: string,
  page: number,
  pageSize: number,
  sortDirection: "asc" | "desc",
  possibleOnly: boolean,
  untaggedOnly: boolean,
): string {
  const params = new URLSearchParams({
    page: String(page),
    page_size: String(pageSize),
    sort_direction: sortDirection,
  });
  if (possibleOnly) {
    params.set("duplicates", "possible");
  }
  if (untaggedOnly) {
    params.set("untagged", "true");
  }
  return `/imports/review/${reviewId}?${params.toString()}`;
}

export default function ReviewWorkspace({
  review,
  categories,
  categoriesUnavailable,
  tags,
  pageSize,
  possibleOnly,
  untaggedOnly,
  sortDirection,
}: ReviewWorkspaceProps) {
  const router = useRouter();
  const [summary, setSummary] = useState(review.summary);
  const [version, setVersion] = useState(summary.version);
  const [hash, setHash] = useState(summary.summary_hash);
  const [rows, setRows] = useState(review.page.rows);
  const [tagList, setTagList] = useState(tags);
  const categoryList = categories;
  const categoriesById = new Map(categoryList.map((category) => [category.id, category]));
  const assignableCategories = sortByDisplayName(
    categoryList.filter((category) => !category.is_archived && category.kind !== "Uncategorised"),
  );
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<Message | null>(null);
  const [repeat, setRepeat] = useState<string[] | null>(null);
  const [confirmAbandon, setConfirmAbandon] = useState(false);
  const [expandedRowId, setExpandedRowId] = useState<string | null>(null);
  const tagsById = new Map(tagList.map((tag) => [tag.id, tag]));
  const totalPages = Math.max(1, Math.ceil(review.page.total / review.page.page_size));
  const invalidUnskipped = summary.invalid_unskipped_count > 0;
  const pendingGroups = summary.within_file_pending_count > 0;
  const hasReviewedOutcome = summary.imported_count > 0 || summary.suppressed_count > 0;
  const includedCount = summary.imported_count;
  const canCommit = !busy && !invalidUnskipped && !pendingGroups && hasReviewedOutcome;
  const commitBlockers: string[] = [];
  if (invalidUnskipped) {
    commitBlockers.push(
      `${summary.invalid_unskipped_count} invalid row${
        summary.invalid_unskipped_count === 1 ? "" : "s"
      } must be marked as skipped before committing.`,
    );
  }
  if (pendingGroups) {
    commitBlockers.push(
      `${summary.within_file_pending_count} repeated-row group${
        summary.within_file_pending_count === 1 ? "" : "s"
      } still need${
        summary.within_file_pending_count === 1 ? "s" : ""
      } a decision. Open Details on a row marked "Repeats in file" and choose keep all, skip all, or skip this row only.`,
    );
  }
  if (!hasReviewedOutcome) {
    commitBlockers.push(
      "Nothing will be imported yet. Include at least one valid row, or keep a suppressed row included, before committing.",
    );
  }

  async function run(
    action: () => Promise<ImportActionResult>,
    onSuccess?: (result: ImportActionResult) => void,
    refreshAfter = false,
  ): Promise<ImportActionResult | null> {
    setBusy(true);
    setMessage(null);
    try {
      const result = await action();
      setMessage({ tone: result.ok ? "status" : "error", text: result.message });
      if (result.review) {
        setSummary(result.review);
        setVersion(result.review.version);
        setHash(result.review.summary_hash);
      }
      if (!result.ok && result.errorKind === "statement_repeat") {
        setRepeat(result.existingImportIds ?? []);
      }
      if (result.ok) {
        onSuccess?.(result);
        if (refreshAfter) {
          router.refresh();
        }
      }
      return result;
    } catch {
      setMessage({ tone: "error", text: "Something went wrong. Please try again." });
      return null;
    } finally {
      setBusy(false);
    }
  }

  /**
   * Applies a successful decision to the visible rows. The review page used to
   * re-fetch the whole review after every decision, which doubled the finance
   * requests and could exhaust the finance IP budget during a large review.
   */
  function applyDecision(decision: ImportReviewDecision) {
    setRows((current) => {
      const updated = current.map((row) => {
        if (decision.within_file_action) {
          if (row.within_file_group !== decision.within_file_group) {
            return row;
          }
          const skip = decision.within_file_action === "skip_all";
          return {
            ...row,
            skip,
            within_file_state: skip ? "skip" : "keep",
            override: skip ? false : row.override,
          };
        }
        if (row.row_id !== decision.row_id) {
          return row;
        }
        const selectedCategoryId = decision.reset_category_to_suggestion
          ? row.suggested_category_id
          : decision.selected_category_id !== undefined
            ? decision.selected_category_id
            : row.selected_category_id;
        const categoryDecided = decision.reset_category_to_suggestion
          ? false
          : decision.selected_category_id !== undefined
            ? true
            : row.category_decided;
        return {
          ...row,
          skip: decision.skip ?? row.skip,
          override: decision.override ?? row.override,
          within_file_state: decision.within_file_state ?? row.within_file_state,
          selected_tag_ids: decision.selected_tag_ids ?? row.selected_tag_ids,
          selected_category_id: selectedCategoryId,
          category_decided: categoryDecided,
        };
      });

      return updated.filter((row) => {
        if (possibleOnly && row.skip) {
          return false;
        }
        if (untaggedOnly && row.selected_tag_ids.length > 0) {
          return false;
        }
        return true;
      });
    });
  }

  function patch(decision: ImportReviewDecision, statementOverride?: boolean) {
    void run(
      () => patchImportReviewAction(summary.id, version, [decision], statementOverride),
      () => applyDecision(decision),
      // A filtered view can gain rows as well as lose them after a group or
      // tag decision. Reload the filtered page so its membership and counts
      // stay authoritative; the local update still makes the current row
      // respond immediately.
      possibleOnly || untaggedOnly,
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

  function changeCategory(row: ImportReviewRow, categoryId: string) {
    patch({ row_id: row.row_id, selected_category_id: categoryId });
  }

  function clearCategory(row: ImportReviewRow) {
    patch({ row_id: row.row_id, selected_category_id: null });
  }

  function restoreSuggestedCategory(row: ImportReviewRow) {
    patch({ row_id: row.row_id, reset_category_to_suggestion: true });
  }

  function categoryLabel(row: ImportReviewRow): string {
    if (!row.selected_category_id) {
      return "Uncategorised";
    }
    const category = categoriesById.get(row.selected_category_id);
    if (!category) {
      return categoriesUnavailable ? "Category list unavailable" : "Unavailable category";
    }
    return `${category.display_name} (${category.kind})${category.is_archived ? " — archived" : ""}`;
  }

  async function commit() {
    setBusy(true);
    setMessage(null);
    try {
      const result = await commitImportReviewAction(summary.id, version, hash);
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
      const patched = await patchImportReviewAction(summary.id, version, [], true);
      if (!patched.ok || !patched.review) {
        setMessage({ tone: "error", text: patched.message });
        return;
      }
      setSummary(patched.review);
      setVersion(patched.review.version);
      setHash(patched.review.summary_hash);
      const committed = await commitImportReviewAction(
        summary.id,
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
    const result = await run(() => abandonImportReviewAction(summary.id, version));
    if (result?.ok) {
      router.push("/imports");
    }
  }

  return (
    <div className="mt-8 space-y-6">
      <section className="grid grid-cols-2 gap-px overflow-hidden rounded-2xl border border-line bg-line sm:grid-cols-4">
        {[
          ["Total rows", summary.total_rows],
          ["Included", summary.imported_count],
          ["Suppressed", summary.suppressed_count],
          ["Skipped", summary.skipped_count],
        ].map(([label, value]) => (
          <div className="bg-surface px-4 py-3" key={String(label)}>
            <p className="text-xs font-semibold tracking-[0.12em] text-muted uppercase">{label}</p>
            <p className="mt-1 font-mono text-xl font-semibold tabular-nums text-ink">{value}</p>
          </div>
        ))}
      </section>

      <section className="rounded-2xl border border-line bg-surface p-4 text-sm text-muted">
        <p className="font-semibold text-ink">
          {summary.start_date ?? "Unknown start"} to {summary.end_date ?? "Unknown end"} ·{" "}
          {summary.source_kind}
        </p>
        <p className="mt-1">
          Review version {version} · {summary.categorised_count} of {summary.imported_count}{" "}
          included rows categorised · {summary.possible_duplicate_count} possible duplicate
          {summary.possible_duplicate_count === 1 ? "" : "s"} · {summary.exact_override_count} exact
          override
          {summary.exact_override_count === 1 ? "" : "s"} · expires{" "}
          <time dateTime={summary.expires_at} suppressHydrationWarning>
            {new Date(summary.expires_at).toLocaleString()}
          </time>
        </p>
        <p className="mt-1">
          Committing an untouched suggested category does not confirm it. Confirm or change it in
          Transactions after import.
        </p>
      </section>

      {categoriesUnavailable ? (
        <p
          className="rounded-xl border border-warning-line bg-warning-soft p-3 text-sm font-semibold text-warning-ink"
          role="alert"
        >
          Categories could not be loaded. Category choices are disabled until this page reloads; a
          selected category is not missing merely because this list failed.
        </p>
      ) : null}

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

      {summary.statement_override ? (
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

      <section aria-labelledby="review-rows-title">
        <div className="flex flex-wrap items-end justify-between gap-2">
          <h2 className="text-lg font-bold text-ink" id="review-rows-title">
            Review rows
          </h2>
          <div className="flex flex-wrap items-center gap-3">
            <p className="text-xs text-muted">
              Expand details for raw source values, duplicate evidence, and group decisions.
            </p>
            <label className="flex items-center gap-2 text-xs font-semibold text-ink">
              <input
                checked={possibleOnly}
                disabled={busy}
                onChange={(event) =>
                  router.push(
                    reviewHref(
                      summary.id,
                      1,
                      pageSize,
                      sortDirection,
                      event.target.checked,
                      untaggedOnly,
                    ),
                  )
                }
                type="checkbox"
              />
              Possible duplicates only ({summary.possible_duplicate_count})
            </label>
            <label className="flex items-center gap-2 text-xs font-semibold text-ink">
              <input
                checked={untaggedOnly}
                disabled={busy}
                onChange={(event) =>
                  router.push(
                    reviewHref(
                      summary.id,
                      1,
                      pageSize,
                      sortDirection,
                      possibleOnly,
                      event.target.checked,
                    ),
                  )
                }
                type="checkbox"
              />
              No tags only
            </label>
          </div>
        </div>

        <div className={TABLE_WRAPPER_CLASS}>
          <table className={`${TABLE_CLASS} min-w-[58rem] table-fixed`}>
            <colgroup>
              <col className="w-[6rem]" />
              <col className="w-[7rem]" />
              <col />
              <col className="w-[6.5rem]" />
              <col className="w-[13rem]" />
              <col className="w-[9rem]" />
              <col className="w-[13rem]" />
              <col className="w-[8rem]" />
            </colgroup>
            <thead className={TABLE_HEAD_CLASS}>
              <tr>
                <th className={TH_CLASS} scope="col">
                  Row
                </th>
                <th className={TH_CLASS} scope="col">
                  Date
                </th>
                <th className={TH_CLASS} scope="col">
                  Description
                </th>
                <th className={`${TH_CLASS} text-right`} scope="col">
                  Amount
                </th>
                <th className={TH_CLASS} scope="col">
                  Category
                </th>
                <th className={TH_CLASS} scope="col">
                  Status
                </th>
                <th className={TH_CLASS} scope="col">
                  Tags
                </th>
                <th className={`${TH_CLASS} text-right`} scope="col">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr>
                  <td className="px-4 py-6 text-sm text-muted" colSpan={8}>
                    No rows match this filter.
                  </td>
                </tr>
              ) : null}
              {rows.map((row) => {
                const suggestions = row.suggested_tag_ids
                  .filter((id) => !row.selected_tag_ids.includes(id))
                  .map((id) => tagsById.get(id))
                  .filter((tag): tag is MerchantTag => tag !== undefined && !tag.is_archived);
                const selected = row.selected_tag_ids.map((id) => ({
                  id,
                  tag: tagsById.get(id),
                }));
                const missingSelected = selected.filter((entry) => entry.tag === undefined);
                const selectedCategory = row.selected_category_id
                  ? categoriesById.get(row.selected_category_id)
                  : undefined;
                const categoryMissing =
                  !categoriesUnavailable && row.selected_category_id !== null && !selectedCategory;
                const isExpanded = expandedRowId === row.row_id;
                const detailId = `review-row-detail-${row.row_id}`;

                return (
                  <Fragment key={row.row_id}>
                    <tr className={TABLE_ROW_CLASS}>
                      <td className={TD_CLASS}>
                        <p className="font-semibold text-ink">Row {row.source_row_number}</p>
                        <p className="mt-0.5 text-xs text-muted">
                          {row.source_page !== null ? `Page ${row.source_page} · ` : ""}Line{" "}
                          {row.source_line}
                        </p>
                      </td>
                      <td className={`${TD_CLASS} whitespace-nowrap text-muted`}>
                        {row.transaction_date ?? "Date unresolved"}
                      </td>
                      <td className={`${TD_CLASS} max-w-md`}>
                        <p className="break-words text-sm font-semibold text-ink">
                          {row.description}
                        </p>
                      </td>
                      <td
                        className={`${TD_CLASS} text-right font-mono font-semibold whitespace-nowrap tabular-nums text-ink`}
                      >
                        {row.amount ?? "—"}
                      </td>
                      <td className={TD_CLASS}>
                        <div className="flex flex-col gap-2 text-xs">
                          <div className="flex flex-wrap items-center gap-1.5">
                            <span
                              className={
                                row.selected_category_id === null
                                  ? "rounded-full border border-warning-line bg-surface px-2 py-1 font-semibold text-ink"
                                  : row.category_decided
                                    ? "rounded-full border border-line bg-page px-2 py-1 font-semibold text-ink"
                                    : "rounded-full border border-suggestion-line bg-suggestion-soft px-2 py-1 font-semibold text-suggestion-ink"
                              }
                            >
                              {categoryLabel(row)}
                            </span>
                            {!row.category_decided && row.selected_category_id !== null ? (
                              <span className="font-semibold text-suggestion-ink">
                                Suggested from your history or alias
                              </span>
                            ) : null}
                            {!row.category_decided && row.selected_category_id === null ? (
                              <span className="font-semibold text-muted">No category evidence</span>
                            ) : null}
                            {row.category_decided && row.selected_category_id === null ? (
                              <span className="font-semibold text-muted">Cleared</span>
                            ) : null}
                            {categoryMissing ? (
                              <span className="font-semibold text-warning-ink">
                                Unavailable — choose again or clear
                              </span>
                            ) : null}
                          </div>
                          <select
                            aria-label={`Category for row ${row.source_row_number}`}
                            className="w-full rounded-lg border border-line bg-page px-2 py-1.5 text-xs font-normal text-ink"
                            disabled={busy || categoriesUnavailable}
                            onChange={(event) => {
                              if (event.target.value) {
                                changeCategory(row, event.target.value);
                              }
                            }}
                            value={row.selected_category_id ?? ""}
                          >
                            <option value="" disabled>
                              Choose a category…
                            </option>
                            {selectedCategory ? (
                              <option value={selectedCategory.id}>
                                {selectedCategory.display_name} ({selectedCategory.kind})
                                {selectedCategory.is_archived ? " — archived" : ""}
                              </option>
                            ) : null}
                            {categoryMissing ? (
                              <option value={row.selected_category_id!}>
                                Unavailable category
                              </option>
                            ) : null}
                            {assignableCategories.map((category) => (
                              <option key={category.id} value={category.id}>
                                {category.display_name} ({category.kind})
                              </option>
                            ))}
                          </select>
                          <div className="flex flex-wrap gap-2">
                            <button
                              className="rounded-full border border-line px-2 py-1 text-xs font-bold text-ink disabled:opacity-60"
                              disabled={
                                busy || categoriesUnavailable || row.selected_category_id === null
                              }
                              onClick={() => clearCategory(row)}
                              type="button"
                            >
                              Clear
                            </button>
                            {row.category_decided ? (
                              <button
                                className="rounded-full border border-suggestion-line bg-suggestion-soft px-2 py-1 text-xs font-bold text-suggestion-ink disabled:opacity-60"
                                disabled={busy || categoriesUnavailable}
                                onClick={() => restoreSuggestedCategory(row)}
                                type="button"
                              >
                                Restore suggestion
                              </button>
                            ) : null}
                          </div>
                        </div>
                      </td>
                      <td className={TD_CLASS}>
                        <div className="flex flex-wrap gap-1.5 text-xs">
                          {row.skip ? (
                            <span className={`${STATUS_PILL_CLASS} bg-elevated text-muted`}>
                              Skipped
                            </span>
                          ) : null}
                          {row.override ? (
                            <span
                              className={`${STATUS_PILL_CLASS} bg-warning-soft text-warning-ink`}
                            >
                              Override active
                            </span>
                          ) : null}
                          {suppressed(row) ? (
                            <span
                              className={`${STATUS_PILL_CLASS} bg-warning-soft text-warning-ink`}
                            >
                              Suppressed duplicate
                            </span>
                          ) : row.duplicate_state === "within_file" ? (
                            <span
                              className={`${STATUS_PILL_CLASS} bg-warning-soft text-warning-ink`}
                            >
                              Repeats in file
                            </span>
                          ) : null}
                          {possibleDuplicate(row) ? (
                            <span
                              className={`${STATUS_PILL_CLASS} bg-warning-soft text-warning-ink`}
                            >
                              Possible duplicate
                            </span>
                          ) : null}
                          {row.validation_codes.map((code) => (
                            <span
                              className={`${STATUS_PILL_CLASS} bg-warning-soft text-warning-ink`}
                              key={code}
                              title={code}
                            >
                              {VALIDATION_MESSAGES[code] ?? code}
                            </span>
                          ))}
                          {row.warnings.map((warning) => (
                            <span
                              className={`${STATUS_PILL_CLASS} bg-elevated text-muted`}
                              key={warning}
                              title={warning}
                            >
                              {VALIDATION_MESSAGES[warning] ?? warning}
                            </span>
                          ))}
                          {row.skip ||
                          row.override ||
                          suppressed(row) ||
                          row.duplicate_state === "within_file" ||
                          row.validation_codes.length > 0 ||
                          row.warnings.length > 0 ? null : (
                            <span className="text-muted">OK</span>
                          )}
                        </div>
                      </td>
                      <td className={`${TD_CLASS} max-w-sm`}>
                        <div className="flex flex-wrap gap-1.5 text-xs">
                          {selected.map(({ id, tag }) => (
                            <span
                              className="inline-flex max-w-full flex-wrap items-center gap-2 rounded-full border border-success-line bg-page px-2 py-1 font-semibold break-words text-ink"
                              key={id}
                            >
                              {tag ? tag.display_name : `Unavailable tag ${id.slice(0, 8)}`}
                              {tag?.is_archived ? " (archived)" : ""}
                              <button
                                className="text-muted underline underline-offset-2"
                                disabled={busy}
                                onClick={() => removeTag(row, id)}
                                type="button"
                              >
                                Remove
                              </button>
                            </span>
                          ))}
                          {selected.length === 0 ? (
                            <span className="font-semibold text-warning-text">
                              No tags selected.
                            </span>
                          ) : null}
                        </div>

                        {suggestions.length > 0 ? (
                          <div className="mt-2 flex flex-wrap items-center gap-1.5 text-xs">
                            <span className="font-semibold text-muted">Suggestions:</span>
                            {suggestions.map((tag) => (
                              <button
                                className="max-w-full rounded-full border border-suggestion-line bg-suggestion-soft px-2 py-1 font-semibold break-words text-suggestion-ink disabled:opacity-60"
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
                            A selected tag is no longer available. Remove it or choose another
                            before committing.
                          </p>
                        ) : null}

                        <label className="mt-2 grid max-w-[13rem] gap-1 text-xs font-semibold text-ink">
                          Choose another tag
                          <select
                            className="w-full rounded-lg border border-line bg-page px-2 py-1.5 text-sm font-normal text-ink"
                            disabled={busy}
                            onChange={(event) => {
                              if (event.target.value) addTag(row, event.target.value);
                              event.target.value = "";
                            }}
                            value=""
                          >
                            <option value="">Select a tag…</option>
                            {tags
                              .filter(
                                (tag) => !tag.is_archived && !row.selected_tag_ids.includes(tag.id),
                              )
                              .map((tag) => (
                                <option key={tag.id} value={tag.id}>
                                  {tag.display_name}
                                </option>
                              ))}
                          </select>
                        </label>

                        <form
                          action={async (formData) => {
                            const result = await createMerchantTagAction(formData);
                            if (!result.ok || !result.value) {
                              setMessage({ tone: "error", text: result.message });
                              return;
                            }
                            setTagList((current) =>
                              current.some((tag) => tag.id === result.value!.id)
                                ? current
                                : [...current, result.value!],
                            );
                            addTag(row, result.value.id);
                          }}
                          className="mt-2 grid max-w-[13rem] gap-2"
                        >
                          <label className="grid min-w-0 gap-1 text-xs font-semibold text-ink">
                            New tag
                            <input
                              aria-label={`New tag name for row ${row.source_row_number}`}
                              className="rounded-lg border border-line bg-page px-2 py-1.5 text-sm font-normal text-ink"
                              disabled={busy}
                              name="display_name"
                              placeholder="e.g. Landlord"
                              required
                            />
                          </label>
                          <button
                            aria-label={`Create and add a tag for row ${row.source_row_number}`}
                            className="w-fit rounded-full border border-line px-3 py-1.5 text-xs font-bold text-ink disabled:opacity-60"
                            disabled={busy}
                            type="submit"
                          >
                            Create and add
                          </button>
                        </form>
                      </td>
                      <td className={TD_CLASS}>
                        <div className="flex flex-col items-end gap-2">
                          <label className="inline-flex items-center gap-2 text-xs font-semibold text-ink">
                            <input
                              aria-label={`Skip row ${row.source_row_number}`}
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
                          <button
                            aria-controls={detailId}
                            aria-expanded={isExpanded}
                            aria-label={`${isExpanded ? "Hide" : "Show"} details for row ${row.source_row_number}`}
                            className="rounded-full border border-line px-3 py-1.5 text-xs font-bold text-ink"
                            onClick={() =>
                              setExpandedRowId((current) =>
                                current === row.row_id ? null : row.row_id,
                              )
                            }
                            type="button"
                          >
                            {isExpanded ? "Hide details" : "Details"}
                          </button>
                        </div>
                      </td>
                    </tr>

                    <tr className={EDITOR_ROW_CLASS} hidden={!isExpanded} id={detailId}>
                      <td className="px-4 py-4" colSpan={8}>
                        <div className="grid gap-4 lg:grid-cols-2">
                          <div>
                            <p className="text-xs font-semibold tracking-[0.12em] text-muted uppercase">
                              Source values · physical line {row.source_line}
                            </p>
                            <dl className="mt-2 grid gap-1 text-xs sm:grid-cols-2">
                              {Object.entries(row.raw_values)
                                .filter(([, value]) => value !== null && value !== "")
                                .map(([name, value]) => (
                                  <div className="break-words" key={name}>
                                    <dt className="inline font-semibold">{name}: </dt>
                                    <dd className="inline font-mono">{value}</dd>
                                  </div>
                                ))}
                            </dl>
                          </div>

                          <div className="space-y-3">
                            {row.duplicate_reasons.length > 0 ? (
                              <div className="rounded-xl border border-line bg-surface p-3 text-sm">
                                <p className="font-semibold text-ink">
                                  {suppressed(row)
                                    ? "Matches an existing occurrence"
                                    : "Possible duplicate warning"}
                                </p>
                                <p className="mt-1 text-muted">
                                  {row.duplicate_reasons.join(" · ")}
                                </p>
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
                                      <li>
                                        {row.near_candidate_total - row.near_candidates.length} more
                                        candidate(s)
                                      </li>
                                    ) : null}
                                  </ul>
                                ) : null}
                              </div>
                            ) : null}

                            {row.within_file_group && row.within_file_state === "pending" ? (
                              <div className="rounded-xl border border-warning-line bg-warning-soft p-3 text-sm">
                                <p className="font-semibold text-warning-ink">
                                  This row repeats within the file. Decide the group before
                                  committing.
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
                          </div>
                        </div>
                      </td>
                    </tr>
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
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
            href={reviewHref(
              summary.id,
              pageNumber,
              pageSize,
              sortDirection,
              possibleOnly,
              untaggedOnly,
            )}
            key={pageNumber}
          >
            {pageNumber}
          </Link>
        ))}
        <span className="text-xs text-muted">
          {includedCount} row{includedCount === 1 ? "" : "s"} currently included across the review.
        </span>
        <label className="ml-auto flex items-center gap-2 text-xs font-semibold text-ink">
          Sort by date
          <select
            className="rounded-lg border border-line bg-surface px-2 py-1.5 text-sm font-normal text-ink"
            disabled={busy}
            onChange={(event) =>
              router.push(
                reviewHref(
                  summary.id,
                  1,
                  pageSize,
                  event.target.value as "asc" | "desc",
                  possibleOnly,
                  untaggedOnly,
                ),
              )
            }
            value={sortDirection}
          >
            <option value="asc">Oldest first</option>
            <option value="desc">Newest first</option>
          </select>
        </label>
        <label className="flex items-center gap-2 text-xs font-semibold text-ink">
          Rows per page
          <select
            className="rounded-lg border border-line bg-surface px-2 py-1.5 text-sm font-normal text-ink"
            disabled={busy}
            onChange={(event) =>
              router.push(
                reviewHref(
                  summary.id,
                  1,
                  Number.parseInt(event.target.value, 10),
                  sortDirection,
                  possibleOnly,
                  untaggedOnly,
                ),
              )
            }
            value={pageSize}
          >
            {[25, 50, 100].map((size) => (
              <option key={size} value={size}>
                {size}
              </option>
            ))}
          </select>
        </label>
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
              {summary.imported_count} included · {summary.skipped_count} skipped ·{" "}
              {summary.suppressed_count} suppressed
            </p>
            <p className="mt-1">
              Invalid rows and repeated groups must be resolved across every page.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <button
              className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
              disabled={busy}
              onClick={() =>
                run(() => refreshImportReviewAction(summary.id, version), undefined, true)
              }
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
        {!busy && commitBlockers.length > 0 ? (
          <div className="mt-3 space-y-2" role="status">
            {commitBlockers.map((blocker) => (
              <p
                className="rounded-xl border border-warning-line bg-warning-soft px-3 py-2 text-sm font-semibold text-warning-ink"
                key={blocker}
              >
                {blocker}
              </p>
            ))}
          </div>
        ) : null}
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
