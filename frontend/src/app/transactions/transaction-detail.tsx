"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useState } from "react";

import {
  loadTransactionAction,
  saveTransactionAction,
} from "@/lib/server/transaction-actions";
import type { TransactionActionResult } from "@/lib/server/transaction-actions";
import type {
  Category,
  MerchantTag,
  TransactionDetail,
  TransactionErrorDetails,
} from "@/lib/server/backend-client";

type TransactionDetailViewProps = {
  initialDetail: TransactionDetail;
  categories: Category[];
  tags: MerchantTag[];
  classificationDisabled?: boolean;
  onClose?: () => void;
  onSaved?: () => void;
  onUnavailable?: () => void;
  standalone?: boolean;
};

type Message = { tone: "status" | "error"; text: string; details?: TransactionErrorDetails };

function categoryLabel(category: Category): string {
  const archived = category.is_archived ? " — archived" : "";
  return `${category.display_name} (${category.kind})${archived}`;
}

function tagLabel(tag: MerchantTag): string {
  return tag.is_archived ? `${tag.display_name} — archived` : tag.display_name;
}

function hiddenExplanation(reason: TransactionDetail["transaction"]["hidden_reason"]): string {
  switch (reason) {
    case "statement":
      return "Hidden because its parent statement is hidden.";
    case "individual":
      return "Hidden individually by you.";
    case "both":
      return "Hidden both individually and because its parent statement is hidden.";
    default:
      return "Visible. Hidden transactions stay available under the Hidden view.";
  }
}

