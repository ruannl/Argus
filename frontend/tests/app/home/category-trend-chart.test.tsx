import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import CategoryTrendChart, { barHeight } from "@/app/home/category-trend-chart";
import { TrendRangeProvider } from "@/app/home/trend-range";
import { sumMoney } from "@/lib/money-format";
import type {
  ReportCategoryTrend,
  ReportCategoryTrendChange,
  ReportCategoryTrendComparison,
  ReportCategoryTrendMonth,
  ReportPeriod,
} from "@/lib/server/backend-client";

describe("category trend bar heights", () => {
  it("keeps percentages, clamping and zero handling exact", () => {
    expect(barHeight("1.00", "2.00")).toBe("50%");
    expect(barHeight("3.00", "2.00")).toBe("100%");
    expect(barHeight("2.00", "2.00")).toBe("100%");
    expect(barHeight("1.00", "0.00")).toBe("0%");
    expect(barHeight("0.01", "3.00")).toBe("0.33%");
  });

  it("does not overflow on amounts beyond the safe Number range", () => {
    const huge = `${"9".repeat(24)}.99`;
    expect(barHeight(huge, huge)).toBe("100%");
    expect(barHeight(`${"1".repeat(24)}.00`, huge)).not.toBe("0%");
  });
});

type Mode = "calendar" | "salary_cycle";
type Spec = {
  name: string;
  archived?: boolean;
  amounts: [string, string, string, string, string, string];
  latest: ReportCategoryTrendChange;
};

function change(
  current_amount: string,
  prior_amount: string,
  state: ReportCategoryTrendChange["state"],
  change_percentage: string | null,
): ReportCategoryTrendChange {
  return { current_amount, prior_amount, state, change_percentage };
}

