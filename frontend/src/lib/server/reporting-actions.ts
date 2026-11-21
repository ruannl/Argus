"use server";

import "server-only";

import { revalidatePath } from "next/cache";

import { auth } from "@/auth";
import {
  confirmSalaryCycle,
  listSalaryCycles,
  previewSalaryCycle,
  removeSalaryCycle,
} from "@/lib/server/backend-client";
import type {
  ReportErrorKind,
  ReportResponse,
  SalaryCycleConfirm,
  SalaryCycleList,
  SalaryCyclePreview,
  SalaryCycleRemove,
} from "@/lib/server/backend-client";
import { parseReportingState } from "@/lib/reporting-state";
import type { ReportingState } from "@/lib/reporting-state";
import { loadReportForState } from "@/lib/server/reporting";
import type { ReportQueryOptions } from "@/lib/server/reporting";

export type ReportActionResult<T> =
  { ok: true; value: T } | { ok: false; kind: ReportErrorKind; message: string };

const MESSAGES: Record<ReportErrorKind, string> = {
  invalid_request: "That Overview request was invalid. Reset the view and try again.",
  not_found: "A selected item no longer exists in your workspace.",
  request_too_large: "That request was too large.",
  unauthorized: "Your session has expired. Sign in again.",
  rate_limited: "Too many requests. Wait a few minutes before retrying.",
  receipt_ineligible:
    "That transaction cannot be a salary-cycle start. Choose an owned, visible, positive receipt dated today or earlier.",
  version_conflict: "That transaction changed since it was previewed. Review it again.",
  cycle_sequence_changed:
    "The salary-cycle sequence changed since you previewed it. Review the latest dates before retrying.",
  report_busy: "The report is busy. Retry in a moment.",
  unavailable: "The report could not be loaded. Retry the view.",
};

function failure<T>(kind: ReportErrorKind): ReportActionResult<T> {
  return { ok: false, kind, message: MESSAGES[kind] };
}

async function token(): Promise<string | null> {
  try {
    return (await auth())?.user?.backendToken ?? null;
  } catch {
    return null;
  }
}

const STATE_KEYS = new Set([
  "view",
  "mode",
  "preset",
  "month",
  "start",
  "end",
  "cycleStartId",
  "account",
  "category",
  "merchantTag",
  "direction",
  "transactionsPage",
  "tagInPage",
  "tagOutPage",
  "coveragePage",
  "comparisonCoveragePage",
  "categoryChangesPage",
]);

function normalizeState(input: ReportingState): ReportingState | null {
  if (Object.keys(input as object).some((key) => !STATE_KEYS.has(key))) {
    return null;
  }
  const params: Record<string, string> = {};
  if (input.view) params.view = input.view;
  if (input.mode) params.mode = input.mode;
  if (input.preset) params.preset = input.preset;
  if (input.month) params.month = input.month;
  if (input.start) params.start_date = input.start;
  if (input.end) params.end_date = input.end;
  if (input.cycleStartId) params.cycle_start_id = input.cycleStartId;
  if (input.account) params.account = input.account;
  if (input.category) params.category = input.category;
  if (input.merchantTag) params.merchant_tag = input.merchantTag;
  if (input.direction) params.direction = input.direction;
  if (input.transactionsPage) params.transactions_page = String(input.transactionsPage);
  if (input.tagInPage) params.tag_in_page = String(input.tagInPage);
  if (input.tagOutPage) params.tag_out_page = String(input.tagOutPage);
  if (input.coveragePage) params.coverage_page = String(input.coveragePage);
  if (input.comparisonCoveragePage) {
    params.comparison_coverage_page = String(input.comparisonCoveragePage);
  }
  if (input.categoryChangesPage !== undefined) {
    params.category_changes_page = String(input.categoryChangesPage);
  }
  const parsed = parseReportingState(params);
  return parsed.error === null ? parsed.state : null;
}

export async function loadReportAction(
  state: ReportingState,
  overrides: Partial<ReportingState> = {},
  options: ReportQueryOptions = {},
): Promise<ReportActionResult<ReportResponse>> {
  const merged = normalizeState({ ...state, ...overrides });
  if (!merged) {
    return failure("invalid_request");
  }
  const sessionToken = await token();
  if (!sessionToken) {
    return failure("unauthorized");
  }
  try {
    const result = await loadReportForState(sessionToken, merged, options);
    return result.ok ? result : failure(result.kind);
  } catch {
    return failure("unavailable");
  }
}

export async function loadSalaryCyclesAction(
  beforeDate: string | null = null,
): Promise<ReportActionResult<SalaryCycleList>> {
  if (beforeDate !== null && !/^\d{4}-\d{2}-\d{2}$/.test(beforeDate)) {
    return failure("invalid_request");
  }
  const sessionToken = await token();
  if (!sessionToken) {
    return failure("unauthorized");
  }
  try {
    const result = await listSalaryCycles(sessionToken, beforeDate);
    return result.ok ? result : failure(result.kind);
  } catch {
    return failure("unavailable");
  }
}

export async function previewSalaryCycleAction(input: {
  action: "confirm" | "remove";
  transaction_id?: string;
  expected_transaction_version?: number;
  start_id?: string;
}): Promise<ReportActionResult<SalaryCyclePreview>> {
  if (
    (input.action === "confirm" &&
      (!input.transaction_id ||
        !input.expected_transaction_version ||
        input.expected_transaction_version < 1 ||
        input.start_id !== undefined)) ||
    (input.action === "remove" && (!input.start_id || input.transaction_id !== undefined))
  ) {
    return failure("invalid_request");
  }
  const sessionToken = await token();
  if (!sessionToken) {
    return failure("unauthorized");
  }
  try {
    const result = await previewSalaryCycle(sessionToken, input);
    return result.ok ? result : failure(result.kind);
  } catch {
    return failure("unavailable");
  }
}

export async function confirmSalaryCycleAction(input: {
  transaction_id: string;
  expected_transaction_version: number;
  expected_revision: string;
  confirm: true;
}): Promise<ReportActionResult<SalaryCycleConfirm>> {
  if (
    !input.transaction_id ||
    !Number.isInteger(input.expected_transaction_version) ||
    input.expected_transaction_version < 1 ||
    !/^\d+$/.test(input.expected_revision) ||
    input.expected_revision.length > 19 ||
    input.confirm !== true
  ) {
    return failure("invalid_request");
  }
  const sessionToken = await token();
  if (!sessionToken) {
    return failure("unauthorized");
  }
  try {
    const result = await confirmSalaryCycle(sessionToken, input);
    if (result.ok) {
      revalidatePath("/home", "layout");
    }
    return result.ok ? result : failure(result.kind);
  } catch {
    return failure("unavailable");
  }
}

export async function removeSalaryCycleAction(input: {
  start_id: string;
  expected_revision: string;
}): Promise<ReportActionResult<SalaryCycleRemove>> {
  if (
    !input.start_id ||
    !/^\d+$/.test(input.expected_revision) ||
    input.expected_revision.length > 19
  ) {
    return failure("invalid_request");
  }
  const sessionToken = await token();
  if (!sessionToken) {
    return failure("unauthorized");
  }
  try {
    const result = await removeSalaryCycle(sessionToken, input.start_id, {
      expected_revision: input.expected_revision,
      confirm: true,
    });
    if (result.ok) {
      revalidatePath("/home", "layout");
    }
    return result.ok ? result : failure(result.kind);
  } catch {
    return failure("unavailable");
  }
}
