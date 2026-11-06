import Link from "next/link";
import { redirect } from "next/navigation";

import { auth } from "@/auth";
import AppShell from "@/components/app-shell";
import WorkspaceExpired from "@/components/workspace-expired";
import {
  getCurrentAccount,
  getImportStatement,
} from "@/lib/server/backend-client";
import type { ImportErrorKind } from "@/lib/server/backend-client";
import StatementWorkspace from "./statement-workspace";

export const dynamic = "force-dynamic";

function StatementUnavailable({ kind }: { kind: ImportErrorKind }) {
  const message =
    kind === "not_found"
      ? "That statement no longer exists."
      : "That statement could not be loaded. Refresh the page or return to Imports.";
  return (
    <section className="mt-8 rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-8">
      <h2 className="text-2xl font-semibold tracking-[-0.03em] text-ink">Statement unavailable</h2>
      <p className="mt-3 max-w-2xl text-base leading-7 text-muted">{message}</p>
      <Link
        className="primary-link mt-5 inline-flex items-center justify-center rounded-full px-5 py-3 text-sm font-bold"
        href="/imports"
      >
        Back to imports
      </Link>
    </section>
  );
}

export default async function StatementPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ page?: string }>;
}) {
  const token = (await auth())?.user?.backendToken;
  if (!token) {
    redirect("/login");
  }

  let account = null;
  try {
    account = await getCurrentAccount(token);
  } catch {
    // Fail closed below.
  }
  if (!account) {
    return <WorkspaceExpired />;
  }

  const { id } = await params;
  const { page: pageParam } = await searchParams;
  const page = Math.max(1, Number.parseInt(pageParam ?? "1", 10) || 1);
  const result = await getImportStatement(token, id, page, 50);

  return (
    <AppShell
      account={account}
      activeNav="imports"
      description="A committed statement and the rows it created. Individual transactions are never deleted; hide or restore the whole statement."
      eyebrow="Workspace"
      title="Statement detail"
    >
      {result.ok ? (
        <StatementWorkspace detail={result.value} />
      ) : (
        <StatementUnavailable kind={result.kind} />
      )}
    </AppShell>
  );
}
