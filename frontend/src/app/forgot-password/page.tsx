import Link from "next/link";

import ForgotPasswordForm from "./forgot-password-form";

export default function ForgotPasswordPage() {
  return (
    <main className="min-h-screen overflow-hidden">
      <div className="mx-auto flex min-h-screen w-full max-w-3xl flex-col px-5 py-6 sm:px-8 sm:py-8 lg:px-12">
        <Link
          className="back-link inline-flex w-fit items-center gap-2 rounded-full px-2 py-1 text-sm font-semibold text-muted transition hover:text-ink"
          href="/login"
        >
          <span aria-hidden="true" className="text-lg leading-none">
            ←
          </span>
          Back to sign in
        </Link>

        <section
          aria-labelledby="forgot-password-title"
          className="my-auto rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-10"
        >
          <p className="text-sm font-semibold tracking-[0.14em] text-accent-text uppercase">
            Account recovery
          </p>
          <h1
            className="mt-4 text-4xl leading-tight font-semibold tracking-[-0.05em] text-ink sm:text-5xl"
            id="forgot-password-title"
          >
            Forgot your password?
          </h1>
          <p className="mt-5 max-w-xl text-base leading-7 text-muted sm:text-lg sm:leading-8">
            Enter your email to request a password reset link.
          </p>
          <ForgotPasswordForm />
        </section>
      </div>
    </main>
  );
}
