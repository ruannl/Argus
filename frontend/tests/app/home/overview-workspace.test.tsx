import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import OverviewWorkspace from "@/app/home/overview-workspace";
import type { ReportResponse } from "@/lib/server/backend-client";

const descriptor = {
  start_date: "2026-10-01",
  end_date: "2026-10-31",
  financial_account_id: null,
  category_id: null,
  merchant_tag: null,
  category_kind: null,
  direction: "out",
  visibility: "visible",
  calculation_scope: "included",
  excluded_category_ids: null,
};

const report = {
  status: "ready",
  as_of: "2026-11-15T10:00:00Z",
  today: "2026-11-15",
  timezone: "Africa/Johannesburg",
  sequence_revision: "1",
  view: "overview",
  period_mode: "calendar",
  period: {
    mode: "calendar",
    preset: "month",
    label: "October 2026",
    start_date: "2026-10-01",
    end_date: "2026-10-31",
    is_partial: false,
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
    money: {
      money_in: "0.00",
      money_out: "300.00",
      net: "-300.00",
      included_count: 3,
      zero_count: 0,
    },
    money_in_descriptor: descriptor,
    money_out_descriptor: descriptor,
    net_descriptor: descriptor,
    uncategorised_in_descriptor: descriptor,
    uncategorised_out_descriptor: descriptor,
    unconfirmed_descriptor: descriptor,
    uncategorised_count: 0,
    uncategorised_money_in: "0.00",
    uncategorised_money_out: "0.00",
    categorised_coverage_percentage: "100.0",
    unconfirmed_category_count: 0,
    tagged_count: 0,
    untagged_count: 3,
    untagged_money_in: "0.00",
    untagged_money_out: "300.00",
    tag_coverage_percentage: null,
    transfers: { count: 0, money_in: "0.00", money_out: "0.00", absolute: "0.00" },
    excluded: { count: 0, money_in: "0.00", money_out: "0.00", absolute: "0.00" },
    hidden: { count: 0, money_in: "0.00", money_out: "0.00", absolute: "0.00" },
    hidden_descriptor: descriptor,
    transfers_descriptor: descriptor,
    excluded_descriptor: descriptor,
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
      other_ranks: [],
    },
    money_out: {
      ranks: [
        {
          id: "11111111-1111-1111-1111-111111111111",
          display_name: "Groceries",
          kind: "Expense",
          is_archived: false,
          amount: "100.00",
          count: 1,
          share: "33.3",
          prior_amount: null,
          change_percentage: null,
          descriptor,
        },
      ],
      uncategorised: null,
      other: {
        amount: "200.00",
        count: 2,
        excluded_category_ids: [],
        prior_amount: null,
        change_percentage: null,
        descriptor,
      },
      other_ranks: [
        {
          id: "22222222-2222-2222-2222-222222222222",
          display_name: "Medical and insurance",
          kind: "Expense",
          is_archived: false,
          amount: "150.00",
          count: 1,
          share: "50.0",
          prior_amount: null,
          change_percentage: null,
          descriptor,
        },
        {
          id: "33333333-3333-3333-3333-333333333333",
          display_name: "Subscriptions",
          kind: "Expense",
          is_archived: false,
          amount: "50.00",
          count: 1,
          share: "16.7",
          prior_amount: null,
          change_percentage: null,
          descriptor,
        },
      ],
    },
  },
  tags: {
    money_in: {
      ranks: [],
      untagged: { amount: "0.00", count: 0, descriptor },
      other: {
        direction: "in",
        items: [],
        total: 0,
        page: 1,
        page_size: 25,
        has_more: false,
        next_page: null,
      },
      all_ranks: [],
    },
    money_out: {
      ranks: [],
      untagged: { amount: "5.00", count: 1, descriptor },
      other: {
        direction: "out",
        items: [],
        total: 0,
        page: 1,
        page_size: 25,
        has_more: false,
        next_page: null,
      },
      all_ranks: [
        {
          id: "44444444-4444-4444-4444-444444444444",
          display_name: "Tag low 10",
          is_archived: false,
          amount: "10.00",
          count: 1,
          share: "10.0",
          prior_amount: null,
          change_percentage: null,
          descriptor,
        },
        {
          id: "55555555-5555-5555-5555-555555555555",
          display_name: "Tag high 70",
          is_archived: false,
          amount: "70.00",
          count: 1,
          share: "70.0",
          prior_amount: null,
          change_percentage: null,
          descriptor,
        },
        {
          id: "66666666-6666-6666-6666-666666666666",
          display_name: "Tag mid 30",
          is_archived: false,
          amount: "30.00",
          count: 1,
          share: "30.0",
          prior_amount: null,
          change_percentage: null,
          descriptor,
        },
      ],
    },
    overlap_disclosure: true,
  },
  trend: null,
  category_trend: null,
  coverage: null,
  focus_group: null,
  transactions: null,
  category_changes: null,
  setup: null,
  policy_labels: [],
} as unknown as ReportResponse;

describe("overview category breakdown", () => {
  it("lists every Money out category individually in descending amount order", () => {
    render(
      <OverviewWorkspace
        accounts={[]}
        categories={[]}
        error={null}
        report={report}
        starts={[]}
        state={{
          view: "summary",
          mode: "calendar",
          preset: "month",
          month: "2026-10",
        }}
        tags={[]}
      />,
    );

    expect(screen.queryByText("Other categories in detail")).not.toBeInTheDocument();
    expect(screen.queryByText("Other categories")).not.toBeInTheDocument();
    expect(screen.getAllByText("Medical and insurance").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Groceries").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Subscriptions").length).toBeGreaterThan(0);
    expect(screen.getAllByText("R 150.00").length).toBeGreaterThan(0);
    expect(screen.getAllByText("R 100.00").length).toBeGreaterThan(0);
    expect(screen.getAllByText("R 50.00").length).toBeGreaterThan(0);

    const medical = screen.getAllByText("Medical and insurance")[0];
    const groceries = screen.getAllByText("Groceries")[0];
    const subscriptions = screen.getAllByText("Subscriptions")[0];
    const follows = (later: Element, earlier: Element) =>
      Boolean(later.compareDocumentPosition(earlier) & Node.DOCUMENT_POSITION_FOLLOWING);
    expect(follows(medical, groceries)).toBe(true);
    expect(follows(groceries, subscriptions)).toBe(true);

    expect(screen.queryByText("Other tags")).not.toBeInTheDocument();
    const high = screen.getAllByText("Tag high 70")[0];
    const mid = screen.getAllByText("Tag mid 30")[0];
    const low = screen.getAllByText("Tag low 10")[0];
    expect(follows(high, mid)).toBe(true);
    expect(follows(mid, low)).toBe(true);
  });
});
