import Link from "next/link";

import type { ReportResponse } from "@/lib/server/backend-client";
import type { ReportingState } from "@/lib/reporting-state";
import { reportingHref } from "@/lib/reporting-state";
import { compareMoneyDescending, formatMoney } from "@/lib/money-format";

import CategoryTrendChart from "./category-trend-chart";
import PeriodControls from "./period-controls";
import {
  categoryDetailHref,
  comparisonHref,
  cyclesHref,
  merchantTagDetailHref,
  transactionHref,
  untaggedDetailHref,
} from "./reporting-links";
import TrendPanel from "./trend-panel";
import { TrendRangeProvider } from "./trend-range";
import type {
  Category,
  FinancialAccount,
  MerchantTag,
  SalaryCycleBoundary,
} from "@/lib/server/backend-client";

type OverviewWorkspaceProps = {
  state: ReportingState;
  report: ReportResponse;
  accounts: FinancialAccount[];
  categories: Category[];
  tags: MerchantTag[];
  starts: SalaryCycleBoundary[];
  startsError?: string | null;
  error: string | null;
};

function percentStyle(share: string | null): { width: string } {
  if (!share) {
    return { width: "0%" };
  }
  const value = Number(share);
  const bounded = Number.isFinite(value) ? Math.max(0, Math.min(100, value)) : 0;
  return { width: `${bounded}%` };
}

function Amount({ value }: { value: string }) {
  return <span className="tabular-nums">{formatMoney(value)}</span>;
}

function PeriodHeading({ report }: { report: ReportResponse }) {
  if (!report.period) {
    return null;
  }
  return (
    <section
      className="mt-5 rounded-[1.25rem] border border-line bg-surface p-3"
      aria-label="Selected period"
    >
      <p className="text-sm font-semibold text-ink">
        {report.period.label}: {report.period.start_date} to {report.period.end_date}
        {report.period.is_partial ? " · Partial period" : ""}
        {report.period.is_future ? " · Future period" : ""}
        {report.period.is_open_salary_cycle ? " · Open cycle to date" : ""}
      </p>
      {report.period.note ? <p className="mt-1 text-xs text-muted">{report.period.note}</p> : null}
    </section>
  );
}

function ViewTabs({ state }: { state: ReportingState }) {
  const tabs = [
    ["summary", "Summary"],
    ["charts", "Charts"],
  ] as const;

  return (
    <nav aria-label="Overview views" className="finance-tabs mt-5">
      {tabs.map(([view, label]) => (
        <Link
          aria-current={state.view === view ? "page" : undefined}
          className={
            state.view === view
              ? "rounded-full bg-ink px-4 py-2 text-sm font-semibold text-page"
              : "rounded-full px-4 py-2 text-sm font-semibold text-muted transition hover:bg-elevated hover:text-ink"
          }
          href={reportingHref(state, { view })}
          key={view}
        >
          {label}
        </Link>
      ))}
      <Link
        className="rounded-full px-4 py-2 text-sm font-semibold text-muted transition hover:bg-elevated hover:text-ink"
        href={cyclesHref(state)}
      >
        Salary cycles
      </Link>
    </nav>
  );
}

function ErrorPanel({ message, state }: { message: string; state: ReportingState }) {
  return (
    <section
      className="mt-6 rounded-[1.5rem] border border-warning-line bg-warning-soft p-4"
      role="alert"
    >
      <h2 className="text-lg font-semibold text-warning-ink">Overview could not be refreshed</h2>
      <p className="mt-1 text-sm leading-6 text-warning-ink">{message}</p>
      <p className="mt-1 text-sm leading-6 text-warning-ink">
        The selected period and filters are retained. Reloading does not clear your session.
      </p>
      <Link
        className="mt-3 inline-flex rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink"
        href={reportingHref(state)}
      >
        Retry the same view
      </Link>
    </section>
  );
}

