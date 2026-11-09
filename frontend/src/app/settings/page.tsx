import Link from "next/link";
import { redirect } from "next/navigation";

import { auth } from "@/auth";
import AppShell from "@/components/app-shell";
import WorkspaceExpired from "@/components/workspace-expired";
import { endSessionAction } from "@/lib/server/account-actions";
import {
  getCurrentAccount,
  listCategories,
  listFinancialAccounts,
  listMerchantAliases,
  listMerchantTags,
} from "@/lib/server/backend-client";
import type {
  Category,
  CurrentAccount,
  FinancialAccount,
  MerchantAlias,
  MerchantTag,
} from "@/lib/server/backend-client";

import AccountsSection from "./accounts-section";
import AliasesSection from "./aliases-section";
import CategoriesSection from "./categories-section";
import MerchantTagsSection from "./merchant-tags-section";
import SectionTabs from "@/components/section-tabs";
import { TABLE_CLASS, TABLE_ROW_CLASS, TABLE_WRAPPER_CLASS, TD_CLASS } from "./settings-ui";

export const dynamic = "force-dynamic";

const TAB_KEYS = ["accounts", "categories", "merchant-tags", "aliases", "profile"] as const;
type SettingsTabKey = (typeof TAB_KEYS)[number];

function parseTab(value: string | string[] | undefined): SettingsTabKey {
  const candidate = Array.isArray(value) ? value[0] : value;
  return TAB_KEYS.find((key) => key === candidate) ?? "accounts";
}

type WorkspaceData = {
  accounts: FinancialAccount[] | null;
  categories: Category[] | null;
  tags: MerchantTag[] | null;
  aliases: MerchantAlias[] | null;
};

async function loadWorkspace(backendToken: string): Promise<WorkspaceData> {
  const [accounts, categories, tags, aliases] = await Promise.all([
    listFinancialAccounts(backendToken),
    listCategories(backendToken),
    listMerchantTags(backendToken),
    listMerchantAliases(backendToken),
  ]);

  return {
    accounts: accounts.ok ? accounts.value.accounts : null,
    categories: categories.ok ? categories.value.categories : null,
    tags: tags.ok ? tags.value.merchant_tags : null,
    aliases: aliases.ok ? aliases.value.merchant_aliases : null,
  };
}

function SectionUnavailable() {
  return (
    <p
      className="mt-6 rounded-2xl bg-warning-soft px-5 py-4 text-base font-semibold text-warning-ink"
      role="status"
    >
      This section could not be loaded. Refresh the page to try again.
    </p>
  );
}

function ProfileSection({ account }: { account: CurrentAccount }) {
  const rows: Array<{ label: string; value: string }> = [
    { label: "Email", value: account.email },
    { label: "Email verified", value: account.email_verified ? "Yes" : "No" },
    { label: "Subscriber active", value: account.subscriber_active ? "Yes" : "No" },
    { label: "Lifetime access", value: account.lifetime ? "Yes" : "No" },
    { label: "Subscription started", value: account.subscription_started_at ?? "Not available" },
    { label: "Next billing date", value: account.next_billing_at ?? "Not available" },
    { label: "Admin account", value: account.admin ? "Yes" : "No" },
  ];

  return (
    <section aria-labelledby="settings-profile-title">
      <h2
        className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
        id="settings-profile-title"
      >
        Profile
      </h2>
      <p className="mt-3 max-w-2xl text-base leading-7 text-muted">
        Your customer account and session details. Bank and credit-card accounts live under the
        Accounts tab.
      </p>

      <div className={TABLE_WRAPPER_CLASS}>
        <table className={TABLE_CLASS}>
          <tbody>
            {rows.map((row) => (
              <tr className={TABLE_ROW_CLASS} key={row.label}>
                <th className={`${TD_CLASS} w-1/3 text-sm font-semibold text-muted`} scope="row">
                  {row.label}
                </th>
                <td className={`${TD_CLASS} font-semibold text-ink`}>{row.value}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

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

function SettingsWorkspace({
  account,
  data,
  initialTab,
}: {
  account: CurrentAccount;
  data: WorkspaceData;
  initialTab: SettingsTabKey;
}) {
  const loadFailed =
    data.accounts === null ||
    data.categories === null ||
    data.tags === null ||
    data.aliases === null;

  return (
    <AppShell
      account={account}
      activeNav="settings"
      description="Manage your ZAR financial accounts, categories, tags, aliases, and profile."
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

      <SectionTabs
        defaultKey="accounts"
        initialTab={initialTab}
        key={initialTab}
        label="Settings sections"
        tabs={[
          {
            key: "accounts",
            label: "Accounts",
            panel: data.accounts ? (
              <AccountsSection accounts={data.accounts} />
            ) : (
              <SectionUnavailable />
            ),
          },
          {
            key: "categories",
            label: "Categories",
            panel: data.categories ? (
              <CategoriesSection categories={data.categories} />
            ) : (
              <SectionUnavailable />
            ),
          },
          {
            key: "merchant-tags",
            label: "Tags",
            panel: data.tags ? <MerchantTagsSection tags={data.tags} /> : <SectionUnavailable />,
          },
          {
            key: "aliases",
            label: "Aliases",
            panel:
              data.aliases && data.tags && data.categories ? (
                <AliasesSection
                  aliases={data.aliases}
                  categories={data.categories}
                  tags={data.tags}
                />
              ) : (
                <SectionUnavailable />
              ),
          },
          {
            key: "profile",
            label: "Profile",
            panel: <ProfileSection account={account} />,
          },
        ]}
      />
    </AppShell>
  );
}

export default async function SettingsPage({
  searchParams,
}: {
  searchParams?: Promise<{ tab?: string | string[] }>;
} = {}) {
  const initialTab = parseTab((await searchParams)?.tab);
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

  let data: WorkspaceData = {
    accounts: null,
    categories: null,
    tags: null,
    aliases: null,
  };
  try {
    data = await loadWorkspace(backendToken);
  } catch {
    // Keep the shell and show the inline load notice.
  }

  return <SettingsWorkspace account={account} data={data} initialTab={initialTab} />;
}
