import { describe, expect, it } from "vitest";

import {
  decodeOverviewContext,
  encodeOverviewContext,
  parseReportingState,
  reportingHref,
  reportingSearchParams,
} from "@/lib/reporting-state";
import type { ReportingState } from "@/lib/reporting-state";

function paramsFrom(state: ReportingState): Record<string, string> {
  return Object.fromEntries(reportingSearchParams(state).entries());
}

describe("reporting URL state", () => {
  it("defaults the Overview to the latest salary cycle", () => {
    const parsed = parseReportingState({});

    expect(parsed.error).toBeNull();
    expect(parsed.state).toEqual({ view: "summary", mode: "salary_cycle" });
  });

  it("preserves a calendar period and view across refresh", () => {
    const parsed = parseReportingState({
      view: "charts",
      mode: "calendar",
      preset: "month",
      month: "2026-02",
      account: "00000000-0000-0000-0000-0000000000aa",
      direction: "out",
    });

    expect(parsed.error).toBeNull();
    expect(parsed.state.view).toBe("charts");
    expect(parsed.state.preset).toBe("month");
    expect(parsed.state.month).toBe("2026-02");
    expect(reportingHref(parsed.state)).toBe(
      "/home?view=charts&mode=calendar&preset=month&month=2026-02&account=00000000-0000-0000-0000-0000000000aa&direction=out",
    );
  });

  it("preserves an inclusive custom range without day-count controls", () => {
    const parsed = parseReportingState({
      mode: "custom",
      start_date: "2026-01-10",
      end_date: "2026-01-31",
    });

    expect(parsed.error).toBeNull();
    expect(parsed.state.start).toBe("2026-01-10");
    expect(parsed.state.end).toBe("2026-01-31");
    expect(reportingHref(parsed.state)).toBe(
      "/home?mode=custom&start_date=2026-01-10&end_date=2026-01-31",
    );
  });

  it("rejects repeated, unknown and empty singleton parameters", () => {
    expect(parseReportingState({ view: ["summary", "charts"] }).error).not.toBeNull();
    expect(parseReportingState({ unexpected: "x" }).error).not.toBeNull();
    expect(parseReportingState({ view: "" }).error).not.toBeNull();
  });

  it("rejects invalid real dates rather than rolling them forward", () => {
    expect(
      parseReportingState({
        mode: "custom",
        start_date: "2026-02-30",
        end_date: "2026-03-01",
      }).error,
    ).not.toBeNull();
    expect(
      parseReportingState({
        mode: "calendar",
        preset: "month",
        month: "2026-13",
      }).error,
    ).not.toBeNull();
  });

  it("rejects mixed period discriminants", () => {
    expect(
      parseReportingState({ mode: "salary_cycle", preset: "month_to_date" }).error,
    ).not.toBeNull();
    expect(
      parseReportingState({
        mode: "calendar",
        preset: "month_to_date",
        start_date: "2026-01-01",
      }).error,
    ).not.toBeNull();
    expect(parseReportingState({ mode: "custom", start_date: "2026-01-01" }).error).not.toBeNull();
    expect(
      parseReportingState({ mode: "custom", start_date: "2026-02-02", end_date: "2026-02-01" })
        .error,
    ).not.toBeNull();
  });

  it("rejects page overflow and non-positive pages", () => {
    expect(parseReportingState({ coverage_page: "100001" }).error).not.toBeNull();
    expect(parseReportingState({ coverage_page: "0" }).error).not.toBeNull();
    expect(parseReportingState({ coverage_page: "1e309" }).error).not.toBeNull();
    expect(parseReportingState({ tag_in_page: "2" }).state.tagInPage).toBe(2);
  });

  it("round-trips a bounded Overview context without private search", () => {
    const state: ReportingState = {
      view: "charts",
      mode: "calendar",
      preset: "last_three_months",
      account: "00000000-0000-0000-0000-0000000000bb",
      merchantTag: "untagged",
    };
    const encoded = encodeOverviewContext(state);
    const decoded = decodeOverviewContext(encoded);

    expect(decoded).toEqual(state);
    expect(encoded).not.toContain("search");
    expect(decodeOverviewContext("not-base64!")).toBeNull();
  });

  it("builds a query parameter record with the same canonical keys", () => {
    const state = parseReportingState({
      mode: "salary_cycle",
      cycle_start_id: "00000000-0000-0000-0000-0000000000cc",
      view: "charts",
    }).state;

    expect(paramsFrom(state)).toEqual({
      view: "charts",
      cycle_start_id: "00000000-0000-0000-0000-0000000000cc",
    });
  });
});
