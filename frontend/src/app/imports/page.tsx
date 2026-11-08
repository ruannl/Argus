import { redirect } from "next/navigation";

import { auth } from "@/auth";
import AppShell from "@/components/app-shell";
import WorkspaceExpired from "@/components/workspace-expired";
import {
  getCurrentAccount,
  listFinancialAccounts,
  listImportHistory,
  listOpenImportReviews,
} from "@/lib/server/backend-client";
import type {
  CurrentAccount,
  FinancialAccount,
  ImportOpenReview,
  ImportStatementSummary,
} from "@/lib/server/backend-client";
import ImportWorkspace from "./import-workspace";

export const dynamic = "force-dynamic";

type ImportsData = {
  accounts: FinancialAccount[];
  reviews: ImportOpenReview[];
  history: ImportStatementSummary[];
  loadError: string | null;
  nextCursor: string | null;
};

async function loadImportsData(backendToken: string, cursor?: string): Promise<ImportsData> {
  const [accounts, reviews, history] = await Promise.all([
    listFinancialAccounts(backendToken),
    listOpenImportReviews(backendToken),
    listImportHistory(backendToken, { limit: 20, cursor }),
  ]);

  const errors: string[] = [];
  if (!accounts.ok) errors.push("accounts");
  if (!reviews.ok) errors.push("open reviews");
  if (!history.ok) errors.push("import history");

  return {
    accounts: accounts.ok ? accounts.value.accounts : [],
    reviews: reviews.ok ? reviews.value.reviews : [],
    history: history.ok ? history.value.items : [],
    loadError:
      errors.length > 0
        ? `Could not load ${errors.join(", ")}. Refresh the page or try again.`
        : null,
    nextCursor: history.ok ? history.value.next_cursor : null,
  };
}

function ImportsWorkspace({ account, data }: { account: CurrentAccount; data: ImportsData }) {
  return (
    <AppShell
      account={account}
      activeNav="imports"
      description="Upload a bank or credit-card statement, confirm the mapping and dates, then review every row before committing."
      eyebrow="Workspace"
      title="Imports"
    >
      <ImportWorkspace key={data.accounts.map((account) => account.id).join(",")} {...data} />
    </AppShell>
  );
}

export default async function ImportsPage({
  searchParams,
}: {
  searchParams?: Promise<{ cursor?: string }>;
} = {}) {
  const cursor = (await searchParams)?.cursor;
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

  const data = await loadImportsData(backendToken, cursor);
  return <ImportsWorkspace account={account} data={data} />;
}
