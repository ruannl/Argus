import { redirect } from "next/navigation";

import { auth } from "@/auth";
import WorkspaceExpired from "@/components/workspace-expired";
import {
  getCurrentAccount,
  listCategories,
  listFinancialAccounts,
  listMerchantTags,
  queryTransactions,
} from "@/lib/server/backend-client";
import type {
  Category,
  CurrentAccount,
  FinancialAccount,
  MerchantTag,
  TransactionFilters,
  TransactionPage,
} from "@/lib/server/backend-client";

import TransactionsWorkspace from "./transactions-workspace";
import type { TransactionsInitialQuery } from "./transactions-workspace";

export const dynamic = "force-dynamic";

const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

const ALLOWED_PARAMS = new Set([
  "start_date",
  "end_date",
  "financial_account_id",
  "statement_import_id",
  "category_id",
  "merchant_tag",
  "category_kind",
  "direction",
  "visibility",
  "sort_by",
  "sort_direction",
  "page",
  "page_size",
  "operation_id",
]);

type SearchParams = Record<string, string | string[] | undefined>;

type ParsedState = {
  query: TransactionsInitialQuery;
  operationId: string | null;
  error: string | null;
};

function isUuid(value: string): boolean {
  return UUID_PATTERN.test(value);
}

function isDate(value: string): boolean {
  if (!DATE_PATTERN.test(value)) {
    return false;
  }
  const parsed = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}

function invalid(): ParsedState {
  return {
    query: {
      filters: { visibility: "visible" },
      page: 1,
      pageSize: 25,
      sortBy: "date",
      sortDirection: "desc",
    },
    operationId: null,
    error:
      "Some transaction filters in the address were invalid. Clear them to load the default view.",
  };
}

function parseState(params: SearchParams): ParsedState {
  const filters: TransactionFilters = { visibility: "visible" };
  let operationId: string | null = null;

  for (const key of Object.keys(params)) {
    if (!ALLOWED_PARAMS.has(key)) {
      return invalid();
    }
    if (Array.isArray(params[key])) {
      return invalid();
    }
  }

  const value = (key: string): string | undefined => {
    const raw = params[key];
    return typeof raw === "string" && raw !== "" ? raw : undefined;
  };

  const start = value("start_date");
  const end = value("end_date");
  if (start !== undefined) {
    if (!isDate(start)) {
      return invalid();
    }
    filters.start_date = start;
  }
  if (end !== undefined) {
    if (!isDate(end)) {
      return invalid();
    }
    filters.end_date = end;
  }
  if (filters.start_date && filters.end_date && filters.start_date > filters.end_date) {
    return invalid();
  }

  for (const key of [
    "financial_account_id",
    "statement_import_id",
    "category_id",
  ] as const) {
    const item = value(key);
    if (item !== undefined) {
      if (!isUuid(item)) {
        return invalid();
      }
      filters[key] = item;
    }
  }

  const merchantTag = value("merchant_tag");
  if (merchantTag !== undefined) {
    if (merchantTag !== "untagged" && !isUuid(merchantTag)) {
      return invalid();
    }
    filters.merchant_tag = merchantTag;
  }

  const kind = value("category_kind");
  if (kind !== undefined) {
    if (!["Income", "Expense", "Transfer", "Excluded", "Uncategorised"].includes(kind)) {
      return invalid();
    }
    filters.category_kind = kind as TransactionFilters["category_kind"];
  }

  const direction = value("direction");
  if (direction !== undefined) {
    if (!["in", "out", "zero"].includes(direction)) {
      return invalid();
    }
    filters.direction = direction as TransactionFilters["direction"];
  }

  const visibility = value("visibility");
  if (visibility !== undefined) {
    if (!["visible", "hidden", "all"].includes(visibility)) {
      return invalid();
    }
    filters.visibility = visibility as TransactionFilters["visibility"];
  }

  const pageRaw = value("page");
  const page = pageRaw === undefined ? 1 : Number(pageRaw);
  if (!Number.isInteger(page) || page < 1) {
    return invalid();
  }

  const pageSizeRaw = value("page_size");
  const pageSize = pageSizeRaw === undefined ? 25 : Number(pageSizeRaw);
  if (![25, 50, 100].includes(pageSize)) {
    return invalid();
  }

  const sortBy = value("sort_by") ?? "date";
  if (!["date", "amount", "description"].includes(sortBy)) {
    return invalid();
  }
  const sortDirection = value("sort_direction") ?? "desc";
  if (!["asc", "desc"].includes(sortDirection)) {
    return invalid();
  }

  const rawOperation = value("operation_id");
  if (rawOperation !== undefined) {
    if (!isUuid(rawOperation)) {
      return invalid();
    }
    operationId = rawOperation;
  }

  return {
    query: {
      filters,
      page,
      pageSize,
      sortBy: sortBy as TransactionsInitialQuery["sortBy"],
      sortDirection: sortDirection as TransactionsInitialQuery["sortDirection"],
    },
    operationId,
    error: null,
  };
}

function queryErrorMessage(kind: string): string {
  switch (kind) {
    case "unauthorized":
      return "Your session expired. Sign in again to load transactions.";
    case "not_found":
      return "A filter referred to an item outside your workspace. Clear the filters and retry.";
    case "rate_limited":
      return "Too many requests. Wait a few minutes before retrying.";
    default:
      return "Transactions could not be loaded. Retry the view.";
  }
}

export default async function TransactionsPage({
  searchParams,
}: {
  searchParams: Promise<SearchParams>;
}) {
  const backendToken = (await auth())?.user?.backendToken;
  if (!backendToken) {
    redirect("/login");
  }

  let account: CurrentAccount | null = null;
  try {
    account = await getCurrentAccount(backendToken);
  } catch {
    // Invalid, expired, revoked, and unavailable backend sessions fail closed.
  }
  if (!account) {
    return <WorkspaceExpired />;
  }

  const parsed = parseState(await searchParams);

  let accounts: FinancialAccount[] = [];
  let categories: Category[] = [];
  let tags: MerchantTag[] = [];
  let inventoryFailed = false;
  try {
    const [accountsResult, categoriesResult, tagsResult] = await Promise.all([
      listFinancialAccounts(backendToken),
      listCategories(backendToken),
      listMerchantTags(backendToken),
    ]);
    if (accountsResult.ok) {
      accounts = accountsResult.value.accounts;
    } else {
      inventoryFailed = true;
    }
    if (categoriesResult.ok) {
      categories = categoriesResult.value.categories;
    } else {
      inventoryFailed = true;
    }
    if (tagsResult.ok) {
      tags = tagsResult.value.merchant_tags;
    } else {
      inventoryFailed = true;
    }
  } catch {
    inventoryFailed = true;
  }

  let initialPage: TransactionPage | null = null;
  let initialError = parsed.error;
  if (!parsed.error) {
    try {
      const result = await queryTransactions(backendToken, {
        filters: parsed.query.filters,
        page: parsed.query.page,
        page_size: parsed.query.pageSize,
        sort_by: parsed.query.sortBy,
        sort_direction: parsed.query.sortDirection,
      });
      if (result.ok) {
        initialPage = result.value;
      } else {
        initialError = queryErrorMessage(result.kind);
      }
    } catch {
      initialError = "Transactions could not be loaded. Retry the view.";
    }
  }

  return (
    <TransactionsWorkspace
      account={account}
      accounts={accounts}
      categories={categories}
      initialError={initialError}
      initialOperationId={parsed.operationId}
      initialPage={initialPage}
      initialQuery={parsed.query}
      inventoryFailed={inventoryFailed}
      tags={tags}
    />
  );
}
