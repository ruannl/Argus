import Link from "next/link";
import type { ReactNode } from "react";

import FinanceScrollRegion from "@/components/finance-scroll-region";
import { PRODUCT_NAME } from "@/lib/brand";
import { endSessionAction } from "@/lib/server/account-actions";
import type { CurrentAccount } from "@/lib/server/backend-client";

export type WorkspaceNav = "overview" | "transactions" | "imports" | "settings";

const NAV_ITEMS: ReadonlyArray<{ key: WorkspaceNav; label: string; href: string }> = [
  { key: "overview", label: "Overview", href: "/home" },
  { key: "transactions", label: "Transactions", href: "/transactions" },
  { key: "imports", label: "Imports", href: "/imports" },
  { key: "settings", label: "Settings", href: "/settings" },
];

type AppShellProps = {
  account: CurrentAccount;
  activeNav: WorkspaceNav;
  title: string;
  description?: string;
  children: ReactNode;
};

function NavigationIcon({ destination }: { destination: WorkspaceNav }) {
  const paths: Record<WorkspaceNav, string> = {
    overview: "M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z",
    transactions: "M4 7h16m-4-4 4 4-4 4M20 17H4m4-4-4 4 4 4",
    imports: "M12 3v12m-4-4 4 4 4-4M4 16v5h16v-5",
    settings: "M4 6h16M4 12h16M4 18h16M8 3v6m8 0v6M8 15v6",
  };
  return (
    <svg
      aria-hidden="true"
      className="finance-nav-icon"
      fill="none"
      focusable="false"
      stroke="currentColor"
      strokeLinecap="round"
      strokeLinejoin="round"
      strokeWidth="1.7"
      viewBox="0 0 24 24"
    >
      <path d={paths[destination]} />
    </svg>
  );
}

/**
 * Persistent authenticated workspace frame. The nav wraps onto its own row on
 * narrow screens so the four destinations stay reachable without horizontal
 * overflow, and the active destination carries `aria-current="page"`.
 */
export default function AppShell({
  account,
  activeNav,
  title,
  description,
  children,
}: AppShellProps) {
  return (
    <main className="finance-workspace finance-app min-h-screen">
      <header className="finance-shell border-b border-line bg-surface">
        <div className="finance-shell-inner mx-auto flex w-full max-w-6xl flex-wrap items-center gap-x-4 gap-y-2 px-4 py-1.5 sm:px-6 lg:px-8">
          <Link
            className="text-[0.8125rem] font-semibold tracking-[0.08em] text-ink uppercase"
            href="/home"
          >
            {PRODUCT_NAME}
          </Link>
          <span className="finance-workspace-marker text-muted">ZAR workspace</span>

          <nav
            aria-label="Workspace"
            className="finance-navigation order-last flex w-full flex-wrap items-center gap-1 lg:order-none lg:w-auto"
          >
            {NAV_ITEMS.map((item) => {
              const isActive = item.key === activeNav;
              return (
                <Link
                  aria-current={isActive ? "page" : undefined}
                  className="finance-nav-link"
                  href={item.href}
                  key={item.key}
                >
                  <NavigationIcon destination={item.key} />
                  {item.label}
                </Link>
              );
            })}
          </nav>

          <div className="ml-auto flex flex-wrap items-center gap-1">
            {account.admin ? (
              <Link className="finance-nav-utility finance-nav-utility-accent" href="/admin">
                Admin
              </Link>
            ) : null}
            <form action={endSessionAction}>
              <button className="finance-nav-utility" type="submit">
                Log out
              </button>
            </form>
          </div>
        </div>
      </header>

      <FinanceScrollRegion>
        <div className="finance-content mx-auto w-full max-w-6xl px-4 pt-5 pb-24 sm:px-6 lg:px-8">
          <header className="finance-page-heading max-w-3xl">
            <h1 className="text-ink">{title}</h1>
            {description ? <p className="mt-1 text-muted">{description}</p> : null}
          </header>
          {children}
        </div>
      </FinanceScrollRegion>
    </main>
  );
}
