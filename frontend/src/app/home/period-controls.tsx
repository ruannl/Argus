import Link from "next/link";

import type {
  Category,
  FinancialAccount,
  MerchantTag,
  ReportResponse,
  SalaryCycleBoundary,
} from "@/lib/server/backend-client";
import { reportingHref } from "@/lib/reporting-state";
import FilterForm from "./filter-form";
import type { ReportingState } from "@/lib/reporting-state";

type PeriodControlsProps = {
  state: ReportingState;
  report: ReportResponse;
  accounts: FinancialAccount[];
  categories: Category[];
  tags: MerchantTag[];
  starts: SalaryCycleBoundary[];
};

function HiddenPeriod({
  state,
  includePeriod = true,
  includeCustomDates = true,
  includeCycleStart = true,
}: {
  state: ReportingState;
  includePeriod?: boolean;
  includeCustomDates?: boolean;
  includeCycleStart?: boolean;
}) {
  return (
    <>
      {includePeriod && state.mode !== "salary_cycle" ? (
        <input name="mode" type="hidden" value={state.mode} />
      ) : null}
      {includePeriod && state.mode === "calendar" && state.preset ? (
        <input name="preset" type="hidden" value={state.preset} />
      ) : null}
      {includePeriod && state.mode === "calendar" && state.preset === "month" && state.month ? (
        <input name="month" type="hidden" value={state.month} />
      ) : null}
      {includePeriod && state.mode === "custom" && includeCustomDates ? (
        <>
          <input name="start_date" type="hidden" value={state.start} />
          <input name="end_date" type="hidden" value={state.end} />
        </>
      ) : null}
      {includePeriod && state.mode === "salary_cycle" && state.cycleStartId && includeCycleStart ? (
        <input name="cycle_start_id" type="hidden" value={state.cycleStartId} />
      ) : null}
      {state.view === "charts" ? <input name="view" type="hidden" value="charts" /> : null}
      {state.account ? <input name="account" type="hidden" value={state.account} /> : null}
      {state.category ? <input name="category" type="hidden" value={state.category} /> : null}
      {state.merchantTag ? (
        <input name="merchant_tag" type="hidden" value={state.merchantTag} />
      ) : null}
      {state.direction ? <input name="direction" type="hidden" value={state.direction} /> : null}
    </>
  );
}

const PRESETS = [
  ["month_to_date", "Month to date"],
  ["previous_month", "Previous month"],
  ["last_three_months", "Last 3 months"],
  ["last_six_months", "Last 6 months"],
] as const;

