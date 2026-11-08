"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";

import AppShell from "@/components/app-shell";
import {
  abandonOperationAction,
  applyOperationAction,
  loadOperationAction,
  loadTransactionAction,
  prepareTransactionsAction,
  queryTransactionsAction,
} from "@/lib/server/transaction-actions";
import type {
  Category,
  CurrentAccount,
  FinancialAccount,
  MerchantTag,
  TransactionApplyResult,
  TransactionDetail,
  TransactionErrorDetails,
  TransactionFilters,
  TransactionOperation,
  TransactionPage,
  TransactionPrepareInput,
  TransactionRow,
} from "@/lib/server/backend-client";

import TransactionDetailView from "./transaction-detail";
import TransactionInlineEditor from "./transaction-inline-editor";

export type TransactionsInitialQuery = {
  filters: TransactionFilters;
  page: number;
  pageSize: number;
  sortBy: "date" | "amount" | "description";
  sortDirection: "asc" | "desc";
};

type TransactionsWorkspaceProps = {
  account: CurrentAccount;
  accounts: FinancialAccount[];
  categories: Category[];
  tags: MerchantTag[];
  inventoryFailed: boolean;
  initialQuery: TransactionsInitialQuery;
  initialPage: TransactionPage | null;
  initialError: string | null;
  initialOperationId: string | null;
};

type Message = { tone: "status" | "error"; text: string; details?: TransactionErrorDetails };

type SelectionEntry = { id: string; version: number };

type PendingPreparation = {
  input: TransactionPrepareInput;
  intent: number;
};


function omitEmptyFilters(filters: TransactionFilters): TransactionFilters {
  const next: TransactionFilters = {};
  if (filters.search?.trim()) next.search = filters.search.trim();
  if (filters.start_date) next.start_date = filters.start_date;
  if (filters.end_date) next.end_date = filters.end_date;
  if (filters.financial_account_id) next.financial_account_id = filters.financial_account_id;
  if (filters.statement_import_id) next.statement_import_id = filters.statement_import_id;
  if (filters.category_id) next.category_id = filters.category_id;
  if (filters.merchant_tag) next.merchant_tag = filters.merchant_tag;
  if (filters.category_kind) next.category_kind = filters.category_kind;
  if (filters.direction) next.direction = filters.direction;
  if (filters.visibility) next.visibility = filters.visibility;
  return next;
}

function formatTimestamp(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(date);
}

function amountLabel(row: TransactionRow): string {
  return `${row.amount} ${row.currency}`;
}

function activeCategories(categories: Category[]): Category[] {
  return categories.filter((category) => !category.is_archived);
}

function activeTags(tags: MerchantTag[]): MerchantTag[] {
  return tags.filter((tag) => !tag.is_archived);
}

function hiddenBadge(row: TransactionRow): string | null {
  switch (row.hidden_reason) {
    case "individual":
      return "Hidden individually";
    case "statement":
      return "Hidden by statement";
    case "both":
      return "Hidden individually and by statement";
    default:
      return null;
  }
}

