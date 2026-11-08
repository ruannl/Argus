"use client";

import { useState } from "react";

import type { Category, CategoryKind, UserCategoryKind } from "@/lib/server/backend-client";
import {
  archiveCategoryAction,
  createCategoryAction,
  deleteCategoryAction,
  restoreCategoryAction,
  updateCategoryAction,
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

const USER_KINDS: UserCategoryKind[] = ["Expense", "Income", "Transfer", "Excluded"];

const KIND_CLASS: Record<CategoryKind, string> = {
  Expense: "bg-warning-soft text-warning-text",
  Income: "bg-success-soft text-success-ink",
  Transfer: "bg-accent-soft text-accent-soft-ink",
  Excluded: "bg-elevated text-muted",
  Uncategorised: "bg-accent-soft text-accent-soft-ink",
};

function CategoryRow({ category }: { category: Category }) {
  const initialKind: UserCategoryKind =
    category.kind === "Uncategorised" ? "Expense" : category.kind;
  const [selectedKind, setSelectedKind] = useState<UserCategoryKind>(initialKind);
  const [result, setResult] = useState<FinanceActionResult | null>(null);
  const kindChanged = selectedKind !== category.kind;
  const nameId = `category-name-${category.id}`;
  const kindId = `category-kind-${category.id}`;
  const confirmId = `category-confirm-${category.id}`;

  return (
    <li className="rounded-3xl border border-line bg-surface p-5 sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-lg font-semibold text-ink">{category.display_name}</p>
          <p className="mt-1 text-sm text-muted">
            {category.transaction_count}{" "}
            {category.transaction_count === 1 ? "transaction" : "transactions"}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-xs font-semibold">
          <span className={`rounded-full px-3 py-1 ${KIND_CLASS[category.kind]}`}>
            {category.kind}
          </span>
          <span
            className={
              category.is_archived
                ? "rounded-full bg-warning-soft px-3 py-1 text-warning-text"
                : "rounded-full bg-success-soft px-3 py-1 text-success-ink"
            }
          >
            {category.is_archived ? "Archived" : "Active"}
          </span>
        </div>
      </div>

      <form
        action={async (formData) => {
          setResult(await updateCategoryAction(formData));
        }}
        className="mt-5"
      >
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <label className={LABEL_CLASS} htmlFor={nameId}>
              Rename category
            </label>
            <input
              className={FIELD_CLASS}
              defaultValue={category.display_name}
              id={nameId}
              name="display_name"
              required
            />
          </div>
          <div>
            <label className={LABEL_CLASS} htmlFor={kindId}>
              Kind
            </label>
            <select
              className={FIELD_CLASS}
              id={kindId}
              name="kind"
              onChange={(event) => setSelectedKind(event.target.value as UserCategoryKind)}
              value={selectedKind}
            >
              {USER_KINDS.map((kind) => (
                <option key={kind} value={kind}>
                  {kind}
                </option>
              ))}
            </select>
          </div>
        </div>

        {kindChanged ? (
          <div className="mt-4 rounded-2xl border border-warning-line bg-warning-soft p-4 text-sm leading-6 text-warning-ink">
            <p className="font-semibold">Changing the kind affects your totals</p>
            <p className="mt-1">
              Money in, Money out, and Net include or exclude this category&apos;s transactions
              based on its kind. The transactions themselves are not changed.
            </p>
            <label className="mt-3 flex items-start gap-2 font-semibold" htmlFor={confirmId}>
              <input
                className="mt-1"
                id={confirmId}
                name="confirm_kind_change"
                type="checkbox"
                value="true"
              />
              I understand and want to change the kind.
            </label>
          </div>
        ) : null}

        <input name="id" type="hidden" value={category.id} />
        <button className={`${BUTTON_CLASS} mt-4`} type="submit">
          Save category
        </button>
      </form>

      <form
        action={async (formData) => {
          setResult(
            await (category.is_archived
              ? restoreCategoryAction(formData)
              : archiveCategoryAction(formData)),
          );
        }}
        className="mt-4"
      >
        <input name="id" type="hidden" value={category.id} />
        <button className={BUTTON_CLASS} type="submit">
          {category.is_archived ? "Restore category" : "Archive category"}
        </button>
      </form>

      <details className="mt-4 rounded-2xl border border-warning-line bg-warning-soft p-4">
        <summary className="cursor-pointer text-sm font-bold text-warning-ink">
          Delete category
        </summary>
        <p className="mt-3 text-sm leading-6 text-warning-ink">
          Deleting this category moves {category.transaction_count}{" "}
          {category.transaction_count === 1 ? "transaction" : "transactions"} to Uncategorised. No
          transactions are deleted.
        </p>
        <form
          action={async (formData) => {
            setResult(await deleteCategoryAction(formData));
          }}
          className="mt-3"
        >
          <input name="id" type="hidden" value={category.id} />
          <button className={BUTTON_CLASS} type="submit">
            Confirm delete category
          </button>
        </form>
      </details>

      <FormResult result={result} />
    </li>
  );
}

export default function CategoriesSection({ categories }: { categories: Category[] }) {
  const [createResult, setCreateResult] = useState<FinanceActionResult | null>(null);
  const system = categories.find((category) => category.is_system);
  const editable = categories.filter((category) => !category.is_system);

  return (
    <section aria-labelledby="settings-categories-title" className="mt-10">
      <h2
        className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
        id="settings-categories-title"
      >
        Categories
      </h2>
      <p className="mt-3 max-w-2xl text-base leading-7 text-muted">
        The default set is seeded once per workspace. Renames and deletions are kept, and the system
        Uncategorised bucket can never be removed.
      </p>

      {system ? (
        <div className="mt-5 flex flex-wrap items-center justify-between gap-3 rounded-3xl border border-accent-soft-line bg-accent-soft p-5">
          <div>
            <p className="text-lg font-semibold text-ink">{system.display_name}</p>
            <p className="mt-1 text-sm text-muted">
              System bucket — cannot be deleted. {system.transaction_count}{" "}
              {system.transaction_count === 1 ? "transaction" : "transactions"}.
            </p>
          </div>
          <span className="rounded-full bg-surface px-3 py-1 text-xs font-semibold text-accent-soft-ink">
            {system.kind}
          </span>
        </div>
      ) : null}

      <form
        action={async (formData) => {
          setCreateResult(await createCategoryAction(formData));
        }}
        className="mt-5 grid gap-4 rounded-3xl border border-line bg-surface p-5 sm:grid-cols-2 sm:p-6"
      >
        <div>
          <label className={LABEL_CLASS} htmlFor="new-category-name">
            Category name
          </label>
          <input
            className={FIELD_CLASS}
            id="new-category-name"
            name="display_name"
            placeholder="e.g. Pet care"
            required
          />
        </div>
        <div>
          <label className={LABEL_CLASS} htmlFor="new-category-kind">
            Kind
          </label>
          <select className={FIELD_CLASS} defaultValue="Expense" id="new-category-kind" name="kind">
            {USER_KINDS.map((kind) => (
              <option key={kind} value={kind}>
                {kind}
              </option>
            ))}
          </select>
        </div>
        <div className="sm:col-span-2">
          <button className={PRIMARY_BUTTON_CLASS} type="submit">
            Add category
          </button>
        </div>
      </form>
      <FormResult result={createResult} />

      {editable.length === 0 ? (
        <p className="mt-6 rounded-3xl border border-line bg-surface p-5 text-base text-muted">
          No categories yet.
        </p>
      ) : (
        <ul className="mt-6 grid gap-4">
          {editable.map((category) => (
            <CategoryRow category={category} key={category.id} />
          ))}
        </ul>
      )}
    </section>
  );
}
