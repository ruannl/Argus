/**
 * Allowlisted Overview URL state. Only non-sensitive mode, date, UUID, enum
 * and small page choices live here. A private receipt search never enters a
 * URL. Duplicate, unknown or malformed singleton parameters are invalid
 * rather than silently broadened to a default report.
 */

export const REPORTING_VIEWS = ["summary", "charts"] as const;
export type ReportingView = (typeof REPORTING_VIEWS)[number];

export const REPORTING_MODES = ["salary_cycle", "calendar", "custom"] as const;
export type ReportingMode = (typeof REPORTING_MODES)[number];

export const CALENDAR_PRESETS = [
  "month_to_date",
  "previous_month",
  "last_three_months",
  "last_six_months",
  "month",
] as const;
export type CalendarPreset = (typeof CALENDAR_PRESETS)[number];

export type ReportingDirection = "in" | "out" | "zero";

export type ReportingFilter = {
  account?: string;
  category?: string;
  merchantTag?: string;
  direction?: ReportingDirection;
};

export type ReportingState = {
  view: ReportingView;
  mode: ReportingMode;
  preset?: CalendarPreset;
  month?: string;
  start?: string;
  end?: string;
  cycleStartId?: string;
  account?: string;
  category?: string;
  merchantTag?: string;
  direction?: ReportingDirection;
  transactionsPage?: number;
  tagInPage?: number;
  tagOutPage?: number;
  coveragePage?: number;
  comparisonCoveragePage?: number;
  categoryChangesPage?: number;
};

export type ParsedReportingState = {
  state: ReportingState;
  error: string | null;
};

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const MONTH_PATTERN = /^\d{4}-\d{2}$/;

const ALLOWED_KEYS = new Set([
  "view",
  "mode",
  "preset",
  "month",
  "start_date",
  "end_date",
  "cycle_start_id",
  "account",
  "category",
  "merchant_tag",
  "direction",
  "transactions_page",
  "tag_in_page",
  "tag_out_page",
  "coverage_page",
  "comparison_coverage_page",
  "category_changes_page",
]);

const INVALID_MESSAGE =
  "Some Overview options in the address were invalid. Reset to the default salary-cycle view.";

export function isUuid(value: string): boolean {
  return UUID_PATTERN.test(value);
}

export function isRealDate(value: string): boolean {
  if (!DATE_PATTERN.test(value)) {
    return false;
  }
  const parsed = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}

export function isRealMonth(value: string): boolean {
  if (!MONTH_PATTERN.test(value)) {
    return false;
  }
  const year = Number(value.slice(0, 4));
  const month = Number(value.slice(5, 7));
  return Number.isInteger(year) && year >= 1 && year <= 9999 && month >= 1 && month <= 12;
}

export function defaultReportingState(view: ReportingView = "summary"): ReportingState {
  return { view, mode: "salary_cycle" };
}

function invalid(view: ReportingView): ParsedReportingState {
  return { state: defaultReportingState(view), error: INVALID_MESSAGE };
}