export default function TransactionsWorkspace({
  account,
  accounts,
  categories,
  tags,
  inventoryFailed,
  initialQuery,
  initialPage,
  initialError,
  initialOperationId,
}: TransactionsWorkspaceProps) {
  const router = useRouter();
  const [filters, setFilters] = useState<TransactionFilters>(initialQuery.filters);
  const [page, setPage] = useState(initialQuery.page);
  const [pageSize, setPageSize] = useState(initialQuery.pageSize);
  const [sortBy, setSortBy] = useState<TransactionsInitialQuery["sortBy"]>(
    initialQuery.sortBy,
  );
  const [sortDirection, setSortDirection] = useState<TransactionsInitialQuery["sortDirection"]>(
    initialQuery.sortDirection,
  );
  const [searchInput, setSearchInput] = useState("");
  const [data, setData] = useState<TransactionPage | null>(initialPage);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(initialError);
  const [message, setMessage] = useState<Message | null>(null);

  const [selection, setSelection] = useState<Map<string, SelectionEntry>>(new Map());
  const [allMatching, setAllMatching] = useState(false);
  const [bulkCategoryId, setBulkCategoryId] = useState("");
  const [bulkAddTagIds, setBulkAddTagIds] = useState<string[]>([]);
  const [bulkRemoveTagIds, setBulkRemoveTagIds] = useState<string[]>([]);
  const [bulkHidden, setBulkHidden] = useState<boolean | null>(null);

  const [prepared, setPrepared] = useState<TransactionOperation | null>(null);
  const [recovery, setRecovery] = useState<TransactionOperation | null>(null);
  const [recoveryLoading, setRecoveryLoading] = useState(initialOperationId !== null);
  const [receipt, setReceipt] = useState<TransactionApplyResult | null>(null);
  const [operationId, setOperationId] = useState<string | null>(initialOperationId);
  const [pendingPreparation, setPendingPreparation] = useState<PendingPreparation | null>(null);
  const [knownRejection, setKnownRejection] = useState(false);
  const [terminalStatus, setTerminalStatus] = useState<"Abandoned" | "Expired" | null>(null);
  const [terminalOperationId, setTerminalOperationId] = useState<string | null>(null);
  const [captureContext, setCaptureContext] = useState<{
    listCount: number | null;
    filters: TransactionFilters;
  } | null>(null);
  const [busy, setBusy] = useState(false);

  const [inlineEditorId, setInlineEditorId] = useState<string | null>(null);
  const [detailId, setDetailId] = useState<string | null>(null);
  const [detail, setDetail] = useState<TransactionDetail | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);

  const generationRef = useRef(0);
  const intentRef = useRef(0);
  const applyingRef = useRef(false);
  const operationIdRef = useRef<string | null>(initialOperationId);
  const busyOwnerRef = useRef(0);
  const statusGenerationRef = useRef(0);
  const viewRef = useRef<TransactionsInitialQuery>(initialQuery);
  const detailOriginRef = useRef<HTMLElement | null>(null);
  const detailRequestRef = useRef(0);
  const drawerRef = useRef<HTMLDivElement | null>(null);
  const selectPageRef = useRef<HTMLInputElement | null>(null);

  const canAssignCategories = !inventoryFailed;
  const rows = data?.transactions ?? [];
  const selectionCount = selection.size;
  const selectedOnPage = rows.filter((row) => selection.has(row.id)).length;
  const allPageSelected = rows.length > 0 && selectedOnPage === rows.length;
  const somePageSelected = selectedOnPage > 0 && !allPageSelected;
  const hasBulkAction =
    bulkCategoryId !== "" ||
    bulkAddTagIds.length > 0 ||
    bulkRemoveTagIds.length > 0 ||
    bulkHidden !== null;
  const canPrepare = hasBulkAction && (allMatching || selectionCount > 0) && !busy && !operationId;

  const selectionLabel = allMatching
    ? prepared
      ? `${prepared.selected_count} transactions captured for this confirmation.`
      : `${data?.filtered_total ?? 0} transactions match the current filters (captured at the moment you review the change).`
    : `${selectionCount} transaction${selectionCount === 1 ? "" : "s"} selected.`;

  useEffect(() => {
    if (selectPageRef.current) {
      selectPageRef.current.indeterminate = somePageSelected;
    }
  }, [somePageSelected]);

  const syncUrl = useCallback(
    (
      nextFilters: TransactionFilters,
      nextPage: number,
      nextPageSize: number,
      nextSortBy: string,
      nextSortDirection: string,
      operationId: string | null,
    ) => {
      const params = new URLSearchParams();
      const clean = omitEmptyFilters(nextFilters);
      for (const key of [
        "start_date",
        "end_date",
        "financial_account_id",
        "statement_import_id",
        "category_id",
        "merchant_tag",
        "category_kind",
        "direction",
        "visibility",
      ] as const) {
        const value = clean[key];
        if (value !== undefined) {
          params.set(key, String(value));
        }
      }
      params.set("page", String(nextPage));
      params.set("page_size", String(nextPageSize));
      params.set("sort_by", nextSortBy);
      params.set("sort_direction", nextSortDirection);
      // Normal view changes must not discard an unresolved operation. The
      // caller clears the ref only after the user acknowledges its outcome.
      const retainedOperationId = operationId ?? operationIdRef.current;
      if (retainedOperationId) {
        params.set("operation_id", retainedOperationId);
      }
      window.history.replaceState(null, "", `/transactions?${params.toString()}`);
    },
    [],
  );

  useEffect(() => {
    if (!initialOperationId) {
      return;
    }
    let cancelled = false;
    void loadOperationAction(initialOperationId).then((result) => {
      if (cancelled) {
        return;
      }
      setRecoveryLoading(false);
      if (!result.ok) {
        setMessage({
          tone: "error",
          text:
            result.kind === "not_found"
              ? "The operation was not found yet. Its outcome remains unresolved; check status again."
              : result.message,
        });
        return;
      }
      const operation = result.value;
      if (operation.status === "Applied") {
        setReceipt({
          operation_id: operation.operation_id,
          status: operation.status,
          selected_count: operation.selected_count,
          changed_count: operation.changed_count,
          unchanged_count: operation.unchanged_count,
          applied_at: operation.applied_at,
          replayed: true,
        });
      } else if (operation.status === "Prepared") {
        setRecovery(operation);
      } else {
        setTerminalStatus(operation.status);
        setTerminalOperationId(operation.operation_id);
      }
    }).catch(() => {
      if (!cancelled) {
        setRecoveryLoading(false);
        setMessage({ tone: "error", text: "Operation status is unavailable. Check status again." });
      }
    });
    return () => {
      cancelled = true;
    };
  }, [initialOperationId]);

  const runQuery = useCallback(
    async (
      nextFilters: TransactionFilters,
      nextPage: number,
      nextPageSize: number,
      nextSortBy: string,
      nextSortDirection: string,
    ) => {
      const generation = ++generationRef.current;
      setLoading(true);
      setLoadError(null);

      const clean = omitEmptyFilters(nextFilters);
      if (nextFilters.search?.trim()) {
        clean.search = nextFilters.search.trim();
      }

      let requestPage = nextPage;
      try {
        for (let attempt = 0; attempt < 2; attempt++) {
          const result = await queryTransactionsAction({
            filters: clean,
            page: requestPage,
            page_size: nextPageSize,
            sort_by: nextSortBy as "date" | "amount" | "description",
            sort_direction: nextSortDirection as "asc" | "desc",
          });
          if (generation !== generationRef.current) {
            return;
          }
          if (!result.ok) {
            setLoadError(result.message);
            return;
          }
          if (
            result.value.transactions.length === 0 &&
            result.value.filtered_total > 0 &&
            requestPage > 1
          ) {
            // The last page emptied after an edit; move back to a valid page
            // rather than showing a misleading empty page.
            requestPage = Math.max(
              1,
              Math.ceil(result.value.filtered_total / result.value.page_size),
            );
            if (requestPage !== nextPage) {
              viewRef.current = { ...viewRef.current, page: requestPage };
              setPage(requestPage);
              syncUrl(
                nextFilters,
                requestPage,
                nextPageSize,
                nextSortBy,
                nextSortDirection,
                null,
              );
              continue;
            }
          }
          setData(result.value);
          return;
        }
      } catch {
        if (generation === generationRef.current) {
          setLoadError("Something went wrong while loading transactions. Retry the view.");
        }
      } finally {
        if (generation === generationRef.current) {
          setLoading(false);
        }
      }
    },
    [syncUrl],
  );

  const invalidateConfirmation = useCallback(
    () => {
      // A changed view invalidates the old confirmation, not its durable
      // operation identity. Keep it recoverable until its outcome is known.
      intentRef.current += 1;
      if (prepared) {
        setRecovery(prepared);
        setPrepared(null);
      }
    },
    [prepared],
  );

  const clearSelection = useCallback(() => {
    invalidateConfirmation();
    setSelection(new Map());
    setAllMatching(false);
  }, [invalidateConfirmation]);

  const changeFilters = useCallback(
    (partial: Partial<TransactionFilters>) => {
      const next = { ...filters, ...partial };
      if (partial.search !== undefined && partial.search.trim() === "") {
        delete next.search;
      }
      viewRef.current = { ...viewRef.current, filters: next, page: 1 };
      setFilters(next);
      setPage(1);
      setSearchInput(next.search ?? "");
      clearSelection();
      syncUrl(next, 1, pageSize, sortBy, sortDirection, null);
      void runQuery(next, 1, pageSize, sortBy, sortDirection);
    },
    [clearSelection, filters, pageSize, runQuery, sortBy, sortDirection, syncUrl],
  );

  function changePage(nextPage: number) {
    if (nextPage < 1) {
      return;
    }
    viewRef.current = { ...viewRef.current, page: nextPage };
    setPage(nextPage);
    syncUrl(filters, nextPage, pageSize, sortBy, sortDirection, null);
    void runQuery(filters, nextPage, pageSize, sortBy, sortDirection);
  }

  function toggleRow(row: TransactionRow) {
    invalidateConfirmation();
    setAllMatching(false);
    setSelection((current) => {
      const next = new Map(current);
      if (next.has(row.id)) {
        next.delete(row.id);
      } else {
        next.set(row.id, { id: row.id, version: row.version });
      }
      return next;
    });
    setMessage(null);
  }

  function togglePage() {
    invalidateConfirmation();
    setAllMatching(false);
    setSelection((current) => {
      const next = new Map(current);
      if (allPageSelected) {
        for (const row of rows) {
          next.delete(row.id);
        }
      } else {
        for (const row of rows) {
          next.set(row.id, { id: row.id, version: row.version });
        }
      }
      return next;
    });
  }

  function toggleAllMatching() {
    invalidateConfirmation();
    setSelection(new Map());
    setAllMatching((current) => !current);
    setMessage(null);
  }

  function resetBulkControls() {
    setBulkCategoryId("");
    setBulkAddTagIds([]);
    setBulkRemoveTagIds([]);
    setBulkHidden(null);
  }

  function syncCurrentUrl() {
    const current = viewRef.current;
    syncUrl(current.filters, current.page, current.pageSize, current.sortBy, current.sortDirection, null);
  }

  function rememberOperation(id: string) {
    // Any status request for an earlier operation is now obsolete. Its result
    // must never replace the recovery state of this operation.
    statusGenerationRef.current += 1;
    setRecoveryLoading(false);
    operationIdRef.current = id;
    setOperationId(id);
    syncCurrentUrl();
  }

  function acknowledgeOutcome() {
    const currentId = operationIdRef.current;
    const displayedId =
      terminalOperationId ??
      receipt?.operation_id ??
      recovery?.operation_id ??
      prepared?.operation_id ??
      pendingPreparation?.input.operation_id ??
      null;
    const matchesCurrent = displayedId === null ? currentId === null : displayedId === currentId;

    // Invalidate any in-flight status request for the acknowledged outcome.
    statusGenerationRef.current += 1;
    setTerminalStatus(null);
    setTerminalOperationId(null);
    setReceipt(null);
    setRecovery(null);
    setPrepared(null);
    setMessage(null);

    if (matchesCurrent) {
      setPendingPreparation(null);
      setKnownRejection(false);
      setCaptureContext(null);
      operationIdRef.current = null;
      setOperationId(null);
    } else {
      // The acknowledged outcome belonged to a different operation. Keep the
      // current operation's retry input and recovery identity intact.
    }
    syncCurrentUrl();
  }

  function refreshCurrentView() {
    const current = viewRef.current;
    void runQuery(current.filters, current.page, current.pageSize, current.sortBy, current.sortDirection);
  }

  function showAppliedReceipt(result: TransactionApplyResult) {
    setReceipt(result);
    setPrepared(null);
    setRecovery(null);
    setPendingPreparation(null);
    setTerminalStatus(null);
    setTerminalOperationId(null);
    setSelection(new Map());
    setAllMatching(false);
    resetBulkControls();
    setMessage({
      tone: "status",
      text: `${result.changed_count} changed and ${result.unchanged_count} already matched.`,
    });
    refreshCurrentView();
  }

  function receiptFromStatus(operation: TransactionOperation): TransactionApplyResult {
    return {
      operation_id: operation.operation_id,
      status: operation.status,
      selected_count: operation.selected_count,
      changed_count: operation.changed_count,
      unchanged_count: operation.unchanged_count,
      applied_at: operation.applied_at,
      replayed: true,
    };
  }

  async function checkOperationStatus(id: string) {
    const request = ++statusGenerationRef.current;
    setRecoveryLoading(true);
    try {
      const result = await loadOperationAction(id);
      if (request !== statusGenerationRef.current || operationIdRef.current !== id) {
        // A newer status request or a different operation owns the recovery
        // state now. This result must not replace it.
        return;
      }
      if (!result.ok) {
        setMessage({
          tone: "error",
          text: result.kind === "not_found"
            ? "The operation was not found yet. Its outcome remains unresolved; check status again."
            : result.message,
        });
        return;
      }
      if (result.value.status === "Applied") {
        showAppliedReceipt(receiptFromStatus(result.value));
      } else if (result.value.status === "Prepared") {
        setPrepared(null);
        setRecovery(result.value);
        setTerminalStatus(null);
        setTerminalOperationId(null);
        setMessage(null);
      } else {
        setPrepared(null);
        setRecovery(null);
        setPendingPreparation(null);
        setTerminalStatus(result.value.status);
        setTerminalOperationId(result.value.operation_id);
      }
    } catch {
      if (request === statusGenerationRef.current && operationIdRef.current === id) {
        setMessage({ tone: "error", text: "Operation status is unavailable. Check status again." });
      }
    } finally {
      if (request === statusGenerationRef.current) {
        setRecoveryLoading(false);
      }
    }
  }

  async function sendPreparation(pending: PendingPreparation) {
    // A retry or new preparation supersedes any status read already in flight.
    statusGenerationRef.current += 1;
    setRecoveryLoading(false);
    const owner = ++busyOwnerRef.current;
    setBusy(true);
    setMessage(null);
    setKnownRejection(false);
    try {
      const result = await prepareTransactionsAction(pending.input);
      if (!result.ok) {
        setMessage({ tone: "error", text: result.message, details: result.details });
        setKnownRejection([
          "invalid_request", "empty_selection", "category_unavailable", "tag_unavailable",
          "tag_limit_exceeded", "selection_too_large", "request_too_large", "parent_hidden",
          "selection_stale", "classification_changed",
        ].includes(result.kind));
        return;
      }
      const operation = result.value.operation;
      if (operation.operation_id !== pending.input.operation_id) {
        setMessage({ tone: "error", text: "The preparation response could not be matched. Check operation status." });
        return;
      }
      setPendingPreparation(null);
      if (operation.status === "Applied") {
        showAppliedReceipt(receiptFromStatus(operation));
      } else if (operation.status === "Prepared") {
        setTerminalStatus(null);
        setTerminalOperationId(null);
        if (pending.intent === intentRef.current) {
          setPrepared(operation);
          setRecovery(null);
        } else {
          // The view changed while preparation was in flight. Do not revive
          // its confirmation; keep the operation available for safe abandon.
          setPrepared(null);
          setRecovery(operation);
        }
      } else {
        setPrepared(null);
        setRecovery(null);
        setTerminalStatus(operation.status);
        setTerminalOperationId(operation.operation_id);
      }
    } catch {
      setMessage({
        tone: "error",
        text: "The preparation outcome is uncertain. Check status or retry the same operation.",
      });
    } finally {
      if (owner === busyOwnerRef.current) {
        setBusy(false);
      }
    }
  }

  async function prepareBulkChange() {
    if (!hasBulkAction) {
      setMessage({ tone: "error", text: "Choose a category, tag change, or hide/restore action first." });
      return;
    }
    if (!allMatching && selectionCount === 0) {
      setMessage({ tone: "error", text: "Select at least one transaction first." });
      return;
    }

    const changes: {
      category_id?: string;
      add_tag_ids?: string[];
      remove_tag_ids?: string[];
      is_hidden_by_user?: boolean;
    } = {};
    if (bulkCategoryId) changes.category_id = bulkCategoryId;
    if (bulkAddTagIds.length > 0) changes.add_tag_ids = bulkAddTagIds;
    if (bulkRemoveTagIds.length > 0) changes.remove_tag_ids = bulkRemoveTagIds;
    if (bulkHidden !== null) changes.is_hidden_by_user = bulkHidden;

    if (operationIdRef.current) {
      setMessage({ tone: "error", text: "Resolve the existing operation before preparing another." });
      return;
    }

    const id = crypto.randomUUID();
    const input: TransactionPrepareInput = {
      operation_id: id,
      selection: allMatching
        ? { mode: "all_matching", filters: omitEmptyFilters(filters) }
        : {
            mode: "explicit",
            transactions: Array.from(selection.values()).map((entry) => ({
              id: entry.id,
              expected_version: entry.version,
            })),
          },
      changes,
    };
    const pending = { input, intent: ++intentRef.current };
    setPendingPreparation(pending);
    setCaptureContext({ listCount: data?.filtered_total ?? null, filters: omitEmptyFilters(filters) });
    setReceipt(null);
    setRecovery(null);
    rememberOperation(id);
    await sendPreparation(pending);
  }

  async function applyPrepared() {
    if (!prepared || !prepared.confirmation_hash) {
      return;
    }
    const operation = prepared;
    statusGenerationRef.current += 1;
    setRecoveryLoading(false);
    const intent = intentRef.current;
    const owner = ++busyOwnerRef.current;
    setBusy(true);
    setMessage(null);
    applyingRef.current = true;
    try {
      const result = await applyOperationAction(
        operation.operation_id,
        operation.confirmation_hash!,
        operation.selected_count,
      );
      if (!result.ok) {
        if (result.kind === "unavailable" || result.kind === "unauthorized") {
          const statusRequest = ++statusGenerationRef.current;
          const recovered = await loadOperationAction(operation.operation_id);
          if (
            statusRequest !== statusGenerationRef.current ||
            operationIdRef.current !== operation.operation_id
          ) {
            // A newer operation/status owns recovery now; do not install an
            // obsolete apply result over it.
            return;
          }
          if (recovered.ok) {
            const recoveredOperation = recovered.value;
            if (recoveredOperation.status === "Applied") {
              showAppliedReceipt(receiptFromStatus(recoveredOperation));
              return;
            }
            if (recoveredOperation.status === "Prepared") {
              setTerminalStatus(null);
              setTerminalOperationId(null);
              if (intent === intentRef.current) {
                setPrepared(recoveredOperation);
              } else {
                setPrepared(null);
                setRecovery(recoveredOperation);
              }
            } else {
              setPrepared(null);
              setRecovery(null);
              setTerminalStatus(recoveredOperation.status);
              setTerminalOperationId(recoveredOperation.operation_id);
              setMessage({
                tone: "status",
                text: recoveredOperation.status === "Expired"
                  ? "Status confirms the operation expired without applying."
                  : "Status confirms the operation was abandoned without applying.",
              });
              return;
            }
          }
          setMessage({
            tone: "error",
            text: "The apply outcome is uncertain. Keep this operation and check its status; do not create a replacement.",
          });
          return;
        }
        setMessage({ tone: "error", text: result.message, details: result.details });
        if (
          result.kind === "selection_stale" ||
          result.kind === "classification_changed"
        ) {
          setPrepared(null);
          setRecovery(operation);
          setTerminalStatus(null);
          setTerminalOperationId(null);
          refreshCurrentView();
        } else if (result.kind === "operation_expired" || result.kind === "operation_abandoned") {
          setPrepared(null);
          setRecovery(null);
          setTerminalStatus(result.kind === "operation_expired" ? "Expired" : "Abandoned");
          setTerminalOperationId(operation.operation_id);
        }
        return;
      }
      showAppliedReceipt(result.value);
    } catch {
      setMessage({
        tone: "error",
        text: "The apply outcome is uncertain. Check status before retrying; never substitute a new version.",
      });
    } finally {
      applyingRef.current = false;
      if (owner === busyOwnerRef.current) {
        setBusy(false);
      }
    }
  }

  async function abandonTrackedOperation(id: string) {
    if (applyingRef.current) {
      return;
    }
    // This deliberate terminal action supersedes any pending status read for
    // the same operation; a late status must not overwrite its result.
    statusGenerationRef.current += 1;
    setRecoveryLoading(false);
    const owner = ++busyOwnerRef.current;
    setBusy(true);
    setMessage(null);
    try {
      const result = await abandonOperationAction(id);
      if (operationIdRef.current !== id) {
        return;
      }
      if (!result.ok) {
        setMessage({ tone: "error", text: result.message, details: result.details });
        return;
      }
      if (result.value.status === "Applied") {
        showAppliedReceipt(result.value);
      } else if (result.value.status === "Abandoned" || result.value.status === "Expired") {
        setPrepared(null);
        setRecovery(null);
        setPendingPreparation(null);
        setTerminalStatus(result.value.status);
        setTerminalOperationId(id);
        setMessage({
          tone: "status",
          text: result.value.status === "Abandoned"
            ? "The operation was abandoned before applying."
            : "The operation expired before applying.",
        });
      } else {
        setMessage({ tone: "error", text: "The operation outcome is unresolved. Check its status again." });
      }
    } catch {
      if (operationIdRef.current === id) {
        setMessage({ tone: "error", text: "The abandon outcome is uncertain. Check operation status again." });
      }
    } finally {
      if (owner === busyOwnerRef.current) {
        setBusy(false);
      }
    }
  }

  async function cancelConfirmation() {
    if (prepared) {
      await abandonTrackedOperation(prepared.operation_id);
    }
  }

  async function abandonRecovered() {
    if (recovery) {
      await abandonTrackedOperation(recovery.operation_id);
    }
  }

  const openDetail = useCallback(async (id: string, origin: HTMLElement | null) => {
    const request = ++detailRequestRef.current;
    detailOriginRef.current = origin;
    setDetailId(id);
    setDetail(null);
    setDetailError(null);
    setDetailLoading(true);
    try {
      const result = await loadTransactionAction(id);
      if (request !== detailRequestRef.current) {
        return;
      }
      if (result.ok) {
        setDetail(result.value);
      } else {
        setDetailError(result.message);
      }
    } catch {
      if (request === detailRequestRef.current) {
        setDetailError("Something went wrong while loading that transaction.");
      }
    } finally {
      if (request === detailRequestRef.current) {
        setDetailLoading(false);
      }
    }
  }, []);

  const closeDetail = useCallback(() => {
    detailRequestRef.current += 1;
    setDetailId(null);
    setDetail(null);
    setDetailError(null);
    setDetailLoading(false);
    const origin = detailOriginRef.current;
    detailOriginRef.current = null;
    if (origin) {
      origin.focus();
    }
  }, []);

  useEffect(() => {
    if (detailId) {
      drawerRef.current?.focus();
    }
  }, [detailId]);

  const refreshList = useCallback(() => {
    void runQuery(filters, page, pageSize, sortBy, sortDirection);
  }, [filters, page, pageSize, runQuery, sortBy, sortDirection]);

  // A saved edit changes that row's version. Drop it from the checked set so a
  // later bulk preparation cannot send the pre-edit version, and invalidate an
  // executable confirmation that was built from the old values.
  const handleRowSaved = useCallback(
    (id: string) => {
      invalidateConfirmation();
      setSelection((current) => {
        if (!current.has(id)) {
          return current;
        }
        const next = new Map(current);
        next.delete(id);
        return next;
      });
      // The edit may finish after the user changed filters, page or sort. Use
      // the live view rather than this callback's render-time closure so the
      // reload cannot install old-filter rows under the new controls.
      const current = viewRef.current;
      void runQuery(
        current.filters,
        current.page,
        current.pageSize,
        current.sortBy,
        current.sortDirection,
      );
    },
    [invalidateConfirmation, runQuery],
  );

  // A stale or uncertain single edit retires its draft, tells the user why, and
  // refreshes the authoritative row before any retry can be attempted.
  const handleRowStale = useCallback(
    (id: string, text: string) => {
      setInlineEditorId((current) => (current === id ? null : current));
      setMessage({ tone: "error", text });
      handleRowSaved(id);
    },
    [handleRowSaved],
  );

  const totalPages = data
    ? Math.max(1, Math.ceil(data.filtered_total / Math.max(1, data.page_size)))
    : 1;
  const visibility = filters.visibility ?? "visible";
  const statementFilter = filters.statement_import_id
    ? data?.transactions.find((row) => row.statement_import.id === filters.statement_import_id)
        ?.statement_import ?? null
    : null;

  const preparedAction = prepared?.action ?? null;

  return (
    <AppShell
      account={account}
      activeNav="transactions"
      description="Search, filter, classify, and hide imported transactions. Counts describe rows, never financial totals."
      eyebrow="Workspace"
      title="Transactions"
    >
      {inventoryFailed ? (
        <div
          className="mt-6 rounded-2xl border border-warning-line bg-warning-soft px-5 py-4 text-sm font-semibold text-warning-ink"
          role="alert"
        >
          <p>
            Some accounts, categories, or tags could not be loaded. Classification controls are
            disabled until the page is refreshed. The list still loads when available.
          </p>
          <button
            className="mt-2 rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink"
            onClick={() => router.refresh()}
            type="button"
          >
            Retry inventory
          </button>
        </div>
      ) : null}

      {loadError ? (
        <section className="mt-6 rounded-2xl border border-warning-line bg-warning-soft p-5">
          <h2 className="text-lg font-semibold text-warning-ink">Transactions could not load</h2>
          <p className="mt-2 text-sm text-warning-ink" role="alert">
            {loadError}
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            <button
              className="rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink"
              onClick={refreshList}
              type="button"
            >
              Retry
            </button>
            <Link
              className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
              href="/transactions"
            >
              Clear filters
            </Link>
          </div>
        </section>
      ) : null}

      {message ? (
        <p
          className={
            message.tone === "error"
              ? "mt-4 rounded-xl border border-warning-line bg-warning-soft p-3 text-sm font-semibold text-warning-ink"
              : "mt-4 rounded-xl border border-line bg-elevated p-3 text-sm font-semibold text-ink"
          }
          role={message.tone === "error" ? "alert" : "status"}
        >
          {message.text}
          {message.details?.statements?.length ? (
            <span className="mt-1 block font-normal">
              Restore{" "}
              {message.details.statements.map((statement, index) => (
                <span key={statement.id}>
                  {index > 0 ? ", " : ""}
                  <Link
                    className="underline underline-offset-4"
                    href={`/imports/${statement.id}`}
                  >
                    {statement.statement_label}
                  </Link>
                </span>
              ))}{" "}
              in Imports first.
            </span>
          ) : null}
        </p>
      ) : null}

      <section
        aria-labelledby="filters-title"
        className="mt-6 rounded-[2rem] border border-line bg-surface p-5 card-shadow sm:p-6"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-lg font-bold text-ink" id="filters-title">
            Find transactions
          </h2>
          <span className="text-xs font-semibold text-muted">
            Search stays in memory and request bodies; it never enters the URL.
          </span>
        </div>

        <form
          className="mt-4 flex flex-wrap items-end gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            changeFilters({ search: searchInput });
          }}
        >
          <label className="min-w-[14rem] flex-1 text-sm font-semibold text-ink">
            Search description or tag
            <input
              className="mt-1 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              onChange={(event) => setSearchInput(event.target.value)}
              placeholder="One literal substring"
              type="search"
              value={searchInput}
            />
          </label>
          <button
            className="rounded-full border border-line bg-surface px-4 py-2 text-sm font-bold text-ink"
            type="submit"
          >
            Search
          </button>
        </form>

        <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <label className="text-sm font-semibold text-ink">
            From date
            <input
              className="mt-1 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              onChange={(event) => changeFilters({ start_date: event.target.value || undefined })}
              type="date"
              value={filters.start_date ?? ""}
            />
          </label>
          <label className="text-sm font-semibold text-ink">
            To date
            <input
              className="mt-1 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              onChange={(event) => changeFilters({ end_date: event.target.value || undefined })}
              type="date"
              value={filters.end_date ?? ""}
            />
          </label>
          <label className="text-sm font-semibold text-ink">
            Account
            <select
              className="mt-1 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              disabled={inventoryFailed}
              onChange={(event) =>
                changeFilters({ financial_account_id: event.target.value || undefined })
              }
              value={filters.financial_account_id ?? ""}
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
              className="mt-1 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              disabled={inventoryFailed}
              onChange={(event) =>
                changeFilters({ category_id: event.target.value || undefined })
              }
              value={filters.category_id ?? ""}
            >
              <option value="">All categories</option>
              {categories.map((category) => (
                <option key={category.id} value={category.id}>
                  {category.display_name} ({category.kind})
                  {category.is_archived ? " — archived" : ""}
                </option>
              ))}
            </select>
          </label>
          <label className="text-sm font-semibold text-ink">
            Merchant tag
            <select
              className="mt-1 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              disabled={inventoryFailed}
              onChange={(event) =>
                changeFilters({ merchant_tag: event.target.value || undefined })
              }
              value={filters.merchant_tag ?? ""}
            >
              <option value="">Any tag</option>
              <option value="untagged">Untagged</option>
              {tags.map((tag) => (
                <option key={tag.id} value={tag.id}>
                  {tag.display_name}
                  {tag.is_archived ? " — archived" : ""}
                </option>
              ))}
            </select>
          </label>
          <label className="text-sm font-semibold text-ink">
            Category kind
            <select
              className="mt-1 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              onChange={(event) =>
                changeFilters({
                  category_kind: (event.target.value || undefined) as
                    | TransactionFilters["category_kind"]
                    | undefined,
                })
              }
              value={filters.category_kind ?? ""}
            >
              <option value="">Any kind</option>
              <option value="Income">Income</option>
              <option value="Expense">Expense</option>
              <option value="Transfer">Transfer</option>
              <option value="Excluded">Excluded</option>
              <option value="Uncategorised">Uncategorised</option>
            </select>
          </label>
          <label className="text-sm font-semibold text-ink">
            Direction
            <select
              className="mt-1 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              onChange={(event) =>
                changeFilters({
                  direction: (event.target.value || undefined) as
                    | TransactionFilters["direction"]
                    | undefined,
                })
              }
              value={filters.direction ?? ""}
            >
              <option value="">Any direction</option>
              <option value="in">Money in</option>
              <option value="out">Money out</option>
              <option value="zero">Zero value</option>
            </select>
          </label>
          <label className="text-sm font-semibold text-ink">
            Sort
            <select
              className="mt-1 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              onChange={(event) => {
                const [nextSort, nextDirection] = event.target.value.split(":");
                viewRef.current = {
                  ...viewRef.current,
                  page: 1,
                  sortBy: nextSort as TransactionsInitialQuery["sortBy"],
                  sortDirection: nextDirection as TransactionsInitialQuery["sortDirection"],
                };
                setSortBy(nextSort as TransactionsInitialQuery["sortBy"]);
                setSortDirection(nextDirection as TransactionsInitialQuery["sortDirection"]);
                setPage(1);
                clearSelection();
                syncUrl(filters, 1, pageSize, nextSort, nextDirection, null);
                void runQuery(filters, 1, pageSize, nextSort, nextDirection);
              }}
              value={`${sortBy}:${sortDirection}`}
            >
              <option value="date:desc">Newest date first</option>
              <option value="date:asc">Oldest date first</option>
              <option value="amount:desc">Largest amount first</option>
              <option value="amount:asc">Smallest amount first</option>
              <option value="description:asc">Description A–Z</option>
              <option value="description:desc">Description Z–A</option>
            </select>
          </label>
        </div>

        {filters.statement_import_id ? (
          <p className="mt-3 text-sm text-muted">
            Showing{" "}
            {statementFilter ? (
              <Link
                className="underline underline-offset-4"
                href={`/imports/${statementFilter.id}`}
              >
                {statementFilter.statement_label}
              </Link>
            ) : (
              "the selected statement"
            )}
            .{" "}
            <button
              className="font-semibold text-ink underline underline-offset-4"
              onClick={() => changeFilters({ statement_import_id: undefined })}
              type="button"
            >
              Clear statement filter
            </button>
          </p>
        ) : null}

        <div className="mt-4 flex flex-wrap items-center gap-2" role="group" aria-label="Visibility">
          {(["visible", "hidden", "all"] as const).map((value) => (
            <button
              aria-pressed={visibility === value}
              className={
                visibility === value
                  ? "rounded-full bg-ink px-4 py-2 text-sm font-bold text-page"
                  : "rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
              }
              key={value}
              onClick={() => changeFilters({ visibility: value })}
              type="button"
            >
              {value === "visible" ? "Visible" : value === "hidden" ? "Hidden" : "All"}
            </button>
          ))}
          <label className="ml-auto text-sm font-semibold text-ink">
            Page size
            <select
              className="ml-2 rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              onChange={(event) => {
                const nextSize = Number(event.target.value);
                viewRef.current = { ...viewRef.current, page: 1, pageSize: nextSize };
                setPageSize(nextSize);
                setPage(1);
                clearSelection();
                syncUrl(filters, 1, nextSize, sortBy, sortDirection, null);
                void runQuery(filters, 1, nextSize, sortBy, sortDirection);
              }}
              value={pageSize}
            >
              <option value={25}>25</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
            </select>
          </label>
        </div>
      </section>

      {data ? (
        <section
          aria-labelledby="list-title"
          className="mt-6 rounded-[2rem] border border-line bg-surface p-5 card-shadow sm:p-6"
        >
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <h2 className="text-lg font-bold text-ink" id="list-title">
                Matching transactions
              </h2>
              <p className="mt-1 text-sm text-muted" role="status">
                {data.matching_visible_count} visible and {data.matching_hidden_count} hidden
                match these filters; showing {data.filtered_total}{" "}
                {visibility === "all"
                  ? "rows"
                  : visibility === "hidden"
                    ? "hidden rows"
                    : "visible rows"}{" "}
                of {data.matching_total}. Page {data.page} of {totalPages}.
                {data.inventory_count === 0
                  ? " Your workspace has no transactions yet."
                  : ""}
              </p>
            </div>
            <label className="flex items-center gap-2 text-sm font-semibold text-ink">
              <input
                checked={allPageSelected}
                disabled={rows.length === 0 || loading}
                onChange={togglePage}
                ref={selectPageRef}
                type="checkbox"
              />
              Select this page
            </label>
          </div>

          {data.filtered_total === 0 ? (
            <div className="mt-4 rounded-2xl border border-line bg-page p-5">
              {data.inventory_count === 0 ? (
                <>
                  <h3 className="text-lg font-semibold text-ink">No transactions yet</h3>
                  <p className="mt-2 text-sm leading-6 text-muted">
                    Transactions appear after a statement import. Create a ZAR account, categories
                    and tags in{" "}
                    <Link className="underline underline-offset-4" href="/settings">
                      Settings
                    </Link>
                    , then import a statement in{" "}
                    <Link className="underline underline-offset-4" href="/imports">
                      Imports
                    </Link>
                    .
                  </p>
                </>
              ) : visibility === "visible" && data.matching_hidden_count > 0 ? (
                <>
                  <h3 className="text-lg font-semibold text-ink">All matching rows are hidden</h3>
                  <p className="mt-2 text-sm leading-6 text-muted">
                    {data.matching_hidden_count} hidden transaction
                    {data.matching_hidden_count === 1 ? "" : "s"} match these filters.
                  </p>
                  <button
                    className="mt-3 rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
                    onClick={() => changeFilters({ visibility: "hidden" })}
                    type="button"
                  >
                    Open the Hidden view
                  </button>
                </>
              ) : (
                <>
                  <h3 className="text-lg font-semibold text-ink">No matching transactions</h3>
                  <p className="mt-2 text-sm leading-6 text-muted">
                    Your filters matched no rows on this page view. Counts above are the truthful
                    totals; page numbers are not totals.
                  </p>
                  <Link
                    className="mt-3 inline-flex rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
                    href="/transactions"
                  >
                    Clear filters
                  </Link>
                </>
              )}
            </div>
          ) : (
            <ul className="mt-4 space-y-3">
              {rows.map((row) => {
                const badge = hiddenBadge(row);
                const isSelected = selection.has(row.id);
                return (
                  <li
                    className="rounded-2xl border border-line bg-page p-4"
                    key={row.id}
                  >
                    <div className="flex flex-wrap items-start gap-3">
                      <input
                        aria-label={`Select transaction ${row.description || "no description"} on ${row.transaction_date} for ${amountLabel(row)}`}
                        checked={isSelected}
                        className="mt-1"
                        disabled={loading}
                        onChange={() => toggleRow(row)}
                        type="checkbox"
                      />
                      <div className="min-w-0 flex-1">
                        <button
                          className="block w-full text-left"
                          onClick={(event) => void openDetail(row.id, event.currentTarget)}
                          type="button"
                        >
                          <span className="block truncate text-sm font-semibold text-ink">
                            {row.description.trim() === "" ? "No description" : row.description}
                          </span>
                          <span className="mt-1 block text-xs text-muted">
                            {row.transaction_date} · {row.financial_account.display_name}
                            {row.financial_account.is_archived ? " (archived)" : ""} · version{" "}
                            {row.version}
                          </span>
                        </button>
                        <div className="mt-2 flex flex-wrap gap-1.5 text-xs">
                          <span className="rounded-full border border-line bg-surface px-2 py-1 font-semibold text-ink">
                            {row.category.display_name} ({row.category.kind})
                          </span>
                          {row.category.is_archived ? (
                            <span className="rounded-full border border-line bg-elevated px-2 py-1 font-semibold text-muted">
                              Archived category
                            </span>
                          ) : null}
                          {row.merchant_tags.map((tag) => (
                            <span
                              className="rounded-full border border-line bg-surface px-2 py-1 font-semibold text-ink"
                              key={tag.id}
                            >
                              {tag.display_name}
                              {tag.is_archived ? " (archived)" : ""}
                            </span>
                          ))}
                          {badge ? (
                            <span className="rounded-full border border-warning-line bg-warning-soft px-2 py-1 font-semibold text-warning-ink">
                              {badge}
                            </span>
                          ) : null}
                        </div>
                      </div>
                      <span className="font-mono text-lg font-semibold tabular-nums text-ink">
                        {amountLabel(row)}
                      </span>
                    </div>
                    <div className="mt-2 flex flex-wrap items-center gap-2">
                      <button
                        aria-expanded={inlineEditorId === row.id}
                        className="rounded-full border border-line px-3 py-1.5 text-xs font-bold text-ink disabled:opacity-50"
                        disabled={inventoryFailed}
                        onClick={() =>
                          setInlineEditorId((current) =>
                            current === row.id ? null : row.id,
                          )
                        }
                        type="button"
                      >
                        {inlineEditorId === row.id ? "Close inline editor" : "Edit inline"}
                      </button>
                    </div>
                    {inlineEditorId === row.id ? (
                      <TransactionInlineEditor
                        categories={categories}
                        classificationDisabled={inventoryFailed}
                        onSaved={() => handleRowSaved(row.id)}
                        onStale={(text) => handleRowStale(row.id, text)}
                        row={row}
                        tags={tags}
                      />
                    ) : null}
                  </li>
                );
              })}
            </ul>
          )}

          <nav aria-label="Transaction pages" className="mt-4 flex flex-wrap items-center gap-2">
            <button
              className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink disabled:opacity-50"
              disabled={data.page <= 1 || loading}
              onClick={() => changePage(data.page - 1)}
              type="button"
            >
              Previous
            </button>
            <span className="text-sm text-muted">
              Page {data.page} of {totalPages}
            </span>
            <button
              className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink disabled:opacity-50"
              disabled={data.page >= totalPages || loading}
              onClick={() => changePage(data.page + 1)}
              type="button"
            >
              Next
            </button>
          </nav>
        </section>
      ) : null}

      <section
        aria-labelledby="bulk-title"
        className="mt-6 rounded-[2rem] border border-line bg-surface p-5 card-shadow sm:p-6"
      >
        <h2 className="text-lg font-bold text-ink" id="bulk-title">
          Bulk classification and visibility
        </h2>
        <p className="mt-1 text-sm text-muted" role="status">
          {selectionLabel}{" "}
          {allMatching
            ? "Later matches are not included. The confirmation captures this exact scope."
            : "Selections persist across pages but not across filter changes."}
        </p>
        <div className="mt-3 flex flex-wrap gap-2">
          <button
            className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            disabled={rows.length === 0 || allMatching}
            onClick={toggleAllMatching}
            type="button"
          >
            Select all matching filters
          </button>
          <button
            className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            disabled={!allMatching && selectionCount === 0}
            onClick={() => clearSelection()}
            type="button"
          >
            Clear selection
          </button>
          {loading ? (
            <span className="text-sm text-muted" role="status">
              Refreshing…
            </span>
          ) : null}
        </div>

        <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <label className="text-sm font-semibold text-ink">
            Set category
            <select
              className="mt-1 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              disabled={!canAssignCategories || busy || operationId !== null}
              onChange={(event) => setBulkCategoryId(event.target.value)}
              value={bulkCategoryId}
            >
              <option value="">Keep category</option>
              {activeCategories(categories).map((category) => (
                <option key={category.id} value={category.id}>
                  {category.display_name} ({category.kind})
                </option>
              ))}
            </select>
          </label>
          <label className="text-sm font-semibold text-ink">
            Add tags
            <select
              className="mt-1 h-24 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              disabled={!canAssignCategories || busy || operationId !== null}
              multiple
              onChange={(event) =>
                setBulkAddTagIds(
                  Array.from(event.target.selectedOptions, (option) => option.value),
                )
              }
              value={bulkAddTagIds}
            >
              {activeTags(tags).map((tag) => (
                <option key={tag.id} value={tag.id}>
                  {tag.display_name}
                </option>
              ))}
            </select>
          </label>
          <label className="text-sm font-semibold text-ink">
            Remove tags
            <select
              className="mt-1 h-24 w-full rounded-xl border border-line bg-page px-3 py-2 text-sm text-ink"
              disabled={!canAssignCategories || busy || operationId !== null}
              multiple
              onChange={(event) =>
                setBulkRemoveTagIds(
                  Array.from(event.target.selectedOptions, (option) => option.value),
                )
              }
              value={bulkRemoveTagIds}
            >
              {tags.map((tag) => (
                <option key={tag.id} value={tag.id}>
                  {tag.display_name}
                  {tag.is_archived ? " — archived" : ""}
                </option>
              ))}
            </select>
          </label>
          <div className="text-sm font-semibold text-ink">
            Visibility
            <div className="mt-1 flex flex-wrap gap-2">
              <button
                aria-pressed={bulkHidden === true}
                className={
                  bulkHidden === true
                    ? "rounded-full bg-ink px-4 py-2 text-sm font-bold text-page"
                    : "rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
                }
                disabled={busy || operationId !== null}
                onClick={() => setBulkHidden((current) => (current === true ? null : true))}
                type="button"
              >
                Hide
              </button>
              <button
                aria-pressed={bulkHidden === false}
                className={
                  bulkHidden === false
                    ? "rounded-full bg-ink px-4 py-2 text-sm font-bold text-page"
                    : "rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
                }
                disabled={busy || operationId !== null}
                onClick={() => setBulkHidden((current) => (current === false ? null : false))}
                type="button"
              >
                Restore
              </button>
            </div>
            <p className="mt-1 text-xs font-normal text-muted">
              Restore refuses wholly when any selected transaction has a hidden parent statement.
            </p>
          </div>
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-2">
          <button
            className="rounded-full bg-ink px-5 py-2.5 text-sm font-bold text-page disabled:opacity-60"
            disabled={!canPrepare}
            onClick={() => void prepareBulkChange()}
            type="button"
          >
            Review change
          </button>
          <button
            className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            disabled={busy}
            onClick={resetBulkControls}
            type="button"
          >
            Reset change
          </button>
          <span className="text-xs text-muted">
            Selected count = changed + already matching; nothing is written until Apply.
          </span>
        </div>
      </section>

      {prepared ? (
        <section
          aria-labelledby="confirmation-title"
          className="mt-6 rounded-[2rem] border border-accent-soft-line bg-accent-soft p-5 sm:p-6"
        >
          <h2 className="text-lg font-bold text-accent-soft-ink" id="confirmation-title">
            Confirm this change
          </h2>
          <p className="mt-2 text-sm leading-6 text-accent-soft-ink">
            {prepared.selection_mode === "all_matching"
              ? `${prepared.selected_count} transactions selected across all pages as of `
              : `${prepared.selected_count} explicitly selected transaction${prepared.selected_count === 1 ? "" : "s"} as of `}
            {formatTimestamp(prepared.captured_at)}. Later matches are not included.{" "}
            {prepared.changed_count} will change and {prepared.unchanged_count} already match.
            Expires {formatTimestamp(prepared.expires_at)}.
          </p>
          {prepared.selection_mode === "all_matching" && captureContext ? (
            <div className="mt-2 text-sm text-accent-soft-ink">
              <p>
                Captured filters:{" "}
                {Object.entries(captureContext.filters).length === 0
                  ? "all visible transactions"
                  : Object.entries(captureContext.filters)
                      .map(([key, value]) => `${key.replaceAll("_", " ")}: ${value}`)
                      .join(" · ")}
              </p>
              {captureContext.listCount !== null && captureContext.listCount !== prepared.selected_count ? (
                <p className="mt-1 font-semibold">
                  The earlier list showed {captureContext.listCount}; the saved scope now contains{" "}
                  {prepared.selected_count}. Review this updated count before applying.
                </p>
              ) : null}
            </div>
          ) : null}
          <p className="mt-2 text-sm text-accent-soft-ink">
            Action:{" "}
            {preparedAction?.category
              ? `category “${preparedAction.category.display_name}”; `
              : ""}
            {preparedAction?.add_tags.length
              ? `add ${preparedAction.add_tags.map((tag) => tag.display_name).join(", ")}; `
              : ""}
            {preparedAction?.remove_tags.length
              ? `remove ${preparedAction.remove_tags.map((tag) => tag.display_name).join(", ")}; `
              : ""}
            {preparedAction?.is_hidden_by_user === true
              ? "hide these records (duplicate protection stays)."
              : preparedAction?.is_hidden_by_user === false
                ? "restore these records individually."
                : ""}
          </p>
          {preparedAction?.stale ? (
            <p className="mt-2 rounded-xl border border-warning-line bg-warning-soft p-3 text-sm font-semibold text-warning-ink">
              A chosen category or tag changed after this confirmation was prepared. Apply will
              refuse; cancel and prepare again.
            </p>
          ) : null}
          <div className="mt-4 flex flex-wrap gap-2">
            <button
              className="rounded-full bg-ink px-5 py-2.5 text-sm font-bold text-page disabled:opacity-60"
              disabled={busy || preparedAction?.stale === true}
              onClick={() => void applyPrepared()}
              type="button"
            >
              Apply change
            </button>
            <button
              className="rounded-full border border-line bg-surface px-4 py-2 text-sm font-bold text-ink"
              disabled={busy}
              onClick={() => void cancelConfirmation()}
              type="button"
            >
              Cancel and abandon
            </button>
          </div>
        </section>
      ) : null}

      {recoveryLoading ? (
        <p className="mt-6 text-sm text-muted" role="status">
          Resolving the saved confirmation…
        </p>
      ) : null}

      {operationId && !prepared && !recovery && !receipt && !terminalStatus ? (
        <section
          aria-labelledby="unresolved-operation-title"
          className="mt-6 rounded-[2rem] border border-warning-line bg-warning-soft p-5 sm:p-6"
        >
          <h2 className="text-lg font-bold text-warning-ink" id="unresolved-operation-title">
            Operation status unresolved
          </h2>
          <p className="mt-2 text-sm leading-6 text-warning-ink">
            The operation identity is retained. A temporary missing or unavailable status does
            not prove that an in-flight preparation was cancelled.
          </p>
          <div className="mt-4 flex flex-wrap gap-2">
            <button
              className="rounded-full border border-line bg-surface px-4 py-2 text-sm font-bold text-ink"
              disabled={busy || recoveryLoading}
              onClick={() => void checkOperationStatus(operationId)}
              type="button"
            >
              Check operation status
            </button>
            {pendingPreparation ? (
              <button
                className="rounded-full border border-line bg-surface px-4 py-2 text-sm font-bold text-ink"
                disabled={busy || recoveryLoading}
                onClick={() => void sendPreparation(pendingPreparation)}
                type="button"
              >
                Retry same preparation
              </button>
            ) : null}
            {knownRejection ? (
              <button
                className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
                disabled={busy || recoveryLoading}
                onClick={acknowledgeOutcome}
                type="button"
              >
                Acknowledge rejection
              </button>
            ) : null}
          </div>
        </section>
      ) : null}

      {recovery ? (
        <section
          aria-labelledby="recovery-title"
          className="mt-6 rounded-[2rem] border border-warning-line bg-warning-soft p-5 sm:p-6"
        >
          <h2 className="text-lg font-bold text-warning-ink" id="recovery-title">
            Recovered confirmation
          </h2>
          <p className="mt-2 text-sm leading-6 text-warning-ink">
            A previously prepared operation is still pending: {recovery.selected_count} selected,{" "}
            {recovery.changed_count} would change, {recovery.unchanged_count} already match.
            Expires {formatTimestamp(recovery.expires_at)}. The original private search and filter
            context is not current or cannot be reconstructed after reload, so this screen does not
            apply it.
          </p>
          {recovery.action?.stale ? (
            <p className="mt-2 text-sm font-semibold text-warning-ink">
              A chosen category or tag has since changed; applying would be refused anyway.
            </p>
          ) : null}
          <div className="mt-4 flex flex-wrap gap-2">
            <button
              className="rounded-full border border-warning-line bg-surface px-4 py-2 text-sm font-bold text-warning-ink"
              disabled={busy}
              onClick={() => void abandonRecovered()}
              type="button"
            >
              Abandon and prepare again
            </button>
            <button
              className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
              disabled={busy || recoveryLoading}
              onClick={() => void checkOperationStatus(recovery.operation_id)}
              type="button"
            >
              Check operation status
            </button>
          </div>
        </section>
      ) : null}

      {terminalStatus ? (
        <section
          aria-labelledby="terminal-operation-title"
          className="mt-6 rounded-[2rem] border border-line bg-elevated p-5 sm:p-6"
        >
          <h2 className="text-lg font-bold text-ink" id="terminal-operation-title">
            {terminalStatus === "Expired" ? "Operation expired" : "Operation abandoned"}
          </h2>
          <p className="mt-2 text-sm text-muted">
            The saved operation is terminal and did not apply. Acknowledge this outcome before
            starting a new preparation.
          </p>
          <button
            className="mt-3 rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            onClick={acknowledgeOutcome}
            type="button"
          >
            Acknowledge outcome
          </button>
        </section>
      ) : null}

      {receipt ? (
        <section
          aria-labelledby="receipt-title"
          className="mt-6 rounded-[2rem] border border-line bg-elevated p-5 sm:p-6"
        >
          <h2 className="text-lg font-bold text-ink" id="receipt-title">
            Applied receipt
          </h2>
          <p className="mt-2 text-sm leading-6 text-muted">
            {receipt.changed_count} changed, {receipt.unchanged_count} already matched,{" "}
            {receipt.selected_count} in the saved scope.
            {receipt.applied_at ? ` Applied ${formatTimestamp(receipt.applied_at)}.` : ""} The
            receipt survives reloads and statement deletion.
          </p>
          <button
            className="mt-3 rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            onClick={acknowledgeOutcome}
            type="button"
          >
            Dismiss receipt
          </button>
        </section>
      ) : null}

      {detailId ? (
        <div className="fixed inset-0 z-40 flex items-stretch justify-end bg-ink/40">
          <button
            aria-label="Close transaction detail"
            className="absolute inset-0 cursor-default"
            onClick={closeDetail}
            type="button"
          />
          <div
            aria-label="Transaction detail drawer"
            aria-modal="true"
            className="relative z-10 h-full w-full max-w-2xl overflow-y-auto bg-page p-4 shadow-2xl focus:outline-none sm:p-6"
            onKeyDown={(event) => {
              if (event.key === "Escape") {
                event.preventDefault();
                closeDetail();
                return;
              }
              if (event.key !== "Tab") {
                return;
              }
              const panel = drawerRef.current;
              if (!panel) {
                return;
              }
              const focusable = Array.from(
                panel.querySelectorAll<HTMLElement>(
                  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
                ),
              );
              if (focusable.length === 0) {
                event.preventDefault();
                panel.focus();
                return;
              }
              const first = focusable[0];
              const last = focusable[focusable.length - 1];
              const active = document.activeElement;
              if (event.shiftKey) {
                if (
                  active === first ||
                  active === panel ||
                  active === null ||
                  !panel.contains(active)
                ) {
                  event.preventDefault();
                  last.focus();
                }
                return;
              }
              if (active === last) {
                event.preventDefault();
                first.focus();
              }
            }}
            ref={drawerRef}
            role="dialog"
            tabIndex={-1}
          >
            {detailLoading ? (
              <p className="text-sm text-muted" role="status">
                Loading transaction detail…
              </p>
            ) : null}
            {detailError ? (
              <div className="rounded-2xl border border-warning-line bg-warning-soft p-4">
                <p className="text-sm font-semibold text-warning-ink" role="alert">
                  {detailError}
                </p>
                <Link
                  className="mt-3 inline-flex rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
                  href="/transactions"
                >
                  Back to transactions
                </Link>
              </div>
            ) : null}
            {detail ? (
              <TransactionDetailView
                categories={categories}
                classificationDisabled={inventoryFailed}
                initialDetail={detail}
                key={detail.transaction.id}
                onClose={closeDetail}
                onSaved={() => handleRowSaved(detail.transaction.id)}
                onUnavailable={() => {
                  setDetail(null);
                  setDetailError(
                    "This transaction no longer exists in your workspace. It may have been deleted with its statement.",
                  );
                }}
                tags={tags}
              />
            ) : null}
          </div>
        </div>
      ) : null}
    </AppShell>
  );
}
