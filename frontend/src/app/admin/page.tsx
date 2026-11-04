import { redirect } from "next/navigation";

import { auth } from "@/auth";
import { getCurrentAccount } from "@/lib/server/backend-client";
import type { CurrentAccount } from "@/lib/server/backend-client";

import AccountDetails from "@/components/account-details";
import ClearSession from "@/components/clear-session";

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

export default async function AdminPage() {
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

  if (!account) {
    return <ExpiredSession />;
  }

  // The backend is the authority for admin capability; this page only renders
  // when `/v1/me` reports admin: true. Non-admins are sent to the customer home.
  if (!account.admin) {
    redirect("/home");
  }

  return (
    <main className="min-h-screen px-5 py-8 sm:px-8 lg:px-12">
      <div className="mx-auto max-w-3xl rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-10">
        <div className="flex flex-wrap items-start justify-between gap-5">
          <div>
            <p className="inline-flex rounded-full border border-warning-line bg-warning-soft px-3 py-1 text-xs font-bold tracking-[0.14em] text-warning-text uppercase">
              ADMIN
            </p>
            <h1 className="mt-4 text-4xl leading-tight font-semibold tracking-[-0.05em] text-ink sm:text-5xl">
              Admin
            </h1>
          </div>
          <a
            className="inline-flex items-center justify-center rounded-full border border-line px-5 py-2.5 text-sm font-bold text-ink transition hover:border-ink hover:text-ink"
            href="/home"
          >
            Customer home
          </a>
        </div>

        <AccountDetails account={account} />
      </div>
    </main>
  );
}
