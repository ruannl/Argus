"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";

import type { Category, FinancialAccount, MerchantTag } from "@/lib/server/backend-client";
import { reportingHref } from "@/lib/reporting-state";
import type { ReportingState } from "@/lib/reporting-state";

type FilterFormProps = {
  state: ReportingState;
  accounts: FinancialAccount[];
  categories: Category[];
  tags: MerchantTag[];
};

/**
 * Explicit filter form. Empty selects are omitted before navigation, so the
 * strict URL parser still rejects malformed/repeated parameters while an
 * ordinary "All" choice means absence rather than an empty string.
 */
export default function FilterForm({ state, accounts, categories, tags }: FilterFormProps) {
  const router = useRouter();
  const [account, setAccount] = useState(state.account ?? "");
  const [category, setCategory] = useState(state.category ?? "");
  const [merchantTag, setMerchantTag] = useState(state.merchantTag ?? "");
  const [direction, setDirection] = useState(state.direction ?? "");

  return (
    <form
      className="finance-unboxed"
      onSubmit={(event) => {
        event.preventDefault();
        const next: ReportingState = {
          ...state,
          account: account || undefined,
          category: category || undefined,
          merchantTag: merchantTag || undefined,
          direction: direction ? (direction as ReportingState["direction"]) : undefined,
          transactionsPage: undefined,
          tagInPage: undefined,
          tagOutPage: undefined,
          coveragePage: undefined,
          comparisonCoveragePage: undefined,
          categoryChangesPage: undefined,
        };
        router.push(reportingHref(next));
      }}
    >
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <label className="text-sm font-semibold text-ink">
          Account
          <select
            className="mt-1 min-h-10 w-full rounded-xl border border-line bg-page px-3 text-sm text-ink"
            onChange={(event) => setAccount(event.target.value)}
            value={account}
          >
            <option value="">All accounts</option>
            {accounts.map((item) => (
              <option key={item.id} value={item.id}>
                {item.display_name}
                {item.is_archived ? " (archived)" : ""}
              </option>
            ))}
          </select>
        </label>
        <label className="text-sm font-semibold text-ink">
          Category
          <select
            className="mt-1 min-h-10 w-full rounded-xl border border-line bg-page px-3 text-sm text-ink"
            onChange={(event) => setCategory(event.target.value)}
            value={category}
          >
            <option value="">All categories</option>
            {categories.map((item) => (
              <option key={item.id} value={item.id}>
                {item.display_name}
                {item.is_archived ? " (archived)" : ""}
              </option>
            ))}
          </select>
        </label>
        <label className="text-sm font-semibold text-ink">
          Merchant tag
          <select
            className="mt-1 min-h-10 w-full rounded-xl border border-line bg-page px-3 text-sm text-ink"
            onChange={(event) => setMerchantTag(event.target.value)}
            value={merchantTag}
          >
            <option value="">All tags</option>
            <option value="untagged">Untagged</option>
            {tags.map((item) => (
              <option key={item.id} value={item.id}>
                {item.display_name}
                {item.is_archived ? " (archived)" : ""}
              </option>
            ))}
          </select>
        </label>
        <label className="text-sm font-semibold text-ink">
          Direction
          <select
            className="mt-1 min-h-10 w-full rounded-xl border border-line bg-page px-3 text-sm text-ink"
            onChange={(event) => setDirection(event.target.value)}
            value={direction}
          >
            <option value="">In and out</option>
            <option value="in">Money in</option>
            <option value="out">Money out</option>
            <option value="zero">Zero-value</option>
          </select>
        </label>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <button
          className="min-h-10 rounded-full bg-ink px-4 text-sm font-bold text-page"
          type="submit"
        >
          Apply filters
        </button>
        <Link
          className="text-sm font-semibold text-accent-text underline underline-offset-4"
          href="/home"
        >
          Reset
        </Link>
        {state.account || state.category || state.merchantTag || state.direction ? (
          <span className="ml-auto text-xs text-muted">
            Active filters stay visible as chips below in both Summary and Charts.
          </span>
        ) : null}
      </div>
    </form>
  );
}
