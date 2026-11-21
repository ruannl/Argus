import Link from "next/link";

import type { ReportResponse } from "@/lib/server/backend-client";
import type { ReportingState } from "@/lib/reporting-state";
import { reportingHref } from "@/lib/reporting-state";
import { compareMoneyDescending, formatMoney } from "@/lib/money-format";

import { transactionDetailHref, transactionHref } from "./reporting-links";
import { bucketDays, TrendBarPlot, TrendLegend, trendMaximum } from "./trend-chart";

type ReportDetailProps = {
  kind: "category" | "merchant_tag" | "untagged" | "comparison";
  state: ReportingState;
  overviewState?: ReportingState;
  report: ReportResponse;
  path: string;
};

function Amount({ value }: { value: string }) {
  return <span className="tabular-nums">{formatMoney(value)}</span>;
}

function CoverageNote({
  report,
  state,
  path,
}: {
  report: ReportResponse;
  state: ReportingState;
  path: string;
}) {
  const coverage = report.coverage?.current;
  if (!coverage) {
    return null;
  }

  return (
    <section className="mt-4 rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
      <h2 className="text-sm font-bold tracking-[0.1em] text-muted uppercase">
        Combined statement coverage
      </h2>
      <p className="mt-1 text-sm text-ink">
        {coverage.covered_days} of {coverage.total_days} days
        {coverage.covered_percentage ? ` · ${coverage.covered_percentage}%` : ""}
      </p>
      {coverage.partial ? (
        <div className="mt-2 rounded-xl border border-warning-line bg-warning-soft p-3">
          <p className="text-xs font-semibold text-warning-ink">
            Missing statement dates are not zero spending.
          </p>
          <ul className="mt-1 space-y-1 text-xs text-warning-ink">
            {coverage.gaps.items.map((gap) => (
              <li key={`${gap.start_date}-${gap.end_date}`}>
                {gap.start_date} to {gap.end_date} ({gap.days} {gap.days === 1 ? "day" : "days"})
              </li>
            ))}
          </ul>
          {coverage.gaps.has_more && coverage.gaps.next_page ? (
            <Link
              className="mt-1 inline-flex text-xs font-bold text-warning-ink underline underline-offset-4"
              href={reportingHref(state, { coveragePage: coverage.gaps.next_page }, path)}
            >
              More uncovered ranges
            </Link>
          ) : null}
        </div>
      ) : null}
      {report.coverage?.prior ? (
        <details className="mt-2 rounded-xl border border-line bg-page p-2">
          <summary className="cursor-pointer text-xs font-semibold text-ink">
            Comparison-period coverage
          </summary>
          <p className="mt-1 text-xs text-muted">
            {report.coverage.prior.covered_days} of {report.coverage.prior.total_days} days covered
            {report.coverage.prior.covered_percentage
              ? ` · ${report.coverage.prior.covered_percentage}%`
              : ""}
            . Missing dates are not zero spending.
          </p>
          <ul className="mt-1 space-y-1 text-xs text-muted">
            {report.coverage.prior.gaps.items.map((gap) => (
              <li key={`detail-prior-${gap.start_date}-${gap.end_date}`}>
                {gap.start_date} to {gap.end_date} ({gap.days} {gap.days === 1 ? "day" : "days"})
              </li>
            ))}
          </ul>
        </details>
      ) : null}
    </section>
  );
}

function Totals({ report }: { report: ReportResponse }) {
  const current = report.current;
  if (!current) {
    return null;
  }

  return (
    <section className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4" aria-label="Totals">
      <div className="rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
        <p className="text-xs font-bold tracking-[0.12em] text-muted uppercase">Money in</p>
        <p className="mt-2 text-xl font-semibold text-ink">
          <Amount value={current.money.money_in} />
        </p>
      </div>
      <div className="rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
        <p className="text-xs font-bold tracking-[0.12em] text-muted uppercase">Money out</p>
        <p className="mt-2 text-xl font-semibold text-ink">
          <Amount value={current.money.money_out} />
        </p>
      </div>
      <div className="rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
        <p className="text-xs font-bold tracking-[0.12em] text-muted uppercase">Net</p>
        <p className="mt-2 text-xl font-semibold text-ink">
          <Amount value={current.money.net} />
        </p>
      </div>
      <div className="rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
        <p className="text-xs font-bold tracking-[0.12em] text-muted uppercase">
          Included transactions
        </p>
        <p className="mt-2 text-xl font-semibold text-ink">{current.money.included_count}</p>
        <p className="mt-1 text-xs text-muted">{current.money.zero_count} zero-value rows</p>
      </div>
    </section>
  );
}

