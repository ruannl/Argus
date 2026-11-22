"use client";

import Link from "next/link";
import { useState } from "react";

import type {
  ReportCategoryTrend,
  ReportCategoryTrendCategory,
  ReportCategoryTrendMonth,
} from "@/lib/server/backend-client";
import type { ReportingState } from "@/lib/reporting-state";
import { compareMoneyDescending, formatMoney, sumMoney } from "@/lib/money-format";

import { transactionHref } from "./reporting-links";

type TrendRange = 2 | 3 | 6;

type SelectedCategory = ReportCategoryTrendCategory & {
  selectedMonths: ReportCategoryTrendCategory["months"];
  selectedTotal: string;
  color: string;
};

const RANGES: TrendRange[] = [2, 3, 6];

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

function rangeLabel(range: TrendRange): string {
  return `${range} months`;
}

function categoryLabel(category: SelectedCategory): string {
  return category.is_archived ? `${category.display_name} (archived)` : category.display_name;
}

export default function CategoryTrendChart({
  trend,
  state,
}: {
  trend: ReportCategoryTrend;
  state: ReportingState;
}) {
  const [range, setRange] = useState<TrendRange>(3);
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
            Included Money out by category for the last {rangeLabel(range)}. Each month groups one
            bar per category. This chart uses the selected account only; other Overview filters do
            not narrow it.
          </p>
        </div>
        <div aria-label="Category trend range" className="flex flex-wrap gap-1" role="group">
          {RANGES.map((option) => (
            <button
              aria-pressed={range === option}
              className="rounded-full border border-line px-3 py-1.5 text-xs font-semibold text-ink"
              key={option}
              onClick={() => setRange(option)}
              type="button"
            >
              {option} months
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
            coverage label on each month says what the statements cover. Exact values and
            transaction links are in the table.
          </p>

          <details className="mt-3 rounded-xl border border-line bg-page p-3">
            <summary className="cursor-pointer text-sm font-semibold text-ink">
              Category spending as a table
            </summary>
            <div className="mt-2 overflow-x-auto">
              <table className="w-full min-w-[38rem] caption-bottom text-left text-xs">
                <caption className="sr-only">
                  Included Money out by category and month, with exact ZAR values and statement
                  coverage.
                </caption>
                <thead>
                  <tr className="text-muted">
                    <th scope="col">Category</th>
                    {months.map((month) => (
                      <th className="text-right" key={month.start_date} scope="col">
                        {month.label}
                        {month.is_partial ? " · partial" : ""}
                      </th>
                    ))}
                    <th className="text-right" scope="col">
                      Total
                    </th>
                  </tr>
                  <tr className="text-muted">
                    <th className="font-normal" scope="col">
                      Coverage
                    </th>
                    {months.map((month) => (
                      <th
                        className="text-right font-normal"
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
                </thead>
                <tbody>
                  {categories.map((category) => (
                    <tr className="border-t border-line" key={`table-${category.id}`}>
                      <th className="py-1 font-semibold text-ink" scope="row">
                        {categoryLabel(category)}
                      </th>
                      {category.selectedMonths.map((month) => (
                        <td
                          className="py-1 text-right tabular-nums"
                          key={`${category.id}-${month.descriptor.start_date}`}
                        >
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
                        </td>
                      ))}
                      <td className="py-1 text-right font-semibold tabular-nums text-ink">
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
          No recorded included Money out in the last {rangeLabel(range)}. An absent bar is not a
          claim of zero spending where statement coverage is missing.
        </p>
      )}
    </section>
  );
}
