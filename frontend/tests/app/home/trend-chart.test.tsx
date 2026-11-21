import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { bucketDays, TrendBarPlot, trendMaximum } from "@/app/home/trend-chart";
import type { ReportBucket } from "@/lib/server/backend-client";

function bucket(overrides: Partial<ReportBucket> = {}): ReportBucket {
  return {
    start_date: "2026-11-01",
    end_date: "2026-11-07",
    label: "2026-11-01 – 2026-11-07",
    granularity: "week",
    money_in: "0.00",
    money_out: "0.00",
    money_in_share: "0.0",
    money_out_share: "0.0",
    count: 0,
    coverage: "covered",
    money_in_descriptor: {} as ReportBucket["money_in_descriptor"],
    money_out_descriptor: {} as ReportBucket["money_out_descriptor"],
    ...overrides,
  };
}

describe("trendMaximum", () => {
  it("takes the largest single series value across every bucket", () => {
    expect(
      trendMaximum([
        bucket({ money_in: "82354.47", money_out: "10063.18" }),
        bucket({ money_in: "500.63", money_out: "47624.08" }),
      ]),
    ).toBe("82354.47");
  });

  it("returns zero when no bucket has activity", () => {
    expect(trendMaximum([bucket(), bucket({ label: "second" })])).toBe("0.00");
  });
});

describe("bucketDays", () => {
  it("counts both the first and last day", () => {
    expect(bucketDays("2026-11-01", "2026-11-07")).toBe(7);
  });

  it("keeps a clipped final bucket at its real length", () => {
    expect(bucketDays("2026-11-12", "2026-11-15")).toBe(4);
  });

  it("handles a leap February", () => {
    expect(bucketDays("2028-02-01", "2028-02-29")).toBe(29);
  });
});

describe("TrendBarPlot", () => {
  it("draws money out with its own series colour instead of the inverse surface", () => {
    const { container } = render(
      <TrendBarPlot
        bucket={bucket({
          money_in: "100.00",
          money_out: "40.00",
          money_in_share: "100.0",
          money_out_share: "40.0",
        })}
        heightClassName="h-24"
        maximum="100.00"
      />,
    );

    const series = container.querySelectorAll(".bg-accent, .bg-chart-out");

    expect(series).toHaveLength(2);
    expect(container.querySelector(".bg-inverse")).toBeNull();
    expect(series[0]).toHaveStyle({ height: "100%" });
    expect(series[1]).toHaveStyle({ height: "40%" });
  });

  it("labels the shared scale with its maximum and a zero baseline", () => {
    const { container } = render(
      <TrendBarPlot bucket={bucket()} heightClassName="h-24" maximum="82354.47" />,
    );

    expect(container.textContent).toContain("R 82 354.47");
    expect(container.textContent).toContain("R 0.00");
  });
});
