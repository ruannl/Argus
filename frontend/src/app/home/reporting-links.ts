import type { ReportFilterDescriptor } from "@/lib/server/backend-client";
import { encodeOverviewContext, reportingHref } from "@/lib/reporting-state";
import type { ReportingState } from "@/lib/reporting-state";

/**
 * Builds an allowlisted /transactions link from a backend descriptor. Names,
 * amounts, search text and backend tokens never enter the URL.
 */
export function transactionHref(
  descriptor: ReportFilterDescriptor,
  overviewState: ReportingState | null,
  options: { includeOverviewContext?: boolean } = {},
): string {
  const params = new URLSearchParams();
  params.set("start_date", descriptor.start_date);
  params.set("end_date", descriptor.end_date);
  if (descriptor.financial_account_id) {
    params.set("financial_account_id", descriptor.financial_account_id);
  }
  if (descriptor.category_id) {
    params.set("category_id", descriptor.category_id);
  }
  if (descriptor.merchant_tag) {
    params.set("merchant_tag", descriptor.merchant_tag);
  }
  if (descriptor.category_kind) {
    params.set("category_kind", descriptor.category_kind);
  }
  if (descriptor.direction) {
    params.set("direction", descriptor.direction);
  }
  if (descriptor.category_confirmed !== undefined && descriptor.category_confirmed !== null) {
    params.set("category_confirmed", String(descriptor.category_confirmed));
  }
  params.set("visibility", descriptor.visibility);
  if (descriptor.calculation_scope === "included") {
    params.set("calculation_scope", "included");
  }
  if (descriptor.excluded_category_ids && descriptor.excluded_category_ids.length > 0) {
    const sorted = [...descriptor.excluded_category_ids].sort();
    params.set("excluded_category_ids", sorted.join(","));
  }
  if (options.includeOverviewContext !== false && overviewState) {
    try {
      params.set("overview_context", encodeOverviewContext(overviewState));
    } catch {
      // A context too large for the navigation budget is omitted, never truncated
      // into a misleading return state.
    }
  }
  params.set("sort_by", "date");
  params.set("sort_direction", "desc");
  return `/transactions?${params.toString()}`;
}

export function transactionDetailHref(
  transactionId: string,
  overviewState: ReportingState | null,
): string {
  const path = `/transactions/${encodeURIComponent(transactionId)}`;
  if (!overviewState) {
    return path;
  }
  try {
    return `${path}?overview_context=${encodeURIComponent(encodeOverviewContext(overviewState))}`;
  } catch {
    return path;
  }
}

export function categoryDetailHref(
  state: ReportingState,
  categoryId: string,
  direction?: ReportingState["direction"],
): string {
  return reportingHref(
    { ...state, category: categoryId, direction: direction ?? state.direction },
    {},
    `/home/categories/${encodeURIComponent(categoryId)}`,
  );
}

export function merchantTagDetailHref(
  state: ReportingState,
  tagId: string,
  direction?: ReportingState["direction"],
): string {
  return reportingHref(
    { ...state, merchantTag: tagId, direction: direction ?? state.direction },
    {},
    `/home/merchant-tags/${encodeURIComponent(tagId)}`,
  );
}

export function untaggedDetailHref(
  state: ReportingState,
  direction?: ReportingState["direction"],
): string {
  return reportingHref(
    { ...state, merchantTag: "untagged", direction: direction ?? state.direction },
    {},
    "/home/untagged",
  );
}

export function comparisonHref(state: ReportingState): string {
  return reportingHref(state, {}, "/home/comparison");
}

export function cyclesHref(state: ReportingState): string {
  return reportingHref(state, {}, "/home/cycles");
}

export function detailBackHref(state: ReportingState): string {
  return reportingHref(state);
}
