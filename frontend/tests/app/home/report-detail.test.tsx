import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import ReportDetail from "@/app/home/report-detail";
import type { ReportResponse } from "@/lib/server/backend-client";
import type { ReportingState } from "@/lib/reporting-state";

const state: ReportingState = {
  view: "summary",
  mode: "salary_cycle",
  account: "11111111-1111-1111-1111-111111111111",
};

const descriptor = {
  start_date: "2026-02-01",
  end_date: "2026-02-28",
  financial_account_id: "11111111-1111-1111-1111-111111111111",
  category_id: null,
  merchant_tag: null,
  category_kind: null,
  direction: "out",
  visibility: "visible",
  calculation_scope: "included",
  excluded_category_ids: null,
};

function report(overrides: Record<string, unknown> = {}): ReportResponse {
  return {
    status: "ready",
    as_of: "2026-11-15T10:00:00Z",
    today: "2026-11-15",
    timezone: "Africa/Johannesburg",
    sequence_revision: "1",
    view: "category",
    period_mode: "salary_cycle",
    period: {
      mode: "salary_cycle",
      preset: null,
      label: "Salary cycle",
      start_date: "2026-02-01",
      end_date: "2026-02-28",
      is_partial: false,
      is_future: false,
      is_open_salary_cycle: false,
      cycle_start_id: null,
      note: null,
    },
    comparison: null,
    focus: {
      kind: "category",
      category_id: "22222222-2222-2222-2222-222222222222",
      category_name: "Groceries",
      category_kind: "Expense",
      category_archived: false,
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
      money: {
        money_in: "0.00",
        money_out: "200.00",
        net: "-200.00",
        included_count: 2,
        zero_count: 0,
      },
      uncategorised_count: 0,
      uncategorised_money_in: "0.00",
      uncategorised_money_out: "0.00",
      categorised_coverage_percentage: "100.0",
      unconfirmed_category_count: 0,
      tagged_count: 2,
      untagged_count: 0,
      untagged_money_in: "0.00",
      untagged_money_out: "0.00",
      tag_coverage_percentage: "100.0",
      transfers: { count: 0, money_in: "0.00", money_out: "0.00", absolute: "0.00" },
      excluded: { count: 0, money_in: "0.00", money_out: "0.00", absolute: "0.00" },
      hidden: { count: 0, money_in: "0.00", money_out: "0.00", absolute: "0.00" },
      hidden_descriptor: { ...descriptor, visibility: "hidden", calculation_scope: null },
      transfers_descriptor: { ...descriptor, category_kind: "Transfer" },
      excluded_descriptor: { ...descriptor, category_kind: "Excluded" },
    },
    prior: null,
    categories: {
      money_in: {
        ranks: [],
        uncategorised: null,
        other: {
          amount: "0.00",
          count: 0,
          excluded_category_ids: [],
          prior_amount: null,
          change_percentage: null,
          descriptor,
        },
      },
      money_out: {
        ranks: [
          {
            id: "22222222-2222-2222-2222-222222222222",
            display_name: "Groceries",
            kind: "Expense",
            is_archived: false,
            amount: "200.00",
            count: 2,
            share: "100.0",
            prior_amount: null,
            change_percentage: null,
            descriptor,
          },
        ],
        uncategorised: null,
        other: {
          amount: "0.00",
          count: 0,
          excluded_category_ids: [],
          prior_amount: null,
          change_percentage: null,
          descriptor,
        },
      },
    },
    tags: null,
    trend: [
      {
        start_date: "2026-02-01",
        end_date: "2026-02-07",
        label: "2026-02-01 – 2026-02-07",
        granularity: "week",
        money_in: "0.00",
        money_out: "100.00",
        money_in_share: "0.0",
        money_out_share: "100.0",
        count: 1,
        coverage: "partial",
        money_in_descriptor: { ...descriptor, direction: "in" },
        money_out_descriptor: descriptor,
      },
    ],
    coverage: {
      current: {
        total_days: 28,
        covered_days: 14,
        covered_percentage: "50.0",
        partial: true,
        gaps: {
          items: [{ start_date: "2026-02-15", end_date: "2026-02-28", days: 14 }],
          total: 1,
          page: 1,
          page_size: 25,
          has_more: false,
          next_page: null,
          gap_days: 14,
        },
      },
      prior: null,
    },
    focus_group: {
      kind: "Expense",
      money_in: "0.00",
      money_out: "200.00",
      net: "-200.00",
      count: 2,
    },
    transactions: {
      items: [
        {
          id: "33333333-3333-3333-3333-333333333333",
          version: 1,
          transaction_date: "2026-02-03",
          description: "Weekly shop",
          amount: "-100.00",
          currency: "ZAR",
          direction: "out",
          financial_account: { id: "account", display_name: "Cheque", is_archived: false },
          category: {
            id: "22222222-2222-2222-2222-222222222222",
            display_name: "Groceries",
            kind: "Expense",
            is_system: false,
            is_archived: false,
          },
          category_confirmed: true,
          merchant_tags: [],
          statement_import: {
            id: "statement",
            statement_label: "Feb",
            source_type: "fnb_csv",
            is_hidden: false,
          },
          is_hidden_by_user: false,
          effective_hidden: false,
          hidden_reason: "none",
        },
      ],
      total: 30,
      page: 1,
      page_size: 25,
      has_more: true,
      next_page: 2,
    },
    category_changes: null,
    setup: null,
    policy_labels: ["Net is Money in minus Money out."],
    ...overrides,
  } as unknown as ReportResponse;
}

