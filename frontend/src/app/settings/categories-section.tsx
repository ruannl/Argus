"use client";

import { Fragment, useState } from "react";

import { sortByDisplayName } from "@/lib/display-order";
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
import {
  BUTTON_CLASS,
  EDITOR_ROW_CLASS,
  EMPTY_STATE_CLASS,
  FIELD_CLASS,
  LABEL_CLASS,
  PRIMARY_BUTTON_CLASS,
  STATUS_PILL_CLASS,
  TABLE_CLASS,
  TABLE_HEAD_CLASS,
  TABLE_ROW_CLASS,
  TABLE_WRAPPER_CLASS,
  TD_CLASS,
  TH_CLASS,
} from "./settings-ui";

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
  const [editing, setEditing] = useState(false);
  const [selectedKind, setSelectedKind] = useState<UserCategoryKind>(initialKind);
  const [result, setResult] = useState<FinanceActionResult | null>(null);
  const kindChanged = selectedKind !== category.kind;
  const nameId = `category-name-${category.id}`;
  const kindId = `category-kind-${category.id}`;
  const confirmId = `category-confirm-${category.id}`;
  const editorId = `category-editor-${category.id}`;

  return (
    <Fragment>
      <tr className={TABLE_ROW_CLASS}>
        <td className={TD_CLASS}>
          <p className="font-semibold text-ink">{category.display_name}</p>
        </td>
        <td className={TD_CLASS}>
          <span className={`${STATUS_PILL_CLASS} ${KIND_CLASS[category.kind]}`}>
            {category.kind}
          </span>
        </td>
        <td className={TD_CLASS}>
          <span
            className={`${STATUS_PILL_CLASS} ${
              category.is_archived
                ? "bg-warning-soft text-warning-text"
                : "bg-success-soft text-success-ink"
            }`}
          >
            {category.is_archived ? "Archived" : "Active"}
          </span>
        </td>
        <td className={`${TD_CLASS} text-muted tabular-nums`}>
          {category.transaction_count}{" "}
          {category.transaction_count === 1 ? "transaction" : "transactions"}
        </td>
        <td className={TD_CLASS}>
          <div className="flex flex-wrap justify-end gap-2">
            <button
              aria-controls={editorId}
              aria-expanded={editing}
              aria-label={`${editing ? "Close" : "Edit"} ${category.display_name}`}
              className={BUTTON_CLASS}
              onClick={() => setEditing((current) => !current)}
              type="button"
            >
              {editing ? "Close" : "Edit"}
            </button>
            <form
              action={async (formData) => {
                setResult(
                  await (category.is_archived
                    ? restoreCategoryAction(formData)
                    : archiveCategoryAction(formData)),
                );
              }}
            >
              <input name="id" type="hidden" value={category.id} />
              <button
                aria-label={`${category.is_archived ? "Restore" : "Archive"} ${category.display_name}`}
                className={BUTTON_CLASS}
                type="submit"
              >
                {category.is_archived ? "Restore" : "Archive"}
              </button>
            </form>
          </div>
        </td>
      </tr>

      <tr className={EDITOR_ROW_CLASS} hidden={!editing} id={editorId}>
        <td className="px-4 py-4" colSpan={5}>
          <form
            action={async (formData) => {
              setResult(await updateCategoryAction(formData));
            }}
          >
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <label className={LABEL_CLASS} htmlFor={nameId}>
                  Rename {category.display_name}
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
                  Kind for {category.display_name}
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

          <details className="mt-4 rounded-2xl border border-warning-line bg-warning-soft p-4">
            <summary className="cursor-pointer text-sm font-bold text-warning-ink">
              Delete category
            </summary>
            <p className="mt-3 text-sm leading-6 text-warning-ink">
              Deleting this category moves {category.transaction_count}{" "}
              {category.transaction_count === 1 ? "transaction" : "transactions"} to Uncategorised.
              No transactions are deleted.
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
        </td>
      </tr>
    </Fragment>
  );
}

export default function CategoriesSection({ categories }: { categories: Category[] }) {
  const [createResult, setCreateResult] = useState<FinanceActionResult | null>(null);
  const system = categories.find((category) => category.is_system);
  const editable = sortByDisplayName(categories.filter((category) => !category.is_system));

  return (
    <section aria-labelledby="settings-categories-title">
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
        <div className="mt-5 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-accent-soft-line bg-accent-soft px-4 py-3">
          <div>
            <p className="text-sm font-semibold text-ink">{system.display_name}</p>
            <p className="mt-0.5 text-sm text-muted">
              System bucket — cannot be deleted. {system.transaction_count}{" "}
              {system.transaction_count === 1 ? "transaction" : "transactions"}.
            </p>
          </div>
          <span className={`${STATUS_PILL_CLASS} bg-surface text-accent-soft-ink`}>
            {system.kind}
          </span>
        </div>
      ) : null}

      <form
        action={async (formData) => {
          setCreateResult(await createCategoryAction(formData));
        }}
        className="mt-5 flex flex-wrap items-end gap-3 rounded-3xl border border-line bg-surface p-4 sm:p-5"
      >
        <div className="min-w-[12rem] flex-1">
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
        <div className="min-w-[10rem]">
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
        <button className={PRIMARY_BUTTON_CLASS} type="submit">
          Add category
        </button>
      </form>
      <FormResult result={createResult} />

      {editable.length === 0 ? (
        <p className={EMPTY_STATE_CLASS}>No categories yet.</p>
      ) : (
        <div className={TABLE_WRAPPER_CLASS}>
          <table className={`${TABLE_CLASS} min-w-[44rem]`}>
            <thead className={TABLE_HEAD_CLASS}>
              <tr>
                <th className={TH_CLASS} scope="col">
                  Category
                </th>
                <th className={TH_CLASS} scope="col">
                  Kind
                </th>
                <th className={TH_CLASS} scope="col">
                  Status
                </th>
                <th className={TH_CLASS} scope="col">
                  Transactions
                </th>
                <th className={`${TH_CLASS} text-right`} scope="col">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody>
              {editable.map((category) => (
                <CategoryRow category={category} key={category.id} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
