import Link from "next/link";
import { redirect } from "next/navigation";

import { auth } from "@/auth";
import AppShell from "@/components/app-shell";
import WorkspaceExpired from "@/components/workspace-expired";
import { getCurrentAccount } from "@/lib/server/backend-client";
import type { CurrentAccount } from "@/lib/server/backend-client";

export const dynamic = "force-dynamic";

function OverviewWorkspace({
  account,
  view,
}: {
  account: CurrentAccount;
  view: "summary" | "charts";
}) {
  const isCharts = view === "charts";
  const summaryClass =
    view === "summary"
      ? "rounded-full bg-ink px-4 py-2 text-sm font-semibold text-page"
      : "rounded-full px-4 py-2 text-sm font-semibold text-muted transition hover:bg-elevated hover:text-ink";
  const chartsClass =
    view === "charts"
      ? "rounded-full bg-ink px-4 py-2 text-sm font-semibold text-page"
      : "rounded-full px-4 py-2 text-sm font-semibold text-muted transition hover:bg-elevated hover:text-ink";

  return (
    <AppShell
      account={account}
      activeNav="overview"
      description="Summary and charts for the statements you have imported."
      eyebrow="Overview"
      title="Overview"
    >
      <nav
        aria-label="Overview views"
        className="mt-8 inline-flex rounded-full border border-line bg-surface p-1"
      >
        <Link
          aria-current={view === "summary" ? "page" : undefined}
          className={summaryClass}
          href="/home"
        >
          Summary
        </Link>
        <Link
          aria-current={view === "charts" ? "page" : undefined}
          className={chartsClass}
          href="/home?view=charts"
        >
          Charts
        </Link>
      </nav>

      <section
        aria-labelledby="overview-empty-title"
        className="mt-6 rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-8"
      >
        <h2
          id="overview-empty-title"
          className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
        >
          {isCharts ? "No charts yet" : "No transactions yet"}
        </h2>
        <p className="mt-4 max-w-2xl text-base leading-7 text-muted">
          {isCharts
            ? "Bar-first charts appear after your first statement import. Start by creating a ZAR financial account, categories, and tags in "
            : "Your Summary and Charts fill in after your first statement import. Start by creating a ZAR financial account, categories, and tags in "}
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

export default async function HomePage({
  searchParams,
}: {
  searchParams?: Promise<{ view?: string }>;
} = {}) {
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

  const params = await searchParams;
  const view = params?.view === "charts" ? "charts" : "summary";

  return account ? <OverviewWorkspace account={account} view={view} /> : <WorkspaceExpired />;
}
