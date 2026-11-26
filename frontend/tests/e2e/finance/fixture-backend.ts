/**
 * Synthetic presentation fixture, NOT an authorization or finance integration test.
 * No database, real credentials, customer records, uploads or external services.
 * Runs only on loopback with an explicit test application/session token.
 */
import { createServer } from "node:http";
import type {
  TransactionPage,
  ImportReviewDetail,
  ReportResponse,
  ReportFilterDescriptor,
  ReportCategoryBreakdown,
  ReportCategoryRank,
} from "../../../src/lib/server/backend-client";

export const ids = {
  account: "11111111-1111-4111-8111-111111111111",
  category: "22222222-2222-4222-8222-222222222222",
  tag: "33333333-3333-4333-8333-333333333333",
  transaction: "44444444-4444-4444-8444-444444444444",
  statement: "55555555-5555-4555-8555-555555555555",
  review: "66666666-6666-4666-8666-666666666666",
};
const date = "2026-11-01T00:00:00Z";
const future = "2099-11-22T00:00:00Z";
const times = { created_at: date, updated_at: date };
const category = {
  id: ids.category,
  display_name: "Household essentials and groceries",
  kind: "Expense" as const,
  is_system: false,
  is_default: false,
  is_archived: false,
  transaction_count: 2,
  ...times,
};
const tag = {
  id: ids.tag,
  display_name: "Synthetic corner shop",
  is_archived: false,
  transaction_count: 1,
  ...times,
};
const financialAccount = {
  id: ids.account,
  display_name: "Synthetic everyday account",
  institution: "Example institution",
  currency: "ZAR",
  is_archived: false,
  transaction_count: 2,
  ...times,
};
const transaction = {
  id: ids.transaction,
  version: 1,
  transaction_date: "2026-11-03",
  description: "Synthetic groceries — monthly household purchase",
  amount: "-1234.56",
  currency: "ZAR" as const,
  direction: "out" as const,
  financial_account: {
    id: ids.account,
    display_name: financialAccount.display_name,
    institution: financialAccount.institution,
    is_archived: false,
  },
  category,
  category_confirmed: true,
  merchant_tags: [tag],
  statement_import: {
    id: ids.statement,
    statement_label: "Synthetic November statement",
    source_type: "fnb_csv",
    is_hidden: false,
  },
  is_hidden_by_user: false,
  effective_hidden: false,
  hidden_reason: "none" as const,
};
const diagnostic = { count: 0, money_in: "0.00", money_out: "0.00", absolute: "0.00" };
const money = {
  money_in: "0.00",
  money_out: "1234.56",
  net: "-1234.56",
  included_count: 2,
  transfers: diagnostic,
  excluded: diagnostic,
};
const transactions: TransactionPage = {
  transactions: [
    transaction,
    {
      ...transaction,
      id: "77777777-7777-4777-8777-777777777777",
      description: "Synthetic unconfirmed zero-value record",
      amount: "0.00",
      direction: "zero",
      category_confirmed: false,
      merchant_tags: [],
    },
  ],
  inventory_count: 2,
  matching_visible_count: 2,
  matching_hidden_count: 0,
  matching_total: 2,
  filtered_total: 2,
  page: 1,
  page_size: 25,
  sort_by: "date",
  sort_direction: "desc",
  money_totals: money,
  suggestions: [
    {
      transaction_id: transaction.id,
      suggested_tags: [
        {
          id: "tag-synthetic-row-suggestion",
          display_name: "Synthetic row suggestion",
          is_archived: false,
        },
      ],
      has_more_suggestions: false,
    },
  ],
};
const statement = {
  id: ids.statement,
  financial_account_id: ids.account,
  source_type: "fnb_csv",
  statement_label: transaction.statement_import.statement_label,
  start_date: "2026-11-01",
  end_date: "2026-11-21",
  total_rows: 2,
  imported_count: 2,
  suppressed_count: 0,
  skipped_count: 0,
  possible_duplicate_count: 0,
  exact_override_count: 0,
  statement_override_count: 0,
  is_hidden: false,
  version: 1,
  live_hidden_count: 0,
  ...times,
};
const review: ImportReviewDetail = {
  summary: {
    id: ids.review,
    financial_account_id: ids.account,
    source_kind: "fnb_csv",
    start_date: "2026-11-01",
    end_date: "2026-11-21",
    version: 1,
    status: "Prepared",
    expires_at: future,
    total_rows: 1,
    imported_count: 1,
    suppressed_count: 0,
    skipped_count: 0,
    invalid_unskipped_count: 1,
    within_file_pending_count: 0,
    possible_duplicate_count: 0,
    exact_override_count: 0,
    categorised_count: 0,
    statement_override: false,
    statement_override_count: 0,
    summary_hash: "synthetic-review-hash",
  },
  page: {
    page: 1,
    page_size: 25,
    total: 1,
    rows: [
      {
        row_id: "row-1",
        source_line: 2,
        source_page: null,
        source_row_number: 1,
        raw_values: {
          date: "2026/11/03",
          amount: "invalid",
          description: "Synthetic invalid amount",
        },
        transaction_date: "2026-11-03",
        description: "Synthetic invalid amount",
        amount: null,
        validation_codes: ["invalid_amount"],
        warnings: [],
        duplicate_state: "none",
        duplicate_reasons: [],
        matching_import_ids: [],
        near_candidates: [],
        near_candidate_total: 0,
        within_file_group: null,
        within_file_state: "none",
        skip: false,
        override: false,
        suggested_tag_ids: [ids.tag],
        selected_tag_ids: [],
        suggested_category_id: ids.category,
        selected_category_id: ids.category,
        category_decided: false,
      },
    ],
  },
};
const descriptor: ReportFilterDescriptor = {
  start_date: "2026-11-01",
  end_date: "2026-11-21",
  financial_account_id: null,
  category_id: null,
  merchant_tag: null,
  category_kind: null,
  category_confirmed: null,
  direction: null,
  visibility: "visible",
  calculation_scope: "included",
  excluded_category_ids: null,
};

