import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
  signOut: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  getCurrentAccount: vi.fn(),
  listSalaryCycles: vi.fn(),
  AccountReadError: class AccountReadError extends Error {
    kind: string;
    constructor(kind: string) {
      super("Account check failed.");
      this.kind = kind;
    }
  },
}));

vi.mock("@/lib/server/reporting", () => ({
  loadReportPage: vi.fn(),
}));

vi.mock("@/lib/server/account-actions", () => ({
  endSessionAction: vi.fn(),
}));

import { redirect } from "next/navigation";

import HomePage from "@/app/home/page";
import { auth } from "@/auth";
import { AccountReadError, getCurrentAccount, listSalaryCycles } from "@/lib/server/backend-client";
import type { ReportFilterDescriptor, ReportResponse } from "@/lib/server/backend-client";
import { loadReportPage } from "@/lib/server/reporting";
import { endSessionAction } from "@/lib/server/account-actions";

const account = {
  id: "account-id",
  email: "customer@example.test",
  email_verified: true,
  subscriber_active: true,
  subscription_started_at: "2030-01-01T00:00:00Z",
  next_billing_at: "2030-02-01T00:00:00Z",
  lifetime: false,
  admin: false,
};

function signedIn(admin = false) {
  vi.mocked(auth).mockResolvedValue({
    expires: "2030-01-01T00:00:00.000Z",
    authenticated: true,
    user: { backendToken: "backend-token" },
  } as never);
  vi.mocked(getCurrentAccount).mockResolvedValue({ ...account, admin });
  vi.mocked(listSalaryCycles).mockResolvedValue({
    ok: true,
    value: { revision: "0", starts: [], next_before_date: null },
  });
}

function descriptor(overrides: Partial<ReportFilterDescriptor> = {}): ReportFilterDescriptor {
  return {
    start_date: "2026-11-01",
    end_date: "2026-11-15",
    financial_account_id: null,
    category_id: null,
    merchant_tag: null,
    category_kind: null,
    direction: "out",
    visibility: "visible",
    calculation_scope: "included",
    excluded_category_ids: null,
    ...overrides,
  } as ReportFilterDescriptor;
}

const TREND_MONTHS = [
  ["2026-06-01", "2026-06-30", "Jun 2026", false, "covered"],
  ["2026-07-01", "2026-07-31", "Jul 2026", false, "covered"],
  ["2026-08-01", "2026-08-31", "Aug 2026", false, "covered"],
  ["2026-09-01", "2026-09-30", "Sep 2026", false, "covered"],
  ["2026-10-01", "2026-10-31", "Oct 2026", false, "covered"],
  ["2026-11-01", "2026-11-15", "Nov 2026", true, "partial"],
] as const;

const trendAccountId = "99999999-9999-4999-8999-999999999999";

function trendCell(
  categoryId: string,
  startDate: string,
  endDate: string,
  amount: string,
  count: number,
) {
  return {
    amount,
    count,
    descriptor: descriptor({
      start_date: startDate,
      end_date: endDate,
      financial_account_id: trendAccountId,
      category_id: categoryId,
      direction: "out",
    }),
  };
}

function categoryTrendFixture(): NonNullable<ReportResponse["category_trend"]> {
  const months = TREND_MONTHS.map(([startDate, endDate, label, isPartial, coverage]) => ({
    start_date: startDate,
    end_date: endDate,
    label,
    is_partial: isPartial,
    coverage,
  }));
  const groceries = "44444444-4444-4444-4444-444444444444";
  const transport = "88888888-8888-4888-8888-888888888888";
  const groceryAmounts = ["600.00", "700.00", "800.00", "900.00", "1000.00", "175.00"];
  const transportAmounts = ["100.00", "0.00", "200.00", "0.00", "50.00", "0.00"];

  return {
    months,
    categories: [
      {
        id: groceries,
        display_name: "Groceries",
        kind: "Expense",
        is_archived: false,
        money_out: "4175.00",
        count: 6,
        months: TREND_MONTHS.map(([startDate, endDate], index) =>
          trendCell(groceries, startDate, endDate, groceryAmounts[index], 1),
        ),
      },
      {
        id: transport,
        display_name: "Transport",
        kind: "Expense",
        is_archived: false,
        money_out: "350.00",
        count: 3,
        months: TREND_MONTHS.map(([startDate, endDate], index) =>
          trendCell(
            transport,
            startDate,
            endDate,
            transportAmounts[index],
            transportAmounts[index] === "0.00" ? 0 : 1,
          ),
        ),
      },
    ],
  };
}

