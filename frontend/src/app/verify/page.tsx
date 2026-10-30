import Link from "next/link";

import { PRODUCT_NAME } from "@/lib/brand";

import ConfirmControl from "./confirm-control";

export const dynamic = "force-dynamic";

type VerifySearchParams = {
  token?: string | string[];
};

export default async function VerifyPage({
  searchParams,
}: {
  searchParams: Promise<VerifySearchParams>;
}) {
  const params = await searchParams;
  const token = params.token;
  const hasValidToken = typeof token === "string" && /^[0-9a-f]{64}$/.test(token);

  return (
    <main className="min-h-screen overflow-hidden">
      <div className="mx-auto flex min-h-screen w-full max-w-3xl flex-col px-5 py-6 sm:px-8 sm:py-8 lg:px-12">
        <Link
          className="back-link inline-flex w-fit items-center gap-2 rounded-full px-2 py-1 text-sm font-semibold text-muted transition hover:text-ink"
          href="/"
        >
          <span aria-hidden="true" className="text-lg leading-none">
            ←
          </span>
          Back to {PRODUCT_NAME}
        </Link>

        <section
          aria-labelledby="verify-title"
          className="my-auto rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-10"
        >
          <p className="text-sm font-semibold tracking-[0.14em] text-accent-text uppercase">
            Email verification
          </p>
          <h1
            className="mt-4 text-4xl leading-tight font-semibold tracking-[-0.05em] text-ink sm:text-5xl"
            id="verify-title"
          >
            Verify your email
          </h1>

          {hasValidToken ? (
            <div className="mt-8">
              <p className="text-base leading-7 text-muted sm:text-lg sm:leading-8">
                Confirm your email to activate your account.
              </p>
              <ConfirmControl />
            </div>
          ) : (
            <p className="mt-8 rounded-2xl bg-elevated px-5 py-4 text-base leading-7 font-semibold text-ink">
              This verification link is invalid or has expired.
            </p>
          )}
        </section>
      </div>
    </main>
  );
}
