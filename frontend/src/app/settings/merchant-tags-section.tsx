"use client";

import { useState } from "react";

import type { MerchantTag } from "@/lib/server/backend-client";
import {
  archiveMerchantTagAction,
  createMerchantTagAction,
  deleteMerchantTagAction,
  restoreMerchantTagAction,
  updateMerchantTagAction,
} from "@/lib/server/finance-actions";
import type { FinanceActionResult } from "@/lib/server/finance-actions";

import FormResult from "./form-result";

const FIELD_CLASS =
  "mt-2 w-full rounded-2xl border border-line bg-page px-4 py-3 text-base text-ink outline-none focus:border-accent-text focus-visible:ring-2 focus-visible:ring-accent-ring";
const LABEL_CLASS = "text-sm font-semibold text-muted";
const BUTTON_CLASS =
  "inline-flex items-center justify-center rounded-full border border-line px-4 py-2 text-sm font-bold text-ink transition hover:border-ink";
const PRIMARY_BUTTON_CLASS =
  "inline-flex items-center justify-center rounded-full bg-ink px-5 py-2.5 text-sm font-bold text-page transition hover:bg-ink-hover";

function MerchantTagRow({ tag }: { tag: MerchantTag }) {
  const [result, setResult] = useState<FinanceActionResult | null>(null);
  const nameId = `merchant-tag-name-${tag.id}`;

  return (
    <li className="rounded-3xl border border-line bg-surface p-5 sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-lg font-semibold text-ink">{tag.display_name}</p>
          <p className="mt-1 text-sm text-muted">
            {tag.transaction_count} {tag.transaction_count === 1 ? "transaction" : "transactions"}{" "}
            assigned
          </p>
        </div>
        <span
          className={
            tag.is_archived
              ? "rounded-full bg-warning-soft px-3 py-1 text-xs font-semibold text-warning-text"
              : "rounded-full bg-success-soft px-3 py-1 text-xs font-semibold text-success-ink"
          }
        >
          {tag.is_archived ? "Archived" : "Active"}
        </span>
      </div>

      <form
        action={async (formData) => {
          setResult(await updateMerchantTagAction(formData));
        }}
        className="mt-5 flex flex-wrap items-end gap-3"
      >
        <div className="min-w-[12rem] flex-1">
          <label className={LABEL_CLASS} htmlFor={nameId}>
            Rename merchant tag
          </label>
          <input
            className={FIELD_CLASS}
            defaultValue={tag.display_name}
            id={nameId}
            name="display_name"
            required
          />
        </div>
        <input name="id" type="hidden" value={tag.id} />
        <button className={BUTTON_CLASS} type="submit">
          Save tag
        </button>
      </form>

      <form
        action={async (formData) => {
          setResult(
            await (tag.is_archived
              ? restoreMerchantTagAction(formData)
              : archiveMerchantTagAction(formData)),
          );
        }}
        className="mt-4"
      >
        <input name="id" type="hidden" value={tag.id} />
        <button className={BUTTON_CLASS} type="submit">
          {tag.is_archived ? "Restore merchant tag" : "Archive merchant tag"}
        </button>
      </form>

      <details className="mt-4 rounded-2xl border border-warning-line bg-warning-soft p-4">
        <summary className="cursor-pointer text-sm font-bold text-warning-ink">
          Delete merchant tag
        </summary>
        <p className="mt-3 text-sm leading-6 text-warning-ink">
          Deleting this tag removes it from {tag.transaction_count}{" "}
          {tag.transaction_count === 1 ? "transaction" : "transactions"}. The transactions
          themselves are kept.
        </p>
        <form
          action={async (formData) => {
            setResult(await deleteMerchantTagAction(formData));
          }}
          className="mt-3"
        >
          <input name="id" type="hidden" value={tag.id} />
          <button className={BUTTON_CLASS} type="submit">
            Confirm delete merchant tag
          </button>
        </form>
      </details>

      <FormResult result={result} />
    </li>
  );
}

export default function MerchantTagsSection({ tags }: { tags: MerchantTag[] }) {
  const [createResult, setCreateResult] = useState<FinanceActionResult | null>(null);

  return (
    <section aria-labelledby="settings-merchant-tags-title" className="mt-10">
      <h2
        className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
        id="settings-merchant-tags-title"
      >
        Merchant tags
      </h2>
      <p className="mt-3 max-w-2xl text-base leading-7 text-muted">
        A workspace-wide tag list for shops and services. Tag names are unique regardless of case or
        surrounding whitespace.
      </p>

      <form
        action={async (formData) => {
          setCreateResult(await createMerchantTagAction(formData));
        }}
        className="mt-5 flex flex-wrap items-end gap-3 rounded-3xl border border-line bg-surface p-5 sm:p-6"
      >
        <div className="min-w-[12rem] flex-1">
          <label className={LABEL_CLASS} htmlFor="new-merchant-tag-name">
            Merchant tag name
          </label>
          <input
            className={FIELD_CLASS}
            id="new-merchant-tag-name"
            name="display_name"
            placeholder="e.g. Woolworths"
            required
          />
        </div>
        <button className={PRIMARY_BUTTON_CLASS} type="submit">
          Add merchant tag
        </button>
      </form>
      <FormResult result={createResult} />

      {tags.length === 0 ? (
        <p className="mt-6 rounded-3xl border border-line bg-surface p-5 text-base text-muted">
          No merchant tags yet. Add your first tag above.
        </p>
      ) : (
        <ul className="mt-6 grid gap-4">
          {tags.map((tag) => (
            <MerchantTagRow key={tag.id} tag={tag} />
          ))}
        </ul>
      )}
    </section>
  );
}