function StatusPanel({ state, report }: { state: ReportingState; report: ReportResponse }) {
  if (report.status === "setup_required") {
    return (
      <section className="mt-6 rounded-[1.75rem] border border-line bg-surface p-6 card-shadow sm:p-8">
        <h2 className="text-2xl font-semibold tracking-[-0.02em] text-ink">
          Set up salary-cycle reporting
        </h2>
        <p className="mt-3 max-w-2xl text-sm leading-7 text-muted">{report.setup?.message}</p>
        <p className="mt-2 max-w-2xl text-sm leading-7 text-muted">
          Salary-cycle reporting uses explicitly confirmed receipt dates. Nothing is inferred from a
          category, description, fixed payday or expected month end.
        </p>
        <div className="mt-4 flex flex-wrap gap-2">
          <Link
            className="rounded-full bg-ink px-4 py-2 text-sm font-bold text-page"
            href={reportingHref({ ...state, mode: "calendar", preset: "month_to_date" })}
          >
            Use calendar month to date
          </Link>
          <Link
            className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            href={reportingHref({
              ...state,
              mode: "custom",
              start: report.today,
              end: report.today,
            })}
          >
            Use custom dates
          </Link>
          {report.setup?.inventory_empty ? (
            <Link
              className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
              href="/imports"
            >
              Import a statement
            </Link>
          ) : null}
          <Link
            className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            href={cyclesHref(state)}
          >
            Choose a salary receipt
          </Link>
        </div>
      </section>
    );
  }

  if (report.status === "no_data") {
    return (
      <section className="mt-6 rounded-[1.75rem] border border-line bg-surface p-6 card-shadow">
        <h2 className="text-2xl font-semibold text-ink">No imported transactions in this period</h2>
        <p className="mt-2 max-w-2xl text-sm leading-7 text-muted">
          Import a statement or change the period. An empty period is not a claim that you spent
          nothing.
        </p>
        <div className="mt-3 flex flex-wrap gap-2">
          <Link
            className="rounded-full bg-ink px-4 py-2 text-sm font-bold text-page"
            href="/imports"
          >
            Open Imports
          </Link>
          <Link
            className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            href={reportingHref({ ...state, mode: "calendar", preset: "month_to_date" })}
          >
            Change period
          </Link>
        </div>
      </section>
    );
  }

  if (report.status === "no_matches") {
    return (
      <section className="mt-6 rounded-[1.75rem] border border-line bg-surface p-6 card-shadow">
        <h2 className="text-2xl font-semibold text-ink">No transactions match these filters</h2>
        <p className="mt-2 max-w-2xl text-sm leading-7 text-muted">
          The period has imported data, but none of it matches the selected account, category, tag
          or direction.
        </p>
        <Link
          className="mt-3 inline-flex rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
          href="/home"
        >
          Clear filters
        </Link>
      </section>
    );
  }

  if (report.status === "all_hidden") {
    return (
      <section className="mt-6 rounded-[1.75rem] border border-warning-line bg-warning-soft p-6">
        <h2 className="text-2xl font-semibold text-warning-ink">
          All transactions in this period are hidden
        </h2>
        <p className="mt-2 max-w-2xl text-sm leading-7 text-warning-ink">
          Nothing here is a zero-spend result. Hidden rows are excluded from every financial value.
          Parent-hidden rows are restored in Imports; individually hidden rows can be restored from
          the transaction list.
        </p>
      </section>
    );
  }

  if (report.status === "excluded_only") {
    return (
      <section className="mt-6 rounded-[1.75rem] border border-warning-line bg-warning-soft p-6">
        <h2 className="text-2xl font-semibold text-warning-ink">
          Only transfers or excluded rows are visible
        </h2>
        <p className="mt-2 max-w-2xl text-sm leading-7 text-warning-ink">
          Money in, Money out and Net are zero because Transfer and Excluded rows are outside the
          included headline. Their visible diagnostics remain below and link to their exact lists.
        </p>
      </section>
    );
  }

  if (
    report.status === "ready" &&
    report.current &&
    report.current.money.included_count > 0 &&
    report.current.money.money_in === "0.00" &&
    report.current.money.money_out === "0.00"
  ) {
    return (
      <section className="mt-6 rounded-[1.5rem] border border-line bg-surface p-4">
        <p className="text-sm text-muted">
          The period has {report.current.money.included_count} included transactions, all
          zero-valued. Inflows and outflows are offset or absent; this is recorded data, not an
          empty period.
        </p>
      </section>
    );
  }

  return null;
}

