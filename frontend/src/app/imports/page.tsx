import Link from "next/link";
import { redirect } from "next/navigation";

import { auth } from "@/auth";
import AppShell from "@/components/app-shell";
import WorkspaceExpired from "@/components/workspace-expired";
import { getCurrentAccount } from "@/lib/server/backend-client";
import type { CurrentAccount } from "@/lib/server/backend-client";

export const dynamic = "force-dynamic";

function ImportsWorkspace({ account }: { account: CurrentAccount }) {
  return (
    <AppShell
      account={account}
      activeNav="imports"
      description="Import and review bank and credit-card statements."
      eyebrow="Workspace"
      title="Imports"
    >
      <section
        aria-labelledby="imports-empty-title"
        className="mt-8 rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-8"
      >
        <h2
          id="imports-empty-title"
          className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
        >
          No statements imported yet
        </h2>
        <p className="mt-4 max-w-2xl text-base leading-7 text-muted">
          First set up your ZAR financial accounts, categories, and merchant tags in{" "}
          <Link
            className="font-semibold text-accent-text underline underline-offset-4"
            href="/settings"
          >
            Settings
          </Link>
          . Statement file upload will appear here once your accounts are ready; nothing is
          imported yet.
        </p>
      </section>
    </AppShell>
  );
}

export default async function ImportsPage() {
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

  return account ? <ImportsWorkspace account={account} /> : <WorkspaceExpired />;
}