function readyReport(overrides: Partial<ReportResponse> = {}): ReportResponse {
  const totals = {
    money: {
      money_in: "1000.00",
      money_out: "200.00",
      net: "800.00",
      included_count: 3,
      zero_count: 0,
    },
    money_in_descriptor: descriptor({ direction: "in" }),
    money_out_descriptor: descriptor({ direction: "out" }),
    net_descriptor: descriptor({ direction: null }),
    uncategorised_in_descriptor: descriptor({
      category_id: "33333333-3333-3333-3333-333333333333",
      direction: "in",
    }),
    uncategorised_out_descriptor: descriptor({
      category_id: "33333333-3333-3333-3333-333333333333",
      direction: "out",
    }),
    unconfirmed_descriptor: descriptor({ category_confirmed: false }),
    uncategorised_count: 1,
    uncategorised_money_in: "0.00",
    uncategorised_money_out: "25.00",
    categorised_coverage_percentage: "87.5",
    unconfirmed_category_count: 0,
    tagged_count: 1,
    untagged_count: 2,
    untagged_money_in: "1000.00",
    untagged_money_out: "25.00",
    tag_coverage_percentage: "33.3",
    transfers: { count: 1, money_in: "0.00", money_out: "50.00", absolute: "50.00" },
    excluded: { count: 0, money_in: "0.00", money_out: "0.00", absolute: "0.00" },
    hidden: { count: 2, money_in: "0.00", money_out: "30.00", absolute: "30.00" },
    hidden_descriptor: descriptor({ visibility: "hidden", calculation_scope: null }),
    transfers_descriptor: descriptor({ category_kind: "Transfer", calculation_scope: null }),
    excluded_descriptor: descriptor({ category_kind: "Excluded", calculation_scope: null }),
  };

  return {
    status: "ready",
    as_of: "2026-11-15T10:00:00Z",
    today: "2026-11-15",
    timezone: "Africa/Johannesburg",
    sequence_revision: "2",
    view: "overview",
    period_mode: "salary_cycle",
    period: {
      mode: "salary_cycle",
      preset: null,
      label: "Salary cycle to date",
      start_date: "2026-11-01",
      end_date: "2026-11-15",
      is_partial: true,
      is_future: false,
      is_open_salary_cycle: true,
      cycle_start_id: "11111111-1111-1111-1111-111111111111",
      note: null,
    },
    comparison: {
      period: {
        mode: "salary_cycle",
        preset: null,
        label: "Same elapsed days as previous cycle",
        start_date: "2026-10-01",
        end_date: "2026-10-14",
        is_partial: false,
        is_future: false,
        is_open_salary_cycle: false,
        cycle_start_id: null,
        note: null,
      },
      label: "Same elapsed days as previous cycle",
      shorter_prior: false,
      unavailable_reason: null,
      has_included_data: true,
    },
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
    current: totals as never,
    prior: totals as never,
    categories: {
      money_in: {
        ranks: [
          {
            id: "22222222-2222-2222-2222-222222222222",
            display_name: "Salary",
            kind: "Income",
            is_archived: false,
            amount: "1000.00",
            count: 1,
            share: "100.0",
            prior_amount: "900.00",
            change_percentage: "11.1",
            descriptor: descriptor({
              category_id: "22222222-2222-2222-2222-222222222222",
              direction: "in",
            }),
          },
        ],
        uncategorised: {
          id: "33333333-3333-3333-3333-333333333333",
          display_name: "Uncategorised",
          kind: "Uncategorised",
          is_archived: false,
          amount: "0.00",
          count: 0,
          share: "0.0",
          prior_amount: "0.00",
          change_percentage: null,
          descriptor: descriptor({
            category_id: "33333333-3333-3333-3333-333333333333",
            direction: "in",
          }),
        },
        other: {
          amount: "0.00",
          count: 0,
          excluded_category_ids: [],
          prior_amount: "0.00",
          change_percentage: null,
          descriptor: descriptor({ direction: "in" }),
        },
      },
      money_out: {
        ranks: [
          {
            id: "44444444-4444-4444-4444-444444444444",
            display_name: "Groceries",
            kind: "Expense",
            is_archived: false,
            amount: "175.00",
            count: 1,
            share: "87.5",
            prior_amount: "150.00",
            change_percentage: "16.7",
            descriptor: descriptor({
              category_id: "44444444-4444-4444-4444-444444444444",
              direction: "out",
            }),
          },
        ],
        uncategorised: {
          id: "33333333-3333-3333-3333-333333333333",
          display_name: "Uncategorised",
          kind: "Uncategorised",
          is_archived: false,
          amount: "25.00",
          count: 1,
          share: "12.5",
          prior_amount: "0.00",
          change_percentage: null,
          descriptor: descriptor({
            category_id: "33333333-3333-3333-3333-333333333333",
            direction: "out",
          }),
        },
        other: {
          amount: "0.00",
          count: 0,
          excluded_category_ids: [],
          prior_amount: "0.00",
          change_percentage: null,
          descriptor: descriptor({ direction: "out" }),
        },
      },
    },
    tags: {
      money_in: {
        ranks: [],
        untagged: { amount: "1000.00", count: 1, descriptor: descriptor({ direction: "in" }) },
        other: {
          direction: "in",
          items: [],
          total: 0,
          page: 1,
          page_size: 25,
          has_more: false,
          next_page: null,
        },
      },
      money_out: {
        ranks: [
          {
            id: "55555555-5555-5555-5555-555555555555",
            display_name: "Grocer",
            is_archived: false,
            amount: "175.00",
            count: 1,
            share: "87.5",
            prior_amount: null,
            change_percentage: null,
            descriptor: descriptor({
              merchant_tag: "55555555-5555-5555-5555-555555555555",
              direction: "out",
            }),
          },
        ],
        untagged: { amount: "25.00", count: 1, descriptor: descriptor({ direction: "out" }) },
        other: {
          direction: "out",
          items: [
            {
              id: "66666666-6666-6666-6666-666666666666",
              display_name: "Corner shop",
              is_archived: false,
              amount: "10.00",
              count: 1,
              share: "5.0",
              prior_amount: null,
              change_percentage: null,
              descriptor: descriptor({
                merchant_tag: "66666666-6666-6666-6666-666666666666",
                direction: "out",
              }),
            },
          ],
          total: 1,
          page: 1,
          page_size: 25,
          has_more: false,
          next_page: null,
        },
      },
      overlap_disclosure: true,
    },
    trend: [
      {
        start_date: "2026-11-01",
        end_date: "2026-11-07",
        label: "2026-11-01 – 2026-11-07",
        granularity: "week",
        money_in: "1000.00",
        money_out: "200.00",
        money_in_share: "100.0",
        money_out_share: "20.0",
        count: 3,
        coverage: "covered",
        money_in_descriptor: descriptor({ direction: "in" }),
        money_out_descriptor: descriptor({ direction: "out" }),
      },
    ],
    category_trend: categoryTrendFixture(),
    coverage: {
      current: {
        total_days: 15,
        covered_days: 15,
        covered_percentage: "100.0",
        partial: false,
        gaps: {
          items: [],
          total: 0,
          page: 1,
          page_size: 25,
          has_more: false,
          next_page: null,
          gap_days: 0,
        },
      },
      prior: null,
    },
    focus_group: null,
    transactions: null,
    category_changes: null,
    setup: null,
    policy_labels: ["Net is Money in minus Money out."],
    ...overrides,
  };
}

