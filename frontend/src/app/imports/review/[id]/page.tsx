import Link from "next/link";
import { redirect } from "next/navigation";

import { auth } from "@/auth";
import AppShell from "@/components/app-shell";
import WorkspaceExpired from "@/components/workspace-expired";
import { getCurrentAccount, getImportReview, listMerchantTags } from "@/lib/server/backend-client";
import type { ImportErrorKind } from "@/lib/server/backend-client";
import ReviewWorkspace from "./review-workspace";

export const dynamic = "force-dynamic";

function ReviewUnavailable({ kind }: { kind: ImportErrorKind }) {
  const message =
    kind === "review_expired"
      ? "This review has expired. Upload the statement again to prepare a new review."
      : kind === "not_found"
        ? "That review no longer exists."
        : "That review could not be loaded. Refresh the page or start a new review.";
  return (
    <section className="mt-8 rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-8">
      <h2 className="text-2xl font-semibold tracking-[-0.03em] text-ink">Review unavailable</h2>
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

export default async function ReviewPage({
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
  const [review, tags] = await Promise.all([
    getImportReview(token, id, page, 50),
    listMerchantTags(token),
  ]);

  if (!review.ok) {
    return (
      <AppShell
        account={account}
        activeNav="imports"
        description="Review a statement before committing it."
        eyebrow="Workspace"
        title="Import review"
      >
        <ReviewUnavailable kind={review.kind} />
      </AppShell>
    );
  }

  return (
    <AppShell
      account={account}
      activeNav="imports"
      description="Every row is read-only. Decisions are limited to skip, duplicate override, and merchant-tag selection."
      eyebrow="Workspace"
      title="Import review"
    >
      <ReviewWorkspace
        key={review.value.summary.version}
        review={review.value}
        tags={tags.ok ? tags.value.merchant_tags : []}
      />
    </AppShell>
  );
}
