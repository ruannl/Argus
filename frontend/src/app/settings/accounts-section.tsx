"use client";

import { useState } from "react";

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

const FIELD_CLASS =
  "mt-2 w-full rounded-2xl border border-line bg-page px-4 py-3 text-base text-ink outline-none focus:border-accent-text focus-visible:ring-2 focus-visible:ring-accent-ring";
const LABEL_CLASS = "text-sm font-semibold text-muted";
const BUTTON_CLASS =
  "inline-flex items-center justify-center rounded-full border border-line px-4 py-2 text-sm font-bold text-ink transition hover:border-ink";
const PRIMARY_BUTTON_CLASS =
  "inline-flex items-center justify-center rounded-full bg-ink px-5 py-2.5 text-sm font-bold text-page transition hover:bg-ink-hover";

function AccountRow({ account }: { account: FinancialAccount }) {
  const [result, setResult] = useState<FinanceActionResult | null>(null);
  const nameId = `account-name-${account.id}`;
  const institutionId = `account-institution-${account.id}`;

  return (
    <li className="rounded-3xl border border-line bg-surface p-5 sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-lg font-semibold text-ink">{account.display_name}</p>
          <p className="mt-1 text-sm text-muted">
            {account.institution ?? "No institution recorded"}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-xs font-semibold">
          <span className="rounded-full border border-accent-soft-line bg-accent-soft px-3 py-1 text-accent-soft-ink">
            ZAR
          </span>
          <span
            className={
              account.is_archived
                ? "rounded-full bg-warning-soft px-3 py-1 text-warning-text"
                : "rounded-full bg-success-soft px-3 py-1 text-success-ink"
            }
          >
            {account.is_archived ? "Archived" : "Active"}
          </span>
          <span className="text-muted">
            {account.transaction_count}{" "}
            {account.transaction_count === 1 ? "transaction" : "transactions"}
          </span>
        </div>
      </div>

      <div className="mt-5 grid gap-4 sm:grid-cols-2">
        <form
          action={async (formData) => {
            setResult(await renameAccountAction(formData));
          }}
        >
          <label className={LABEL_CLASS} htmlFor={nameId}>
            Rename account
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
            Institution (clear to remove)
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

      <form
        action={async (formData) => {
          setResult(
            await (account.is_archived
              ? restoreAccountAction(formData)
              : archiveAccountAction(formData)),
          );
        }}
        className="mt-4"
      >
        <input name="id" type="hidden" value={account.id} />
        <button className={BUTTON_CLASS} type="submit">
          {account.is_archived ? "Restore account" : "Archive account"}
        </button>
      </form>

      <FormResult result={result} />
    </li>
  );
}

export default function AccountsSection({ accounts }: { accounts: FinancialAccount[] }) {
  const [createResult, setCreateResult] = useState<FinanceActionResult | null>(null);

  return (
    <section aria-labelledby="settings-accounts-title" className="mt-10">
      <h2
        className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
        id="settings-accounts-title"
      >
        Accounts
      </h2>
      <p className="mt-3 max-w-2xl text-base leading-7 text-muted">
        Bank and credit-card accounts you import statements from. Every account uses the fixed
        ZAR workspace currency. Accounts are archived, never deleted.
      </p>

      <form
        action={async (formData) => {
          setCreateResult(await createAccountAction(formData));
        }}
        className="mt-5 grid gap-4 rounded-3xl border border-line bg-surface p-5 sm:grid-cols-2 sm:p-6"
      >
        <div>
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
        <div>
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
        <div className="sm:col-span-2">
          <button className={PRIMARY_BUTTON_CLASS} type="submit">
            Add ZAR account
          </button>
        </div>
      </form>
      <FormResult result={createResult} />

      {accounts.length === 0 ? (
        <p className="mt-6 rounded-3xl border border-line bg-surface p-5 text-base text-muted">
          No financial accounts yet. Add your first ZAR account above.
        </p>
      ) : (
        <ul className="mt-6 grid gap-4">
          {accounts.map((account) => (
            <AccountRow account={account} key={account.id} />
          ))}
        </ul>
      )}
    </section>
  );
}
