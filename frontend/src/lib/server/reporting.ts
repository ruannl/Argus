import "server-only";

import {
  getCurrentAccount,
  listCategories,
  listFinancialAccounts,
  listMerchantTags,
  queryReport,
} from "@/lib/server/backend-client";
import type {
  Category,
  CurrentAccount,
  FinancialAccount,
  MerchantTag,
  ReportRequestResult,
  ReportResponse,
} from "@/lib/server/backend-client";
import type { ReportingState } from "@/lib/reporting-state";

export type ReportView = "overview" | "category" | "merchant_tag" | "comparison";

export type ReportFocusRequest = {
  categoryId?: string;
  merchantTag?: string;
  untagged?: boolean;
};

export type ReportQueryOptions = {
  view?: ReportView;
  focus?: ReportFocusRequest;
};

export function reportQueryForState(
  state: ReportingState,
  options: ReportQueryOptions = {},
): import("@/lib/server/backend-client").ReportQuery {
  const view = options.view ?? "overview";
  const period =
    state.mode === "salary_cycle"
      ? {
          mode: "salary_cycle" as const,
          ...(state.cycleStartId ? { cycle_start_id: state.cycleStartId } : {}),
        }
      : state.mode === "calendar"
        ? {
            mode: "calendar" as const,
            preset: state.preset,
            ...(state.preset === "month" && state.month ? { month: state.month } : {}),
          }
        : {
            mode: "custom" as const,
            start_date: state.start!,
            end_date: state.end!,
          };

  const focus = options.focus ?? {};
  const categoryId = focus.categoryId ?? state.category;
  const merchantTag = focus.merchantTag ?? state.merchantTag;
  const untagged = focus.untagged ?? false;

  const filters: import("@/lib/server/backend-client").ReportFilters = {};
  if (state.account) filters.financial_account_id = state.account;
  if (categoryId) filters.category_id = categoryId;
  if (untagged) filters.merchant_tag = "untagged";
  else if (merchantTag) filters.merchant_tag = merchantTag;
  if (state.direction) filters.direction = state.direction;

  return {
    view,
    period,
    ...(Object.keys(filters).length > 0 ? { filters } : {}),
    ...(state.transactionsPage ? { transactions_page: state.transactionsPage } : {}),
    ...(state.tagInPage ? { tag_in_page: state.tagInPage } : {}),
    ...(state.tagOutPage ? { tag_out_page: state.tagOutPage } : {}),
    ...(state.coveragePage ? { coverage_page: state.coveragePage } : {}),
    ...(state.comparisonCoveragePage
      ? { comparison_coverage_page: state.comparisonCoveragePage }
      : {}),
    ...(state.categoryChangesPage ? { category_changes_page: state.categoryChangesPage } : {}),
  };
}

export async function loadReportForState(
  sessionToken: string,
  state: ReportingState,
  options: ReportQueryOptions = {},
): Promise<ReportRequestResult<ReportResponse>> {
  return queryReport(sessionToken, reportQueryForState(state, options));
}

export type ReportInventory = {
  accounts: FinancialAccount[];
  categories: Category[];
  tags: MerchantTag[];
};

export type ReportPageData =
  | { ok: true; account: CurrentAccount; inventory: ReportInventory; report: ReportResponse }
  | { ok: false; kind: import("@/lib/server/backend-client").ReportErrorKind };

export async function loadReportPage(
  sessionToken: string,
  account: CurrentAccount,
  state: ReportingState,
  options: ReportQueryOptions = {},
): Promise<ReportPageData> {
  let inventory: ReportInventory;
  try {
    const [accountsResult, categoriesResult, tagsResult] = await Promise.all([
      listFinancialAccounts(sessionToken),
      listCategories(sessionToken),
      listMerchantTags(sessionToken),
    ]);
    if (!accountsResult.ok || !categoriesResult.ok || !tagsResult.ok) {
      return { ok: false, kind: "unavailable" };
    }
    inventory = {
      accounts: accountsResult.value.accounts,
      categories: categoriesResult.value.categories,
      tags: tagsResult.value.merchant_tags,
    };
  } catch {
    return { ok: false, kind: "unavailable" };
  }

  const report = await loadReportForState(sessionToken, state, options);
  if (!report.ok) {
    return { ok: false, kind: report.kind };
  }

  return { ok: true, account, inventory, report: report.value };
}

export async function loadAccount(
  sessionToken: string,
): Promise<
  | { ok: true; account: CurrentAccount }
  | { ok: false; kind: "unauthorized" | "rate_limited" | "unavailable" }
> {
  try {
    return { ok: true, account: await getCurrentAccount(sessionToken) };
  } catch (error) {
    const kind =
      typeof error === "object" && error !== null && "kind" in error
        ? (error as { kind?: unknown }).kind
        : "unavailable";
    if (kind === "unauthorized") {
      return { ok: false, kind: "unauthorized" };
    }
    return { ok: false, kind: kind === "rate_limited" ? "rate_limited" : "unavailable" };
  }
}