function CategoryMix({ report, state }: { report: ReportResponse; state: ReportingState }) {
  const breakdown = report.categories;
  if (!breakdown) {
    return null;
  }

  return (
    <section className="mt-4 grid gap-3 lg:grid-cols-2" aria-label="Category mix">
      {(["money_in", "money_out"] as const).map((direction) => {
        const data = breakdown[direction];
        const ranks = (
          data.all_ranks && data.all_ranks.length > 0
            ? [...data.all_ranks]
            : [
                ...data.ranks,
                ...(data.uncategorised ? [data.uncategorised] : []),
                ...(data.other_ranks ?? []),
              ]
        ).sort(
          (left, right) =>
            compareMoneyDescending(left.amount, right.amount) || left.id.localeCompare(right.id),
        );
        return (
          <div
            className="rounded-[1.25rem] border border-line bg-surface p-4 card-shadow"
            key={direction}
          >
            <h2 className="text-sm font-bold tracking-[0.1em] text-muted uppercase">
              {direction === "money_in" ? "Money in category mix" : "Money out category mix"}
            </h2>
            <ul className="mt-2 space-y-1 text-sm">
              {ranks.map((rank) => (
                <li className="flex flex-wrap items-baseline justify-between gap-2" key={rank.id}>
                  <Link
                    className="font-semibold text-ink underline-offset-4 hover:underline"
                    href={reportingHref(
                      state,
                      { direction: direction === "money_in" ? "in" : "out" },
                      `/home/categories/${rank.id}`,
                    )}
                  >
                    {rank.display_name}
                  </Link>
                  <span className="text-ink">
                    <Amount value={rank.amount} /> · {rank.count}
                  </span>
                </li>
              ))}
            </ul>
            <p className="mt-2 text-xs text-muted">
              Categories are additive in this direction, ordered from largest to smallest amount.
            </p>
          </div>
        );
      })}
    </section>
  );
}

function TagBreakdown({ report, state }: { report: ReportResponse; state: ReportingState }) {
  const breakdown = report.tags;
  if (!breakdown) {
    return null;
  }

  return (
    <section className="mt-4 grid gap-3 lg:grid-cols-2" aria-label="Merchant tag breakdown">
      {(["money_in", "money_out"] as const).map((direction) => {
        const data = breakdown[direction];
        const ranks = (
          data.all_ranks && data.all_ranks.length > 0
            ? [...data.all_ranks]
            : [...data.ranks, ...data.other.items]
        ).sort(
          (left, right) =>
            compareMoneyDescending(left.amount, right.amount) || left.id.localeCompare(right.id),
        );
        return (
          <div
            className="rounded-[1.25rem] border border-line bg-surface p-4 card-shadow"
            key={direction}
          >
            <h2 className="text-sm font-bold tracking-[0.1em] text-muted uppercase">
              {direction === "money_in" ? "Money in by tag" : "Money out by tag"}
            </h2>
            <ul className="mt-2 space-y-1 text-sm">
              {ranks.map((rank) => (
                <li className="flex flex-wrap items-baseline justify-between gap-2" key={rank.id}>
                  <span className="font-semibold text-ink">{rank.display_name}</span>
                  <span className="text-ink">
                    <Amount value={rank.amount} /> · {rank.count}
                    {rank.prior_amount !== null
                      ? ` · prior ${formatMoney(rank.prior_amount)}${rank.change_percentage !== null ? ` (${rank.change_percentage}%)` : ""}`
                      : ""}
                  </span>
                </li>
              ))}
              <li className="flex flex-wrap items-baseline justify-between gap-2">
                <Link
                  className="font-semibold text-ink underline-offset-4 hover:underline"
                  href={reportingHref(state, {}, "/home/untagged")}
                >
                  Untagged
                </Link>
                <span className="text-ink">
                  <Amount value={data.untagged.amount} /> · {data.untagged.count}
                </span>
              </li>
            </ul>
            <p className="mt-2 text-xs text-muted">
              Every tag is individual and ordered by amount; tag totals overlap and never sum to a
              headline.
            </p>
          </div>
        );
      })}
    </section>
  );
}

