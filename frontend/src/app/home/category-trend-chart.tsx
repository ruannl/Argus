"use client";

import Link from "next/link";

import type {
  ReportCategoryTrend,
  ReportCategoryTrendCategory,
  ReportCategoryTrendChange,
  ReportCategoryTrendComparison,
  ReportCategoryTrendMonth,
} from "@/lib/server/backend-client";
import type { ReportingState } from "@/lib/reporting-state";
import {
  compareMoneyDescending,
  formatMoney,
  formatPercentage,
  sumMoney,
} from "@/lib/money-format";

import { transactionHref } from "./reporting-links";
import { TREND_RANGES, useTrendRange, type TrendRange } from "./trend-range";

type SelectedCategory = ReportCategoryTrendCategory & {
  selectedMonths: ReportCategoryTrendCategory["months"];
  selectedTotal: string;
  color: string;
};

const COVERAGE_LABELS: Record<ReportCategoryTrendMonth["coverage"], string> = {
  covered: "Covered",
  partial: "Partial coverage",
  uncovered: "No statement coverage",
};

/**
 * Eight distinguishable series colours, defined per theme in the finance
 * stylesheet. Categories beyond the eighth repeat the palette in a fixed
 * order; the legend, tooltips and table keep each category separately named.
 */
const SERIES_COLORS = [
  "var(--chart-1)",
  "var(--chart-2)",
  "var(--chart-3)",
  "var(--chart-4)",
  "var(--chart-5)",
  "var(--chart-6)",
  "var(--chart-7)",
  "var(--chart-8)",
];

function centsOf(value: string): bigint | null {
  const match = /^(-?)(\d+)(?:\.(\d{1,2}))?$/.exec(value.trim());
  if (!match) {
    return null;
  }
  const [, sign, whole, fraction = "0"] = match;
  const cents = BigInt(whole) * BigInt(100) + BigInt(fraction.padEnd(2, "0").slice(0, 2));

  return sign === "-" ? -cents : cents;
}

export function barHeight(amount: string, maximum: string): string {
  const value = centsOf(amount);
  const max = centsOf(maximum);
  if (value === null || max === null || max <= BigInt(0)) {
    return "0%";
  }
  const basisPoints = (value * BigInt(10_000)) / max;
  const clamped =
    basisPoints < BigInt(0)
      ? BigInt(0)
      : basisPoints > BigInt(10_000)
        ? BigInt(10_000)
        : basisPoints;

  return `${Number(clamped) / 100}%`;
}

function selectedCategories(trend: ReportCategoryTrend, monthCount: number): SelectedCategory[] {
  const offset = Math.max(0, trend.months.length - monthCount);
  return trend.categories
    .map((category, index) => {
      const selectedMonths = category.months.slice(offset);
      return {
        ...category,
        selectedMonths,
        selectedTotal: sumMoney(selectedMonths.map((month) => month.amount)),
        color: SERIES_COLORS[index % SERIES_COLORS.length],
      };
    })
    .filter((category) => category.selectedMonths.some((month) => month.count > 0));
}

function rangeLabel(range: TrendRange, salaryCycles: boolean): string {
  return `${range} ${salaryCycles ? "salary cycles" : "months"}`;
}

function categoryLabel(category: SelectedCategory): string {
  return category.is_archived ? `${category.display_name} (archived)` : category.display_name;
}

const SHORT_MONTHS = [
  "Jan",
  "Feb",
  "Mar",
  "Apr",
  "May",
  "Jun",
  "Jul",
  "Aug",
  "Sep",
  "Oct",
  "Nov",
  "Dec",
] as const;

function periodRange(start: string, end: string): string {
  const parts = (value: string) => {
    const [year, month, day] = value.split("-").map(Number);
    return { year, month, day };
  };
  const from = parts(start);
  const to = parts(end);
  const startMonth = SHORT_MONTHS[from.month - 1];
  const endMonth = SHORT_MONTHS[to.month - 1];
  if (from.year === to.year && from.month === to.month) {
    return `${from.day}–${to.day} ${endMonth} ${to.year}`;
  }
  if (from.year === to.year) {
    return `${from.day} ${startMonth} – ${to.day} ${endMonth} ${to.year}`;
  }
  return `${from.day} ${startMonth} ${from.year} – ${to.day} ${endMonth} ${to.year}`;
}

