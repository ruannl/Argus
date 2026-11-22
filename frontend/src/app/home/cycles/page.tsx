import Link from "next/link";
import { redirect } from "next/navigation";

import { auth } from "@/auth";
import AppShell from "@/components/app-shell";
import WorkspaceExpired from "@/components/workspace-expired";
import AccountReadUnavailable from "@/app/transactions/account-read-unavailable";
import { getCurrentAccount, listSalaryCycles } from "@/lib/server/backend-client";
import { parseReportingState, reportingHref } from "@/lib/reporting-state";
import { loadReportPage } from "@/lib/server/reporting";

import SalaryCycleWorkspace from "../salary-cycles";

export const dynamic = "force-dynamic";

export default async function SalaryCyclesPage({
  searchParams,
}: {
  searchParams?: Promise<Record<string, string | string[] | undefined>>;
}) {
  const backendToken = (await auth())?.user?.backendToken;
  if (!backendToken) {
    redirect("/login");
    return null;
  }

  let account = null;
  try {
    account = await getCurrentAccount(backendToken);
  } catch (error) {
    const kind =
      typeof error === "object" && error !== null && "kind" in error
        ? (error as { kind?: unknown }).kind
        : "unavailable";
    if (kind === "unauthorized") {
      return <WorkspaceExpired />;
    }
    return (
      <AccountReadUnavailable
        kind={kind === "rate_limited" ? "rate_limited" : "unavailable"}
        view="detail"
      />
    );
  }

  const parsed = parseReportingState((await searchParams) ?? {});
  const state = parsed.error
    ? { view: "summary" as const, mode: "salary_cycle" as const }
    : parsed.state;
  const data = await loadReportPage(backendToken, account, state);
  if (!data.ok) {
    if (data.kind === "unauthorized") {
      return <WorkspaceExpired />;
    }
    return (
      <AppShell
        account={account}
        activeNav="overview"
        description="Manage the receipt-confirmed dates used by salary-cycle reporting."
        title="Salary cycles"
      >
        <section className="mt-6 rounded-[1.5rem] border border-warning-line bg-warning-soft p-4">
          <p className="text-sm text-warning-ink" role="alert">
            The salary-cycle sequence could not be loaded. Missing sequence data is never shown as
            an empty sequence.
          </p>
          <Link
            className="mt-3 inline-flex rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink"
            href="/home/cycles"
          >
            Retry
          </Link>
        </section>
      </AppShell>
    );
  }

  const list = await listSalaryCycles(backendToken);
  if (!list.ok) {
    return (
      <AppShell
        account={account}
        activeNav="overview"
        description="Manage the receipt-confirmed dates used by salary-cycle reporting."
        title="Salary cycles"
      >
        <section className="mt-6 rounded-[1.5rem] border border-warning-line bg-warning-soft p-4">
          <p className="text-sm text-warning-ink" role="alert">
            The confirmed dates could not be read. Nothing was changed. Retry without repeating a
            removal.
          </p>
          <Link
            className="mt-3 inline-flex rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink"
            href="/home/cycles"
          >
            Reload the sequence
          </Link>
        </section>
      </AppShell>
    );
  }

  return (
    <AppShell
      account={account}
      activeNav="overview"
      description="Confirm, inspect and correct the receipt dates that define salary-cycle periods."
      title="Salary cycles"
    >
      <Link
        className="text-sm font-semibold text-accent-text underline underline-offset-4"
        href={reportingHref(parsed.state)}
      >
        Back to Overview
      </Link>
      <p className="mt-2 max-w-3xl text-sm leading-6 text-muted">
        A confirmed start is a small, customer-owned reporting date. It is independent of account,
        category and tag filters. The latest confirmed start forms the open cycle through today;
        confirm another eligible receipt to close it.
      </p>
      <SalaryCycleWorkspace
        accounts={data.inventory.accounts}
        initialRevision={list.value.revision}
        initialStarts={list.value.starts}
        nextBeforeDate={list.value.next_before_date}
        today={data.report.today}
      />
    </AppShell>
  );
}