function DetailComparison({ report }: { report: ReportResponse }) {
  const comparison = report.comparison;
  if (!comparison) {
    return (
      <section className="mt-4 rounded-[1.25rem] border border-line bg-surface p-4">
        <h2 className="text-lg font-semibold text-ink">No comparison available</h2>
        <p className="mt-1 text-sm text-muted">
          At least two confirmed salary-cycle starts, or an earlier representable period with
          included data, are needed.
        </p>
      </section>
    );
  }

  return (
    <section className="mt-4 rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
      <h2 className="text-lg font-semibold text-ink">Comparison</h2>
      {comparison.unavailable_reason || !comparison.period ? (
        <p className="mt-1 text-sm text-muted">
          No comparison available.
          {comparison.unavailable_reason === "insufficient_salary_boundaries"
            ? " At least two confirmed salary-cycle starts are needed."
            : comparison.unavailable_reason === "no_prior_data"
              ? " The comparison period has no included transactions."
              : comparison.unavailable_reason === "date_boundary"
                ? " The comparison range cannot be represented at this date boundary."
                : ""}
        </p>
      ) : (
        <p className="mt-1 text-sm text-muted">
          {comparison.period.label}: {comparison.period.start_date} to {comparison.period.end_date}
          {comparison.shorter_prior ? " · The prior cycle is shorter and capped at its end." : ""}
          {report.prior ? ` · Prior Net ${formatMoney(report.prior.money.net)}` : ""}
        </p>
      )}
    </section>
  );
}

