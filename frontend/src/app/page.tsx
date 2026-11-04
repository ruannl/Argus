import Link from "next/link";

import { PRODUCT_NAME } from "@/lib/brand";
import {
  AUDIENCE_OUTCOME_DETAIL,
  AUDIENCE_OUTCOME_LEAD,
  FEATURE_HIGHLIGHTS,
  HOW_IT_WORKS_STEPS,
  PRIMARY_CTA_LABEL,
  PRODUCT_TAGLINE,
  STATUS_EXAMPLE,
} from "@/lib/landing-content";

export default function Home() {
  return (
    <main className="min-h-screen overflow-hidden">
      <div className="mx-auto flex min-h-screen w-full max-w-7xl flex-col px-5 py-6 sm:px-8 sm:py-8 lg:px-12">
        <header className="flex items-center justify-between gap-4 border-b border-line pb-5">
          <p className="text-sm font-semibold tracking-[0.16em] text-ink uppercase">
            {PRODUCT_NAME}
          </p>
          <nav aria-label="Account" className="flex items-center gap-4">
            <Link
              className="text-sm font-semibold text-muted transition hover:text-ink"
              href="/login"
            >
              Log in
            </Link>
            <Link
              className="rounded-full bg-ink px-4 py-2 text-sm font-semibold text-page transition hover:bg-ink-hover"
              href="/register"
            >
              Create an account
            </Link>
          </nav>
        </header>

        <div className="flex flex-1 flex-col">
          <section
            aria-labelledby="landing-title"
            className="grid gap-10 py-16 sm:py-20 lg:grid-cols-[minmax(0,1.15fr)_minmax(18rem,0.85fr)] lg:items-end lg:gap-16 lg:py-28"
          >
            <div>
              <p className="mb-5 inline-flex rounded-full border border-accent-soft-line bg-accent-soft px-3 py-1 text-xs font-semibold tracking-[0.12em] text-accent-soft-ink uppercase">
                ZAR personal finance insights
              </p>
              <h1
                id="landing-title"
                className="max-w-3xl text-5xl leading-[0.98] font-semibold tracking-[-0.055em] text-ink sm:text-7xl lg:text-8xl"
              >
                {PRODUCT_NAME}
              </h1>
              <div className="mt-8 max-w-2xl space-y-5 text-lg leading-8 text-muted sm:text-xl sm:leading-9">
                <p className="font-medium text-ink">{AUDIENCE_OUTCOME_LEAD}</p>
                <p>{AUDIENCE_OUTCOME_DETAIL}</p>
              </div>
            </div>

            <div className="relative rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-8">
              <div className="absolute -top-3 right-7 h-6 w-6 rounded-full border-4 border-page bg-accent" />
              <p className="text-xs font-semibold tracking-[0.14em] text-muted uppercase">
                {PRODUCT_TAGLINE}
              </p>
              <p className="mt-5 text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl">
                Every rand, categorised and reconciled to the statement it came from.
              </p>
              <div className="mt-8 flex items-center gap-3 text-sm font-medium text-muted">
                <span className="h-2 w-2 rounded-full bg-accent" />
                <span>ZAR workspace</span>
              </div>
            </div>
          </section>

          <section
            aria-labelledby="features-title"
            className="border-t border-line py-16 sm:py-20"
          >
            <div className="max-w-2xl">
              <p className="text-sm font-semibold tracking-[0.14em] text-accent-text uppercase">
                What you get
              </p>
              <h2
                id="features-title"
                className="mt-4 text-3xl leading-tight font-semibold tracking-[-0.04em] text-ink sm:text-5xl"
              >
                Your statements, made legible
              </h2>
              <p className="mt-5 text-lg leading-8 text-muted">
                A focused workspace for South African bank and credit-card statements, with
                categories, merchant tags, and totals that reconcile.
              </p>
            </div>
            <div className="mt-10 grid gap-4 md:grid-cols-3">
              {FEATURE_HIGHLIGHTS.map((feature, index) => (
                <article
                  className="rounded-3xl border border-line bg-surface p-6 sm:p-7"
                  key={feature.heading}
                >
                  <p className="text-sm font-semibold text-accent-text">0{index + 1}</p>
                  <h3 className="mt-8 text-xl leading-snug font-semibold tracking-[-0.025em] text-ink">
                    {feature.heading}
                  </h3>
                  <p className="mt-4 text-base leading-7 text-muted">
                    {feature.description}
                  </p>
                </article>
              ))}
            </div>
          </section>

          <section
            aria-labelledby="how-it-works-title"
            className="border-t border-line py-16 sm:py-20"
          >
            <div className="grid gap-10 lg:grid-cols-[0.75fr_1.25fr] lg:gap-20">
              <div>
                <p className="text-sm font-semibold tracking-[0.14em] text-accent-text uppercase">
                  From statement to insight
                </p>
                <h2
                  id="how-it-works-title"
                  className="mt-4 text-3xl leading-tight font-semibold tracking-[-0.04em] text-ink sm:text-5xl"
                >
                  How it works
                </h2>
              </div>
              <ol className="divide-y divide-line rounded-3xl border border-line bg-surface">
                {HOW_IT_WORKS_STEPS.map((step, index) => (
                  <li
                    className="flex items-start gap-5 px-5 py-5 sm:px-7 sm:py-6"
                    key={step}
                  >
                    <span className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-ink text-sm font-semibold text-page">
                      {index + 1}
                    </span>
                    <span className="text-lg leading-8 font-medium text-ink">{step}</span>
                  </li>
                ))}
              </ol>
            </div>
          </section>

          <section
            aria-labelledby="status-title"
            className="border-t border-line py-16 sm:py-20"
          >
            <div className="mx-auto max-w-4xl rounded-[2rem] border border-accent-soft-line bg-accent-soft p-6 sm:p-8 lg:flex lg:items-center lg:justify-between lg:gap-12 lg:p-10">
              <div>
                <p className="text-xs font-semibold tracking-[0.14em] text-accent-soft-ink uppercase">
                  Workspace status
                </p>
                <h2
                  id="status-title"
                  className="mt-3 text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
                >
                  Reconciled to every transaction
                </h2>
                <p className="mt-3 max-w-xl text-base leading-7 text-muted">
                  Your overview totals are built from the same transactions you can inspect
                  and classify, so the numbers always agree.
                </p>
              </div>
              <p className="mt-7 shrink-0 rounded-2xl border border-accent-soft-line bg-surface px-5 py-4 text-base font-semibold text-ink shadow-sm lg:mt-0">
                {STATUS_EXAMPLE}
              </p>
            </div>
          </section>

          <section
            aria-labelledby="start-cta-title"
            className="border-t border-line py-16 sm:py-20"
          >
            <div className="rounded-[2rem] bg-inverse px-6 py-10 text-inverse-ink sm:px-10 sm:py-12 lg:flex lg:items-center lg:justify-between lg:gap-10 lg:px-12">
              <div className="max-w-2xl">
                <p className="text-sm font-semibold tracking-[0.14em] text-accent-inverse uppercase">
                  Ready when you are
                </p>
                <h2
                  id="start-cta-title"
                  className="mt-4 text-3xl leading-tight font-semibold tracking-[-0.04em] sm:text-5xl"
                >
                  Start with your statements.
                </h2>
                <p className="mt-5 text-base leading-7 text-inverse-muted sm:text-lg">
                  Create an account, add your ZAR financial accounts, and import your first
                  statement.
                </p>
              </div>
              <Link
                aria-label={PRIMARY_CTA_LABEL}
                className="primary-link mt-8 inline-flex shrink-0 items-center justify-center gap-3 rounded-full bg-accent px-6 py-4 text-base font-bold text-accent-ink transition hover:bg-accent-hover lg:mt-0"
                href="/register"
              >
                {PRIMARY_CTA_LABEL}
                <span aria-hidden="true" className="text-xl leading-none">
                  →
                </span>
              </Link>
              <Link
                className="mt-4 inline-flex text-sm font-semibold text-inverse-muted underline decoration-inverse-muted underline-offset-4 transition hover:text-inverse-ink lg:ml-6 lg:mt-0"
                href="/login"
              >
                Log in
              </Link>
            </div>
          </section>
        </div>
      </div>
    </main>
  );
}