export function parseReportingState(
  params: Record<string, string | string[] | undefined>,
  options: { defaultView?: ReportingView } = {},
): ParsedReportingState {
  const fallbackView = options.defaultView ?? "summary";

  for (const [key, value] of Object.entries(params)) {
    if (!ALLOWED_KEYS.has(key)) {
      return invalid(fallbackView);
    }
    if (Array.isArray(value)) {
      return invalid(fallbackView);
    }
    if (value !== undefined && value === "") {
      return invalid(fallbackView);
    }
  }

  const value = (key: string): string | undefined => {
    const raw = params[key];
    return typeof raw === "string" ? raw : undefined;
  };

  const viewRaw = value("view") ?? fallbackView;
  if (!REPORTING_VIEWS.includes(viewRaw as ReportingView)) {
    return invalid(fallbackView);
  }
  const view = viewRaw as ReportingView;

  const modeRaw = value("mode") ?? "salary_cycle";
  if (!REPORTING_MODES.includes(modeRaw as ReportingMode)) {
    return invalid(view);
  }
  const mode = modeRaw as ReportingMode;

  const state: ReportingState = { view, mode };

  const presetRaw = value("preset");
  const month = value("month");
  const start = value("start_date");
  const end = value("end_date");
  const cycleStartId = value("cycle_start_id");

  if (mode === "salary_cycle") {
    if (
      presetRaw !== undefined ||
      month !== undefined ||
      start !== undefined ||
      end !== undefined
    ) {
      return invalid(view);
    }
    if (cycleStartId !== undefined) {
      if (!isUuid(cycleStartId)) {
        return invalid(view);
      }
      state.cycleStartId = cycleStartId;
    }
  } else if (mode === "calendar") {
    if (start !== undefined || end !== undefined || cycleStartId !== undefined) {
      return invalid(view);
    }
    if (presetRaw === undefined || !CALENDAR_PRESETS.includes(presetRaw as CalendarPreset)) {
      return invalid(view);
    }
    state.preset = presetRaw as CalendarPreset;
    if (state.preset === "month") {
      if (month === undefined || !isRealMonth(month)) {
        return invalid(view);
      }
      state.month = month;
    } else if (month !== undefined) {
      return invalid(view);
    }
  } else {
    if (presetRaw !== undefined || month !== undefined || cycleStartId !== undefined) {
      return invalid(view);
    }
    if (
      start === undefined ||
      end === undefined ||
      !isRealDate(start) ||
      !isRealDate(end) ||
      start > end
    ) {
      return invalid(view);
    }
    state.start = start;
    state.end = end;
  }

  const account = value("account");
  if (account !== undefined) {
    if (!isUuid(account)) {
      return invalid(view);
    }
    state.account = account;
  }

  const category = value("category");
  if (category !== undefined) {
    if (!isUuid(category)) {
      return invalid(view);
    }
    state.category = category;
  }

  const merchantTag = value("merchant_tag");
  if (merchantTag !== undefined) {
    if (merchantTag !== "untagged" && !isUuid(merchantTag)) {
      return invalid(view);
    }
    state.merchantTag = merchantTag;
  }

  const direction = value("direction");
  if (direction !== undefined) {
    if (!["in", "out", "zero"].includes(direction)) {
      return invalid(view);
    }
    state.direction = direction as ReportingDirection;
  }

  const pageKeys = [
    ["transactions_page", "transactionsPage"],
    ["tag_in_page", "tagInPage"],
    ["tag_out_page", "tagOutPage"],
    ["coverage_page", "coveragePage"],
    ["comparison_coverage_page", "comparisonCoveragePage"],
    ["category_changes_page", "categoryChangesPage"],
  ] as const;
  for (const [key, property] of pageKeys) {
    const raw = value(key);
    if (raw === undefined) {
      continue;
    }
    if (!/^\d+$/.test(raw)) {
      return invalid(view);
    }
    const parsed = Number(raw);
    if (!Number.isSafeInteger(parsed) || parsed < 1 || parsed > 100_000) {
      return invalid(view);
    }
    state[property] = parsed;
  }

  return { state, error: null };
}

export function reportingSearchParams(state: ReportingState): URLSearchParams {
  const params = new URLSearchParams();
  if (state.view === "charts") {
    params.set("view", "charts");
  }
  if (state.mode !== "salary_cycle") {
    params.set("mode", state.mode);
  }
  if (state.mode === "salary_cycle" && state.cycleStartId) {
    params.set("cycle_start_id", state.cycleStartId);
  }
  if (state.mode === "calendar" && state.preset) {
    params.set("preset", state.preset);
    if (state.preset === "month" && state.month) {
      params.set("month", state.month);
    }
  }
  if (state.mode === "custom") {
    if (state.start) params.set("start_date", state.start);
    if (state.end) params.set("end_date", state.end);
  }
  if (state.account) params.set("account", state.account);
  if (state.category) params.set("category", state.category);
  if (state.merchantTag) params.set("merchant_tag", state.merchantTag);
  if (state.direction) params.set("direction", state.direction);
  if (state.transactionsPage) params.set("transactions_page", String(state.transactionsPage));
  if (state.tagInPage) params.set("tag_in_page", String(state.tagInPage));
  if (state.tagOutPage) params.set("tag_out_page", String(state.tagOutPage));
  if (state.coveragePage) params.set("coverage_page", String(state.coveragePage));
  if (state.comparisonCoveragePage) {
    params.set("comparison_coverage_page", String(state.comparisonCoveragePage));
  }
  if (state.categoryChangesPage) {
    params.set("category_changes_page", String(state.categoryChangesPage));
  }
  return params;
}

