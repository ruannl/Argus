import { redirect } from "next/navigation";

import { auth } from "@/auth";
import { endSessionAction } from "@/lib/server/account-actions";
import { getCurrentAccount } from "@/lib/server/backend-client";
import type { CurrentAccount } from "@/lib/server/backend-client";

import AccountDetails from "@/components/account-details";
import ClearSession from "./clear-session";

export const dynamic = "force-dynamic";

function ExpiredSession() {
  return (
    <main className="min-h-screen px-5 py-8 sm:px-8 lg:px-12">
      <div className="mx-auto max-w-3xl rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-10">
        <ClearSession />
      </div>
    </main>
  );
}

function AccountHome({ account }: { account: CurrentAccount }) {
  return (
    <main className="min-h-screen px-5 py-8 sm:px-8 lg:px-12">
      <div className="mx-auto max-w-3xl rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-10">
        <div className="flex flex-wrap items-start justify-between gap-5">
          <div>
            <p className="text-sm font-semibold tracking-[0.14em] text-accent-text uppercase">
              Customer home
            </p>
            <h1 className="mt-4 text-4xl leading-tight font-semibold tracking-[-0.05em] text-ink sm:text-5xl">
              Your account
            </h1>
          </div>
          <div className="flex items-center gap-3">
            {account.admin ? (
              <a
                className="inline-flex items-center justify-center rounded-full border border-warning-line bg-warning-soft px-5 py-2.5 text-sm font-bold text-warning-ink-soft transition hover:bg-warning-soft-hover"
                href="/admin"
              >
                Admin
              </a>
            ) : null}
            <form action={endSessionAction}>
              <button
                className="inline-flex items-center justify-center rounded-full border border-line px-5 py-2.5 text-sm font-bold text-ink transition hover:border-ink hover:text-ink"
                type="submit"
              >
                Log out
              </button>
            </form>
          </div>
        </div>

        <AccountDetails account={account} />
      </div>
    </main>
  );
}

export default async function HomePage() {
  const backendToken = (await auth())?.user?.backendToken;
  if (!backendToken) {
    redirect("/login");
  }

  let account: CurrentAccount | null = null;
  try {
    account = await getCurrentAccount(backendToken);
  } catch {
    // Invalid, expired, revoked, and unavailable backend sessions all fail closed.
  }

  return account ? <AccountHome account={account} /> : <ExpiredSession />;
}
