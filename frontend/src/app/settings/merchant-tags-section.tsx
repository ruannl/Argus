"use client";

import { Fragment, useState } from "react";

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

function MerchantTagRow({ tag }: { tag: MerchantTag }) {
  const [editing, setEditing] = useState(false);
  const [result, setResult] = useState<FinanceActionResult | null>(null);
  const nameId = `merchant-tag-name-${tag.id}`;
  const editorId = `merchant-tag-editor-${tag.id}`;

  return (
    <Fragment>
      <tr className={TABLE_ROW_CLASS}>
        <td className={TD_CLASS}>
          <p className="font-semibold text-ink">{tag.display_name}</p>
        </td>
        <td className={TD_CLASS}>
          <span
            className={`${STATUS_PILL_CLASS} ${
              tag.is_archived
                ? "bg-warning-soft text-warning-text"
                : "bg-success-soft text-success-ink"
            }`}
          >
            {tag.is_archived ? "Archived" : "Active"}
          </span>
        </td>
        <td className={`${TD_CLASS} text-muted tabular-nums`}>
          {tag.transaction_count} {tag.transaction_count === 1 ? "transaction" : "transactions"}{" "}
          assigned
        </td>
        <td className={TD_CLASS}>
          <div className="flex flex-wrap justify-end gap-2">
            <button
              aria-controls={editorId}
              aria-expanded={editing}
              aria-label={`${editing ? "Close" : "Edit"} ${tag.display_name}`}
              className={BUTTON_CLASS}
              onClick={() => setEditing((current) => !current)}
              type="button"
            >
              {editing ? "Close" : "Edit"}
            </button>
            <form
              action={async (formData) => {
                setResult(
                  await (tag.is_archived
                    ? restoreMerchantTagAction(formData)
                    : archiveMerchantTagAction(formData)),
                );
              }}
            >
              <input name="id" type="hidden" value={tag.id} />
              <button
                aria-label={`${tag.is_archived ? "Restore" : "Archive"} ${tag.display_name}`}
                className={BUTTON_CLASS}
                type="submit"
              >
                {tag.is_archived ? "Restore" : "Archive"}
              </button>
            </form>
          </div>
        </td>
      </tr>

      <tr className={EDITOR_ROW_CLASS} hidden={!editing} id={editorId}>
        <td className="px-4 py-4" colSpan={4}>
          <form
            action={async (formData) => {
              setResult(await updateMerchantTagAction(formData));
            }}
            className="max-w-md"
          >
            <label className={LABEL_CLASS} htmlFor={nameId}>
              Rename {tag.display_name}
            </label>
            <input
              className={FIELD_CLASS}
              defaultValue={tag.display_name}
              id={nameId}
              name="display_name"
              required
            />
            <input name="id" type="hidden" value={tag.id} />
            <button className={`${BUTTON_CLASS} mt-3`} type="submit">
              Save tag
            </button>
          </form>

          <details className="mt-4 max-w-md rounded-2xl border border-warning-line bg-warning-soft p-4">
            <summary className="cursor-pointer text-sm font-bold text-warning-ink">
              Delete tag
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
                Confirm delete tag
              </button>
            </form>
          </details>

          <FormResult result={result} />
        </td>
      </tr>
    </Fragment>
  );
}

export default function MerchantTagsSection({ tags }: { tags: MerchantTag[] }) {
  const [createResult, setCreateResult] = useState<FinanceActionResult | null>(null);

  return (
    <section aria-labelledby="settings-merchant-tags-title">
      <h2
        className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
        id="settings-merchant-tags-title"
      >
        Tags
      </h2>
      <p className="mt-3 max-w-2xl text-base leading-7 text-muted">
        Tags label the shops, services, or people you transact with. Tag names are unique regardless
        of case or surrounding whitespace.
      </p>

      <form
        action={async (formData) => {
          setCreateResult(await createMerchantTagAction(formData));
        }}
        className="mt-5 flex flex-wrap items-end gap-3 rounded-3xl border border-line bg-surface p-4 sm:p-5"
      >
        <div className="min-w-[12rem] flex-1">
          <label className={LABEL_CLASS} htmlFor="new-merchant-tag-name">
            Tag name
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
          Add tag
        </button>
      </form>
      <FormResult result={createResult} />

      {tags.length === 0 ? (
        <p className={EMPTY_STATE_CLASS}>No tags yet. Add your first tag above.</p>
      ) : (
        <div className={TABLE_WRAPPER_CLASS}>
          <table className={`${TABLE_CLASS} min-w-[38rem]`}>
            <thead className={TABLE_HEAD_CLASS}>
              <tr>
                <th className={TH_CLASS} scope="col">
                  Tag
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
              {tags.map((tag) => (
                <MerchantTagRow key={tag.id} tag={tag} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
