import Link from "next/link";
import { redirect } from "next/navigation";

import { auth } from "@/auth";
import AccountDetails from "@/components/account-details";
import AppShell from "@/components/app-shell";
import WorkspaceExpired from "@/components/workspace-expired";
import { endSessionAction } from "@/lib/server/account-actions";
import {
  getCurrentAccount,
  listCategories,
  listFinancialAccounts,
  listMerchantTags,
} from "@/lib/server/backend-client";
import type {
  Category,
  CurrentAccount,
  FinancialAccount,
  MerchantTag,
} from "@/lib/server/backend-client";

import AccountsSection from "./accounts-section";
import CategoriesSection from "./categories-section";
import MerchantTagsSection from "./merchant-tags-section";

export const dynamic = "force-dynamic";

type WorkspaceData = {
  accounts: FinancialAccount[] | null;
  categories: Category[] | null;
  tags: MerchantTag[] | null;
};

async function loadWorkspace(backendToken: string): Promise<WorkspaceData> {
  const [accounts, categories, tags] = await Promise.all([
    listFinancialAccounts(backendToken),
    listCategories(backendToken),
    listMerchantTags(backendToken),
  ]);

  return {
    accounts: accounts.ok ? accounts.value.accounts : null,
    categories: categories.ok ? categories.value.categories : null,
    tags: tags.ok ? tags.value.merchant_tags : null,
  };
}

function ProfileSection({ account }: { account: CurrentAccount }) {
  return (
    <section
      aria-labelledby="settings-profile-title"
      className="mt-10 rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-8"
    >
      <h2
        className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
        id="settings-profile-title"
      >
        Profile
      </h2>
      <p className="mt-3 max-w-2xl text-base leading-7 text-muted">
        Your customer account and session details. Bank and credit-card accounts live under Accounts
        above.
      </p>

      <AccountDetails account={account} />

      <div className="mt-8 flex flex-wrap items-center gap-3">
        {account.admin ? (
          <Link
            className="inline-flex items-center justify-center rounded-full border border-warning-line bg-warning-soft px-5 py-2.5 text-sm font-bold text-warning-ink-soft transition hover:bg-warning-soft-hover"
            href="/admin"
          >
            Admin
          </Link>
        ) : null}
        <form action={endSessionAction}>
          <button
            className="inline-flex items-center justify-center rounded-full border border-line px-5 py-2.5 text-sm font-bold text-ink transition hover:border-ink hover:text-ink"
            type="submit"
          >
            Log out
          </button>
        </form>
      </div>

      <p className="mt-6 text-sm leading-6 text-muted">
        Theme preference: use the theme button in the bottom-right corner to switch between light
        and dark. Your choice is remembered in this browser.
      </p>
    </section>
  );
}

function SettingsWorkspace({ account, data }: { account: CurrentAccount; data: WorkspaceData }) {
  const loadFailed = data.accounts === null || data.categories === null || data.tags === null;

  return (
    <AppShell
      account={account}
      activeNav="settings"
      description="Manage your ZAR financial accounts, categories, merchant tags, and profile."
      eyebrow="Settings"
      title="Settings"
    >
      {loadFailed ? (
        <p
          className="mt-8 rounded-2xl bg-warning-soft px-5 py-4 text-base font-semibold text-warning-ink"
          role="status"
        >
          We could not load your finance workspace. Please refresh the page.
        </p>
      ) : null}

      {data.accounts ? <AccountsSection accounts={data.accounts} /> : null}
      {data.categories ? <CategoriesSection categories={data.categories} /> : null}
      {data.tags ? <MerchantTagsSection tags={data.tags} /> : null}

      <ProfileSection account={account} />
    </AppShell>
  );
}

export default async function SettingsPage() {
  const backendToken = (await auth())?.user?.backendToken;
  if (!backendToken) {
    redirect("/login");
  }

  let account: CurrentAccount | null = null;
  try {
    account = await getCurrentAccount(backendToken);
  } catch {
    // Invalid, expired, revoked, and unavailable backend sessions all fail closed.
  }

  if (!account) {
    return <WorkspaceExpired />;
  }

  let data: WorkspaceData = { accounts: null, categories: null, tags: null };
  try {
    data = await loadWorkspace(backendToken);
  } catch {
    // Keep the shell and show the inline load notice.
  }

  return <SettingsWorkspace account={account} data={data} />;
}