export default function TransactionDetailView({
  initialDetail,
  categories,
  tags,
  classificationDisabled = false,
  onClose,
  onSaved,
  onUnavailable,
  standalone = false,
}: TransactionDetailViewProps) {
  const router = useRouter();
  const [detail, setDetail] = useState(initialDetail);
  const transaction = detail.transaction;
  const [categoryId, setCategoryId] = useState(transaction.category.id);
  const [addIds, setAddIds] = useState<string[]>([]);
  const [removeIds, setRemoveIds] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<Message | null>(null);
  const [confirmVisibility, setConfirmVisibility] = useState(false);

  const activeCategories = categories.filter(
    (category) => !category.is_archived || category.id === transaction.category.id,
  );
  const assignedIds = new Set(transaction.merchant_tags.map((tag) => tag.id));
  const removableIds = new Set(
    transaction.merchant_tags
      .filter((tag) => !removeIds.includes(tag.id))
      .map((tag) => tag.id),
  );
  const activeAddOptions = tags.filter(
    (tag) => !tag.is_archived && !assignedIds.has(tag.id) && !addIds.includes(tag.id),
  );

  const handleFailure = useCallback(
    async (result: Extract<TransactionActionResult<unknown>, { ok: false }>) => {
      if (result.kind === "not_found") {
        if (onUnavailable) {
          onUnavailable();
        } else {
          setMessage({
            tone: "error",
            text: "This transaction no longer exists in your workspace.",
            details: result.details,
          });
        }
        return;
      }
      if (
        result.kind === "version_conflict" ||
        result.kind === "selection_stale" ||
        result.kind === "unavailable"
      ) {
        const refreshed = await loadTransactionAction(transaction.id);
        if (refreshed.ok) {
          setDetail(refreshed.value);
          setCategoryId(refreshed.value.transaction.category.id);
          setAddIds([]);
          setRemoveIds([]);
          setMessage({
            tone: "error",
            text:
              result.kind === "unavailable"
                ? "The save outcome is uncertain. The latest saved state has been loaded; check it before retrying."
                : result.message,
            details: result.details,
          });
        } else if (refreshed.kind === "not_found") {
          if (onUnavailable) {
            onUnavailable();
          } else {
            setMessage({
              tone: "error",
              text: "This transaction no longer exists in your workspace.",
            });
          }
        } else {
          setMessage({
            tone: "error",
            text: "The save could not be confirmed and the latest values could not be reloaded. Retry loading this transaction before saving again.",
            details: result.details,
          });
        }
        return;
      }
      setMessage({ tone: "error", text: result.message, details: result.details });
    },
    [onUnavailable, transaction.id],
  );

  const refreshDetail = useCallback(async () => {
    const refreshed = await loadTransactionAction(transaction.id);
    if (refreshed.ok) {
      setDetail(refreshed.value);
      setCategoryId(refreshed.value.transaction.category.id);
      setAddIds([]);
      setRemoveIds([]);
    }
    return refreshed;
  }, [transaction.id]);

  async function saveMetadata(
    changes: {
      category_id?: string;
      add_tag_ids?: string[];
      remove_tag_ids?: string[];
      is_hidden_by_user?: boolean;
    },
    successText: string,
  ) {
    setBusy(true);
    setMessage(null);
    try {
      const result = await saveTransactionAction({
        id: transaction.id,
        expected_version: transaction.version,
        changes,
        confirm: changes.is_hidden_by_user !== undefined ? true : undefined,
      });
      if (!result.ok) {
        await handleFailure(result);
        return false;
      }
      // Install the authoritative mutation result before asking for the rest
      // of the detail, so a failed refresh cannot make a completed save look
      // like it never happened.
      const savedTransaction = result.value.transaction;
      setDetail((current) => ({ ...current, transaction: savedTransaction }));
      setCategoryId(savedTransaction.category.id);
      setAddIds([]);
      setRemoveIds([]);
      const refreshed = await refreshDetail();
      onSaved?.();
      if (standalone) {
        router.refresh();
      }
      if (refreshed.ok) {
        setMessage({ tone: "status", text: successText });
        return true;
      }
      if (refreshed.kind === "not_found") {
        if (onUnavailable) {
          onUnavailable();
        } else {
          setMessage({
            tone: "status",
            text: `${successText} This transaction no longer exists in your workspace.`,
          });
        }
        return false;
      }
      setMessage({
        tone: "status",
        text: `${successText} The rest of this detail could not be refreshed, so suggestions may be stale. Reload before the next save.`,
      });
      return true;
    } catch {
      const refreshed = await refreshDetail();
      if (refreshed.ok) {
        setMessage({
          tone: "error",
          text: "The save outcome is uncertain. The latest saved state has been loaded; check it before retrying.",
        });
      } else if (refreshed.kind === "not_found") {
        if (onUnavailable) {
          onUnavailable();
        } else {
          setMessage({
            tone: "error",
            text: "The save outcome is uncertain and this transaction no longer exists in your workspace.",
          });
        }
      } else {
        setMessage({
          tone: "error",
          text: "The save outcome is uncertain and the latest state could not be reloaded. Retry loading this transaction before saving again.",
        });
      }
      return false;
    } finally {
      setBusy(false);
      setConfirmVisibility(false);
    }
  }

  async function saveSelectedChanges() {
    const changes: {
      category_id?: string;
      add_tag_ids?: string[];
      remove_tag_ids?: string[];
    } = {};
    if (categoryId !== transaction.category.id) {
      changes.category_id = categoryId;
    }
    if (addIds.length > 0) {
      changes.add_tag_ids = addIds;
    }
    if (removeIds.length > 0) {
      changes.remove_tag_ids = removeIds;
    }
    if (Object.keys(changes).length === 0) {
      setMessage({ tone: "status", text: "No classification changes are selected." });
      return;
    }
    await saveMetadata(changes, "Transaction classification saved.");
  }

  async function acceptSuggestion(tagId: string) {
    const tag = detail.suggested_tags.find((suggestion) => suggestion.id === tagId);
    await saveMetadata(
      { add_tag_ids: [tagId] },
      tag ? `Suggested tag “${tag.display_name}” added.` : "Suggested tag added.",
    );
  }

  async function toggleHidden() {
    const nextHidden = !transaction.is_hidden_by_user;
    await saveMetadata(
      { is_hidden_by_user: nextHidden },
      nextHidden ? "Transaction hidden." : "Transaction restored.",
    );
  }

  return (
    <section
      aria-labelledby="transaction-detail-title"
      className={
        standalone
          ? "mt-8 rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-8"
          : "rounded-[2rem] border border-line bg-surface p-5 card-shadow sm:p-7"
      }
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-xs font-semibold tracking-[0.14em] text-accent-text uppercase">
            Transaction detail
          </p>
          <h2
            className="mt-2 text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink"
            id="transaction-detail-title"
            tabIndex={-1}
          >
            {transaction.description.trim() === ""
              ? "No description"
              : transaction.description}
          </h2>
          <p className="mt-1 text-sm text-muted">
            {transaction.transaction_date} · version {transaction.version} ·{" "}
            {transaction.direction === "in"
              ? "money in"
              : transaction.direction === "out"
                ? "money out"
                : "zero value"}
          </p>
        </div>
        {onClose ? (
          <button
            autoFocus
            className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            onClick={onClose}
            type="button"
          >
            Close detail
          </button>
        ) : (
          <Link
            className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            href="/transactions"
          >
            Back to transactions
          </Link>
        )}
      </div>

      <dl className="mt-6 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <div className="rounded-xl border border-line bg-page p-3">
          <dt className="text-xs font-semibold tracking-[0.1em] text-muted uppercase">Amount</dt>
          <dd className="mt-1 font-mono text-xl font-semibold tabular-nums text-ink">
            {transaction.amount} {transaction.currency}
          </dd>
        </div>
        <div className="rounded-xl border border-line bg-page p-3">
          <dt className="text-xs font-semibold tracking-[0.1em] text-muted uppercase">Account</dt>
          <dd className="mt-1 text-sm font-semibold text-ink">
            {transaction.financial_account.display_name}
            {transaction.financial_account.is_archived ? " (archived)" : ""}
          </dd>
        </div>
        <div className="rounded-xl border border-line bg-page p-3">
          <dt className="text-xs font-semibold tracking-[0.1em] text-muted uppercase">
            Statement
          </dt>
          <dd className="mt-1 text-sm font-semibold text-ink">
            <Link
              className="underline underline-offset-4"
              href={`/imports/${transaction.statement_import.id}`}
            >
              {transaction.statement_import.statement_label}
            </Link>
            {transaction.statement_import.is_hidden ? " (hidden)" : ""}
          </dd>
        </div>
        <div className="rounded-xl border border-line bg-page p-3">
          <dt className="text-xs font-semibold tracking-[0.1em] text-muted uppercase">
            Source position
          </dt>
          <dd className="mt-1 text-sm font-semibold text-ink">
            Row {detail.source_row_number}
            {detail.source_page !== null ? ` · page ${detail.source_page}` : ""}
          </dd>
        </div>
      </dl>

      <p className="mt-4 rounded-xl border border-line bg-elevated px-4 py-3 text-sm leading-6 text-muted">
        Imported date, description, amount, account and statement identity are read-only.
        Only category, merchant tags and your individual hide flag can change.{" "}
        {hiddenExplanation(transaction.hidden_reason)}
      </p>

      {message ? (
        <div
          className={
            message.tone === "error"
              ? "mt-4 rounded-xl border border-warning-line bg-warning-soft p-3 text-sm font-semibold text-warning-ink"
              : "mt-4 rounded-xl border border-line bg-elevated p-3 text-sm font-semibold text-ink"
          }
          role={message.tone === "error" ? "alert" : "status"}
        >
          <p>{message.text}</p>
          {message.details?.statements?.length ? (
            <ul className="mt-2 space-y-1 font-normal">
              {message.details.statements.map((statement) => (
                <li key={statement.id}>
                  Restore{" "}
                  <Link
                    className="underline underline-offset-4"
                    href={`/imports/${statement.id}`}
                  >
                    {statement.statement_label}
                  </Link>{" "}
                  in Imports first.
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}

      <fieldset
        className="mt-6 rounded-2xl border border-line p-4"
        disabled={busy || classificationDisabled}
      >
        <legend className="px-2 text-sm font-bold text-ink">Classification</legend>
        {classificationDisabled ? (
          <p
            className="mb-3 rounded-xl border border-warning-line bg-warning-soft p-3 text-sm font-semibold text-warning-ink"
            role="alert"
          >
            Categories or tags did not load, so classification choices may be incomplete. Refresh
            the page before editing.
          </p>
        ) : null}
        <div className="grid gap-4 lg:grid-cols-2">
          <label className="text-sm font-semibold text-ink">
            Category
            <select
              className="mt-1 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              onChange={(event) => setCategoryId(event.target.value)}
              value={categoryId}
            >
              {activeCategories.map((category) => (
                <option key={category.id} value={category.id}>
                  {categoryLabel(category)}
                </option>
              ))}
            </select>
          </label>

          <div>
            <p className="text-sm font-semibold text-ink">Assigned tags</p>
            <div className="mt-1 flex flex-wrap gap-2">
              {transaction.merchant_tags.length === 0 ? (
                <span className="text-sm text-muted">No tags assigned.</span>
              ) : null}
              {transaction.merchant_tags.map((tag) => {
                const markedForRemoval = removeIds.includes(tag.id);
                return (
                  <span
                    className={
                      markedForRemoval
                        ? "rounded-full border border-warning-line bg-warning-soft px-3 py-1 text-xs font-semibold text-warning-ink line-through"
                        : "rounded-full border border-line bg-page px-3 py-1 text-xs font-semibold text-ink"
                    }
                    key={tag.id}
                  >
                    {tag.display_name}
                    {tag.is_archived ? " (archived)" : ""}
                    <button
                      aria-label={
                        markedForRemoval
                          ? `Keep tag ${tag.display_name}`
                          : `Remove tag ${tag.display_name}`
                      }
                      className="ml-2 underline underline-offset-2"
                      onClick={() =>
                        setRemoveIds((current) =>
                          markedForRemoval
                            ? current.filter((id) => id !== tag.id)
                            : [...current, tag.id],
                        )
                      }
                      type="button"
                    >
                      {markedForRemoval ? "Undo" : "Remove"}
                    </button>
                  </span>
                );
              })}
            </div>
            {removableIds.size === 0 && transaction.merchant_tags.length > 0 ? (
              <p className="mt-1 text-xs text-muted">
                All currently assigned tags are marked for removal.
              </p>
            ) : null}
          </div>
        </div>

        <div className="mt-4">
          <p className="text-sm font-semibold text-ink">Add active tags</p>
          <div className="mt-2 flex max-h-40 flex-wrap gap-2 overflow-y-auto">
            {activeAddOptions.length === 0 ? (
              <span className="text-sm text-muted">
                No more active tags are available. Create or restore tags in{" "}
                <Link className="underline underline-offset-4" href="/settings">
                  Settings
                </Link>
                .
              </span>
            ) : null}
            {activeAddOptions.map((tag) => (
              <button
                className="rounded-full border border-line px-3 py-1 text-xs font-semibold text-ink"
                key={tag.id}
                onClick={() => setAddIds((current) => [...current, tag.id])}
                type="button"
              >
                Add {tagLabel(tag)}
              </button>
            ))}
          </div>
          {addIds.length > 0 ? (
            <div className="mt-2 flex flex-wrap items-center gap-2 text-xs text-muted">
              <span>Pending additions:</span>
              {addIds.map((id) => {
                const tag = tags.find((candidate) => candidate.id === id);
                return (
                  <span
                    className="rounded-full border border-accent-soft-line bg-accent-soft px-2 py-1 font-semibold text-accent-soft-ink"
                    key={id}
                  >
                    {tag?.display_name ?? id}
                    <button
                      aria-label={`Remove pending tag ${tag?.display_name ?? id}`}
                      className="ml-2 underline underline-offset-2 disabled:opacity-60"
                      disabled={busy || classificationDisabled}
                      onClick={() =>
                        setAddIds((current) => current.filter((candidate) => candidate !== id))
                      }
                      type="button"
                    >
                      Remove
                    </button>
                  </span>
                );
              })}
            </div>
          ) : null}
        </div>

        <div className="mt-4 flex flex-wrap gap-2">
          <button
            className="rounded-full border border-line bg-surface px-4 py-2 text-sm font-bold text-ink"
            disabled={busy}
            onClick={() => void saveSelectedChanges()}
            type="button"
          >
            Save classification
          </button>
          <button
            className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            disabled={busy}
            onClick={() => {
              setCategoryId(transaction.category.id);
              setAddIds([]);
              setRemoveIds([]);
            }}
            type="button"
          >
            Reset selection
          </button>
        </div>
      </fieldset>

      <section aria-labelledby="suggestions-title" className="mt-6">
        <h3 className="text-sm font-bold text-ink" id="suggestions-title">
          Suggested tags
        </h3>
        <p className="mt-1 text-sm text-muted">
          Suggestions come from your active tags and this description. Nothing is applied until
          you accept it.
        </p>
        <div className="mt-2 flex flex-wrap gap-2">
          {detail.suggested_tags.length === 0 ? (
            <span className="text-sm text-muted">No suggestions for this description.</span>
          ) : null}
          {detail.suggested_tags.map((tag) => (
            <button
              className="rounded-full border border-accent-soft-line bg-accent-soft px-3 py-1 text-xs font-bold text-accent-soft-ink disabled:opacity-60"
              disabled={busy || classificationDisabled}
              key={tag.id}
              onClick={() => void acceptSuggestion(tag.id)}
              type="button"
            >
              Suggested: {tag.display_name} — add
            </button>
          ))}
        </div>
        {detail.has_more_suggestions ? (
          <p className="mt-2 text-xs text-muted">
            More suggestions exist; assigning or removing a tag refreshes this list.
          </p>
        ) : null}
      </section>

      <section aria-labelledby="visibility-title" className="mt-6">
        <h3 className="text-sm font-bold text-ink" id="visibility-title">
          Visibility
        </h3>
        <p className="mt-1 text-sm text-muted">
          Hiding keeps the record and duplicate protection. Restoring clears only your individual
          flag. The statement layer stays authoritative.
        </p>
        {transaction.statement_import.is_hidden ? (
          <p className="mt-2 rounded-xl border border-warning-line bg-warning-soft p-3 text-sm font-semibold text-warning-ink">
            This statement is hidden. Restore{" "}
            <Link
              className="underline underline-offset-4"
              href={`/imports/${transaction.statement_import.id}`}
            >
              {transaction.statement_import.statement_label}
            </Link>{" "}
            in Imports before restoring this transaction individually.
          </p>
        ) : null}
        {confirmVisibility ? (
          <div className="mt-3 rounded-2xl border border-line bg-elevated p-3">
            <p className="text-sm font-semibold text-ink">
              {transaction.is_hidden_by_user
                ? "Restore this transaction?"
                : "Hide this transaction?"}
            </p>
            <p className="mt-1 text-sm text-muted">
              {transaction.is_hidden_by_user
                ? "Only your individual hide flag is cleared. The parent statement and other transactions are unchanged."
                : "The record, amount and duplicate identity stay; it leaves the default visible list."}
            </p>
            <div className="mt-2 flex flex-wrap gap-2">
              <button
                className="rounded-full border border-line bg-surface px-4 py-2 text-sm font-bold text-ink"
                disabled={busy}
                onClick={() => void toggleHidden()}
                type="button"
              >
                {transaction.is_hidden_by_user ? "Restore transaction" : "Hide transaction"}
              </button>
              <button
                className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
                onClick={() => setConfirmVisibility(false)}
                type="button"
              >
                Cancel
              </button>
            </div>
          </div>
        ) : (
          <button
            className="mt-3 rounded-full border border-line px-4 py-2 text-sm font-bold text-ink disabled:opacity-60"
            disabled={busy || (transaction.statement_import.is_hidden && transaction.is_hidden_by_user)}
            onClick={() => setConfirmVisibility(true)}
            type="button"
          >
            {transaction.is_hidden_by_user ? "Restore transaction…" : "Hide transaction…"}
          </button>
        )}
      </section>
    </section>
  );
}