function MoneyCard({
  label,
  amount,
  detail,
  href,
}: {
  label: string;
  amount: string;
  detail?: string;
  href?: string;
}) {
  return (
    <div className="finance-summary rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
      <p className="text-xs font-bold tracking-[0.12em] text-muted uppercase">{label}</p>
      <p className="mt-2 text-2xl font-semibold tracking-[-0.02em] text-ink">
        {href ? (
          <Link className="underline-offset-4 hover:underline" href={href}>
            <Amount value={amount} />
          </Link>
        ) : (
          <Amount value={amount} />
        )}
      </p>
      {detail ? <p className="mt-1 text-xs leading-5 text-muted">{detail}</p> : null}
    </div>
  );
}

function MoneyCards({ report, state }: { report: ReportResponse; state: ReportingState }) {
  const totals = report.current;
  if (!totals) {
    return null;
  }

  return (
    <section aria-label="Period totals" className="mt-3">
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <MoneyCard
          amount={totals.money.money_in}
          detail="Gross visible included inflows. Open the exact filtered rows."
          href={
            state.direction === "out"
              ? undefined
              : transactionHref(totals.money_in_descriptor, state)
          }
          label="Money in"
        />
        <MoneyCard
          amount={totals.money.money_out}
          detail="Gross magnitude of visible included outflows. Open the exact filtered rows."
          href={
            state.direction === "in"
              ? undefined
              : transactionHref(totals.money_out_descriptor, state)
          }
          label="Money out"
        />
        <MoneyCard
          amount={totals.money.net}
          detail="Money in minus Money out; a negative Net is neutral information."
          href={transactionHref(totals.net_descriptor, state)}
          label="Net"
        />
        <MoneyCard
          amount={totals.uncategorised_money_out}
          detail={`${totals.uncategorised_count} uncategorised ${totals.uncategorised_count === 1 ? "transaction" : "transactions"}; in ${formatMoney(totals.uncategorised_money_in)}. Categorise them in the exact list.`}
          href={transactionHref(totals.uncategorised_out_descriptor, state)}
          label="Needs categorisation"
        />
      </div>
      <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted">
        <span>
          Categorised value coverage:{" "}
          <strong className="font-semibold text-ink">
            {totals.categorised_coverage_percentage
              ? `${totals.categorised_coverage_percentage}%`
              : "Not defined"}
          </strong>
        </span>
        <span>
          Tagged value coverage:{" "}
          <strong className="font-semibold text-ink">
            {totals.tag_coverage_percentage ? `${totals.tag_coverage_percentage}%` : "Not defined"}
          </strong>
        </span>
        <span>
          Unconfirmed category assignments:{" "}
          <Link
            className="font-semibold text-accent-text underline underline-offset-4"
            href={transactionHref(totals.unconfirmed_descriptor, state)}
          >
            {totals.unconfirmed_category_count}
          </Link>
        </span>
      </div>
    </section>
  );
}

