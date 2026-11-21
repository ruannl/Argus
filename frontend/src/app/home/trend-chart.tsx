import type { ReportBucket } from "@/lib/server/backend-client";
import { compareMoneyDescending, formatMoney } from "@/lib/money-format";

/**
 * Largest single bucket amount. Every trend bar is drawn against this one
 * value so heights compare across buckets rather than within a card.
 */
export function trendMaximum(buckets: ReportBucket[]): string {
  let maximum = "0.00";
  for (const bucket of buckets) {
    if (compareMoneyDescending(bucket.money_in, maximum) < 0) {
      maximum = bucket.money_in;
    }
    if (compareMoneyDescending(bucket.money_out, maximum) < 0) {
      maximum = bucket.money_out;
    }
  }
  return maximum;
}

/** Inclusive day count for a bucket, so clipped edge buckets are visible. */
export function bucketDays(start: string, end: string): number {
  const [startYear, startMonth, startDay] = start.split("-").map(Number);
  const [endYear, endMonth, endDay] = end.split("-").map(Number);
  const elapsed =
    Date.UTC(endYear, endMonth - 1, endDay) - Date.UTC(startYear, startMonth - 1, startDay);
  return Math.round(elapsed / 86_400_000) + 1;
}

function heightStyle(share: string | null): { height: string } {
  if (!share) {
    return { height: "0%" };
  }
  const value = Number(share);
  const bounded = Number.isFinite(value) ? Math.max(0, Math.min(100, value)) : 0;
  return { height: `${bounded}%` };
}

export function TrendLegend() {
  return (
    <div className="mt-2 flex flex-wrap items-center gap-4 text-xs text-muted">
      <span className="inline-flex items-center gap-1.5">
        <span aria-hidden="true" className="h-2.5 w-2.5 rounded-sm bg-accent" />
        Money in
      </span>
      <span className="inline-flex items-center gap-1.5">
        <span aria-hidden="true" className="h-2.5 w-2.5 rounded-sm bg-chart-out" />
        Money out
      </span>
    </div>
  );
}

/**
 * One bucket's in/out pair on a labelled zero-based axis. The plot is
 * decorative; the trend table carries the same values for assistive use.
 */
export function TrendBarPlot({
  bucket,
  maximum,
  heightClassName,
}: {
  bucket: ReportBucket;
  maximum: string;
  heightClassName: string;
}) {
  const hasScale = compareMoneyDescending(maximum, "0.00") < 0;

  return (
    <div aria-hidden="true" className="mt-2 flex gap-2">
      <div
        className={`flex w-20 shrink-0 flex-col justify-between text-right text-[10px] leading-none text-muted tabular-nums ${heightClassName}`}
      >
        <span>{hasScale ? formatMoney(maximum) : ""}</span>
        <span>{formatMoney("0.00")}</span>
      </div>
      <div className={`relative flex-1 ${heightClassName}`}>
        <div className="absolute inset-x-0 top-0 border-t border-dashed border-line" />
        <div className="absolute inset-x-0 top-1/2 border-t border-dashed border-line" />
        <div className="absolute inset-x-0 bottom-0 border-t border-line" />
        <div className="absolute inset-0 flex items-end gap-2">
          <div className="w-1/2 rounded-t bg-accent" style={heightStyle(bucket.money_in_share)} />
          <div
            className="w-1/2 rounded-t bg-chart-out"
            style={heightStyle(bucket.money_out_share)}
          />
        </div>
      </div>
    </div>
  );
}
