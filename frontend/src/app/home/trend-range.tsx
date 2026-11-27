"use client";

import { createContext, useContext, useState, type ReactNode } from "react";

export const TREND_RANGES = [2, 3, 6] as const;
export type TrendRange = (typeof TREND_RANGES)[number];

type TrendRangeValue = {
  range: TrendRange;
  setRange: (range: TrendRange) => void;
};

const TrendRangeContext = createContext<TrendRangeValue | null>(null);

/** Shares the 2/3/6 range between the category and salary-cycle trend cards. */
export function TrendRangeProvider({ children }: { children: ReactNode }) {
  const [range, setRange] = useState<TrendRange>(3);
  return (
    <TrendRangeContext.Provider value={{ range, setRange }}>{children}</TrendRangeContext.Provider>
  );
}

export function useTrendRange(): TrendRangeValue {
  const value = useContext(TrendRangeContext);
  if (!value) {
    throw new Error("useTrendRange must be used inside TrendRangeProvider");
  }
  return value;
}
