import type { CurrentAccount } from "@/lib/server/backend-client";

function displayTimestamp(value: string | null): string {
  return value ?? "Not available";
}

/**
 * The canonical account-state field list, shared by the customer home and the
 * admin page. Renders only backend `CurrentAccount` fields — never the raw
 * backend token or anything not present on that type.
 */
export default function AccountDetails({ account }: { account: CurrentAccount }) {
  return (
    <dl className="mt-10 grid gap-5 sm:grid-cols-2">
      <div>
        <dt className="text-sm font-semibold text-muted">Email</dt>
        <dd className="mt-1 text-base font-semibold text-ink">{account.email}</dd>
      </div>
      <div>
        <dt className="text-sm font-semibold text-muted">Email verified</dt>
        <dd className="mt-1 text-base font-semibold text-ink">
          {account.email_verified ? "Yes" : "No"}
        </dd>
      </div>
      <div>
        <dt className="text-sm font-semibold text-muted">Subscriber active</dt>
        <dd className="mt-1 text-base font-semibold text-ink">
          {account.subscriber_active ? "Yes" : "No"}
        </dd>
      </div>
      <div>
        <dt className="text-sm font-semibold text-muted">Lifetime access</dt>
        <dd className="mt-1 text-base font-semibold text-ink">{account.lifetime ? "Yes" : "No"}</dd>
      </div>
      <div>
        <dt className="text-sm font-semibold text-muted">Subscription started</dt>
        <dd className="mt-1 text-base font-semibold text-ink">
          {displayTimestamp(account.subscription_started_at)}
        </dd>
      </div>
      <div>
        <dt className="text-sm font-semibold text-muted">Next billing date</dt>
        <dd className="mt-1 text-base font-semibold text-ink">
          {displayTimestamp(account.next_billing_at)}
        </dd>
      </div>
      <div>
        <dt className="text-sm font-semibold text-muted">Admin account</dt>
        <dd className="mt-1 text-base font-semibold text-ink">{account.admin ? "Yes" : "No"}</dd>
      </div>
    </dl>
  );
}