function setupReport(): ReportResponse {
  return readyReport({
    status: "setup_required",
    period: null,
    comparison: null,
    current: null,
    prior: null,
    categories: null,
    tags: null,
    trend: null,
    category_trend: null,
    coverage: null,
    setup: {
      reason: "no_salary_boundaries",
      inventory_empty: false,
      message: "Confirm a salary receipt date to start salary-cycle reporting.",
      alternatives: ["calendar", "custom"],
    },
  });
}

describe("home page", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(listSalaryCycles).mockReset();
    vi.mocked(loadReportPage).mockReset();
    vi.mocked(endSessionAction).mockReset();
    vi.mocked(redirect).mockReset();
  });

  it("renders the Overview workspace with persistent navigation", async () => {
    signedIn();
    vi.mocked(loadReportPage).mockResolvedValue({
      ok: true,
      account,
      inventory: { accounts: [], categories: [], tags: [] },
      report: readyReport(),
    });

    render(await HomePage());

    expect(getCurrentAccount).toHaveBeenCalledWith("backend-token");
    expect(screen.getByRole("heading", { level: 1, name: "Overview" })).toBeInTheDocument();
    expect(screen.getAllByText("Money in").length).toBeGreaterThan(0);
    expect(screen.getAllByText("R 1 000.00").length).toBeGreaterThan(0);
    expect(screen.getAllByText("R 800.00").length).toBeGreaterThan(0);
    expect(screen.getByText("Summary")).toBeInTheDocument();
    expect(screen.getByText("Charts")).toBeInTheDocument();

    const nav = screen.getByRole("navigation", { name: "Workspace" });
    expect(within(nav).getByRole("link", { name: "Overview" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(within(nav).getByRole("link", { name: "Transactions" })).toHaveAttribute(
      "href",
      "/transactions",
    );
    expect(within(nav).getByRole("link", { name: "Imports" })).toHaveAttribute("href", "/imports");
    expect(within(nav).getByRole("link", { name: "Settings" })).toHaveAttribute(
      "href",
      "/settings",
    );

    expect(screen.queryByText("customer@example.test")).not.toBeInTheDocument();
    expect(redirect).not.toHaveBeenCalled();
  });

  it("renders Charts over the same report and keeps table alternatives", async () => {
    signedIn();
    vi.mocked(loadReportPage).mockResolvedValue({
      ok: true,
      account,
      inventory: { accounts: [], categories: [], tags: [] },
      report: readyReport(),
    });

    render(await HomePage({ searchParams: Promise.resolve({ view: "charts" }) }));

    const nav = screen.getByRole("navigation", { name: "Overview views" });
    expect(within(nav).getByRole("link", { name: "Charts" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(within(nav).getByRole("link", { name: "Summary" })).not.toHaveAttribute("aria-current");
    expect(screen.getByText("Trend as a table")).toBeInTheDocument();
    expect(screen.getByText("Merchant tags")).toBeInTheDocument();
    expect(screen.getAllByText("Corner shop").length).toBeGreaterThan(0);
  });

  it("draws a proportional bar for every category in the breakdown on Summary", async () => {
    signedIn();
    vi.mocked(loadReportPage).mockResolvedValue({
      ok: true,
      account,
      inventory: { accounts: [], categories: [], tags: [] },
      report: readyReport(),
    });

    render(await HomePage());

    const breakdown = screen.getByRole("region", { name: "Category breakdown" });
    expect(breakdown.querySelectorAll(".category-share-track")).toHaveLength(4);
    expect(breakdown.querySelectorAll(".category-share-fill")).toHaveLength(4);
  });

  it("explains the shared trend scale and the real length of each bucket", async () => {
    signedIn();
    vi.mocked(loadReportPage).mockResolvedValue({
      ok: true,
      account,
      inventory: { accounts: [], categories: [], tags: [] },
      report: readyReport(),
    });

    render(await HomePage());

    const trend = screen.getByRole("region", { name: "Trend" });

    expect(within(trend).getByText(/Bars share one scale from zero/)).toBeInTheDocument();
    expect(within(trend).getByText("2026-11-01 – 2026-11-07 · 7 days")).toBeInTheDocument();
    expect(within(trend).getByText("R 0.00")).toBeInTheDocument();
    expect(within(trend).getAllByText("Money out").length).toBeGreaterThan(0);
  });

  it("defaults the category trend to the latest three months and keeps each category separate", async () => {
    signedIn();
    vi.mocked(loadReportPage).mockResolvedValue({
      ok: true,
      account,
      inventory: { accounts: [], categories: [], tags: [] },
      report: readyReport(),
    });

    render(await HomePage());

    const trend = screen.getByRole("region", { name: "Category spending trend" });
    expect(within(trend).getByRole("button", { name: "3 months" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(within(trend).getByRole("button", { name: "2 months" })).toHaveAttribute(
      "aria-pressed",
      "false",
    );

    const table = within(trend).getByRole("table", {
      name: /Included Money out by category and month/,
    });
    expect(within(table).getByRole("columnheader", { name: /Sep 2026/ })).toBeInTheDocument();
    expect(within(table).getByRole("columnheader", { name: /Oct 2026/ })).toBeInTheDocument();
    expect(within(table).getByRole("columnheader", { name: /Nov 2026/ })).toBeInTheDocument();
    expect(within(table).queryByRole("columnheader", { name: /Aug 2026/ })).not.toBeInTheDocument();
    expect(within(table).getByRole("rowheader", { name: "Groceries" })).toBeInTheDocument();
    expect(within(table).getByRole("rowheader", { name: "Transport" })).toBeInTheDocument();
    expect(within(table).getByRole("cell", { name: "R 2 075.00" })).toBeInTheDocument();
  });

  it("switches the category trend between two, three and six months with exact drill-down values", async () => {
    signedIn();
    vi.mocked(loadReportPage).mockResolvedValue({
      ok: true,
      account,
      inventory: { accounts: [], categories: [], tags: [] },
      report: readyReport(),
    });

    const user = userEvent.setup();
    render(await HomePage());

    const trend = screen.getByRole("region", { name: "Category spending trend" });
    const table = within(trend).getByRole("table", {
      name: /Included Money out by category and month/,
    });

    await user.click(within(trend).getByRole("button", { name: "2 months" }));
    expect(within(trend).getByRole("button", { name: "2 months" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(within(table).queryByRole("columnheader", { name: /Sep 2026/ })).not.toBeInTheDocument();
    expect(within(table).getByRole("columnheader", { name: /Oct 2026/ })).toBeInTheDocument();
    expect(within(table).getByRole("rowheader", { name: "Transport" })).toBeInTheDocument();

    const groceriesRow = within(table).getByRole("row", { name: /Groceries/ });
    const octoberLink = within(groceriesRow).getByRole("link", { name: "R 1 000.00" });
    const href = octoberLink.getAttribute("href") ?? "";
    expect(href).toContain("start_date=2026-10-01");
    expect(href).toContain("end_date=2026-10-31");
    expect(href).toContain("financial_account_id=99999999-9999-4999-8999-999999999999");
    expect(href).toContain("category_id=44444444-4444-4444-4444-444444444444");
    expect(href).toContain("direction=out");
    expect(href).toContain("calculation_scope=included");

    await user.click(within(trend).getByRole("button", { name: "6 months" }));
    expect(within(trend).getByRole("button", { name: "6 months" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(within(table).getByRole("columnheader", { name: /Jun 2026/ })).toBeInTheDocument();
    expect(within(table).getByRole("cell", { name: "R 4 175.00" })).toBeInTheDocument();
  });

  it("keeps the category trend honest when the selected range has no recorded spending", async () => {
    const fixture = categoryTrendFixture();
    signedIn();
    vi.mocked(loadReportPage).mockResolvedValue({
      ok: true,
      account,
      inventory: { accounts: [], categories: [], tags: [] },
      report: readyReport({
        category_trend: { months: fixture.months, categories: [] },
      }),
    });

    render(await HomePage());

    const trend = screen.getByRole("region", { name: "Category spending trend" });
    expect(within(trend).getByText("Nov 2026: Partial coverage")).toBeInTheDocument();
    expect(
      within(trend).getByText(/No recorded included Money out in the last 3 months/),
    ).toBeInTheDocument();
    expect(
      within(trend).getByText(
        /An absent bar is not a claim of zero spending where statement coverage is missing/,
      ),
    ).toBeInTheDocument();
  });

  it("shows salary setup with Calendar and Custom alternatives, never invented dates", async () => {
    signedIn();
    vi.mocked(loadReportPage).mockResolvedValue({
      ok: true,
      account,
      inventory: { accounts: [], categories: [], tags: [] },
      report: setupReport(),
    });

    render(await HomePage());

    expect(screen.getByText("Set up salary-cycle reporting")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Use calendar month to date" })).toHaveAttribute(
      "href",
      "/home?mode=calendar&preset=month_to_date",
    );
    expect(screen.getByRole("link", { name: "Use custom dates" })).toBeInTheDocument();
    expect(loadReportPage).toHaveBeenCalledWith("backend-token", account, {
      view: "summary",
      mode: "salary_cycle",
    });
  });

  it("shows the expired-session child only for a confirmed unauthorized account read", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "dead-backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockRejectedValue(new AccountReadError("unauthorized"));

    render(await HomePage());

    expect(screen.getByText("Your session has expired. Please sign in again.")).toBeInTheDocument();
    expect(screen.queryByText("customer@example.test")).not.toBeInTheDocument();
    await waitFor(() => expect(endSessionAction).toHaveBeenCalledTimes(1));
  });

  it("offers retry without signing out when the account read is unavailable", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockRejectedValue(new AccountReadError("unavailable"));

    render(await HomePage());

    expect(screen.getByText("Overview unavailable")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry account check" })).toBeInTheDocument();
    expect(endSessionAction).not.toHaveBeenCalled();
  });

  it("rejects malformed or repeated URL options without broadening the report", async () => {
    signedIn();

    render(await HomePage({ searchParams: Promise.resolve({ view: ["summary", "charts"] }) }));

    expect(screen.getByText("Overview options are invalid")).toBeInTheDocument();
    expect(loadReportPage).not.toHaveBeenCalled();
  });

  it("redirects to login when no backend session exists", async () => {
    vi.mocked(auth).mockResolvedValue(null as never);

    render(await HomePage());

    expect(redirect).toHaveBeenCalledWith("/login");
  });

  it("shows an admin link only for admin accounts", async () => {
    signedIn(true);
    vi.mocked(loadReportPage).mockResolvedValue({
      ok: true,
      account: { ...account, admin: true },
      inventory: { accounts: [], categories: [], tags: [] },
      report: readyReport(),
    });

    render(await HomePage());

    expect(screen.getByRole("link", { name: "Admin" })).toHaveAttribute("href", "/admin");
  });

  it("hides the admin link for non-admin accounts", async () => {
    signedIn(false);
    vi.mocked(loadReportPage).mockResolvedValue({
      ok: true,
      account,
      inventory: { accounts: [], categories: [], tags: [] },
      report: readyReport(),
    });

    render(await HomePage());

    expect(screen.queryByRole("link", { name: "Admin" })).not.toBeInTheDocument();
  });
});
