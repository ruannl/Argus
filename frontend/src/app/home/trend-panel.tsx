"use client";

import Link from "next/link";

import type { ReportResponse } from "@/lib/server/backend-client";
import type { ReportingState } from "@/lib/reporting-state";
import { formatMoney } from "@/lib/money-format";

import { transactionHref } from "./reporting-links";
import { bucketDays, TrendBarPlot, TrendLegend, trendMaximum } from "./trend-chart";
import { useTrendRange } from "./trend-range";

export default function TrendPanel({
  report,
  state,
}: {
  report: ReportResponse;
  state: ReportingState;
}) {
  const { range } = useTrendRange();
  const cycleBuckets = report.salary_cycle_trend ?? [];
  const salaryCycles = state.mode === "salary_cycle" && cycleBuckets.length > 0;
  const buckets = salaryCycles ? cycleBuckets.slice(-range) : (report.trend ?? []);
  if (buckets.length === 0) {
    return null;
  }

  const maximum = trendMaximum(buckets);
  const cycleCount = Math.min(range, cycleBuckets.length);

  return (
    <section
      aria-labelledby="trend-title"
      className="mt-5 rounded-[1.25rem] border border-line bg-surface p-4 card-shadow"
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold text-ink" id="trend-title">
          Trend
        </h2>
        <p className="text-xs text-muted">
          Bars share one scale from zero, so heights compare across buckets. The table carries the
          same values.
        </p>
      </div>
      {salaryCycles ? (
        <p className="mt-1 text-xs text-muted">
          One bucket per salary cycle; this uses the same last {cycleCount} cycles as the Category
          spending trend.
        </p>
      ) : null}
      <TrendLegend />
      <div className="mt-3 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {buckets.map((bucket) => {
          const days = bucketDays(bucket.start_date, bucket.end_date);
          return (
            <div
              className="rounded-xl border border-line bg-page p-2"
              key={`${bucket.start_date}-${bucket.end_date}`}
            >
              <p className="text-xs font-semibold text-muted">
                {bucket.label} · {days} {days === 1 ? "day" : "days"}
              </p>
              <TrendBarPlot bucket={bucket} heightClassName="h-24" maximum={maximum} />
              <div className="mt-2 space-y-1 text-xs">
                <Link
                  className="block font-semibold text-ink underline-offset-4 hover:underline"
                  href={transactionHref(bucket.money_in_descriptor, state)}
                >
                  In <Amount value={bucket.money_in} /> · {bucket.start_date} to {bucket.end_date}
                </Link>
                <Link
                  className="block font-semibold text-ink underline-offset-4 hover:underline"
                  href={transactionHref(bucket.money_out_descriptor, state)}
                >
                  Out <Amount value={bucket.money_out} /> · {bucket.start_date} to {bucket.end_date}
                </Link>
                <p className="text-muted">
                  {bucket.count} {bucket.count === 1 ? "transaction" : "transactions"} ·{" "}
                  {bucket.coverage} statement coverage
                </p>
              </div>
            </div>
          );
        })}
      </div>
      <details className="mt-3 rounded-xl border border-line bg-page p-3">
        <summary className="cursor-pointer text-sm font-semibold text-ink">
          Trend as a table
        </summary>
        <div className="mt-2 overflow-x-auto">
          <table className="w-full caption-bottom text-left text-xs">
            <caption className="sr-only">Money in and money out by trend bucket</caption>
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
                <tr
                  className="border-t border-line"
                  key={`table-${bucket.start_date}-${bucket.end_date}`}
                >
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
      </details>
    </section>
  );
}

function Amount({ value }: { value: string }) {
  return <span className="tabular-nums">{formatMoney(value)}</span>;
}
