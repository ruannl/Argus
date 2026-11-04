import Link from "next/link";
import { redirect } from "next/navigation";

import { auth } from "@/auth";
import AppShell from "@/components/app-shell";
import WorkspaceExpired from "@/components/workspace-expired";
import { getCurrentAccount } from "@/lib/server/backend-client";
import type { CurrentAccount } from "@/lib/server/backend-client";

export const dynamic = "force-dynamic";

function TransactionsWorkspace({ account }: { account: CurrentAccount }) {
  return (
    <AppShell
      account={account}
      activeNav="transactions"
      description="Search, filter, and classify every imported transaction."
      eyebrow="Workspace"
      title="Transactions"
    >
      <section
        aria-labelledby="transactions-empty-title"
        className="mt-8 rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-8"
      >
        <h2
          id="transactions-empty-title"
          className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
        >
          No transactions yet
        </h2>
        <p className="mt-4 max-w-2xl text-base leading-7 text-muted">
          Transactions appear here after your first statement import. Create a ZAR financial
          account, categories, and merchant tags in{" "}
          <Link
            className="font-semibold text-accent-text underline underline-offset-4"
            href="/settings"
          >
            Settings
          </Link>
          , then import a statement in{" "}
          <Link
            className="font-semibold text-accent-text underline underline-offset-4"
            href="/imports"
          >
            Imports
          </Link>
          .
        </p>
      </section>
    </AppShell>
  );
}

export default async function TransactionsPage() {
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

  return account ? <TransactionsWorkspace account={account} /> : <WorkspaceExpired />;
}
