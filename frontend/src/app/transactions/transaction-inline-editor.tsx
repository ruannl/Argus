"use client";

import { useState } from "react";

import { saveTransactionAction } from "@/lib/server/transaction-actions";
import type { Category, MerchantTag, TransactionRow } from "@/lib/server/backend-client";

type TransactionInlineEditorProps = {
  row: TransactionRow;
  categories: Category[];
  tags: MerchantTag[];
  onSaved: () => void;
  onStale: (message: string) => void;
};

export default function TransactionInlineEditor({
  row,
  categories,
  tags,
  onSaved,
  onStale,
}: TransactionInlineEditorProps) {
  const [categoryId, setCategoryId] = useState(row.category.id);
  const [addIds, setAddIds] = useState<string[]>([]);
  const [removeIds, setRemoveIds] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  // The draft belongs to the row version it was opened from. A background
  // refresh must never pair this draft's category with a newer row version.
  const [base, setBase] = useState({
    version: row.version,
    categoryId: row.category.id,
  });
  const draftIsStale = row.version !== base.version;

  const assignable = categories.filter(
    (category) => !category.is_archived || category.id === row.category.id,
  );
  const assignedIds = new Set(row.merchant_tags.map((tag) => tag.id));
  // Selected additions stay in the option list so the multi-select keeps
  // showing them and the user can deselect them before saving.
  const addOptions = tags.filter((tag) => !tag.is_archived && !assignedIds.has(tag.id));

  async function save() {
    if (draftIsStale) {
      setMessage(
        "This transaction changed while you were editing. Reopen the inline editor to review the latest values before saving.",
      );
      return;
    }
    const changes: {
      category_id?: string;
      add_tag_ids?: string[];
      remove_tag_ids?: string[];
    } = {};
    if (categoryId !== base.categoryId) {
      changes.category_id = categoryId;
    }
    if (addIds.length > 0) {
      changes.add_tag_ids = addIds;
    }
    if (removeIds.length > 0) {
      changes.remove_tag_ids = removeIds;
    }
    if (Object.keys(changes).length === 0) {
      setMessage("No inline changes are selected.");
      return;
    }

    setBusy(true);
    setMessage(null);
    try {
      const result = await saveTransactionAction({
        id: row.id,
        expected_version: base.version,
        changes,
      });
      if (!result.ok) {
        if (result.kind === "version_conflict" || result.kind === "not_found") {
          onStale(
            result.kind === "not_found"
              ? "That transaction no longer exists in your workspace. The list has been refreshed."
              : "This transaction changed while you were editing. The latest row values are loaded; reopen the inline editor to save.",
          );
          return;
        }
        if (result.kind === "unavailable" || result.kind === "unauthorized") {
          onStale(
            result.kind === "unauthorized"
              ? "Your session expired. Sign in again before retrying."
              : "The inline save outcome is uncertain. The list has been refreshed; check the saved value before retrying.",
          );
          return;
        }
        setMessage(result.message);
        return;
      }
      setBase({
        version: result.value.transaction.version,
        categoryId: result.value.transaction.category.id,
      });
      setMessage("Inline change saved.");
      setAddIds([]);
      setRemoveIds([]);
      onSaved();
    } catch {
      onStale(
        "The inline save outcome is uncertain. The list has been refreshed; check the saved value before retrying.",
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mt-3 rounded-xl border border-line bg-surface p-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="text-xs font-semibold text-ink">
          Inline category for {row.description || "this transaction"}
          <select
            className="mt-1 w-full rounded-lg border border-line bg-page px-2 py-1.5 text-sm text-ink"
            disabled={busy || draftIsStale}
            onChange={(event) => setCategoryId(event.target.value)}
            value={categoryId}
          >
            {assignable.map((category) => (
              <option key={category.id} value={category.id}>
                {category.display_name} ({category.kind})
                {category.is_archived ? " — archived" : ""}
              </option>
            ))}
          </select>
        </label>
        <label className="text-xs font-semibold text-ink">
          Add tags inline for {row.description || "this transaction"}
          <select
            className="mt-1 h-20 w-full rounded-lg border border-line bg-page px-2 py-1.5 text-sm text-ink"
            disabled={busy || draftIsStale}
            multiple
            onChange={(event) =>
              setAddIds(Array.from(event.target.selectedOptions, (option) => option.value))
            }
            value={addIds}
          >
            {addOptions.map((tag) => (
              <option key={tag.id} value={tag.id}>
                {tag.display_name}
              </option>
            ))}
          </select>
        </label>
      </div>
      {row.merchant_tags.length > 0 ? (
        <fieldset className="mt-2" disabled={busy || draftIsStale}>
          <legend className="text-xs font-semibold text-ink">Remove assigned tags inline</legend>
          <div className="mt-1 flex flex-wrap gap-2">
            {row.merchant_tags.map((tag) => {
              const marked = removeIds.includes(tag.id);
              return (
                <label className="flex items-center gap-1 text-xs text-ink" key={tag.id}>
                  <input
                    checked={marked}
                    onChange={() =>
                      setRemoveIds((current) =>
                        marked
                          ? current.filter((id) => id !== tag.id)
                          : [...current, tag.id],
                      )
                    }
                    type="checkbox"
                  />
                  {tag.display_name}
                  {tag.is_archived ? " (archived)" : ""}
                </label>
              );
            })}
          </div>
        </fieldset>
      ) : null}
      {draftIsStale ? (
        <p
          className="mt-2 rounded-lg border border-warning-line bg-warning-soft p-2 text-xs font-semibold text-warning-ink"
          role="alert"
        >
          This transaction changed elsewhere while you were editing. Close and reopen the inline
          editor to review the latest values before saving.
        </p>
      ) : null}
      <div className="mt-2 flex flex-wrap items-center gap-2">
        <button
          className="rounded-full border border-line bg-page px-3 py-1.5 text-xs font-bold text-ink disabled:opacity-60"
          disabled={busy || draftIsStale}
          onClick={() => void save()}
          type="button"
        >
          Save inline changes
        </button>
        {message ? (
          <span className="text-xs font-semibold text-muted" role="status">
            {message}
          </span>
        ) : null}
      </div>
    </div>
  );
}
