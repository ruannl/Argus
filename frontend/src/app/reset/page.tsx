import ResetPasswordForm from "./reset-password-form";

export const dynamic = "force-dynamic";

type ResetSearchParams = {
  token?: string | string[];
};

const INVALID_RESET_LINK_MESSAGE = "This reset link is invalid or has expired.";

export default async function ResetPage({
  searchParams,
}: {
  searchParams: Promise<ResetSearchParams>;
}) {
  const params = await searchParams;
  const token = params.token;
  const hasValidToken = typeof token === "string" && /^[0-9a-f]{64}$/.test(token);

  return (
    <main className="min-h-screen overflow-hidden">
      <div className="mx-auto flex min-h-screen w-full max-w-3xl flex-col px-5 py-6 sm:px-8 sm:py-8 lg:px-12">
        <section
          aria-labelledby="reset-password-title"
          className="my-auto rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-10"
        >
          <p className="text-sm font-semibold tracking-[0.14em] text-accent-text uppercase">
            Account recovery
          </p>
          <h1
            className="mt-4 text-4xl leading-tight font-semibold tracking-[-0.05em] text-ink sm:text-5xl"
            id="reset-password-title"
          >
            Reset your password
          </h1>

          {hasValidToken ? (
            <div className="mt-8">
              <p className="text-base leading-7 text-muted sm:text-lg sm:leading-8">
                Choose a new password for your account.
              </p>
              <ResetPasswordForm />
            </div>
          ) : (
            <p
              aria-live="polite"
              className="mt-8 rounded-2xl bg-elevated px-5 py-4 text-base leading-7 font-semibold text-ink"
            >
              {INVALID_RESET_LINK_MESSAGE}
            </p>
          )}
        </section>
      </div>
    </main>
  );
}
