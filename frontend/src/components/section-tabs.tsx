"use client";

import { useRef, useState } from "react";
import type { KeyboardEvent, ReactNode } from "react";

export type SectionTab = {
  key: string;
  label: string;
  panel: ReactNode;
  /** Marks the tab as having pending work, without changing its label. */
  attention?: boolean;
};

const TAB_CLASS =
  "rounded-full px-4 py-2 text-sm font-semibold whitespace-nowrap transition focus-visible:ring-2 focus-visible:ring-accent-ring";
const ACTIVE_TAB_CLASS = "bg-ink text-page";
const INACTIVE_TAB_CLASS = "text-muted hover:bg-elevated hover:text-ink";

/**
 * Section tabs shared by the workspace pages. The active tab is mirrored into
 * `?tab=` so a refresh or a shared link opens the same section, and the tab
 * list follows the WAI-ARIA tabs keyboard contract (arrows move, Home/End jump
 * to the ends). `clearParams` removes stale query state, such as a history
 * cursor, when the section changes.
 */
export default function SectionTabs({
  initialTab,
  tabs,
  label,
  defaultKey,
  clearParams = [],
}: {
  initialTab: string;
  tabs: SectionTab[];
  label: string;
  defaultKey: string;
  clearParams?: string[];
}) {
  const [activeTab, setActiveTab] = useState(() =>
    tabs.some((tab) => tab.key === initialTab) ? initialTab : defaultKey,
  );
  const idPrefix = label
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "");

  const tabRefs = useRef<Record<string, HTMLButtonElement | null>>({});

  function replaceTabInUrl(key: string) {
    const url = new URL(window.location.href);
    if (key === defaultKey) {
      url.searchParams.delete("tab");
    } else {
      url.searchParams.set("tab", key);
    }
    for (const param of clearParams) {
      url.searchParams.delete(param);
    }
    window.history.replaceState(null, "", url);
  }

  function selectTab(key: string, focus: boolean) {
    setActiveTab(key);
    replaceTabInUrl(key);
    if (focus) {
      tabRefs.current[key]?.focus();
    }
  }

  function handleKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    const currentIndex = tabs.findIndex((tab) => tab.key === activeTab);
    let nextIndex: number | null = null;

    if (event.key === "ArrowRight" || event.key === "ArrowDown") {
      nextIndex = (currentIndex + 1) % tabs.length;
    } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
      nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
    } else if (event.key === "Home") {
      nextIndex = 0;
    } else if (event.key === "End") {
      nextIndex = tabs.length - 1;
    }

    if (nextIndex === null) {
      return;
    }

    event.preventDefault();
    selectTab(tabs[nextIndex].key, true);
  }

  return (
    <div>
      <div
        aria-label={label}
        className="flex flex-wrap gap-1 rounded-2xl border border-line bg-surface p-1"
        role="tablist"
      >
        {tabs.map((tab) => {
          const isActive = tab.key === activeTab;
          return (
            <button
              aria-controls={`${idPrefix}-panel-${tab.key}`}
              aria-selected={isActive}
              className={`${TAB_CLASS} border-2 ${
                tab.attention ? "border-warning-line" : "border-transparent"
              } ${isActive ? ACTIVE_TAB_CLASS : INACTIVE_TAB_CLASS}`}
              title={tab.attention ? "There is work waiting in this section." : undefined}
              id={`${idPrefix}-tab-${tab.key}`}
              key={tab.key}
              onClick={() => selectTab(tab.key, false)}
              onKeyDown={handleKeyDown}
              ref={(node) => {
                tabRefs.current[tab.key] = node;
              }}
              role="tab"
              tabIndex={isActive ? 0 : -1}
              type="button"
            >
              {tab.label}
            </button>
          );
        })}
      </div>

      {tabs.map((tab) => (
        <div
          aria-labelledby={`${idPrefix}-tab-${tab.key}`}
          className="mt-6"
          hidden={tab.key !== activeTab}
          id={`${idPrefix}-panel-${tab.key}`}
          key={tab.key}
          role="tabpanel"
          tabIndex={0}
        >
          {tab.panel}
        </div>
      ))}
    </div>
  );
}