function ComparisonHeading({ comparison }: { comparison?: ReportCategoryTrendComparison }) {
  if (!comparison) {
    return <span>No comparison available</span>;
  }
  const { current_period: current, prior_period: prior } = comparison;
  if (!prior) {
    return (
      <span>
        {comparison.unavailable_reason === "insufficient_salary_boundaries"
          ? "No previous confirmed cycle"
          : "No previous period to compare"}
      </span>
    );
  }

  const missingFrom =
    comparison.current_coverage !== "covered" && comparison.prior_coverage !== "covered"
      ? "both periods"
      : comparison.current_coverage !== "covered"
        ? "the current period"
        : "the previous period";
  return (
    <>
      <span className="block">{`vs ${periodRange(prior.start_date, prior.end_date)}`}</span>
      {current?.is_partial ? (
        <>
          <span className="block">{`To date: ${periodRange(current.start_date, current.end_date)}`}</span>
          {current.is_open_salary_cycle ? (
            <span className="block">Open cycle through today vs full previous cycle.</span>
          ) : (
            <span className="block">
              {comparison.shorter_prior
                ? "Previous month capped at its last day."
                : "Same elapsed days"}
            </span>
          )}
        </>
      ) : null}
      {comparison.availability === "incomplete_coverage" ? (
        <span className="block font-medium text-ink">{`Missing coverage: ${missingFrom}`}</span>
      ) : comparison.unavailable_reason === "no_prior_data" ? (
        <span className="block font-medium text-ink">
          No included transactions in the previous period.
        </span>
      ) : null}
    </>
  );
}

function ChangeValue({
  change,
  comparison,
}: {
  change?: ReportCategoryTrendChange;
  comparison?: ReportCategoryTrendComparison;
}) {
  // Old backend responses can omit either new object. Do not derive a substitute
  // from the full-period amounts, which may use different dates.
  const state =
    !change || !comparison
      ? "unavailable"
      : comparison.availability === "available"
        ? change.state
        : comparison.availability;
  const percentage = formatPercentage(change?.change_percentage ?? null);
  if (percentage !== null && (state === "increase" || state === "decrease")) {
    const increased = state === "increase";
    return (
      <span
        className={`whitespace-nowrap font-semibold ${increased ? "text-chart-in" : "text-chart-out"}`}
      >
        <span className="sr-only">{increased ? "Spending increased" : "Spending decreased"} </span>
        <span aria-hidden="true">{increased ? "↑" : "↓"}</span>{" "}
        <span>{`${increased ? "+" : "−"}${percentage.replace(/^-/, "")}`}</span>
      </span>
    );
  }
  if (percentage !== null && state === "unchanged") {
    return (
      <span className="font-semibold text-ink">
        <span className="sr-only">Spending unchanged </span>
        <span>{percentage}</span>
      </span>
    );
  }

  if (state === "new_spending" || state === "no_spending") {
    return (
      <span className="font-medium text-ink">
        {state === "new_spending" ? "New spending" : "No spending"}
      </span>
    );
  }

  return (
    <span className="text-muted">
      <span aria-hidden="true">—</span>
      <span className="sr-only">No comparison available; see period heading</span>
    </span>
  );
}

function PeriodChange({
  comparison,
  change,
  label,
  firstDisplayed,
}: {
  comparison?: ReportCategoryTrendComparison;
  change?: ReportCategoryTrendChange;
  label: string;
  firstDisplayed: boolean;
}) {
  const current = change?.current_amount;
  const prior = change?.prior_amount;
  const isMatchedMonth =
    comparison?.current_period?.mode === "calendar" && comparison.current_period.is_partial;

  return (
    <div className="mt-1">
      <ChangeValue change={change} comparison={comparison} />
      {(firstDisplayed || isMatchedMonth) &&
      comparison?.prior_period &&
      current != null &&
      prior != null ? (
        <details className="mt-1 text-muted">
          <summary className="cursor-pointer underline underline-offset-2">
            Compared amounts<span className="sr-only"> for {label}</span>
          </summary>
          <dl className="mt-1">
            <dt>Current slice</dt>
            <dd className="font-semibold whitespace-nowrap text-ink">{formatMoney(current)}</dd>
            <dt>Previous slice</dt>
            <dd className="font-semibold whitespace-nowrap text-ink">{formatMoney(prior)}</dd>
          </dl>
        </details>
      ) : null}
    </div>
  );
}

