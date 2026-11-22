import Link from "next/link";
import { redirect } from "next/navigation";

import { auth } from "@/auth";
import AppShell from "@/components/app-shell";
import WorkspaceExpired from "@/components/workspace-expired";
import { getCurrentAccount, listSalaryCycles } from "@/lib/server/backend-client";
import type { SalaryCycleBoundary } from "@/lib/server/backend-client";
import { parseReportingState, reportingHref } from "@/lib/reporting-state";
import { loadReportPage } from "@/lib/server/reporting";
import AccountReadUnavailable from "@/app/transactions/account-read-unavailable";

import OverviewWorkspace from "./overview-workspace";

export const dynamic = "force-dynamic";

const REPORT_ERROR_MESSAGES = {
  unauthorized: "Your session expired. Sign in again to load Overview.",
  rate_limited:
    "Too many Overview requests. Wait a few minutes, then retry. We did not sign you out.",
  invalid_request: "The selected period or filters are invalid. Reset the view.",
  not_found: "A selected item no longer exists in your workspace.",
  request_too_large: "That Overview request was too large.",
  receipt_ineligible: "That transaction cannot be a salary-cycle start.",
  version_conflict: "Data changed while the report was loading. Retry the same view.",
  cycle_sequence_changed: "The salary-cycle sequence changed. Reload the report.",
  report_busy: "The report is busy. Retry in a moment.",
  unavailable: "Overview could not be loaded. That does not prove your session expired.",
} as const;

function InvalidState() {
  return (
    <section className="mt-8 rounded-[2rem] border border-warning-line bg-warning-soft p-6">
      <h2 className="text-xl font-semibold text-warning-ink">Overview options are invalid</h2>
      <p className="mt-2 max-w-2xl text-sm leading-6 text-warning-ink" role="alert">
        Some period, filter or page options in the address were malformed, repeated or unknown.
        Nothing has been broadened to a default report.
      </p>
      <Link
        className="mt-4 inline-flex rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink"
        href="/home"
      >
        Reset to the default salary-cycle view
      </Link>
    </section>
  );
}

export default async function HomePage({
  searchParams,
}: {
  searchParams?: Promise<Record<string, string | string[] | undefined>>;
} = {}) {
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
        title="Overview unavailable"
        view="list"
      />
    );
  }

  const params = (await searchParams) ?? {};
  const parsed = parseReportingState(params);
  if (parsed.error) {
    return (
      <AppShell
        account={account}
        activeNav="overview"
        description="Summary and charts built from your imported statements."
        title="Overview"
      >
        <InvalidState />
      </AppShell>
    );
  }

  const data = await loadReportPage(backendToken, account, parsed.state);
  if (!data.ok) {
    if (data.kind === "unauthorized") {
      return <WorkspaceExpired />;
    }
    return (
      <AppShell
        account={account}
        activeNav="overview"
        description="Summary and charts built from your imported statements."
        title="Overview"
      >
        <section className="mt-8 rounded-[2rem] border border-warning-line bg-warning-soft p-6">
          <h2 className="text-xl font-semibold text-warning-ink">Overview unavailable</h2>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-warning-ink" role="alert">
            {REPORT_ERROR_MESSAGES[data.kind]}
          </p>
          <p className="mt-1 max-w-2xl text-sm leading-6 text-warning-ink">
            The selected period and filters are retained. Missing report or inventory data is never
            shown as zero.
          </p>
          <div className="mt-4 flex flex-wrap gap-2">
            <Link
              className="rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink"
              href={reportingHref(parsed.state)}
            >
              Retry the same view
            </Link>
            <Link
              className="rounded-full border border-line bg-surface px-4 py-2 text-sm font-bold text-ink"
              href="/imports"
            >
              Open Imports
            </Link>
          </div>
        </section>
      </AppShell>
    );
  }

  let starts: SalaryCycleBoundary[] = [];
  let startsError: string | null = null;
  let beforeDate: string | null = null;
  for (let page = 0; page < 20; page++) {
    const salaryList = await listSalaryCycles(backendToken, beforeDate);
    if (!salaryList.ok) {
      startsError =
        salaryList.kind === "rate_limited"
          ? "Salary-cycle choices could not be refreshed because of rate limiting. The report below is still the selected snapshot."
          : "Confirmed salary-cycle choices could not be loaded. This is not an empty sequence; retry the cycle page.";
      break;
    }
    starts = [...starts, ...salaryList.value.starts];
    beforeDate = salaryList.value.next_before_date;
    if (!beforeDate) {
      break;
    }
  }

  return (
    <AppShell
      account={data.account}
      activeNav="overview"
      description="Summary and charts built from your imported statements, with a path to every matching transaction."
      title="Overview"
    >
      <OverviewWorkspace
        accounts={data.inventory.accounts}
        categories={data.inventory.categories}
        error={null}
        report={data.report}
        starts={starts}
        startsError={startsError}
        state={parsed.state}
        tags={data.inventory.tags}
      />
    </AppShell>
  );
}
