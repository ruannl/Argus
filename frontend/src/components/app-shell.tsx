import Link from "next/link";
import type { ReactNode } from "react";

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
  eyebrow: string;
  description?: string;
  children: ReactNode;
};

/**
 * Persistent authenticated workspace frame. The nav wraps onto its own row on
 * narrow screens so the four destinations stay reachable without horizontal
 * overflow, and the active destination carries `aria-current="page"`.
 */
export default function AppShell({
  account,
  activeNav,
  title,
  eyebrow,
  description,
  children,
}: AppShellProps) {
  return (
    <main className="min-h-screen">
      <header className="border-b border-line bg-surface">
        <div className="mx-auto flex w-full max-w-6xl flex-wrap items-center gap-x-4 gap-y-3 px-4 py-4 sm:px-6 lg:px-8">
          <Link
            className="text-sm font-semibold tracking-[0.16em] text-ink uppercase"
            href="/home"
          >
            {PRODUCT_NAME}
          </Link>
          <span className="inline-flex rounded-full border border-accent-soft-line bg-accent-soft px-3 py-1 text-xs font-semibold text-accent-soft-ink">
            ZAR workspace
          </span>

          <nav
            aria-label="Workspace"
            className="order-last flex w-full flex-wrap items-center gap-1 sm:order-none sm:ml-auto sm:w-auto"
          >
            {NAV_ITEMS.map((item) => {
              const isActive = item.key === activeNav;
              return (
                <Link
                  aria-current={isActive ? "page" : undefined}
                  className={
                    isActive
                      ? "rounded-full bg-ink px-4 py-2 text-sm font-semibold text-page"
                      : "rounded-full px-4 py-2 text-sm font-semibold text-muted transition hover:bg-elevated hover:text-ink"
                  }
                  href={item.href}
                  key={item.key}
                >
                  {item.label}
                </Link>
              );
            })}
          </nav>

          <div className="ml-auto flex items-center gap-2 sm:ml-0">
            {account.admin ? (
              <Link
                className="inline-flex items-center justify-center rounded-full border border-warning-line bg-warning-soft px-4 py-2 text-sm font-bold text-warning-ink-soft transition hover:bg-warning-soft-hover"
                href="/admin"
              >
                Admin
              </Link>
            ) : null}
            <form action={endSessionAction}>
              <button
                className="inline-flex items-center justify-center rounded-full border border-line px-4 py-2 text-sm font-bold text-ink transition hover:border-ink hover:text-ink"
                type="submit"
              >
                Log out
              </button>
            </form>
          </div>
        </div>
      </header>

      <div className="mx-auto w-full max-w-6xl px-4 py-8 sm:px-6 sm:py-10 lg:px-8">
        <header className="max-w-3xl">
          <p className="text-xs font-semibold tracking-[0.14em] text-accent-text uppercase">
            {eyebrow}
          </p>
          <h1 className="mt-3 text-3xl leading-tight font-semibold tracking-[-0.04em] text-ink sm:text-4xl">
            {title}
          </h1>
          {description ? (
            <p className="mt-4 text-base leading-7 text-muted">{description}</p>
          ) : null}
        </header>
        {children}
      </div>
    </main>
  );
}