export default function PeriodControls({
  state,
  report,
  accounts,
  categories,
  tags,
  starts,
}: PeriodControlsProps) {
  const activeAccount = accounts.find((account) => account.id === state.account);
  const activeCategory = categories.find((category) => category.id === state.category);
  const activeTag = tags.find((tag) => tag.id === state.merchantTag);

  return (
    <section aria-labelledby="period-controls-title" className="mt-6 space-y-4">
      <h2 className="sr-only" id="period-controls-title">
        Reporting period and filters
      </h2>

      <div className="finance-unboxed">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-xs font-bold tracking-[0.14em] text-muted uppercase">Period</span>
          <Link
            aria-current={state.mode === "salary_cycle" ? "page" : undefined}
            className={
              state.mode === "salary_cycle"
                ? "rounded-full bg-ink px-4 py-2 text-sm font-semibold text-page"
                : "rounded-full border border-line px-4 py-2 text-sm font-semibold text-ink transition hover:bg-elevated"
            }
            href={reportingHref({
              ...state,
              mode: "salary_cycle",
              preset: undefined,
              month: undefined,
              start: undefined,
              end: undefined,
            })}
          >
            Salary cycle
          </Link>
          {PRESETS.map(([preset, label]) => (
            <Link
              aria-current={
                state.mode === "calendar" && state.preset === preset ? "page" : undefined
              }
              className={
                state.mode === "calendar" && state.preset === preset
                  ? "rounded-full bg-ink px-4 py-2 text-sm font-semibold text-page"
                  : "rounded-full border border-line px-4 py-2 text-sm font-semibold text-ink transition hover:bg-elevated"
              }
              href={reportingHref({
                ...state,
                mode: "calendar",
                preset,
                month: undefined,
                start: undefined,
                end: undefined,
                cycleStartId: undefined,
              })}
              key={preset}
            >
              {label}
            </Link>
          ))}
        </div>
        <form action="/home" className="mt-3 flex flex-wrap items-end gap-2" method="get">
          <HiddenPeriod includePeriod={false} state={state} />
          <input name="mode" type="hidden" value="calendar" />
          <input name="preset" type="hidden" value="month" />
          <label className="text-sm font-semibold text-ink" htmlFor="calendar-month-select">
            Calendar month
          </label>
          <input
            className="min-h-10 rounded-xl border border-line bg-page px-3 text-sm text-ink"
            defaultValue={
              state.mode === "calendar" && state.preset === "month"
                ? state.month
                : report.today.slice(0, 7)
            }
            id="calendar-month-select"
            name="month"
            type="month"
          />
          <button
            className="min-h-10 rounded-full bg-ink px-4 text-sm font-bold text-page"
            type="submit"
          >
            Use month
          </button>
        </form>

        {state.mode === "salary_cycle" && starts.length > 0 ? (
          <form action="/home" className="mt-3 flex flex-wrap items-end gap-2" method="get">
            <HiddenPeriod includeCycleStart={false} state={state} />
            <label className="text-sm font-semibold text-ink" htmlFor="cycle-start-select">
              Confirmed start
            </label>
            <select
              className="min-h-10 rounded-xl border border-line bg-page px-3 text-sm text-ink"
              defaultValue={state.cycleStartId ?? starts[0]?.id}
              id="cycle-start-select"
              name="cycle_start_id"
            >
              {starts.map((start, index) => (
                <option key={start.id} value={start.id}>
                  {start.start_date}
                  {index === 0 ? " (latest)" : ""}
                </option>
              ))}
            </select>
            <button
              className="min-h-10 rounded-full bg-ink px-4 text-sm font-bold text-page"
              type="submit"
            >
              View cycle
            </button>
          </form>
        ) : null}

        {state.mode === "custom" || state.mode === "calendar" ? (
          <form action="/home" className="mt-3 flex flex-wrap items-end gap-2" method="get">
            <HiddenPeriod includePeriod={false} state={state} />
            <input name="mode" type="hidden" value="custom" />
            <label className="text-sm font-semibold text-ink" htmlFor="custom-start">
              Start
            </label>
            <input
              className="min-h-10 rounded-xl border border-line bg-page px-3 text-sm text-ink"
              defaultValue={state.start ?? report.period?.start_date ?? report.today}
              id="custom-start"
              name="start_date"
              type="date"
            />
            <label className="text-sm font-semibold text-ink" htmlFor="custom-end">
              End
            </label>
            <input
              className="min-h-10 rounded-xl border border-line bg-page px-3 text-sm text-ink"
              defaultValue={state.end ?? report.period?.end_date ?? report.today}
              id="custom-end"
              name="end_date"
              type="date"
            />
            <button
              className="min-h-10 rounded-full bg-ink px-4 text-sm font-bold text-page"
              type="submit"
            >
              Use dates
            </button>
          </form>
        ) : null}
      </div>

      <FilterForm accounts={accounts} categories={categories} state={state} tags={tags} />

      <div className="flex flex-wrap gap-2" aria-label="Active filters">
        {activeAccount ? (
          <Link
            className="rounded-full border border-accent-soft-line bg-accent-soft px-3 py-1 text-xs font-semibold text-accent-soft-ink"
            href={reportingHref({ ...state, account: undefined })}
          >
            Account: {activeAccount.display_name} ×
          </Link>
        ) : null}
        {activeCategory ? (
          <Link
            className="rounded-full border border-accent-soft-line bg-accent-soft px-3 py-1 text-xs font-semibold text-accent-soft-ink"
            href={reportingHref({ ...state, category: undefined })}
          >
            Category: {activeCategory.display_name} ×
          </Link>
        ) : null}
        {state.merchantTag ? (
          <Link
            className="rounded-full border border-accent-soft-line bg-accent-soft px-3 py-1 text-xs font-semibold text-accent-soft-ink"
            href={reportingHref({ ...state, merchantTag: undefined })}
          >
            Tag: {activeTag?.display_name ?? "Untagged"} ×
          </Link>
        ) : null}
        {state.direction ? (
          <Link
            className="rounded-full border border-accent-soft-line bg-accent-soft px-3 py-1 text-xs font-semibold text-accent-soft-ink"
            href={reportingHref({ ...state, direction: undefined })}
          >
            Direction: {state.direction} ×
          </Link>
        ) : null}
      </div>
    </section>
  );
}