function CoveragePanel({ report, state }: { report: ReportResponse; state: ReportingState }) {
  const coverage = report.coverage?.current;
  if (!coverage) {
    return null;
  }

  return (
    <section
      className="mt-5 rounded-[1.25rem] border border-line bg-surface p-4 card-shadow"
      aria-labelledby="coverage-title"
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold text-ink" id="coverage-title">
          Combined statement coverage
        </h2>
        <p className="text-sm font-semibold text-muted">
          {coverage.covered_days} of {coverage.total_days} days
          {coverage.covered_percentage ? ` · ${coverage.covered_percentage}%` : ""}
        </p>
      </div>
      <p className="mt-1 text-xs leading-5 text-muted">
        The union of committed, non-hidden statement dates for the selected account(s). It is not a
        claim that each account is fully covered.
      </p>
      {coverage.partial ? (
        <div
          className="mt-3 rounded-xl border border-warning-line bg-warning-soft p-3"
          role="status"
        >
          <p className="text-sm font-semibold text-warning-ink">
            The selected period is not fully covered by imported statements.
          </p>
          <p className="mt-1 text-xs leading-5 text-warning-ink">
            Missing statement dates are not zero spending. Uncovered ranges:
          </p>
          <ul className="mt-2 space-y-1 text-xs text-warning-ink">
            {coverage.gaps.items.map((gap) => (
              <li key={`${gap.start_date}-${gap.end_date}`}>
                {gap.start_date} to {gap.end_date} ({gap.days} {gap.days === 1 ? "day" : "days"})
              </li>
            ))}
            {coverage.gaps.items.length === 0 ? (
              <li>Coverage gaps exist; open the gap page.</li>
            ) : null}
          </ul>
          {coverage.gaps.has_more && coverage.gaps.next_page ? (
            <Link
              className="mt-2 inline-flex text-xs font-bold text-warning-ink underline underline-offset-4"
              href={reportingHref(state, { coveragePage: coverage.gaps.next_page })}
            >
              More uncovered ranges
            </Link>
          ) : null}
        </div>
      ) : (
        <p className="mt-2 text-xs font-semibold text-success-ink">
          The selected period is fully covered.
        </p>
      )}
      {report.coverage?.prior ? (
        <details className="mt-3 rounded-xl border border-line bg-page p-3">
          <summary className="cursor-pointer text-xs font-semibold text-ink">
            Comparison-period statement coverage
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
              <li key={`prior-${gap.start_date}-${gap.end_date}`}>
                {gap.start_date} to {gap.end_date} ({gap.days} {gap.days === 1 ? "day" : "days"})
              </li>
            ))}
            {report.coverage.prior.gaps.items.length === 0 && report.coverage.prior.partial ? (
              <li>Coverage gaps exist; open the comparison coverage page.</li>
            ) : null}
          </ul>
          {report.coverage.prior.gaps.has_more && report.coverage.prior.gaps.next_page ? (
            <Link
              className="mt-1 inline-flex text-xs font-bold text-accent-text underline underline-offset-4"
              href={reportingHref(state, {
                comparisonCoveragePage: report.coverage.prior.gaps.next_page,
              })}
            >
              More comparison uncovered ranges
            </Link>
          ) : null}
        </details>
      ) : null}
    </section>
  );
}

