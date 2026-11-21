import { describe, expect, it } from "vitest";

import { reportQueryForState } from "@/lib/server/reporting";

describe("report request builder", () => {
  it("forwards detail pagination and inclusive filters", () => {
    const query = reportQueryForState(
      {
        view: "summary",
        mode: "custom",
        start: "2025-01-01",
        end: "2025-01-31",
        account: "11111111-1111-1111-1111-111111111111",
        category: "22222222-2222-2222-2222-222222222222",
        direction: "out",
        transactionsPage: 2,
      },
      { view: "category", focus: { categoryId: "22222222-2222-2222-2222-222222222222" } },
    );

    expect(query.view).toBe("category");
    expect(query.period).toEqual({
      mode: "custom",
      start_date: "2025-01-01",
      end_date: "2025-01-31",
    });
    expect(query.filters).toEqual({
      financial_account_id: "11111111-1111-1111-1111-111111111111",
      category_id: "22222222-2222-2222-2222-222222222222",
      direction: "out",
    });
    expect(query.transactions_page).toBe(2);
  });
});