function comparisonFixture(mode: Mode = "calendar", availableLatest = false): ReportCategoryTrend {
  const salary = mode === "salary_cycle";
  const definitions = (
    salary
      ? [
          ["2023-10-25", "2023-11-24", "25 Oct – 24 Nov 2023"],
          ["2023-11-25", "2023-12-24", "25 Nov – 24 Dec 2023"],
          ["2023-12-25", "2024-01-24", "25 Dec 2023 – 24 Jan 2024"],
          ["2024-01-25", "2024-02-23", "25 Jan – 23 Feb 2024"],
          ["2024-02-24", "2024-03-24", "24 Feb – 24 Mar 2024"],
          ["2024-03-25", "2024-04-08", "25 Mar – 8 Apr 2024"],
        ]
      : [
          ["2023-10-01", "2023-10-31", "Oct 2023"],
          ["2023-11-01", "2023-11-30", "Nov 2023"],
          ["2023-12-01", "2023-12-31", "Dec 2023"],
          ["2024-01-01", "2024-01-31", "Jan 2024"],
          ["2024-02-01", "2024-02-29", "Feb 2024"],
          ["2024-03-01", "2024-03-15", "Mar 2024"],
        ]
  ) as [string, string, string][];

  const period = (index: number, isPartial: boolean): ReportPeriod => ({
    mode,
    preset: salary ? null : isPartial ? "month_to_date" : "month",
    label: definitions[index][2],
    start_date: definitions[index][0],
    end_date: definitions[index][1],
    is_partial: isPartial,
    is_future: false,
    is_open_salary_cycle: salary && isPartial,
    cycle_start_id: null,
    note: null,
  });

  const comparisonFor = (index: number): ReportCategoryTrendComparison => {
    if (index === 0 && salary) {
      return {
        current_period: period(0, false),
        prior_period: null,
        label: "Previous salary cycle",
        shorter_prior: false,
        current_coverage: "covered",
        prior_coverage: null,
        has_prior_included_data: false,
        availability: "unavailable",
        unavailable_reason: "insufficient_salary_boundaries",
      };
    }
    if (index === 0) {
      return {
        current_period: period(0, false),
        prior_period: {
          mode,
          preset: null,
          label: "Sep 2023",
          start_date: "2023-09-01",
          end_date: "2023-09-30",
          is_partial: false,
          is_future: false,
          is_open_salary_cycle: false,
          cycle_start_id: null,
          note: null,
        },
        label: "Previous calendar month",
        shorter_prior: false,
        current_coverage: "covered",
        prior_coverage: "covered",
        has_prior_included_data: true,
        availability: "available",
        unavailable_reason: null,
      };
    }
    const isPartial = index === 5;
    const prior = period(index - 1, false);
    const covered = index === 5 ? availableLatest || salary : true;
    return {
      current_period: period(index, isPartial),
      prior_period:
        isPartial && !salary ? { ...prior, end_date: "2024-02-15", is_partial: false } : prior,
      label: salary
        ? "Previous salary cycle"
        : isPartial
          ? "Same period last month"
          : "Previous calendar month",
      shorter_prior: false,
      current_coverage: covered ? "covered" : "partial",
      prior_coverage: "covered",
      has_prior_included_data: true,
      availability: covered ? "available" : "incomplete_coverage",
      unavailable_reason: null,
    };
  };

  const months: ReportCategoryTrendMonth[] = definitions.map(
    ([start_date, end_date, label], index) => ({
      start_date,
      end_date,
      label,
      is_partial: index === 5,
      coverage: index === 5 && !salary && !availableLatest ? "partial" : "covered",
      comparison: comparisonFor(index),
    }),
  );

  const specs: Spec[] = [
    {
      name: "Household essentials",
      amounts: ["600.00", "700.00", "800.00", "900.00", "1000.00", "120.00"],
      latest: change("120.00", "100.00", "increase", "20.0"),
    },
    {
      name: "Transport",
      archived: true,
      amounts: ["100.00", "0.00", "200.00", "0.00", "50.00", "0.00"],
      latest: change("0.00", "100.00", "decrease", "-100.0"),
    },
    {
      name: "Subscriptions",
      amounts: ["0.00", "100.00", "0.00", "0.00", "100.00", "80.00"],
      latest: change("80.00", "100.00", "decrease", "-20.0"),
    },
    {
      name: "Utilities",
      amounts: ["0.00", "100.00", "0.00", "0.00", "100.00", "100.00"],
      latest: change("100.00", "100.00", "unchanged", "0.0"),
    },
    {
      name: "Tiny",
      amounts: ["0.00", "100.00", "0.00", "0.00", "100.00", "99.99"],
      latest: change("99.99", "100.00", "decrease", "0.0"),
    },
    {
      name: "New",
      amounts: ["0.00", "0.00", "0.00", "0.00", "0.00", "10.00"],
      latest: change("10.00", "0.00", "new_spending", null),
    },
    {
      name: "Earlier activity",
      amounts: ["0.00", "0.00", "0.00", "10.00", "0.00", "0.00"],
      latest: change("0.00", "0.00", "no_spending", null),
    },
  ];

  return {
    months,
    latest_comparison: months[5].comparison,
    categories: specs.map((spec, index) => {
      const id = `44444444-4444-4444-8444-44444444444${index}`;
      return {
        id,
        display_name: spec.name,
        kind: "Expense" as const,
        is_archived: spec.archived ?? false,
        count: spec.amounts.filter((amount) => amount !== "0.00").length,
        money_out: sumMoney(spec.amounts),
        latest_change: spec.latest,
        months: months.map((month, monthIndex) => {
          const amount = spec.amounts[monthIndex];
          const isLatest = monthIndex === 5;
          const priorAmount = monthIndex === 0 ? "550.00" : spec.amounts[monthIndex - 1];
          const state: ReportCategoryTrendChange["state"] =
            priorAmount === "0.00"
              ? amount === "0.00"
                ? "no_spending"
                : "new_spending"
              : amount === "0.00"
                ? "decrease"
                : amount === priorAmount
                  ? "unchanged"
                  : "increase";
          const percentage =
            state === "increase"
              ? monthIndex === 0
                ? "9.1"
                : "10.0"
              : state === "decrease"
                ? "-100.0"
                : state === "unchanged"
                  ? "0.0"
                  : null;
          return {
            amount,
            count: amount === "0.00" ? 0 : 1,
            change: isLatest ? spec.latest : change(amount, priorAmount, state, percentage),
            descriptor: {
              start_date: month.start_date,
              end_date: month.end_date,
              financial_account_id: null,
              category_id: id,
              merchant_tag: null,
              category_kind: null,
              direction: "out" as const,
              visibility: "visible" as const,
              calculation_scope: "included" as const,
              excluded_category_ids: null,
            },
          };
        }),
      };
    }),
  };
}

