import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { testRouter } from "../../../vitest.setup";
import FilterForm from "@/app/home/filter-form";

const account = {
  id: "11111111-1111-1111-1111-111111111111",
  display_name: "Cheque",
  institution: null,
  currency: "ZAR",
  is_archived: false,
  transaction_count: 1,
  created_at: "2026-01-01T00:00:00Z",
  updated_at: "2026-01-01T00:00:00Z",
};

const category = {
  id: "22222222-2222-2222-2222-222222222222",
  display_name: "Groceries",
  kind: "Expense" as const,
  is_system: false,
  is_default: false,
  is_archived: false,
  transaction_count: 1,
  created_at: "2026-01-01T00:00:00Z",
  updated_at: "2026-01-01T00:00:00Z",
};

describe("overview filter form", () => {
  it("omits All selections instead of emitting malformed empty parameters", async () => {
    const user = userEvent.setup();
    testRouter.push.mockReset();
    render(
      <FilterForm
        accounts={[account]}
        categories={[category]}
        state={{ view: "charts", mode: "salary_cycle" }}
        tags={[]}
      />,
    );

    await user.selectOptions(screen.getByLabelText("Account"), account.id);
    await user.click(screen.getByRole("button", { name: "Apply filters" }));

    expect(testRouter.push).toHaveBeenCalledWith(`/home?view=charts&account=${account.id}`);
    expect(testRouter.push.mock.calls[0][0]).not.toContain("category=");
    expect(testRouter.push.mock.calls[0][0]).not.toContain("merchant_tag=");
    expect(testRouter.push.mock.calls[0][0]).not.toContain("direction=");
  });

  it("preserves the selected period and clears expansion pages when filters change", async () => {
    const user = userEvent.setup();
    testRouter.push.mockReset();
    render(
      <FilterForm
        accounts={[account]}
        categories={[category]}
        state={{
          view: "summary",
          mode: "custom",
          start: "2026-01-01",
          end: "2026-01-31",
          coveragePage: 2,
        }}
        tags={[]}
      />,
    );

    await user.selectOptions(screen.getByLabelText("Category"), category.id);
    await user.click(screen.getByRole("button", { name: "Apply filters" }));

    expect(testRouter.push).toHaveBeenCalledWith(
      `/home?mode=custom&start_date=2026-01-01&end_date=2026-01-31&category=${category.id}`,
    );
  });
});
