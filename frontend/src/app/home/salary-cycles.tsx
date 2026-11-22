"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";

import type {
  FinancialAccount,
  SalaryCycleBoundary,
  SalaryCyclePreview,
  TransactionRow,
} from "@/lib/server/backend-client";
import { queryTransactionsAction } from "@/lib/server/transaction-actions";
import {
  confirmSalaryCycleAction,
  loadSalaryCyclesAction,
  previewSalaryCycleAction,
  removeSalaryCycleAction,
} from "@/lib/server/reporting-actions";
import { formatMoney } from "@/lib/money-format";

type SalaryCycleWorkspaceProps = {
  initialRevision: string;
  initialStarts: SalaryCycleBoundary[];
  nextBeforeDate: string | null;
  accounts: FinancialAccount[];
  today: string;
};

type ReceiptState = {
  rows: TransactionRow[];
  page: number;
  hasMore: boolean;
  loading: boolean;
  error: string | null;
};

export default function SalaryCycleWorkspace({
  initialRevision,
  initialStarts,
  nextBeforeDate,
  accounts,
  today,
}: SalaryCycleWorkspaceProps) {
  const router = useRouter();
  const [revision, setRevision] = useState(initialRevision);
  const [starts, setStarts] = useState(initialStarts);
  const [cursor, setCursor] = useState(nextBeforeDate);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  type PreviewContext = {
    value: SalaryCyclePreview;
    startId: string | null;
    expectedRevision: string;
  };

  const [preview, setPreview] = useState<PreviewContext | null>(null);
  const [previewBusy, setPreviewBusy] = useState(false);
  const [accountId, setAccountId] = useState("");
  const [search, setSearch] = useState("");
  const [receipts, setReceipts] = useState<ReceiptState>({
    rows: [],
    page: 1,
    hasMore: false,
    loading: false,
    error: null,
  });
  const generation = useRef(0);
  const previewGeneration = useRef(0);
  const previewSectionRef = useRef<HTMLElement | null>(null);
  const previewTriggerRef = useRef<HTMLElement | null>(null);
  const messageRef = useRef<HTMLParagraphElement | null>(null);
  const errorRef = useRef<HTMLParagraphElement | null>(null);
  const restoreFocusRef = useRef<"trigger" | "message" | "error" | null>(null);
  const [announcement, setAnnouncement] = useState("");

  // The preview replaces the trigger in reading order, so keyboard focus
  // follows the preview into view and returns to the trigger when cancelled.
  useEffect(() => {
    if (preview) {
      previewSectionRef.current?.focus();
      return;
    }
    const target = restoreFocusRef.current;
    restoreFocusRef.current = null;
    if (target === "trigger") {
      const trigger = previewTriggerRef.current;
      if (trigger?.isConnected) {
        trigger.focus();
      }
      return;
    }
    const destination =
      target === "message" ? messageRef.current : target === "error" ? errorRef.current : null;
    if (destination) {
      destination.focus();
    }
  }, [preview]);

  const applyFirstPage = (
    nextStarts: SalaryCycleBoundary[],
    nextRevision: string,
    nextCursor: string | null,
  ) => {
    setStarts(nextStarts);
    setRevision(nextRevision);
    setCursor(nextCursor);
  };

  const refreshFirstPage = async (reason: string) => {
    generation.current += 1;
    previewGeneration.current += 1;
    setPreview(null);
    const current = generation.current;
    const result = await loadSalaryCyclesAction(null);
    if (current !== generation.current) {
      return;
    }
    if (!result.ok) {
      setError(result.message);
      return;
    }
    applyFirstPage(result.value.starts, result.value.revision, result.value.next_before_date);
    setAnnouncement(reason);
  };

  const retirePreview = async (message: string, reason: string, clearReceipts = false) => {
    restoreFocusRef.current = "error";
    setError(message);
    setMessage(null);
    if (clearReceipts) {
      setReceipts({ rows: [], page: 1, hasMore: false, loading: false, error: null });
    }
    await refreshFirstPage(reason);
  };

  const loadMore = async () => {
    if (!cursor || loadingMore) {
      return;
    }
    const requestGeneration = generation.current;
    setLoadingMore(true);
    const result = await loadSalaryCyclesAction(cursor);
    if (requestGeneration !== generation.current) {
      setLoadingMore(false);
      return;
    }
    setLoadingMore(false);
    if (!result.ok) {
      setError(result.message);
      return;
    }
    if (result.value.revision !== revision) {
      // A page from a different sequence must never be appended. Discard all
      // accumulated pages, reload the first page and announce the change.
      await refreshFirstPage(
        "The salary-cycle sequence changed while paging. The list was reloaded.",
      );
      return;
    }
    const seen = new Set(starts.map((start) => start.id));
    setStarts([...starts, ...result.value.starts.filter((start) => !seen.has(start.id))]);
    setRevision(result.value.revision);
    setCursor(result.value.next_before_date);
  };

  const findReceipts = async (page = 1, append = false) => {
    setReceipts((current) => ({ ...current, loading: true, error: null }));
    const result = await queryTransactionsAction({
      filters: {
        visibility: "visible",
        direction: "in",
        end_date: today,
        ...(accountId ? { financial_account_id: accountId } : {}),
        ...(search.trim() ? { search: search.trim() } : {}),
      },
      page,
      page_size: 25,
      sort_by: "date",
      sort_direction: "desc",
    });
    if (!result.ok) {
      setReceipts((current) => ({ ...current, loading: false, error: result.message }));
      return;
    }
    setReceipts((current) => ({
      rows: append ? [...current.rows, ...result.value.transactions] : result.value.transactions,
      page,
      hasMore: page * result.value.page_size < result.value.matching_visible_count,
      loading: false,
      error: null,
    }));
  };

  const previewConfirm = async (row: TransactionRow) => {
    const request = ++previewGeneration.current;
    setPreviewBusy(true);
    setError(null);
    const result = await previewSalaryCycleAction({
      action: "confirm",
      transaction_id: row.id,
      expected_transaction_version: row.version,
    });
    setPreviewBusy(false);
    if (request !== previewGeneration.current) {
      return;
    }
    if (!result.ok) {
      setError(result.message);
      return;
    }
    setPreview({
      value: result.value,
      startId: null,
      expectedRevision: result.value.revision,
    });
  };

  const previewRemove = async (start: SalaryCycleBoundary) => {
    const request = ++previewGeneration.current;
    setPreviewBusy(true);
    setError(null);
    const result = await previewSalaryCycleAction({ action: "remove", start_id: start.id });
    setPreviewBusy(false);
    if (request !== previewGeneration.current) {
      return;
    }
    if (!result.ok) {
      setError(result.message);
      return;
    }
    setPreview({
      value: result.value,
      startId: start.id,
      expectedRevision: result.value.revision,
    });
  };

  const confirmStart = async () => {
    if (!preview?.value.source) {
      return;
    }
    const source = preview.value.source;
    const expectedRevision = preview.expectedRevision;
    setPreviewBusy(true);
    setError(null);
    const result = await confirmSalaryCycleAction({
      transaction_id: source.id,
      expected_transaction_version: source.version,
      expected_revision: expectedRevision,
      confirm: true,
    });
    setPreviewBusy(false);
    if (!result.ok) {
      await retirePreview(
        result.kind === "version_conflict"
          ? "That transaction changed since preview. The sequence was reloaded and the receipt list cleared; preview an eligible receipt again."
          : `${result.message} The sequence was reloaded; preview again before retrying.`,
        "The salary-cycle sequence was reloaded after a failed confirmation. Review the latest dates before retrying.",
        result.kind === "version_conflict",
      );
      return;
    }
    restoreFocusRef.current = "message";
    setMessage(
      result.value.status === "already_confirmed"
        ? `${result.value.start_date} was already confirmed. No boundary, source or revision changed.`
        : `Confirmed ${result.value.start_date}. Reporting periods and comparisons were recomputed.`,
    );
    await refreshFirstPage("The salary-cycle sequence was refreshed.");
    router.refresh();
  };

  const confirmRemoval = async () => {
    if (!preview?.startId) {
      await retirePreview(
        "That preview did not identify a boundary. The sequence was reloaded; preview the removal again.",
        "The salary-cycle sequence was reloaded because the removal preview had no boundary identity.",
      );
      return;
    }
    const startId = preview.startId;
    const expectedRevision = preview.expectedRevision;
    setPreviewBusy(true);
    setError(null);
    const result = await removeSalaryCycleAction({
      start_id: startId,
      expected_revision: expectedRevision,
    });
    setPreviewBusy(false);
    if (!result.ok) {
      await retirePreview(
        `${result.message} The sequence was reloaded; preview the removal again before retrying.`,
        "The salary-cycle sequence was reloaded after a failed removal. Review the latest dates before retrying removal.",
      );
      return;
    }
    restoreFocusRef.current = "message";
    setMessage(
      `Removed ${result.value.removed_date}. Affected periods and comparisons were recomputed.`,
    );
    await refreshFirstPage("The salary-cycle sequence was refreshed.");
    router.refresh();
  };

  return (
    <div className="mt-6 space-y-4">
      <section className="rounded-[1.5rem] border border-line bg-surface p-4 card-shadow">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 className="text-lg font-semibold text-ink">Confirmed salary-cycle starts</h2>
          <p className="text-xs text-muted">Sequence revision {revision}</p>
        </div>
        <p className="mt-1 text-xs leading-5 text-muted">
          Confirmed dates are workspace-wide reporting metadata. They survive source hiding or
          deletion and never change the source transaction. Removing a date changes affected periods
          and comparisons after a warning.
        </p>
        {starts.length === 0 ? (
          <p className="mt-3 rounded-xl border border-warning-line bg-warning-soft p-3 text-sm text-warning-ink">
            No salary-cycle start is confirmed. Salary-cycle reporting shows setup until an eligible
            receipt date is selected, or you can use Calendar or Custom dates.
          </p>
        ) : (
          <ul className="mt-3 space-y-2">
            {starts.map((start, index) => (
              <li
                className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-line bg-page p-2"
                key={start.id}
              >
                <div>
                  <p className="text-sm font-semibold text-ink">
                    {start.start_date} {index === 0 ? "· latest open cycle" : ""}
                  </p>
                  <p className="text-xs text-muted">Confirmed {start.created_at.slice(0, 10)}</p>
                </div>
                <button
                  className="min-h-9 rounded-full border border-line px-3 text-xs font-bold text-ink disabled:opacity-50"
                  disabled={previewBusy}
                  onClick={(event) => {
                    previewTriggerRef.current = event.currentTarget;
                    void previewRemove(start);
                  }}
                  type="button"
                >
                  Remove date
                </button>
              </li>
            ))}
          </ul>
        )}
        {cursor ? (
          <button
            className="mt-3 min-h-9 rounded-full border border-line px-3 text-xs font-bold text-ink disabled:opacity-50"
            disabled={loadingMore}
            onClick={loadMore}
            type="button"
          >
            {loadingMore ? "Loading more dates…" : "More confirmed dates"}
          </button>
        ) : null}
      </section>

      {announcement ? (
        <p
          aria-live="polite"
          className="rounded-xl border border-suggestion-line bg-suggestion-soft p-3 text-xs font-semibold text-suggestion-ink"
        >
          {announcement}
        </p>
      ) : null}
      {message ? (
        <p
          aria-live="polite"
          className="rounded-xl border border-success-line bg-success-soft p-3 text-sm font-semibold text-success-ink"
          ref={messageRef}
          tabIndex={-1}
        >
          {message}
        </p>
      ) : null}
      {error ? (
        <p
          aria-live="assertive"
          className="rounded-xl border border-warning-line bg-warning-soft p-3 text-sm text-warning-ink"
          ref={errorRef}
          role="alert"
          tabIndex={-1}
        >
          {error}
        </p>
      ) : null}

      <section className="rounded-[1.5rem] border border-line bg-surface p-4 card-shadow">
        <h2 className="text-lg font-semibold text-ink">Choose an eligible receipt</h2>
        <p className="mt-1 text-xs leading-5 text-muted">
          Only an owned, visible, positive imported transaction dated today or earlier is eligible.
          Any category is allowed; the receipt’s category, tags, description, amount and date are
          never changed.
        </p>
        <form
          className="mt-3 grid gap-2 sm:grid-cols-[1fr_1fr_auto]"
          onSubmit={(event) => {
            event.preventDefault();
            void findReceipts(1, false);
          }}
        >
          <label className="text-sm font-semibold text-ink">
            Account
            <select
              className="mt-1 min-h-10 w-full rounded-xl border border-line bg-page px-3 text-sm text-ink"
              onChange={(event) => setAccountId(event.target.value)}
              value={accountId}
            >
              <option value="">All accounts</option>
              {accounts.map((account) => (
                <option key={account.id} value={account.id}>
                  {account.display_name}
                  {account.is_archived ? " (archived)" : ""}
                </option>
              ))}
            </select>
          </label>
          <label className="text-sm font-semibold text-ink">
            Private description search
            <input
              className="mt-1 min-h-10 w-full rounded-xl border border-line bg-page px-3 text-sm text-ink"
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Optional; stays in this form"
              type="search"
              value={search}
            />
          </label>
          <button
            className="min-h-10 self-end rounded-full bg-ink px-4 text-sm font-bold text-page"
            disabled={receipts.loading}
            type="submit"
          >
            {receipts.loading ? "Searching…" : "Find receipts"}
          </button>
        </form>
        {receipts.error ? (
          <p className="mt-2 text-sm text-warning-ink" role="alert">
            {receipts.error}
          </p>
        ) : null}
        {receipts.rows.length > 0 ? (
          <ul className="mt-3 space-y-2">
            {receipts.rows.map((row) => (
              <li
                className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-line bg-page p-2"
                key={row.id}
              >
                <div>
                  <p className="text-sm font-semibold text-ink">
                    {row.transaction_date} · {formatMoney(row.amount)}
                  </p>
                  <p className="text-xs text-muted">
                    {row.description} · {row.financial_account.display_name} ·{" "}
                    {row.category.display_name}
                  </p>
                </div>
                <button
                  className="min-h-9 rounded-full border border-line px-3 text-xs font-bold text-ink disabled:opacity-50"
                  disabled={previewBusy}
                  onClick={(event) => {
                    previewTriggerRef.current = event.currentTarget;
                    void previewConfirm(row);
                  }}
                  type="button"
                >
                  Use as salary start
                </button>
              </li>
            ))}
          </ul>
        ) : receipts.loading ? null : (
          <p className="mt-3 text-xs text-muted">Search for visible incoming receipts to begin.</p>
        )}
        {receipts.hasMore ? (
          <button
            className="mt-3 min-h-9 rounded-full border border-line px-3 text-xs font-bold text-ink"
            onClick={() => void findReceipts(receipts.page + 1, true)}
            type="button"
          >
            More receipts
          </button>
        ) : null}
      </section>

      {preview ? (
        <section
          aria-live="polite"
          className="rounded-[1.5rem] border border-accent-soft-line bg-accent-soft p-4"
          ref={previewSectionRef}
          tabIndex={-1}
        >
          <h2 className="text-lg font-semibold text-accent-soft-ink">
            {preview.value.action === "confirm" ? "Confirm this reporting date" : "Confirm removal"}
          </h2>
          <p className="mt-1 text-sm text-accent-soft-ink">
            {preview.value.action === "confirm" && preview.value.source
              ? `${preview.value.source.transaction_date} · ${formatMoney(preview.value.source.amount)} · ${preview.value.source.description}`
              : `Remove ${preview.value.date}`}
          </p>
          {preview.value.already_confirmed ? (
            <p className="mt-2 rounded-xl border border-warning-line bg-warning-soft p-3 text-sm text-warning-ink">
              This date is already confirmed. Selecting another receipt on the same date creates no
              boundary, replaces no source and changes no revision or cycle.
            </p>
          ) : null}
          {preview.value.affected_intervals.length > 0 ? (
            <div className="mt-2">
              <p className="text-xs font-bold tracking-[0.1em] text-accent-soft-ink uppercase">
                Affected ranges
              </p>
              <ul className="mt-1 space-y-1 text-sm text-accent-soft-ink">
                {preview.value.affected_intervals.map((interval) => (
                  <li key={`${interval.label}-${interval.start_date}-${interval.end_date}`}>
                    {interval.label}: {interval.start_date} to {interval.end_date}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
          <ul className="mt-2 space-y-1 text-xs leading-5 text-accent-soft-ink">
            {preview.value.warnings.map((warning) => (
              <li key={warning}>{warning}</li>
            ))}
          </ul>
          <p className="mt-2 rounded-xl border border-accent-soft-line bg-surface p-2 text-xs text-muted">
            Retention policy: this confirms only the calendar date as reporting metadata. The source
            transaction’s description, amount, account, category and tags stay unchanged and are
            never stored in the boundary. The date remains if its source is later hidden or deleted.
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            {preview.value.action === "confirm" && !preview.value.already_confirmed ? (
              <button
                className="min-h-10 rounded-full bg-ink px-4 text-sm font-bold text-page disabled:opacity-50"
                disabled={previewBusy}
                onClick={() => void confirmStart()}
                type="button"
              >
                {previewBusy ? "Confirming…" : "Confirm start date"}
              </button>
            ) : null}
            {preview.value.action === "remove" ? (
              <button
                className="min-h-10 rounded-full bg-ink px-4 text-sm font-bold text-page disabled:opacity-50"
                disabled={previewBusy}
                onClick={() => void confirmRemoval()}
                type="button"
              >
                {previewBusy ? "Removing…" : "Confirm removal"}
              </button>
            ) : null}
            <button
              className="min-h-10 rounded-full border border-line bg-surface px-4 text-sm font-bold text-ink"
              onClick={() => {
                restoreFocusRef.current = "trigger";
                setPreview(null);
              }}
              type="button"
            >
              Cancel
            </button>
          </div>
        </section>
      ) : null}
    </div>
  );
}