export default function CategoryTrendChart({
  trend,
  state,
}: {
  trend: ReportCategoryTrend;
  state: ReportingState;
}) {
  const { range, setRange } = useTrendRange();
  const salaryCycles = state.mode === "salary_cycle";
  const monthCount = Math.min(range, trend.months.length);
  const months = trend.months.slice(-monthCount);
  const categories = selectedCategories(trend, monthCount);
  const maximum = categories.reduce(
    (current, category) =>
      category.selectedMonths.reduce(
        (amount, month) =>
          compareMoneyDescending(month.amount, amount) < 0 ? month.amount : amount,
        current,
      ),
    "0.00",
  );
  const hasData = categories.length > 0;

  return (
    <section
      aria-labelledby="category-trend-title"
      className="mt-5 rounded-[1.25rem] border border-line bg-surface p-4 card-shadow"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold text-ink" id="category-trend-title">
            Category spending trend
          </h2>
          <p className="mt-1 text-xs leading-5 text-muted">
            Included Money out by category for the last {rangeLabel(range, salaryCycles)}.{" "}
            {salaryCycles
              ? "Each salary cycle groups one bar per category."
              : "Each month groups one bar per category."}{" "}
            This chart uses the selected account only; other Overview filters do not narrow it.
          </p>
        </div>
        <div aria-label="Category trend range" className="flex flex-wrap gap-1" role="group">
          {TREND_RANGES.map((option) => (
            <button
              aria-pressed={range === option}
              className="rounded-full border border-line px-3 py-1.5 text-xs font-semibold text-ink"
              key={option}
              onClick={() => setRange(option)}
              type="button"
            >
              {option} {salaryCycles ? "salary cycles" : "months"}
            </button>
          ))}
        </div>
      </div>

      <div className="mt-3 flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted">
        {months.map((month) => (
          <span key={`${month.start_date}-${month.end_date}`}>
            {month.label}: {COVERAGE_LABELS[month.coverage]}
          </span>
        ))}
      </div>

      {hasData ? (
        <>
          <div className="mt-3 overflow-x-auto">
            <div
              aria-hidden="true"
              className="border-b border-line pb-1"
              style={{ minWidth: `${months.length * 4.5}rem` }}
            >
              <div
                className="grid items-end gap-2"
                style={{ gridTemplateColumns: `repeat(${months.length}, minmax(0, 1fr))` }}
              >
                {months.map((month, monthIndex) => (
                  <div key={month.start_date}>
                    <div className="flex h-48 items-end justify-center gap-[2px]">
                      {categories.map((category) => {
                        const cell = category.selectedMonths[monthIndex];
                        return (
                          <span
                            className="w-full max-w-6 flex-1"
                            key={`${category.id}-${month.start_date}`}
                            style={{
                              backgroundColor: category.color,
                              height: barHeight(cell.amount, maximum),
                            }}
                            title={`${month.label} · ${categoryLabel(category)}: ${formatMoney(cell.amount)}`}
                          />
                        );
                      })}
                    </div>
                    <p className="mt-1 truncate text-center text-[10px] leading-4 text-muted">
                      {month.label}
                      {month.is_partial ? " · partial" : ""}
                    </p>
                  </div>
                ))}
              </div>
            </div>
          </div>

          <ul className="mt-3 flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted">
            {categories.map((category) => (
              <li className="inline-flex items-center gap-1.5" key={`legend-${category.id}`}>
                <span
                  aria-hidden="true"
                  className="h-2.5 w-2.5 shrink-0 rounded-sm"
                  style={{ backgroundColor: category.color }}
                />
                <span>{categoryLabel(category)}</span>
                <span className="font-semibold tabular-nums text-ink">
                  {formatMoney(category.selectedTotal)}
                </span>
              </li>
            ))}
          </ul>

          <p className="mt-3 text-xs leading-5 text-muted">
            Bars share one scale from zero. Missing statement dates are not zero spending; the
            coverage label on each period says what the statements cover. Exact values and
            transaction links are in the table.
          </p>

          <details className="mt-3 rounded-xl border border-line bg-page p-3">
            <summary className="cursor-pointer text-sm font-semibold text-ink">
              Category spending as a table
            </summary>
            <p className="mt-2 text-xs leading-5 text-muted">
              Below each amount, change compares that period with the one before it. Green up means
              spending increased; red down means it decreased. A dash means no comparison; see the
              explanation at the top of that column.
            </p>
            <div
              aria-label="Scrollable category spending table"
              className="mt-2 overflow-x-auto focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-accent-text"
              role="region"
              tabIndex={0}
            >
              <table className="w-full min-w-[38rem] caption-bottom text-left text-xs">
                <caption className="sr-only">
                  Included Money out by category and {salaryCycles ? "salary cycle" : "month"}, with
                  exact ZAR values, statement coverage and change from the previous period.
                </caption>
                <thead>
                  <tr className="text-muted">
                    <th className="min-w-36 pr-3" scope="col">
                      Category
                    </th>
                    {months.map((month) => (
                      <th className="min-w-36 px-2 text-right" key={month.start_date} scope="col">
                        {month.label}
                        {month.is_partial ? " · partial" : ""}
                      </th>
                    ))}
                    <th className="min-w-24 pl-2 text-right" scope="col">
                      Total
                    </th>
                  </tr>
                  <tr className="text-muted">
                    <th className="font-normal" scope="col">
                      Coverage
                    </th>
                    {months.map((month) => (
                      <th
                        className="px-1 text-right font-normal"
                        key={`coverage-${month.start_date}`}
                        scope="col"
                      >
                        {COVERAGE_LABELS[month.coverage]}
                      </th>
                    ))}
                    <th className="text-right font-normal" scope="col">
                      —
                    </th>
                  </tr>
                  <tr className="text-muted">
                    <th className="py-1 pr-3 font-normal align-top" scope="row">
                      Change vs previous period
                    </th>
                    {months.map((month) => (
                      <th
                        className="px-2 py-1 text-right font-normal align-top"
                        key={`comparison-${month.start_date}`}
                        scope="col"
                      >
                        <ComparisonHeading comparison={month.comparison} />
                      </th>
                    ))}
                    <th className="text-right font-normal" scope="col">
                      —
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {categories.map((category) => (
                    <tr className="border-t border-line" key={`table-${category.id}`}>
                      <th className="py-1 pr-3 font-semibold text-ink" scope="row">
                        {categoryLabel(category)}
                      </th>
                      {category.selectedMonths.map((month, index) => (
                        <td
                          className="px-2 py-1 text-right tabular-nums align-top"
                          key={`${category.id}-${month.descriptor.start_date}`}
                        >
                          <div className="category-trend-exact-amount">
                            {month.count > 0 ? (
                              <Link
                                className="underline underline-offset-4"
                                href={transactionHref(month.descriptor, state)}
                              >
                                {formatMoney(month.amount)}
                              </Link>
                            ) : (
                              formatMoney(month.amount)
                            )}
                          </div>
                          <PeriodChange
                            change={month.change}
                            comparison={months[index]?.comparison}
                            firstDisplayed={index === 0}
                            label={`${categoryLabel(category)}, ${months[index]?.label}`}
                          />
                        </td>
                      ))}
                      <td className="category-trend-exact-amount py-1 pl-2 text-right font-semibold tabular-nums text-ink">
                        {formatMoney(category.selectedTotal)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </details>
        </>
      ) : (
        <p className="mt-3 rounded-xl border border-dashed border-line bg-page p-3 text-sm text-muted">
          No recorded included Money out in the last {rangeLabel(range, salaryCycles)}. An absent
          bar is not a claim of zero spending where statement coverage is missing.
        </p>
      )}
    </section>
  );
}
