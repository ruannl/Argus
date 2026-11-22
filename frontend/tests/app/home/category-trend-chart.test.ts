import { describe, expect, it } from "vitest";

import { barHeight } from "@/app/home/category-trend-chart";

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