function TrendTable({ report, state }: { report: ReportResponse; state: ReportingState }) {
  const buckets = report.trend ?? [];
  if (buckets.length === 0) {
    return null;
  }

  const maximum = trendMaximum(buckets);

  return (
    <section className="mt-4 rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
      <h2 className="text-sm font-bold tracking-[0.1em] text-muted uppercase">Trend</h2>
      <p className="mt-1 text-xs text-muted">
        Bars share one scale from zero, so heights compare across buckets.
      </p>
      <TrendLegend />
      <div className="mt-2 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {buckets.map((bucket) => {
          const days = bucketDays(bucket.start_date, bucket.end_date);
          return (
            <div
              className="rounded-xl border border-line bg-page p-2"
              key={`detail-bar-${bucket.start_date}-${bucket.end_date}`}
            >
              <p className="text-xs font-semibold text-muted">
                {bucket.label} · {days} {days === 1 ? "day" : "days"}
              </p>
              <TrendBarPlot bucket={bucket} heightClassName="h-16" maximum={maximum} />
              <p className="mt-1 text-xs text-muted">
                In <Amount value={bucket.money_in} /> · Out <Amount value={bucket.money_out} />
              </p>
            </div>
          );
        })}
      </div>
      <div className="mt-2 overflow-x-auto">
        <table className="w-full caption-bottom text-left text-xs">
          <caption className="sr-only">Money in and money out by period bucket</caption>
          <thead>
            <tr className="text-muted">
              <th scope="col">Bucket</th>
              <th scope="col">Money in</th>
              <th scope="col">Money out</th>
              <th scope="col">Transactions</th>
              <th scope="col">Coverage</th>
            </tr>
          </thead>
          <tbody>
            {buckets.map((bucket) => (
              <tr className="border-t border-line" key={`${bucket.start_date}-${bucket.end_date}`}>
                <th className="py-1 font-semibold text-ink" scope="row">
                  {bucket.label}
                </th>
                <td className="py-1">
                  <Link
                    className="underline underline-offset-4"
                    href={transactionHref(bucket.money_in_descriptor, state)}
                  >
                    {formatMoney(bucket.money_in)}
                  </Link>
                </td>
                <td className="py-1">
                  <Link
                    className="underline underline-offset-4"
                    href={transactionHref(bucket.money_out_descriptor, state)}
                  >
                    {formatMoney(bucket.money_out)}
                  </Link>
                </td>
                <td className="py-1">{bucket.count}</td>
                <td className="py-1">{bucket.coverage}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function TransactionTable({
  report,
  state,
  path,
}: {
  report: ReportResponse;
  state: ReportingState;
  path: string;
}) {
  const page = report.transactions;
  if (!page) {
    return null;
  }

  return (
    <section className="mt-4 rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-sm font-bold tracking-[0.1em] text-muted uppercase">
          Matching transactions
        </h2>
        <p className="text-xs text-muted">
          {formatMoney(report.current?.money.net ?? "0.00")} Net on this report
        </p>
      </div>
      {page.items.length === 0 ? (
        <p className="mt-2 text-sm text-muted">No included transactions on this page.</p>
      ) : (
        <div className="mt-2 overflow-x-auto">
          <table className="w-full caption-bottom text-left text-xs">
            <caption className="sr-only">Read-only transactions matching this report</caption>
            <thead>
              <tr className="text-muted">
                <th scope="col">Date</th>
                <th scope="col">Description</th>
                <th scope="col">Amount</th>
                <th scope="col">Category</th>
                <th scope="col">Tags</th>
              </tr>
            </thead>
            <tbody>
              {page.items.map((row) => (
                <tr className="border-t border-line align-top" key={row.id}>
                  <th className="py-1 font-semibold text-ink" scope="row">
                    {row.transaction_date}
                  </th>
                  <td className="py-1">
                    <Link
                      className="underline underline-offset-4"
                      href={transactionDetailHref(row.id, state)}
                    >
                      {row.description}
                    </Link>
                  </td>
                  <td className="py-1">
                    <Amount value={row.amount} />
                  </td>
                  <td className="py-1">{row.category.display_name}</td>
                  <td className="py-1">
                    {row.merchant_tags.map((tag) => tag.display_name).join(", ") || "Untagged"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <div className="mt-2 flex flex-wrap gap-3 text-xs font-semibold">
        {page.page > 1 ? (
          <Link
            className="text-accent-text underline underline-offset-4"
            href={reportingHref(state, { transactionsPage: page.page - 1 }, path)}
          >
            Previous page
          </Link>
        ) : null}
        {page.has_more && page.next_page ? (
          <Link
            className="text-accent-text underline underline-offset-4"
            href={reportingHref(state, { transactionsPage: page.next_page }, path)}
          >
            More matching transactions
          </Link>
        ) : null}
      </div>
    </section>
  );
}

function ComparisonChanges({
  report,
  state,
  path,
}: {
  report: ReportResponse;
  state: ReportingState;
  path: string;
}) {
  const changes = report.category_changes;
  if (!changes) {
    return (
      <section className="mt-4 rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
        <h2 className="text-lg font-semibold text-ink">No comparison available</h2>
        <p className="mt-1 text-sm text-muted">
          {report.comparison?.unavailable_reason === "insufficient_salary_boundaries"
            ? "At least two confirmed salary-cycle starts are needed."
            : report.comparison?.unavailable_reason === "no_prior_data"
              ? "The comparison period has no included transactions."
              : "The comparison period could not be defined from the selected boundaries."}
        </p>
      </section>
    );
  }

  const renderDirection = (name: "money_in" | "money_out") => {
    const page = changes[name];
    const label = name === "money_in" ? "Money in category changes" : "Money out category changes";
    return (
      <div className="rounded-[1.25rem] border border-line bg-surface p-4 card-shadow" key={name}>
        <h2 className="text-sm font-bold tracking-[0.1em] text-muted uppercase">{label}</h2>
        <p className="mt-1 text-xs text-muted">
          Ordered by absolute directional change, largest first. A negative delta is a decrease, not
          a judgement.
        </p>
        <ul className="mt-2 space-y-2">
          {page.items.map((item) => (
            <li className="rounded-xl border border-line bg-page p-2" key={`${name}-${item.id}`}>
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <Link
                  className="text-sm font-semibold text-ink underline-offset-4 hover:underline"
                  href={reportingHref(
                    state,
                    { direction: name === "money_in" ? "in" : "out" },
                    `/home/categories/${item.id}`,
                  )}
                >
                  {item.display_name}
                </Link>
                <span className="text-sm font-semibold text-ink">
                  <Amount value={item.delta} />
                  {item.delta_percentage ? ` · ${item.delta_percentage}%` : ""}
                </span>
              </div>
              <p className="mt-1 text-xs text-muted">
                Current <Amount value={item.current_amount} /> · Prior{" "}
                <Amount value={item.prior_amount} />
              </p>
              <div className="mt-1 flex flex-wrap gap-3 text-xs font-semibold">
                <Link
                  className="text-accent-text underline underline-offset-4"
                  href={transactionHref(item.current_descriptor, state)}
                >
                  Current period transactions
                </Link>
                <Link
                  className="text-accent-text underline underline-offset-4"
                  href={transactionHref(item.prior_descriptor, state)}
                >
                  Prior period transactions
                </Link>
              </div>
            </li>
          ))}
        </ul>
        {page.has_more && page.next_page ? (
          <Link
            className="mt-2 inline-flex text-xs font-bold text-accent-text underline underline-offset-4"
            href={reportingHref(state, { categoryChangesPage: page.next_page }, path)}
          >
            More {name === "money_in" ? "inflow" : "outflow"} category changes
          </Link>
        ) : null}
      </div>
    );
  };

  return (
    <section className="mt-4 grid gap-3 lg:grid-cols-2" aria-label="Category changes">
      {renderDirection("money_in")}
      {renderDirection("money_out")}
    </section>
  );
}

function BackToOverview({ state }: { state: ReportingState }) {
  return (
    <Link
      className="text-sm font-semibold text-accent-text underline underline-offset-4"
      href={reportingHref(state)}
    >
      Back to Overview
    </Link>
  );
}

export default function ReportDetail({
  kind,
  state,
  overviewState = state,
  report,
  path,
}: ReportDetailProps) {
  const title =
    kind === "category"
      ? (report.focus.category_name ?? "Category")
      : kind === "untagged"
        ? "Untagged"
        : kind === "merchant_tag"
          ? (report.focus.merchant_tag_name ?? "Merchant tag")
          : "Period Comparison";

  return (
    <div>
      <BackToOverview state={overviewState} />
      <header className="mt-2">
        <h1 className="text-2xl font-semibold tracking-[-0.02em] text-ink">{title}</h1>
        <p className="mt-1 text-sm text-muted">
          {report.period
            ? `${report.period.label}: ${report.period.start_date} to ${report.period.end_date}`
            : "Salary-cycle setup required"}
          {report.period?.is_partial ? " · Partial period" : ""}
        </p>
        {report.focus.category_kind ? (
          <p className="mt-1 text-xs text-muted">Category kind: {report.focus.category_kind}</p>
        ) : null}
        {report.focus_group ? (
          <p className="mt-1 text-xs text-muted">
            {report.focus_group.kind} group: {formatMoney(report.focus_group.money_in)} in,{" "}
            {formatMoney(report.focus_group.money_out)} out, {formatMoney(report.focus_group.net)}{" "}
            net.
          </p>
        ) : null}
      </header>

      {report.status === "setup_required" ? (
        <section className="mt-4 rounded-[1.25rem] border border-line bg-surface p-4">
          <p className="text-sm text-muted">{report.setup?.message}</p>
        </section>
      ) : (
        <>
          <Totals report={report} />
          <CoverageNote path={path} report={report} state={state} />
          {kind === "comparison" ? (
            <ComparisonChanges path={path} report={report} state={state} />
          ) : (
            <>
              <CategoryMix report={report} state={state} />
              <TagBreakdown report={report} state={state} />
              <DetailComparison report={report} />
              {report.category_changes ? (
                <ComparisonChanges path={path} report={report} state={state} />
              ) : null}
              <TrendTable report={report} state={state} />
              <TransactionTable path={path} report={report} state={state} />
            </>
          )}
          {report.status === "all_hidden" ? (
            <section className="mt-4 rounded-[1.25rem] border border-warning-line bg-warning-soft p-4">
              <p className="text-sm text-warning-ink">
                All transactions in this period are hidden. Nothing here is a zero-spend result.
                Parent-hidden rows are restored in Imports.
              </p>
            </section>
          ) : null}
          {report.status === "excluded_only" ? (
            <section className="mt-4 rounded-[1.25rem] border border-warning-line bg-warning-soft p-4">
              <p className="text-sm text-warning-ink">
                The period contains only Transfer or Excluded rows, so the included headline is
                zero. Their visible diagnostics remain in the Overview.
              </p>
            </section>
          ) : null}
        </>
      )}

      <details className="mt-4 rounded-[1.25rem] border border-line bg-surface p-4">
        <summary className="cursor-pointer text-sm font-bold text-ink">
          Definitions used on this page
        </summary>
        <ul className="mt-2 space-y-1 text-xs leading-5 text-muted">
          {report.policy_labels.map((label) => (
            <li key={label}>{label}</li>
          ))}
        </ul>
      </details>
    </div>
  );
}
