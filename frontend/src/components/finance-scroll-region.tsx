"use client";

import { usePathname, useSearchParams } from "next/navigation";
import { useEffect, useLayoutEffect, useRef } from "react";
import type { ReactNode } from "react";

const POSITION_PREFIX = "argus-finance-scroll:";

type NavigationWindow = Window & {
  navigation?: {
    currentEntry: {
      key: string;
    } | null;
  };
};

function entryKey(): string {
  const navigation = (window as NavigationWindow).navigation;
  if (navigation?.currentEntry?.key) {
    return `entry:${navigation.currentEntry.key}`;
  }

  // Navigation API is Baseline 2026. The fallback keeps one position per route
  // for a browser that cannot identify individual history entries.
  return `path:${window.location.pathname}`;
}

function readPosition(key: string): number | null {
  try {
    const stored = window.sessionStorage.getItem(`${POSITION_PREFIX}${key}`);
    if (stored === null) {
      return null;
    }
    const value = Number(stored);
    return Number.isFinite(value) && value >= 0 ? value : null;
  } catch {
    return null;
  }
}

function writePosition(key: string, value: number): void {
  try {
    // Only a numeric offset is stored; never search text, money or row data.
    window.sessionStorage.setItem(`${POSITION_PREFIX}${key}`, String(Math.max(0, value)));
  } catch {
    // Scroll restoration is a convenience. Restricted storage must not break
    // navigation or the finance workflow.
  }
}

/**
 * Finance-only scroll region. The shell stays in place while this element
 * scrolls, so a new destination starts at its content top and each browser
 * history entry can retain an independent position for Back/Forward.
 */
export default function FinanceScrollRegion({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const search = useSearchParams().toString();
  const nodeRef = useRef<HTMLDivElement>(null);
  const activeKey = useRef<string | null>(null);
  const latestPosition = useRef(0);

  useLayoutEffect(() => {
    const node = nodeRef.current;
    if (!node) {
      return;
    }
    const nextKey = entryKey();
    if (activeKey.current === nextKey) {
      return;
    }
    if (activeKey.current) {
      writePosition(activeKey.current, latestPosition.current);
    }
    activeKey.current = nextKey;
    const saved = readPosition(nextKey);
    if (saved === null) {
      node.scrollTop = 0;
      latestPosition.current = 0;
      return;
    }
    const maximum = Math.max(0, node.scrollHeight - node.clientHeight);
    const target = Math.min(saved, maximum);
    node.scrollTop = target;
    latestPosition.current = target;
  }, [pathname, search]);

  useEffect(() => {
    const node = nodeRef.current;
    if (!node) {
      return;
    }
    const scroller = node;

    let frame: number | null = null;
    function persist() {
      frame = null;
      if (activeKey.current) {
        writePosition(activeKey.current, latestPosition.current);
      }
    }
    function handleScroll() {
      latestPosition.current = scroller.scrollTop;
      if (frame === null) {
        frame = window.requestAnimationFrame(persist);
      }
    }

    scroller.addEventListener("scroll", handleScroll, { passive: true });
    return () => {
      scroller.removeEventListener("scroll", handleScroll);
      if (frame !== null) {
        window.cancelAnimationFrame(frame);
      }
      if (activeKey.current) {
        writePosition(activeKey.current, latestPosition.current);
      }
    };
  }, []);

  return (
    <div className="finance-scroll" ref={nodeRef}>
      {children}
    </div>
  );
}