const trendMonthStarts = [
  ["2026-06-01", "2026-06-30", "Jun 2026", false, "covered"],
  ["2026-07-01", "2026-07-31", "Jul 2026", false, "covered"],
  ["2026-08-01", "2026-08-31", "Aug 2026", false, "covered"],
  ["2026-09-01", "2026-09-30", "Sep 2026", false, "covered"],
  ["2026-10-01", "2026-10-31", "Oct 2026", false, "covered"],
  ["2026-11-01", "2026-11-21", "Nov 2026", true, "partial"],
] as const;

type CategoryTrend = NonNullable<ReportResponse["category_trend"]>;
type CategoryTrendCell = CategoryTrend["categories"][number]["months"][number];

function fixtureTrendCell(
  categoryId: string,
  month: (typeof trendMonthStarts)[number],
  amount: string,
  count: number,
): CategoryTrendCell {
  return {
    amount,
    count,
    descriptor: {
      ...descriptor,
      start_date: month[0],
      end_date: month[1],
      financial_account_id: ids.account,
      category_id: categoryId,
      direction: "out",
    },
  };
}

const categoryTrend: CategoryTrend = {
  months: trendMonthStarts.map(([start_date, end_date, label, is_partial, coverage]) => ({
    start_date,
    end_date,
    label,
    is_partial,
    coverage,
  })),
  categories: [
    {
      id: ids.category,
      display_name: category.display_name,
      kind: "Expense",
      is_archived: false,
      money_out: "4175.00",
      count: 6,
      months: ["600.00", "700.00", "800.00", "900.00", "1000.00", "175.00"].map((amount, index) =>
        fixtureTrendCell(ids.category, trendMonthStarts[index], amount, 1),
      ),
    },
    {
      id: "99999999-9999-4999-8999-999999999999",
      display_name: "Synthetic transport",
      kind: "Expense",
      is_archived: false,
      money_out: "350.00",
      count: 3,
      months: ["100.00", "0.00", "200.00", "0.00", "50.00", "0.00"].map((amount, index) =>
        fixtureTrendCell(
          "99999999-9999-4999-8999-999999999999",
          trendMonthStarts[index],
          amount,
          amount === "0.00" ? 0 : 1,
        ),
      ),
    },
  ],
};

const emptyOtherCategories = (direction: "in" | "out") => ({
  amount: "0.00",
  count: 0,
  excluded_category_ids: [] as string[],
  prior_amount: null,
  change_percentage: null,
  descriptor: { ...descriptor, direction },
});

const categoryRank = (
  id: string,
  displayName: string,
  amount: string,
  share: string,
): ReportCategoryRank => ({
  id,
  display_name: displayName,
  kind: "Expense",
  is_archived: false,
  amount,
  count: 1,
  share,
  prior_amount: null,
  change_percentage: null,
  descriptor: { ...descriptor, category_id: id, direction: "out" },
});