function CategoryBars({ report, state }: { report: ReportResponse; state: ReportingState }) {
  const breakdown = report.categories;
  if (!breakdown) {
    return null;
  }

  const direction = (name: "money_in" | "money_out", label: string) => {
    if (
      (state.direction === "in" && name === "money_out") ||
      (state.direction === "out" && name === "money_in")
    ) {
      return null;
    }
    const data = breakdown[name];
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
      <div className="rounded-[1.25rem] border border-line bg-surface p-4 card-shadow" key={name}>
        <h3 className="text-sm font-bold tracking-[0.1em] text-muted uppercase">{label}</h3>
        {ranks.length === 0 ? (
          <p className="mt-2 text-sm text-muted">No category activity in this direction.</p>
        ) : (
          <ul className="mt-2 space-y-2">
            {ranks.map((rank) => (
              <li key={rank.id}>
                <div className="flex items-baseline justify-between gap-3">
                  <Link
                    className="text-sm font-semibold text-ink underline-offset-4 hover:underline"
                    href={categoryDetailHref(state, rank.id, name === "money_in" ? "in" : "out")}
                  >
                    {rank.display_name}
                    {rank.is_archived ? " (archived)" : ""}
                  </Link>
                  <span className="text-sm font-semibold text-ink">
                    <Amount value={rank.amount} />
                  </span>
                </div>
                <div aria-hidden="true" className="category-share-track mt-1">
                  <div className="category-share-fill" style={percentStyle(rank.share)} />
                </div>
                <div className="mt-1 flex flex-wrap justify-between gap-2 text-xs text-muted">
                  <span>
                    {rank.count} {rank.count === 1 ? "transaction" : "transactions"}
                    {rank.share ? ` · ${rank.share}%` : ""}
                    {rank.change_percentage !== null
                      ? ` · ${rank.change_percentage}% vs prior`
                      : ""}
                  </span>
                  <Link
                    className="font-semibold text-accent-text underline underline-offset-4"
                    href={transactionHref(rank.descriptor, state)}
                  >
                    Open transactions
                  </Link>
                </div>
              </li>
            ))}
          </ul>
        )}
        <details className="mt-2 rounded-xl border border-line bg-page p-2">
          <summary className="cursor-pointer text-xs font-semibold text-ink">
            View {label.toLowerCase()} as a table
          </summary>
          <div className="mt-2 overflow-x-auto">
            <table className="w-full caption-bottom text-left text-xs">
              <caption className="sr-only">{label}, with the same values as the bars</caption>
              <thead>
                <tr className="text-muted">
                  <th scope="col">Category</th>
                  <th scope="col">Amount</th>
                  <th scope="col">Transactions</th>
                  <th scope="col">Share</th>
                </tr>
              </thead>
              <tbody>
                {ranks.map((rank) => (
                  <tr className="border-t border-line" key={`table-${name}-${rank.id}`}>
                    <th className="py-1 font-semibold text-ink" scope="row">
                      {rank.display_name}
                    </th>
                    <td className="py-1">
                      <Link
                        className="underline underline-offset-4"
                        href={categoryDetailHref(
                          state,
                          rank.id,
                          name === "money_in" ? "in" : "out",
                        )}
                      >
                        {formatMoney(rank.amount)}
                      </Link>
                    </td>
                    <td className="py-1">{rank.count}</td>
                    <td className="py-1">{rank.share ? `${rank.share}%` : "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      </div>
    );
  };

  return (
    <section aria-labelledby="category-breakdown-title" className="mt-5">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold text-ink" id="category-breakdown-title">
          Category breakdown
        </h2>
        <p className="text-xs text-muted">
          Categories are additive and reconcile to the headline for their direction, ordered from
          largest to smallest amount.
        </p>
      </div>
      <div className="mt-2 grid gap-3 lg:grid-cols-2">
        {direction("money_in", "Money in by category")}
        {direction("money_out", "Money out by category")}
      </div>
    </section>
  );
}

function TagBars({
  report,
  state,
  visual,
}: {
  report: ReportResponse;
  state: ReportingState;
  visual: boolean;
}) {
  const breakdown = report.tags;
  if (!breakdown) {
    return null;
  }

  const direction = (name: "money_in" | "money_out", label: string) => {
    if (
      (state.direction === "in" && name === "money_out") ||
      (state.direction === "out" && name === "money_in")
    ) {
      return null;
    }
    const data = breakdown[name];
    const ranks = (
      data.all_ranks && data.all_ranks.length > 0
        ? [...data.all_ranks]
        : [...data.ranks, ...data.other.items]
    ).sort(
      (left, right) =>
        compareMoneyDescending(left.amount, right.amount) || left.id.localeCompare(right.id),
    );

    return (
      <div className="rounded-[1.25rem] border border-line bg-surface p-4 card-shadow" key={name}>
        <h3 className="text-sm font-bold tracking-[0.1em] text-muted uppercase">{label}</h3>
        <ul className="mt-2 space-y-2">
          {ranks.map((rank) => (
            <li key={rank.id}>
              <div className="flex items-baseline justify-between gap-3">
                <Link
                  className="text-sm font-semibold text-ink underline-offset-4 hover:underline"
                  href={merchantTagDetailHref(state, rank.id, name === "money_in" ? "in" : "out")}
                >
                  {rank.display_name}
                  {rank.is_archived ? " (archived)" : ""}
                </Link>
                <span className="text-sm font-semibold text-ink">
                  <Amount value={rank.amount} />
                </span>
              </div>
              {visual ? (
                <div aria-hidden="true" className="mt-1 h-2 rounded-full bg-elevated">
                  <div className="h-2 rounded-full bg-accent" style={percentStyle(rank.share)} />
                </div>
              ) : null}
              <div className="mt-1 flex flex-wrap justify-between gap-2 text-xs text-muted">
                <span>
                  {rank.count} tagged {rank.count === 1 ? "transaction" : "transactions"}
                  {rank.prior_amount !== null
                    ? ` · prior ${formatMoney(rank.prior_amount)}${rank.change_percentage !== null ? ` (${rank.change_percentage}%)` : ""}`
                    : ""}
                </span>
                <Link
                  className="font-semibold text-accent-text underline underline-offset-4"
                  href={transactionHref(rank.descriptor, state)}
                >
                  Open tagged transactions
                </Link>
              </div>
            </li>
          ))}
          {ranks.length === 0 ? (
            <li className="text-sm text-muted">No tagged activity in this direction.</li>
          ) : null}
          <li className="rounded-xl border border-dashed border-line p-2">
            <div className="flex items-baseline justify-between gap-3">
              <Link
                className="text-sm font-semibold text-ink underline-offset-4 hover:underline"
                href={untaggedDetailHref(state, name === "money_in" ? "in" : "out")}
              >
                Untagged
              </Link>
              <span className="text-sm font-semibold text-ink">
                <Amount value={data.untagged.amount} />
              </span>
            </div>
            <p className="mt-1 text-xs text-muted">
              {data.untagged.count} untagged{" "}
              {data.untagged.count === 1 ? "transaction" : "transactions"} in this direction.
            </p>
          </li>
        </ul>
        <details className="mt-2 rounded-xl border border-line bg-page p-2">
          <summary className="cursor-pointer text-xs font-semibold text-ink">
            View {label.toLowerCase()} as a table
          </summary>
          <div className="mt-2 overflow-x-auto">
            <table className="w-full caption-bottom text-left text-xs">
              <caption className="sr-only">{label}, with overlap disclosure</caption>
              <thead>
                <tr className="text-muted">
                  <th scope="col">Tag</th>
                  <th scope="col">Amount</th>
                  <th scope="col">Tagged transactions</th>
                  <th scope="col">Share</th>
                </tr>
              </thead>
              <tbody>
                {ranks.map((rank) => (
                  <tr className="border-t border-line" key={`table-${name}-${rank.id}`}>
                    <th className="py-1 font-semibold text-ink" scope="row">
                      {rank.display_name}
                    </th>
                    <td className="py-1">
                      <Link
                        className="underline underline-offset-4"
                        href={merchantTagDetailHref(
                          state,
                          rank.id,
                          name === "money_in" ? "in" : "out",
                        )}
                      >
                        {formatMoney(rank.amount)}
                      </Link>
                    </td>
                    <td className="py-1">{rank.count}</td>
                    <td className="py-1">{rank.share ? `${rank.share}%` : "—"}</td>
                  </tr>
                ))}
                <tr className="border-t border-line">
                  <th className="py-1 font-semibold text-ink" scope="row">
                    Untagged
                  </th>
                  <td className="py-1">
                    <Link
                      className="underline underline-offset-4"
                      href={untaggedDetailHref(state, name === "money_in" ? "in" : "out")}
                    >
                      {formatMoney(data.untagged.amount)}
                    </Link>
                  </td>
                  <td className="py-1">{data.untagged.count}</td>
                  <td className="py-1">—</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p className="mt-1 text-xs text-muted">
            Tag amounts overlap and are never summed; each tag reconciles only to its own tagged
            transactions.
          </p>
        </details>
      </div>
    );
  };

  return (
    <section aria-labelledby="tag-breakdown-title" className="mt-5">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold text-ink" id="tag-breakdown-title">
          Merchant tags
        </h2>
        <p className="text-xs text-muted">Tag totals overlap; they never sum to a headline.</p>
      </div>
      <p className="mt-1 rounded-xl border border-suggestion-line bg-suggestion-soft p-2 text-xs text-suggestion-ink">
        A transaction may carry several tags, so tag amounts can overlap. Every tag is listed
        individually in descending amount order and reconciles only to the transactions carrying
        that tag.
      </p>
      <div className="mt-2 grid gap-3 lg:grid-cols-2">
        {direction("money_in", "Money in by tag")}
        {direction("money_out", "Money out by tag")}
      </div>
    </section>
  );
}

function HiddenAndExcluded({ report, state }: { report: ReportResponse; state: ReportingState }) {
  const totals = report.current;
  if (!totals) {
    return null;
  }

  return (
    <section
      className="mt-5 grid gap-3 lg:grid-cols-3"
      aria-label="Hidden, transfer and excluded diagnostics"
    >
      <div className="rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
        <h2 className="text-sm font-bold tracking-[0.1em] text-muted uppercase">
          Hidden transactions
        </h2>
        <p className="mt-2 text-sm text-ink">
          {totals.hidden.count} hidden · <Amount value={totals.hidden.absolute} /> gross
        </p>
        <p className="mt-1 text-xs leading-5 text-muted">
          Hidden transactions are excluded from every financial value. Parent-hidden rows are
          restored in Imports; individually hidden rows can be restored from the transaction list.
        </p>
        <Link
          className="mt-2 inline-flex text-xs font-semibold text-accent-text underline underline-offset-4"
          href={transactionHref(totals.hidden_descriptor, state)}
        >
          Open hidden transactions ({totals.hidden.count})
        </Link>
      </div>
      <div className="rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
        <h2 className="text-sm font-bold tracking-[0.1em] text-muted uppercase">Transfers</h2>
        <p className="mt-2 text-sm text-ink">
          {totals.transfers.count} · <Amount value={totals.transfers.absolute} /> gross
        </p>
        <p className="mt-1 text-xs leading-5 text-muted">
          Transfers are excluded from Money in, Money out and Net. They remain visible in their own
          list.
        </p>
        <Link
          className="mt-2 inline-flex text-xs font-semibold text-accent-text underline underline-offset-4"
          href={transactionHref(totals.transfers_descriptor, state)}
        >
          Open transfer transactions
        </Link>
      </div>
      <div className="rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
        <h2 className="text-sm font-bold tracking-[0.1em] text-muted uppercase">Excluded</h2>
        <p className="mt-2 text-sm text-ink">
          {totals.excluded.count} · <Amount value={totals.excluded.absolute} /> gross
        </p>
        <p className="mt-1 text-xs leading-5 text-muted">
          Excluded transactions are outside the headline financial values and remain available in
          their own list.
        </p>
        <Link
          className="mt-2 inline-flex text-xs font-semibold text-accent-text underline underline-offset-4"
          href={transactionHref(totals.excluded_descriptor, state)}
        >
          Open excluded transactions
        </Link>
      </div>
    </section>
  );
}

function ComparisonSummary({ report, state }: { report: ReportResponse; state: ReportingState }) {
  const comparison = report.comparison;
  if (!comparison) {
    return (
      <section className="mt-5 rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
        <h2 className="text-lg font-semibold text-ink">Comparison</h2>
        <p className="mt-1 text-sm text-muted">
          No comparison available. At least two confirmed salary-cycle starts, or an earlier
          representable period with included data, are needed.
        </p>
      </section>
    );
  }

  return (
    <section
      className="mt-5 rounded-[1.25rem] border border-line bg-surface p-4 card-shadow"
      aria-labelledby="comparison-summary-title"
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold text-ink" id="comparison-summary-title">
          Comparison
        </h2>
        <Link
          className="text-xs font-semibold text-accent-text underline underline-offset-4"
          href={comparisonHref(state)}
        >
          Open Period Comparison
        </Link>
      </div>
      {comparison.unavailable_reason || !comparison.period ? (
        <p className="mt-2 text-sm text-muted">
          No comparison available.{" "}
          {comparison.unavailable_reason === "insufficient_salary_boundaries"
            ? "At least two confirmed salary-cycle starts are needed."
            : comparison.unavailable_reason === "no_prior_data"
              ? "The comparison period has no included transactions."
              : comparison.unavailable_reason === "date_boundary"
                ? "The comparison range cannot be represented at this date boundary."
                : comparison.unavailable_reason === "no_current_data"
                  ? "The current period has no included transactions."
                  : "The comparison period could not be defined."}
        </p>
      ) : (
        <div className="mt-2 grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
          <div className="text-sm text-muted">
            <p className="font-semibold text-ink">{comparison.period.label}</p>
            <p>
              {comparison.period.start_date} to {comparison.period.end_date}
            </p>
            {comparison.shorter_prior ? (
              <p className="text-xs">The prior cycle is shorter; the range is capped at its end.</p>
            ) : null}
          </div>
          {comparison.has_included_data && report.prior ? (
            <>
              <div className="text-sm text-muted">
                <p className="font-semibold text-ink">Prior Money in</p>
                <p>
                  <Amount value={report.prior.money.money_in} />
                </p>
              </div>
              <div className="text-sm text-muted">
                <p className="font-semibold text-ink">Prior Money out</p>
                <p>
                  <Amount value={report.prior.money.money_out} />
                </p>
              </div>
              <div className="text-sm text-muted">
                <p className="font-semibold text-ink">Prior Net</p>
                <p>
                  <Amount value={report.prior.money.net} />
                </p>
              </div>
            </>
          ) : null}
        </div>
      )}
    </section>
  );
}

function MetricDictionary() {
  return (
    <details className="mt-6 rounded-[1.25rem] border border-line bg-surface p-4 card-shadow">
      <summary className="cursor-pointer text-sm font-bold text-ink">
        Metric dictionary and data policy
      </summary>
      <dl className="mt-3 grid gap-2 text-xs leading-5 text-muted sm:grid-cols-2">
        <div>
          <dt className="font-semibold text-ink">Money in</dt>
          <dd>Gross positive visible included amounts.</dd>
        </div>
        <div>
          <dt className="font-semibold text-ink">Money out</dt>
          <dd>Gross magnitude of visible included negative amounts.</dd>
        </div>
        <div>
          <dt className="font-semibold text-ink">Net</dt>
          <dd>Money in minus Money out; neutral, not a health judgement.</dd>
        </div>
        <div>
          <dt className="font-semibold text-ink">Category totals</dt>
          <dd>Additive; each included transaction appears in one directional category bucket.</dd>
        </div>
        <div>
          <dt className="font-semibold text-ink">Tag totals</dt>
          <dd>
            Overlapping; a transaction with several tags counts once per tag, never summed into a
            headline.
          </dd>
        </div>
        <div>
          <dt className="font-semibold text-ink">Coverage</dt>
          <dd>
            Union of committed non-hidden statement dates for the selected account(s), independent
            of category and tag filters.
          </dd>
        </div>
        <div>
          <dt className="font-semibold text-ink">Hidden</dt>
          <dd>
            Excluded from every financial value; the count and gross values are diagnostics with a
            restore path.
          </dd>
        </div>
        <div>
          <dt className="font-semibold text-ink">Salary dates</dt>
          <dd>
            Explicitly confirmed reporting metadata. Source transaction fields are never changed.
          </dd>
        </div>
      </dl>
      <p className="mt-3 text-xs text-muted">
        This is a live view. A later navigation or refresh is a new ledger snapshot; a concurrent
        change may explain a different drill-down total.
      </p>
    </details>
  );
}

export default function OverviewWorkspace({
  state,
  report,
  accounts,
  categories,
  tags,
  starts,
  startsError = null,
  error,
}: OverviewWorkspaceProps) {
  return (
    <TrendRangeProvider>
      <div>
        <ViewTabs state={state} />
        {error ? <ErrorPanel message={error} state={state} /> : null}
        {startsError ? (
          <p
            className="mt-4 rounded-xl border border-warning-line bg-warning-soft p-3 text-sm text-warning-ink"
            role="alert"
          >
            {startsError}
          </p>
        ) : null}
        {report.category_trend ? (
          <CategoryTrendChart state={state} trend={report.category_trend} />
        ) : null}
        <PeriodControls
          accounts={accounts}
          categories={categories}
          report={report}
          starts={starts}
          state={state}
          tags={tags}
        />
        {report.status === "ready" && report.current ? (
          <>
            <PeriodHeading report={report} />
            <CoveragePanel report={report} state={state} />
            <MoneyCards report={report} state={state} />
            <HiddenAndExcluded report={report} state={state} />
            <ComparisonSummary report={report} state={state} />
            <CategoryBars report={report} state={state} />
            <TagBars report={report} state={state} visual={state.view === "charts"} />
            <TrendPanel report={report} state={state} />
          </>
        ) : (
          <>
            {report.period ? <PeriodHeading report={report} /> : null}
            <StatusPanel report={report} state={state} />
            {report.current ? (
              <>
                <CoveragePanel report={report} state={state} />
                <MoneyCards report={report} state={state} />
                <HiddenAndExcluded report={report} state={state} />
                <ComparisonSummary report={report} state={state} />
              </>
            ) : null}
          </>
        )}
        <MetricDictionary />
      </div>
    </TrendRangeProvider>
  );
}
