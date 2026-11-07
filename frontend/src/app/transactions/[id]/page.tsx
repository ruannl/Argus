import Link from "next/link";
import { redirect } from "next/navigation";

import { auth } from "@/auth";
import AppShell from "@/components/app-shell";
import WorkspaceExpired from "@/components/workspace-expired";
import {
  getCurrentAccount,
  getTransaction,
  listCategories,
  listMerchantTags,
} from "@/lib/server/backend-client";
import type {
  Category,
  CurrentAccount,
  MerchantTag,
  TransactionDetail,
  TransactionErrorKind,
} from "@/lib/server/backend-client";

import TransactionDetailView from "../transaction-detail";

export const dynamic = "force-dynamic";

const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function DetailUnavailable({
  account,
  message,
  title = "Transaction unavailable",
}: {
  account: CurrentAccount;
  message: string;
  title?: string;
}) {
  return (
    <AppShell
      account={account}
      activeNav="transactions"
      description="A single imported transaction with its read-only source identity and editable classification."
      eyebrow="Transaction"
      title="Transaction detail"
    >
      <section className="mt-8 rounded-[2rem] border border-warning-line bg-warning-soft p-6">
        <h2 className="text-xl font-semibold text-warning-ink">{title}</h2>
        <p className="mt-2 max-w-2xl text-sm leading-6 text-warning-ink" role="alert">
          {message}
        </p>
        <div className="mt-4 flex flex-wrap gap-2">
          <Link
            className="rounded-full border border-line bg-surface px-4 py-2 text-sm font-bold text-ink"
            href="/transactions"
          >
            Back to transactions
          </Link>
          <Link
            className="rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
            href="/imports"
          >
            Open imports
          </Link>
        </div>
      </section>
    </AppShell>
  );
}

export default async function TransactionDetailPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const backendToken = (await auth())?.user?.backendToken;
  if (!backendToken) {
    redirect("/login");
  }

  let account: CurrentAccount | null = null;
  try {
    account = await getCurrentAccount(backendToken);
  } catch {
    // Invalid, expired, revoked, and unavailable backend sessions fail closed.
  }
  if (!account) {
    return <WorkspaceExpired />;
  }

  const { id } = await params;
  if (!UUID_PATTERN.test(id)) {
    return (
      <DetailUnavailable
        account={account}
        message="That transaction address is not valid. Use the list or an Imports link to open an owned transaction."
      />
    );
  }

  let detail: TransactionDetail | null = null;
  let detailFailure: TransactionErrorKind | null = null;
  let categories: Category[] = [];
  let tags: MerchantTag[] = [];
  let inventoryFailed = false;
  try {
    const [detailResult, categoriesResult, tagsResult] = await Promise.all([
      getTransaction(backendToken, id),
      listCategories(backendToken),
      listMerchantTags(backendToken),
    ]);
    if (detailResult.ok) {
      detail = detailResult.value;
    } else {
      detailFailure = detailResult.kind;
    }
    if (categoriesResult.ok) {
      categories = categoriesResult.value.categories;
    } else {
      inventoryFailed = true;
    }
    if (tagsResult.ok) {
      tags = tagsResult.value.merchant_tags;
    } else {
      inventoryFailed = true;
    }
  } catch {
    return (
      <DetailUnavailable
        account={account}
        message="The transaction service could not be reached. This is not proof that the transaction was deleted. Retry this page."
        title="Transaction detail could not load"
      />
    );
  }

  if (!detail) {
    if (detailFailure === "not_found") {
      return (
        <DetailUnavailable
          account={account}
          message="That transaction no longer exists in your workspace. A deleted statement or a foreign identifier has no source to edit or recreate."
          title="Transaction not found"
        />
      );
    }
    if (detailFailure === "unauthorized") {
      return <WorkspaceExpired />;
    }
    return (
      <DetailUnavailable
        account={account}
        message={
          detailFailure === "rate_limited"
            ? "Too many requests right now. Wait a few minutes and retry this page."
            : "The transaction service is unavailable right now. This is not proof that the transaction was deleted. Retry this page."
        }
        title="Transaction detail could not load"
      />
    );
  }

  return (
    <AppShell
      account={account}
      activeNav="transactions"
      description="Imported identity is read-only; category, tags and your individual hide flag are editable under a version check."
      eyebrow="Transaction"
      title="Transaction detail"
    >
      {inventoryFailed ? (
        <p
          className="mt-6 rounded-2xl border border-warning-line bg-warning-soft px-5 py-4 text-sm font-semibold text-warning-ink"
          role="alert"
        >
          Some categories or tags could not be loaded, so classification choices may be incomplete.
          Refresh the page before editing.
        </p>
      ) : null}
      <TransactionDetailView
        categories={categories}
        classificationDisabled={inventoryFailed}
        initialDetail={detail}
        standalone
        tags={tags}
      />
    </AppShell>
  );
}