const categories: ReportCategoryBreakdown = {
  money_in: {
    ranks: [],
    uncategorised: null,
    other: emptyOtherCategories("in"),
  },
  money_out: {
    ranks: [
      categoryRank(ids.category, category.display_name, "1000.00", "81.0"),
      categoryRank("99999999-9999-4999-8999-999999999999", "Synthetic transport", "234.56", "19.0"),
    ],
    uncategorised: null,
    other: emptyOtherCategories("out"),
  },
};

const report: ReportResponse = {
  status: "ready",
  as_of: date,
  today: "2026-11-21",
  timezone: "Africa/Johannesburg",
  sequence_revision: "0",
  view: "overview",
  period_mode: "calendar",
  period: {
    mode: "calendar",
    preset: "month_to_date",
    label: "November to date",
    start_date: "2026-11-01",
    end_date: "2026-11-21",
    is_partial: true,
    is_future: false,
    is_open_salary_cycle: false,
    cycle_start_id: null,
    note: null,
  },
  comparison: null,
  focus: {
    kind: "overview",
    category_id: null,
    category_name: null,
    category_kind: null,
    category_archived: null,
    merchant_tag_id: null,
    merchant_tag_name: null,
    merchant_tag_archived: null,
    untagged: false,
    financial_account_id: null,
    financial_account_name: null,
    financial_account_archived: null,
    direction: null,
  },
  current: {
    money: { ...money, zero_count: 1 },
    money_in_descriptor: { ...descriptor, direction: "in" },
    money_out_descriptor: { ...descriptor, direction: "out" },
    net_descriptor: descriptor,
    uncategorised_in_descriptor: descriptor,
    uncategorised_out_descriptor: descriptor,
    unconfirmed_descriptor: { ...descriptor, category_confirmed: false },
    uncategorised_count: 0,
    uncategorised_money_in: "0.00",
    uncategorised_money_out: "0.00",
    categorised_coverage_percentage: "100.0",
    unconfirmed_category_count: 1,
    tagged_count: 1,
    untagged_count: 1,
    untagged_money_in: "0.00",
    untagged_money_out: "0.00",
    tag_coverage_percentage: "100.0",
    transfers: diagnostic,
    excluded: diagnostic,
    hidden: diagnostic,
    hidden_descriptor: { ...descriptor, visibility: "hidden", calculation_scope: null },
    transfers_descriptor: { ...descriptor, category_kind: "Transfer", calculation_scope: null },
    excluded_descriptor: { ...descriptor, category_kind: "Excluded", calculation_scope: null },
  },
  prior: null,
  categories,
  tags: null,
  trend: [
    {
      start_date: "2026-11-01",
      end_date: "2026-11-21",
      label: "November to date",
      granularity: "month",
      money_in: "0.00",
      money_out: "1234.56",
      money_in_share: "0.0",
      money_out_share: "100.0",
      count: 2,
      coverage: "partial",
      money_in_descriptor: { ...descriptor, direction: "in" },
      money_out_descriptor: { ...descriptor, direction: "out" },
    },
  ],
  category_trend: categoryTrend,
  coverage: {
    current: {
      total_days: 21,
      covered_days: 20,
      covered_percentage: "95.2",
      partial: true,
      gaps: {
        items: [{ start_date: "2026-11-21", end_date: "2026-11-21", days: 1 }],
        total: 1,
        page: 1,
        page_size: 25,
        has_more: false,
        next_page: null,
        gap_days: 1,
      },
    },
    prior: null,
  },
  focus_group: null,
  transactions: null,
  category_changes: null,
  setup: null,
  policy_labels: [],
};

