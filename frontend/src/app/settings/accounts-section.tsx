"use client";

import { Fragment, useState } from "react";

import type { FinancialAccount } from "@/lib/server/backend-client";
import {
  archiveAccountAction,
  createAccountAction,
  renameAccountAction,
  restoreAccountAction,
  updateAccountInstitutionAction,
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

function TransactionCount({ count }: { count: number }) {
  return (
    <span className="tabular-nums">
      {count} {count === 1 ? "transaction" : "transactions"}
    </span>
  );
}

function AccountRow({ account }: { account: FinancialAccount }) {
  const [editing, setEditing] = useState(false);
  const [result, setResult] = useState<FinanceActionResult | null>(null);
  const nameId = `account-name-${account.id}`;
  const institutionId = `account-institution-${account.id}`;
  const editorId = `account-editor-${account.id}`;

  return (
    <Fragment>
      <tr className={TABLE_ROW_CLASS}>
        <td className={TD_CLASS}>
          <p className="font-semibold text-ink">{account.display_name}</p>
          <p className="mt-0.5 text-xs text-muted">ZAR</p>
        </td>
        <td className={`${TD_CLASS} text-muted`}>{account.institution ?? "—"}</td>
        <td className={TD_CLASS}>
          <span
            className={`${STATUS_PILL_CLASS} ${
              account.is_archived
                ? "bg-warning-soft text-warning-text"
                : "bg-success-soft text-success-ink"
            }`}
          >
            {account.is_archived ? "Archived" : "Active"}
          </span>
        </td>
        <td className={`${TD_CLASS} text-muted`}>
          <TransactionCount count={account.transaction_count} />
        </td>
        <td className={TD_CLASS}>
          <div className="flex flex-wrap justify-end gap-2">
            <button
              aria-controls={editorId}
              aria-expanded={editing}
              aria-label={`${editing ? "Close" : "Edit"} ${account.display_name}`}
              className={BUTTON_CLASS}
              onClick={() => setEditing((current) => !current)}
              type="button"
            >
              {editing ? "Close" : "Edit"}
            </button>
            <form
              action={async (formData) => {
                setResult(
                  await (account.is_archived
                    ? restoreAccountAction(formData)
                    : archiveAccountAction(formData)),
                );
              }}
            >
              <input name="id" type="hidden" value={account.id} />
              <button
                aria-label={`${account.is_archived ? "Restore" : "Archive"} ${account.display_name}`}
                className={BUTTON_CLASS}
                type="submit"
              >
                {account.is_archived ? "Restore" : "Archive"}
              </button>
            </form>
          </div>
        </td>
      </tr>

      <tr className={EDITOR_ROW_CLASS} hidden={!editing} id={editorId}>
        <td className="px-4 py-4" colSpan={5}>
          <div className="grid gap-4 sm:grid-cols-2">
            <form
              action={async (formData) => {
                setResult(await renameAccountAction(formData));
              }}
            >
              <label className={LABEL_CLASS} htmlFor={nameId}>
                Rename {account.display_name}
              </label>
              <input
                className={FIELD_CLASS}
                defaultValue={account.display_name}
                id={nameId}
                name="display_name"
                required
              />
              <input name="id" type="hidden" value={account.id} />
              <button className={`${BUTTON_CLASS} mt-3`} type="submit">
                Save name
              </button>
            </form>

            <form
              action={async (formData) => {
                setResult(await updateAccountInstitutionAction(formData));
              }}
            >
              <label className={LABEL_CLASS} htmlFor={institutionId}>
                Institution for {account.display_name} (clear to remove)
              </label>
              <input
                className={FIELD_CLASS}
                defaultValue={account.institution ?? ""}
                id={institutionId}
                name="institution"
              />
              <input name="display_name" type="hidden" value={account.display_name} />
              <input name="id" type="hidden" value={account.id} />
              <button className={`${BUTTON_CLASS} mt-3`} type="submit">
                Save institution
              </button>
            </form>
          </div>

          <FormResult result={result} />
        </td>
      </tr>
    </Fragment>
  );
}

export default function AccountsSection({ accounts }: { accounts: FinancialAccount[] }) {
  const [createResult, setCreateResult] = useState<FinanceActionResult | null>(null);

  return (
    <section aria-labelledby="settings-accounts-title">
      <h2
        className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
        id="settings-accounts-title"
      >
        Accounts
      </h2>
      <p className="mt-3 max-w-2xl text-base leading-7 text-muted">
        Bank and credit-card accounts you import statements from. Every account uses the fixed ZAR
        workspace currency. Accounts are archived, never deleted.
      </p>

      <form
        action={async (formData) => {
          setCreateResult(await createAccountAction(formData));
        }}
        className="mt-5 flex flex-wrap items-end gap-3 rounded-3xl border border-line bg-surface p-4 sm:p-5"
      >
        <div className="min-w-[12rem] flex-1">
          <label className={LABEL_CLASS} htmlFor="new-account-name">
            Account name
          </label>
          <input
            className={FIELD_CLASS}
            id="new-account-name"
            name="display_name"
            placeholder="e.g. Cheque account"
            required
          />
        </div>
        <div className="min-w-[12rem] flex-1">
          <label className={LABEL_CLASS} htmlFor="new-account-institution">
            Institution (optional)
          </label>
          <input
            className={FIELD_CLASS}
            id="new-account-institution"
            name="institution"
            placeholder="e.g. FNB"
          />
        </div>
        <button className={PRIMARY_BUTTON_CLASS} type="submit">
          Add ZAR account
        </button>
      </form>
      <FormResult result={createResult} />

      {accounts.length === 0 ? (
        <p className={EMPTY_STATE_CLASS}>
          No financial accounts yet. Add your first ZAR account above.
        </p>
      ) : (
        <div className={TABLE_WRAPPER_CLASS}>
          <table className={`${TABLE_CLASS} min-w-[44rem]`}>
            <thead className={TABLE_HEAD_CLASS}>
              <tr>
                <th className={TH_CLASS} scope="col">
                  Account
                </th>
                <th className={TH_CLASS} scope="col">
                  Institution
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
              {accounts.map((account) => (
                <AccountRow account={account} key={account.id} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
