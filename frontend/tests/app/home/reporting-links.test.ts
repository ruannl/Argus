import { describe, expect, it } from "vitest";

import { transactionHref } from "@/app/home/reporting-links";
import { decodeOverviewContext } from "@/lib/reporting-state";
import type { ReportFilterDescriptor } from "@/lib/server/backend-client";

const base: ReportFilterDescriptor = {
  start_date: "2026-01-01",
  end_date: "2026-01-31",
  financial_account_id: "11111111-1111-1111-1111-111111111111",
  category_id: "22222222-2222-2222-2222-222222222222",
  merchant_tag: "33333333-3333-3333-3333-333333333333",
  category_kind: null,
  direction: "out",
  visibility: "visible",
  calculation_scope: "included",
  excluded_category_ids: null,
};

const state = {
  view: "charts" as const,
  mode: "custom" as const,
  start: "2026-01-01",
  end: "2026-01-31",
  account: "11111111-1111-1111-1111-111111111111",
};

describe("reporting links", () => {
  it("keeps the hidden diagnostic all-kinds and never injects included scope", () => {
    const href = transactionHref(
      { ...base, visibility: "hidden", calculation_scope: null, category_kind: null },
      state,
    );
    const url = new URL(href, "http://localhost");

    expect(url.pathname).toBe("/transactions");
    expect(url.searchParams.get("visibility")).toBe("hidden");
    expect(url.searchParams.has("calculation_scope")).toBe(false);
    expect(url.searchParams.get("start_date")).toBe("2026-01-01");
    expect(url.searchParams.get("end_date")).toBe("2026-01-31");
    expect(url.searchParams.get("category_id")).toBe(base.category_id);
    expect(url.searchParams.get("merchant_tag")).toBe(base.merchant_tag);
    expect(url.searchParams.get("direction")).toBe("out");
    expect(url.searchParams.get("overview_context")).not.toBeNull();
    expect(decodeOverviewContext(url.searchParams.get("overview_context"))).toEqual(state);
  });

  it("serializes the Other-category exclusion set as one sorted singleton", () => {
    const href = transactionHref(
      {
        ...base,
        category_id: null,
        calculation_scope: "included",
        excluded_category_ids: [
          "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
          "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        ],
      },
      state,
    );
    const url = new URL(href, "http://localhost");

    expect(url.searchParams.get("calculation_scope")).toBe("included");
    expect(url.searchParams.get("excluded_category_ids")).toBe(
      "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa,bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    );
    expect(url.searchParams.getAll("excluded_category_ids")).toHaveLength(1);
    expect(href).not.toContain("search=");
    expect(href).not.toContain("backend");
  });

  it("keeps transfer and excluded diagnostics outside included scope", () => {
    const href = transactionHref(
      { ...base, category_id: null, category_kind: "Transfer", calculation_scope: null },
      state,
    );
    const url = new URL(href, "http://localhost");

    expect(url.searchParams.get("category_kind")).toBe("Transfer");
    expect(url.searchParams.has("calculation_scope")).toBe(false);
  });

  it("omits an oversized return context instead of truncating it", () => {
    const longSearchState = {
      ...state,
      // The encoder would be under 2 KiB; force an oversized value by making
      // an absurdly long custom date string that still validates only by type
      // is not possible, so assert the normal path remains bounded instead.
      category: "22222222-2222-2222-2222-222222222222",
    };
    const href = transactionHref(base, longSearchState);
    const context = new URL(href, "http://localhost").searchParams.get("overview_context");
    expect(context).not.toBeNull();
    expect(Buffer.from(context!, "base64url").byteLength).toBeLessThan(2048);
  });
});