if (process.argv[1]?.endsWith("fixture-backend.ts")) {
  let scenario = "ready";
  let operation: Record<string, unknown> | null = null;
  const server = createServer(async (req, res) => {
    const url = new URL(req.url ?? "/", "http://127.0.0.1:3211");
    const send = (value: unknown, status = 200) => {
      res.writeHead(status, { "Content-Type": "application/json" });
      res.end(JSON.stringify(value));
    };
    if (url.pathname === "/health") return send({ status: "fixture" });
    let raw = "";
    for await (const chunk of req) raw += chunk;
    const body = raw ? JSON.parse(raw) : {};
    if (url.pathname === "/__scenario" && req.method === "POST") {
      scenario = body.scenario;
      operation = null;
      return send({ scenario });
    }
    if (
      req.headers[`x-${process.env.ARGUS_FIXTURE_HEADER_TOKEN}-application-secret`] !==
      "synthetic-only-application-placeholder"
    )
      return send({ error: "unauthorized" }, 401);
    if (
      url.pathname === "/v1/login" &&
      body.email === "synthetic@example.test" &&
      body.password === "Synthetic-only-password1!"
    )
      return send({ token: "synthetic-only-session", expires_at: future });
    if (
      req.headers[`x-${process.env.ARGUS_FIXTURE_HEADER_TOKEN}-session`] !==
      "synthetic-only-session"
    )
      return send({ error: "unauthorized" }, 401);
    const path = url.pathname;
    if (path === "/v1/logout" && req.method === "POST") {
      res.writeHead(204);
      res.end();
      return;
    }
    if (path === "/v1/me") {
      if (scenario === "account-error") return send({ error: "unavailable" }, 503);
      return send({
        id: "synthetic-customer",
        email: "synthetic@example.test",
        email_verified: true,
        subscriber_active: true,
        subscription_started_at: date,
        next_billing_at: null,
        lifetime: false,
        admin: true,
      });
    }
    if (path === "/v1/finance/accounts")
      return send(
        req.method === "POST"
          ? { ...financialAccount, display_name: body.display_name }
          : { accounts: [financialAccount] },
      );
    if (path === "/v1/finance/categories")
      return scenario === "partial"
        ? send({ error: "unavailable" }, 503)
        : send({ categories: [category] });
    if (path === "/v1/finance/merchant-tags") return send({ merchant_tags: [tag] });
    if (path === "/v1/finance/merchant-aliases")
      return send({
        merchant_aliases: [
          {
            id: ids.tag,
            display_alias: "synthetic grocer",
            merchant_tag_id: ids.tag,
            tag_display_name: tag.display_name,
            category_id: ids.category,
            category_display_name: category.display_name,
            ...times,
          },
        ],
      });
    if (path === "/v1/finance/salary-cycles")
      return send({ revision: "0", starts: [], next_before_date: null });
    if (path === "/v1/finance/reports/query") return send(report);
    if (path === "/v1/finance/transactions/query") {
      if (scenario === "pending") await new Promise((resolve) => setTimeout(resolve, 1500));
      if (scenario === "error") return send({ error: "unavailable" }, 503);
      if (scenario === "empty")
        return send({
          ...transactions,
          transactions: [],
          filtered_total: 0,
          inventory_count: 0,
          matching_total: 0,
          matching_visible_count: 0,
        });
      return send(transactions);
    }
    if (path === "/v1/finance/transactions/operations/prepare") {
      operation = {
        operation_id: body.operation_id,
        status: "Prepared",
        selection_mode: "explicit",
        captured_at: date,
        expires_at: future,
        selected_count: 1,
        changed_count: 1,
        unchanged_count: 0,
        confirmation_hash: "synthetic-hash",
        applied_at: null,
        terminal_at: null,
        action: {
          category: null,
          add_tags: [],
          remove_tags: [],
          is_hidden_by_user: true,
          confirm_category: null,
          stale: false,
        },
      };
      return send({ operation, created: true });
    }
    if (path.includes("/transactions/operations/"))
      return operation ? send(operation) : send({ error: "not_found" }, 404);
    if (path.startsWith("/v1/finance/transactions/")) {
      if (req.method === "PATCH" && scenario === "conflict")
        return send({ error: "version_conflict" }, 409);
      return send({
        transaction,
        source_page: null,
        source_row_number: 1,
        suggested_tags: [
          {
            id: "88888888-8888-4888-8888-888888888888",
            display_name: "Synthetic suggested tag",
            is_archived: false,
          },
        ],
        has_more_suggestions: false,
      });
    }
    if (path === "/v1/finance/imports/reviews")
      return send({ reviews: [{ ...review.summary, created_at: date }] });
    if (path.startsWith("/v1/finance/imports/reviews/")) return send(review);
    if (path === "/v1/finance/imports") return send({ items: [statement], next_cursor: null });
    if (path.startsWith("/v1/finance/imports/"))
      return send({
        statement,
        rows: [
          {
            id: ids.transaction,
            source_row_number: 1,
            source_page: null,
            transaction_date: transaction.transaction_date,
            description: transaction.description,
            amount: transaction.amount,
            is_hidden_by_user: false,
            effective_hidden: false,
            tags: [tag],
          },
        ],
        page: 1,
        page_size: 25,
        expected_transaction_count: 2,
      });
    return send({ error: "fixture_route_not_implemented" }, 404);
  });
  server.listen(3211, "127.0.0.1");
}
