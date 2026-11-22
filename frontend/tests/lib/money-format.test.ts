import { describe, expect, it } from "vitest";

import {
  formatMoney,
  formatPercentage,
  formatSignedMoney,
  isZeroAmount,
  normalizeAmount,
  sumMoney,
} from "@/lib/money-format";

describe("exact ZAR formatting", () => {
  it("formats exact cents with grouping and a currency label", () => {
    expect(formatMoney("0.00")).toBe("R 0.00");
    expect(formatMoney("1234.56")).toBe("R 1 234.56");
    expect(formatMoney("-1234.56")).toBe("-R 1 234.56");
    expect(formatMoney("999999.99")).toBe("R 999 999.99");
  });

  it("normalizes negative zero to zero", () => {
    expect(formatMoney("-0.00")).toBe("R 0.00");
    expect(formatMoney("-0")).toBe("R 0.00");
    expect(normalizeAmount("-0.00")).toBe("0.00");
    expect(isZeroAmount("-0.00")).toBe(true);
  });

  it("never routes money through JavaScript Number", () => {
    const huge = "123456789012345678901234567890.99";
    const formatted = formatMoney(huge);

    expect(formatted).toBe("R 123 456 789 012 345 678 901 234 567 890.99");
    expect(formatted).not.toContain("e+");
    expect(normalizeAmount(huge)).toBe(huge);
  });

  it("rounds only for display, half away from zero", () => {
    expect(formatMoney("0.005")).toBe("R 0.01");
    expect(formatMoney("-0.005")).toBe("-R 0.01");
    expect(formatMoney("1.004")).toBe("R 1.00");
  });

  it("formats signed changes without implying financial health", () => {
    expect(formatSignedMoney("10.00")).toBe("+R 10.00");
    expect(formatSignedMoney("-10.00")).toBe("-R 10.00");
    expect(formatSignedMoney("0.00")).toBe("R 0.00");
  });

  it("sums exact signed amounts without Number precision loss", () => {
    expect(sumMoney([])).toBe("0.00");
    expect(sumMoney(["1000.00", "234.56"])).toBe("1234.56");
    expect(sumMoney(["10.00", "-3.50"])).toBe("6.50");
    expect(sumMoney(["-10.00", "-3.50"])).toBe("-13.50");
    expect(sumMoney(["123456789012345678901234567890.99", "0.01"])).toBe(
      "123456789012345678901234567891.00",
    );
  });

  it("rejects malformed amounts instead of inventing a value", () => {
    expect(() => formatMoney("1,234.56")).toThrow("invalid amount");
    expect(() => formatMoney("")).toThrow("invalid amount");
    expect(() => formatMoney("NaN")).toThrow("invalid amount");
  });

  it("renders backend percentages or an explicit absence", () => {
    expect(formatPercentage("12.3")).toBe("12.3%");
    expect(formatPercentage("-5.0")).toBe("-5.0%");
    expect(formatPercentage(null)).toBeNull();
    expect(formatPercentage("not-a-number")).toBeNull();
  });
});