export function reportingHref(
  state: ReportingState,
  overrides: Partial<ReportingState> = {},
  path = "/home",
): string {
  const merged: ReportingState = { ...state, ...overrides };
  const query = reportingSearchParams(merged).toString();
  return query ? `${path}?${query}` : path;
}

export function reportingStateKey(state: ReportingState): string {
  return reportingSearchParams(state).toString();
}

/** A stable focus descriptor for the report request body. */
export function reportingFilters(state: ReportingState): Record<string, string> {
  const filters: Record<string, string> = {};
  if (state.account) filters.financial_account_id = state.account;
  if (state.category) filters.category_id = state.category;
  if (state.merchantTag) filters.merchant_tag = state.merchantTag;
  if (state.direction) filters.direction = state.direction;
  return filters;
}

const OVERVIEW_CONTEXT_MAX_BYTES = 2048;

type OverviewContext = {
  v: 1;
  state: ReportingState;
};

/**
 * Encodes only allowlisted Overview navigation state for a transaction return
 * link. It is not a filter authority, an operation identity or a URL.
 */
export function encodeOverviewContext(state: ReportingState): string {
  const payload: OverviewContext = {
    v: 1,
    state: {
      view: state.view,
      mode: state.mode,
      preset: state.preset,
      month: state.month,
      start: state.start,
      end: state.end,
      cycleStartId: state.cycleStartId,
      account: state.account,
      category: state.category,
      merchantTag: state.merchantTag,
      direction: state.direction,
    },
  };
  const json = JSON.stringify(payload);
  const bytes = new TextEncoder().encode(json);
  if (bytes.byteLength > OVERVIEW_CONTEXT_MAX_BYTES) {
    throw new Error("overview context is too large");
  }
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export function decodeOverviewContext(value: string | undefined | null): ReportingState | null {
  if (!value || value.length > OVERVIEW_CONTEXT_MAX_BYTES) {
    return null;
  }
  try {
    const padded = value.replace(/-/g, "+").replace(/_/g, "/");
    const withPadding = padded + "=".repeat((4 - (padded.length % 4)) % 4);
    const binary = atob(withPadding);
    const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
    const json = new TextDecoder().decode(bytes);
    const parsed: unknown = JSON.parse(json);
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
      return null;
    }
    const context = parsed as { v?: unknown; state?: unknown };
    if (context.v !== 1 || typeof context.state !== "object" || context.state === null) {
      return null;
    }
    const raw = context.state as Record<string, unknown>;
    const params: Record<string, string> = {};
    const mapping: Record<string, string> = {
      view: "view",
      mode: "mode",
      preset: "preset",
      month: "month",
      start: "start_date",
      end: "end_date",
      cycleStartId: "cycle_start_id",
      account: "account",
      category: "category",
      merchantTag: "merchant_tag",
      direction: "direction",
    };
    for (const [property, key] of Object.entries(mapping)) {
      const item = raw[property];
      if (typeof item === "string" && item.length > 0) {
        params[key] = item;
      }
    }
    const decoded = parseReportingState(params);
    return decoded.error === null ? decoded.state : null;
  } catch {
    return null;
  }
}

// Kept for callers that validate a page value without a URL parameter.
export function isPositivePage(value: unknown): value is number {
  return typeof value === "number" && Number.isSafeInteger(value) && value >= 1 && value <= 100_000;
}
