"use client";

import { Fragment, useState } from "react";

import { sortByDisplayName } from "@/lib/display-order";
import {
  createAliasAction,
  deleteAliasAction,
  updateAliasAction,
} from "@/lib/server/alias-actions";
import type { AliasActionResult } from "@/lib/server/alias-actions";
import type { Category, MerchantAlias, MerchantTag } from "@/lib/server/backend-client";

import FormResult from "./form-result";
import {
  BUTTON_CLASS,
  EDITOR_ROW_CLASS,
  EMPTY_STATE_CLASS,
  FIELD_CLASS,
  LABEL_CLASS,
  PRIMARY_BUTTON_CLASS,
  TABLE_CLASS,
  TABLE_HEAD_CLASS,
  TABLE_ROW_CLASS,
  TABLE_WRAPPER_CLASS,
  TD_CLASS,
  TH_CLASS,
} from "./settings-ui";

function AliasEditorFields({
  alias,
  categories,
  tags,
}: {
  alias?: MerchantAlias;
  categories: Category[];
  tags: MerchantTag[];
}) {
  return (
    <div className="grid gap-4 sm:grid-cols-3">
      <div>
        <label className={LABEL_CLASS} htmlFor={`alias-name-${alias?.id ?? "new"}`}>
          Alias text
        </label>
        <input
          className={FIELD_CLASS}
          defaultValue={alias?.display_alias ?? ""}
          id={`alias-name-${alias?.id ?? "new"}`}
          name="display_alias"
          placeholder="e.g. apteek"
          required
        />
      </div>
      <div>
        <label className={LABEL_CLASS} htmlFor={`alias-tag-${alias?.id ?? "new"}`}>
          Suggests tag
        </label>
        <select
          className={FIELD_CLASS}
          defaultValue={alias?.merchant_tag_id ?? ""}
          id={`alias-tag-${alias?.id ?? "new"}`}
          name="merchant_tag_id"
          required
        >
          <option disabled value="">
            Choose a tag
          </option>
          {sortByDisplayName(tags)
            .filter((tag) => !tag.is_archived || tag.id === alias?.merchant_tag_id)
            .map((tag) => (
              <option key={tag.id} value={tag.id}>
                {tag.display_name}
                {tag.is_archived ? " (archived)" : ""}
              </option>
            ))}
        </select>
      </div>
      <div>
        <label className={LABEL_CLASS} htmlFor={`alias-category-${alias?.id ?? "new"}`}>
          Suggests category (optional)
        </label>
        <select
          className={FIELD_CLASS}
          defaultValue={alias?.category_id ?? ""}
          id={`alias-category-${alias?.id ?? "new"}`}
          name="category_id"
        >
          <option value="">No specific category</option>
          {sortByDisplayName(categories)
            .filter(
              (category) =>
                !category.is_system &&
                (!category.is_archived || category.id === alias?.category_id),
            )
            .map((category) => (
              <option key={category.id} value={category.id}>
                {category.display_name}
                {category.is_archived ? " (archived)" : ""}
              </option>
            ))}
        </select>
      </div>
    </div>
  );
}

function AliasRow({
  alias,
  categories,
  tags,
}: {
  alias: MerchantAlias;
  categories: Category[];
  tags: MerchantTag[];
}) {
  const [editing, setEditing] = useState(false);
  const [result, setResult] = useState<AliasActionResult | null>(null);
  const editorId = `alias-editor-${alias.id}`;

  return (
    <Fragment>
      <tr className={TABLE_ROW_CLASS}>
        <td className={`${TD_CLASS} font-semibold text-ink`}>{alias.display_alias}</td>
        <td className={TD_CLASS}>{alias.tag_display_name}</td>
        <td className={`${TD_CLASS} text-muted`}>{alias.category_display_name ?? "—"}</td>
        <td className={TD_CLASS}>
          <div className="flex flex-wrap justify-end gap-2">
            <button
              aria-controls={editorId}
              aria-expanded={editing}
              aria-label={`${editing ? "Close" : "Edit"} alias ${alias.display_alias}`}
              className={BUTTON_CLASS}
              onClick={() => setEditing((current) => !current)}
              type="button"
            >
              {editing ? "Close" : "Edit"}
            </button>
            <form
              action={async (formData) => {
                setResult(await deleteAliasAction(formData));
              }}
            >
              <input name="id" type="hidden" value={alias.id} />
              <button className={BUTTON_CLASS} type="submit">
                Delete
              </button>
            </form>
          </div>
        </td>
      </tr>
      <tr className={EDITOR_ROW_CLASS} hidden={!editing} id={editorId}>
        <td className="px-4 py-4" colSpan={4}>
          <form
            action={async (formData) => {
              setResult(await updateAliasAction(formData));
            }}
          >
            <AliasEditorFields alias={alias} categories={categories} tags={tags} />
            <input name="id" type="hidden" value={alias.id} />
            <button className={`${BUTTON_CLASS} mt-4`} type="submit">
              Save alias
            </button>
          </form>
          <FormResult result={result} />
        </td>
      </tr>
    </Fragment>
  );
}

export default function AliasesSection({
  aliases,
  categories,
  tags,
}: {
  aliases: MerchantAlias[];
  categories: Category[];
  tags: MerchantTag[];
}) {
  const [createResult, setCreateResult] = useState<AliasActionResult | null>(null);
  const ordered = [...aliases].sort((left, right) =>
    left.display_alias.localeCompare(right.display_alias, undefined, { sensitivity: "base" }),
  );

  return (
    <section aria-labelledby="settings-aliases-title">
      <h2
        className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
        id="settings-aliases-title"
      >
        Aliases
      </h2>
      <p className="mt-3 max-w-2xl text-base leading-7 text-muted">
        Map a bank description to a tag and an optional category. Suggestions use your own aliases
        first, then what you previously chose for the same merchant, then matching name tokens.
      </p>

      <form
        action={async (formData) => {
          setCreateResult(await createAliasAction(formData));
        }}
        className="mt-5 rounded-3xl border border-line bg-surface p-4 sm:p-5"
      >
        <AliasEditorFields categories={categories} tags={tags} />
        <button className={`${PRIMARY_BUTTON_CLASS} mt-4`} type="submit">
          Add alias
        </button>
      </form>
      <FormResult result={createResult} />

      {ordered.length === 0 ? (
        <p className={EMPTY_STATE_CLASS}>No aliases yet. Add one above.</p>
      ) : (
        <div className={TABLE_WRAPPER_CLASS}>
          <table className={`${TABLE_CLASS} min-w-[46rem]`}>
            <thead className={TABLE_HEAD_CLASS}>
              <tr>
                <th className={TH_CLASS} scope="col">
                  Alias
                </th>
                <th className={TH_CLASS} scope="col">
                  Suggests tag
                </th>
                <th className={TH_CLASS} scope="col">
                  Suggests category
                </th>
                <th className={`${TH_CLASS} text-right`} scope="col">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody>
              {ordered.map((alias) => (
                <AliasRow alias={alias} categories={categories} key={alias.id} tags={tags} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
