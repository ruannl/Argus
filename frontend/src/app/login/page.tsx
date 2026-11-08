import Link from "next/link";
import { redirect } from "next/navigation";

import { auth } from "@/auth";
import { PRODUCT_NAME } from "@/lib/brand";
import { getCurrentAccount } from "@/lib/server/backend-client";

import ClearSession from "@/components/clear-session";
import LoginForm from "./login-form";

export const dynamic = "force-dynamic";

export default async function LoginPage() {
  const backendToken = (await auth())?.user?.backendToken;
  if (backendToken) {
    let backendSessionIsValid = false;
    try {
      await getCurrentAccount(backendToken);
      backendSessionIsValid = true;
    } catch {}

    if (backendSessionIsValid) {
      redirect("/home");
    }
  }

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
          aria-labelledby="login-title"
          className="my-auto rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-10"
        >
          <p className="text-sm font-semibold tracking-[0.14em] text-accent-text uppercase">
            Customer access
          </p>
          <h1
            className="mt-4 text-4xl leading-tight font-semibold tracking-[-0.05em] text-ink sm:text-5xl"
            id="login-title"
          >
            Sign in
          </h1>
          {backendToken ? <ClearSession /> : null}
          <p className="mt-5 max-w-xl text-base leading-7 text-muted sm:text-lg sm:leading-8">
            Use the email and password for your verified account.
          </p>
          <LoginForm />
        </section>
      </div>
    </main>
  );
}
