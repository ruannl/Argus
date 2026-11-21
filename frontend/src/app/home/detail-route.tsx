import Link from "next/link";
import { notFound, redirect } from "next/navigation";

import { auth } from "@/auth";
import AppShell from "@/components/app-shell";
import WorkspaceExpired from "@/components/workspace-expired";
import AccountReadUnavailable from "@/app/transactions/account-read-unavailable";
import { getCurrentAccount } from "@/lib/server/backend-client";
import { parseReportingState, reportingHref } from "@/lib/reporting-state";
import type { ReportingState } from "@/lib/reporting-state";
import { loadReportPage } from "@/lib/server/reporting";

import ReportDetail from "./report-detail";

type DetailRouteProps = {
  kind: "category" | "merchant_tag" | "untagged" | "comparison";
  id?: string;
  path: string;
  searchParams?: Record<string, string | string[] | undefined>;
};

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export default async function DetailRoute({ kind, id, path, searchParams }: DetailRouteProps) {
  const backendToken = (await auth())?.user?.backendToken;
  if (!backendToken) {
    redirect("/login");
    return null;
  }

  let account = null;
  try {
    account = await getCurrentAccount(backendToken);
  } catch (error) {
    const kindValue =
      typeof error === "object" && error !== null && "kind" in error
        ? (error as { kind?: unknown }).kind
        : "unavailable";
    if (kindValue === "unauthorized") {
      return <WorkspaceExpired />;
    }
    return (
      <AccountReadUnavailable
        kind={kindValue === "rate_limited" ? "rate_limited" : "unavailable"}
        view="detail"
      />
    );
  }

  if (kind !== "comparison" && kind !== "untagged" && !id) {
    notFound();
  }
  if (id && !UUID_PATTERN.test(id)) {
    notFound();
  }

  const parsed = parseReportingState(searchParams ?? {});
  if (parsed.error) {
    return (
      <AppShell
        account={account}
        activeNav="overview"
        description="A focused view of the money behind the Overview."
        eyebrow="Overview"
        title="Invalid Overview options"
      >
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
      </AppShell>
    );
  }
  const overviewState: ReportingState = { ...parsed.state };
  const state: ReportingState = { ...parsed.state };
  if (kind === "category" && id) {
    state.category = id;
  }
  if (kind === "merchant_tag" && id) {
    state.merchantTag = id;
  }
  if (kind === "untagged") {
    state.merchantTag = "untagged";
  }

  const options =
    kind === "category"
      ? { view: "category" as const, focus: { categoryId: id } }
      : kind === "merchant_tag"
        ? { view: "merchant_tag" as const, focus: { merchantTag: id } }
        : kind === "untagged"
          ? { view: "merchant_tag" as const, focus: { untagged: true } }
          : { view: "comparison" as const };

  const data = await loadReportPage(backendToken, account, state, options);
  if (!data.ok) {
    if (data.kind === "unauthorized") {
      return <WorkspaceExpired />;
    }
    return (
      <AppShell
        account={account}
        activeNav="overview"
        description="A focused view of the money behind the Overview."
        eyebrow="Overview"
        title="Report unavailable"
      >
        <section className="mt-8 rounded-[2rem] border border-warning-line bg-warning-soft p-6">
          <h2 className="text-xl font-semibold text-warning-ink">This view could not be loaded</h2>
          <p className="mt-2 text-sm leading-6 text-warning-ink" role="alert">
            {data.kind === "not_found"
              ? "That item is not in your workspace."
              : data.kind === "rate_limited"
                ? "Too many requests. Wait a few minutes, then retry. We did not sign you out."
                : "Missing report data is never shown as zero. Retry the selected scope."}
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            <Link
              className="rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink"
              href={reportingHref(state)}
            >
              Back to Overview
            </Link>
            <Link
              className="rounded-full border border-line bg-surface px-4 py-2 text-sm font-bold text-ink"
              href={path}
            >
              Retry this view
            </Link>
          </div>
        </section>
      </AppShell>
    );
  }

  return (
    <AppShell
      account={data.account}
      activeNav="overview"
      description="A focused view of the money behind the Overview, with the same filtered transactions."
      eyebrow="Overview"
      title={kind === "comparison" ? "Period Comparison" : "Overview detail"}
    >
      <ReportDetail
        kind={kind}
        overviewState={overviewState}
        path={path}
        report={data.report}
        state={state}
      />
    </AppShell>
  );
}