function mount(trend: ReportCategoryTrend, mode: Mode = "calendar") {
  render(
    <TrendRangeProvider>
      <CategoryTrendChart trend={trend} state={{ view: "summary", mode }} />
    </TrendRangeProvider>,
  );
}

function periodCell(table: HTMLElement, rowName: RegExp, periodLabel: string) {
  const headers = within(table).getAllByRole("columnheader");
  const column = headers.findIndex((header) => header.textContent?.startsWith(periodLabel));
  expect(column).toBeGreaterThan(0);
  const row = within(table).getByRole("row", { name: rowName });
  return within(row).getAllByRole("cell")[column - 1];
}

describe("category trend period comparison", () => {
  it("renders the backend's exact direction under every displayed period", async () => {
    const user = userEvent.setup();
    mount(comparisonFixture("calendar", true));
    await user.click(screen.getByText("Category spending as a table"));
    await user.click(screen.getByRole("button", { name: "6 months" }));
    const table = screen.getByRole("table", { name: /Included Money out by category/ });
    expect(
      within(table).getAllByRole("rowheader", { name: "Change vs previous period" }),
    ).toHaveLength(1);
    expect(within(table).getByText("vs 1–30 Sep 2023", { exact: true })).toBeInTheDocument();
    expect(within(table).getByText("vs 1–15 Feb 2024", { exact: true })).toBeInTheDocument();

    for (const [name, direction, amount, arrow, color] of [
      [/Household essentials/, "Spending increased", "+20.0%", "↑", "text-chart-in"],
      [/Subscriptions/, "Spending decreased", "−20.0%", "↓", "text-chart-out"],
      [/Transport/, "Spending decreased", "−100.0%", "↓", "text-chart-out"],
      [/Tiny/, "Spending decreased", "−0.0%", "↓", "text-chart-out"],
    ] as const) {
      const cell = periodCell(table, name, "Mar 2024");
      expect(within(cell).getByText(direction)).not.toHaveAttribute("aria-hidden");
      expect(within(cell).getByText(amount).parentElement).toHaveClass(color);
      expect(within(cell).getByText(arrow)).toHaveAttribute("aria-hidden", "true");
    }
    const equal = periodCell(table, /Utilities/, "Mar 2024");
    expect(within(equal).getByText("Spending unchanged")).toBeInTheDocument();
    expect(within(equal).getByText("0.0%")).toBeInTheDocument();
    expect(equal).not.toHaveTextContent(/[↑↓]/);
    for (const [row, label] of [
      [/^New /, "New spending"],
      [/Earlier activity/, "No spending"],
    ] as const) {
      const cell = periodCell(table, row, "Mar 2024");
      expect(within(cell).getByText(label)).toBeInTheDocument();
      expect(cell).not.toHaveTextContent(/[%↑↓]/);
    }
  });

  it("keeps earlier periods comparable when the latest period has a coverage gap", async () => {
    const user = userEvent.setup();
    mount(comparisonFixture());
    await user.click(screen.getByText("Category spending as a table"));
    await user.click(screen.getByRole("button", { name: "6 months" }));
    const table = screen.getByRole("table");
    const latestHeading = within(table).getByText("Missing coverage: the current period");
    expect(latestHeading).toBeVisible();
    const latest = periodCell(table, /Household essentials/, "Mar 2024");
    expect(latest).toHaveTextContent("—");
    expect(latest).not.toHaveTextContent(/[%↑↓]/);
    for (const label of ["Oct 2023", "Nov 2023", "Dec 2023", "Jan 2024", "Feb 2024"]) {
      expect(periodCell(table, /Household essentials/, label)).toHaveTextContent("%");
      expect(periodCell(table, /Household essentials/, label)).not.toHaveTextContent("—");
    }
    // The first displayed period uses its off-screen predecessor, not the next cell.
    expect(periodCell(table, /Household essentials/, "Oct 2023")).toHaveTextContent(
      "Spending increased ↑ +9.1%",
    );
  });

  it("shows an explicit no-predecessor state per period without inventing a percentage", async () => {
    const user = userEvent.setup();
    mount(comparisonFixture("salary_cycle"), "salary_cycle");
    await user.click(screen.getByText("Category spending as a table"));
    await user.click(screen.getByRole("button", { name: "6 salary cycles" }));
    const table = screen.getByRole("table");
    expect(within(table).getByText("No previous confirmed cycle", { exact: true })).toBeVisible();
    const first = periodCell(table, /Household essentials/, "25 Oct – 24 Nov 2023");
    expect(first).toHaveTextContent("—");
    expect(first).not.toHaveTextContent(/[%↑↓]/);
    expect(periodCell(table, /Household essentials/, "25 Nov – 24 Dec 2023")).toHaveTextContent(
      "Spending increased ↑ +10.0%",
    );
    expect(within(table).getByText("vs 24 Feb – 24 Mar 2024", { exact: true })).toBeVisible();
    expect(within(table).getByText("To date: 25 Mar – 8 Apr 2024", { exact: true })).toBeVisible();
    expect(
      within(table).getByText("Open cycle through today vs full previous cycle.", {
        exact: true,
      }),
    ).toBeVisible();
  });

  it("keeps each period's change, disclosed amounts and original links across range switches", async () => {
    const user = userEvent.setup();
    mount(comparisonFixture());
    await user.click(screen.getByText("Category spending as a table"));
    const table = screen.getByRole("table");
    for (const count of [3, 2, 6]) {
      await user.click(screen.getByRole("button", { name: `${count} months` }));
      const household = within(table).getByRole("row", { name: /Household essentials/ });
      expect(
        within(table).getByRole("rowheader", { name: "Transport (archived)" }),
      ).toBeInTheDocument();
      expect(within(table).getByRole("columnheader", { name: /Partial coverage/ })).toBeVisible();
      expect(periodCell(table, /Household essentials/, "Mar 2024")).toHaveTextContent("—");
      expect(periodCell(table, /Household essentials/, "Feb 2024")).toHaveTextContent("+10.0%");
      expect(periodCell(table, /Transport/, "Feb 2024")).toHaveTextContent("New spending");
      expect(within(household).getByRole("link", { name: "R 1 000.00" })).toHaveAttribute(
        "href",
        expect.stringContaining("start_date=2024-02-01"),
      );
      if (count === 2) {
        expect(
          within(table).queryByRole("rowheader", { name: "Earlier activity" }),
        ).not.toBeInTheDocument();
      } else {
        expect(periodCell(table, /Earlier activity/, "Jan 2024")).toHaveTextContent("New spending");
        expect(periodCell(table, /Earlier activity/, "Mar 2024")).toHaveTextContent("—");
      }
    }

    // The first displayed period still discloses its off-screen predecessor amounts.
    const october = periodCell(table, /Household essentials/, "Oct 2023");
    const summary = within(october).getByText("Compared amounts");
    summary.focus();
    expect(summary).toHaveFocus();
    // JSDOM does not implement native keyboard activation on <summary>.
    // The Chromium workspace journey exercises Enter and touch separately.
    await user.click(summary);
    expect(summary.parentElement).toHaveAttribute("open");
    expect(within(summary.parentElement!).getByText("R 600.00")).toBeVisible();
    expect(within(summary.parentElement!).getByText("R 550.00")).toBeVisible();
  });

  it("uses a dash when a period's comparison metadata is missing", async () => {
    const user = userEvent.setup();
    const trend = comparisonFixture();
    Reflect.deleteProperty(trend.months[0], "comparison");
    Reflect.deleteProperty(trend.categories[0].months[1], "change");
    mount(trend);
    await user.click(screen.getByText("Category spending as a table"));
    await user.click(screen.getByRole("button", { name: "6 months" }));
    const table = screen.getByRole("table");
    const first = periodCell(table, /Household essentials/, "Oct 2023");
    expect(first).toHaveTextContent("—");
    expect(first).not.toHaveTextContent(/[%↑↓]/);
    const second = periodCell(table, /Household essentials/, "Nov 2023");
    expect(second).toHaveTextContent("—");
    expect(second).not.toHaveTextContent(/[%↑↓]/);
  });

  it("explains a shorter prior month without changing the compared dates", async () => {
    const user = userEvent.setup();
    const trend = comparisonFixture();
    trend.months[5].comparison.shorter_prior = true;
    trend.months[5].comparison.prior_period!.end_date = "2024-02-15";
    mount(trend);
    await user.click(screen.getByText("Category spending as a table"));
    expect(screen.getByText(/Previous month capped at its last day\./)).toBeVisible();
    expect(screen.getByText("vs 1–15 Feb 2024", { exact: true })).toBeVisible();
  });
});