describe("report detail", () => {
  it("shows exact transactions, coverage gaps and period-preserving links", () => {
    render(
      <ReportDetail
        kind="category"
        path="/home/categories/22222222-2222-2222-2222-222222222222"
        report={report()}
        state={state}
      />,
    );

    expect(screen.getByRole("heading", { level: 1, name: "Groceries" })).toBeInTheDocument();
    expect(screen.getByText(/2026-02-15 to 2026-02-28/)).toBeInTheDocument();
    expect(screen.getByText(/Missing statement dates are not zero spending/)).toBeInTheDocument();
    expect(screen.getByText(/Bars share one scale from zero/)).toBeInTheDocument();
    expect(screen.getByText("2026-02-01 – 2026-02-07 · 7 days")).toBeInTheDocument();
    const transactionLink = screen.getByRole("link", { name: "Weekly shop" });
    expect(transactionLink.getAttribute("href")).toContain(
      "/transactions/33333333-3333-3333-3333-333333333333?overview_context=",
    );
    expect(screen.getByRole("link", { name: "More matching transactions" })).toHaveAttribute(
      "href",
      "/home/categories/22222222-2222-2222-2222-222222222222?account=11111111-1111-1111-1111-111111111111&transactions_page=2",
    );
    expect(screen.getByRole("link", { name: "Back to Overview" })).toHaveAttribute(
      "href",
      "/home?account=11111111-1111-1111-1111-111111111111",
    );
  });

  it("shows comparison deltas with distinct current and prior transaction links", () => {
    const comparison = report({
      view: "comparison",
      category_changes: {
        money_in: {
          items: [
            {
              id: "44444444-4444-4444-4444-444444444444",
              display_name: "Salary",
              kind: "Income",
              is_archived: false,
              current_amount: "100.00",
              prior_amount: "600.00",
              delta: "-500.00",
              delta_percentage: "-83.3",
              current_descriptor: {
                ...descriptor,
                start_date: "2026-02-01",
                end_date: "2026-02-28",
                direction: "in",
              },
              prior_descriptor: {
                ...descriptor,
                start_date: "2026-01-01",
                end_date: "2026-01-31",
                direction: "in",
              },
            },
          ],
          total: 26,
          page: 0,
          page_size: 10,
          has_more: true,
          next_page: 1,
        },
        money_out: {
          items: [],
          total: 0,
          page: 0,
          page_size: 10,
          has_more: false,
          next_page: null,
        },
      },
      comparison: {
        period: {
          mode: "calendar",
          preset: "month",
          label: "January 2026",
          start_date: "2026-01-01",
          end_date: "2026-01-31",
          is_partial: false,
          is_future: false,
          is_open_salary_cycle: false,
          cycle_start_id: null,
          note: null,
        },
        label: "Previous calendar month",
        shorter_prior: false,
        unavailable_reason: null,
        has_included_data: true,
      },
    });

    render(
      <ReportDetail kind="comparison" path="/home/comparison" report={comparison} state={state} />,
    );

    expect(screen.getByText("Period Comparison")).toBeInTheDocument();
    expect(screen.getByText("-R 500.00")).toBeInTheDocument();
    expect(screen.getAllByText(/Current/).length).toBeGreaterThan(0);
    expect(screen.getAllByText("R 100.00").length).toBeGreaterThan(0);
    expect(screen.getAllByText("R 600.00").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "Current period transactions" })).toHaveAttribute(
      "href",
      expect.stringContaining("start_date=2026-02-01"),
    );
    expect(screen.getByRole("link", { name: "Prior period transactions" })).toHaveAttribute(
      "href",
      expect.stringContaining("start_date=2026-01-01"),
    );
    expect(screen.getByRole("link", { name: "More inflow category changes" })).toHaveAttribute(
      "href",
      expect.stringContaining("category_changes_page=1"),
    );
  });
});
