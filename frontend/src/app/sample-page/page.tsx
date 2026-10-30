import Link from "next/link";

import { PRODUCT_NAME } from "@/lib/brand";
import {
  ILLUSTRATIVE_SAMPLE_LABEL,
  SAMPLE_PAGE_CONTENT,
  SAMPLE_PAGE_FOOTER,
} from "@/lib/landing-content";

export default function SamplePage() {
  const sample = SAMPLE_PAGE_CONTENT;

  return (
    <main className="min-h-screen overflow-hidden">
      <div className="mx-auto w-full max-w-5xl px-5 py-6 sm:px-8 sm:py-8 lg:px-12">
        <Link
          className="back-link inline-flex items-center gap-2 rounded-full px-2 py-1 text-sm font-semibold text-muted transition hover:text-ink"
          href="/"
        >
          <span aria-hidden="true" className="text-lg leading-none">
            ←
          </span>
          Back to {PRODUCT_NAME}
        </Link>

        <div className="mt-8 rounded-[2rem] border border-warning-line bg-warning-soft p-5 sm:mt-10 sm:p-7">
          <p className="text-xs font-bold tracking-[0.14em] text-warning-ink-soft uppercase">
            Sample label
          </p>
          <p className="mt-3 max-w-3xl text-base leading-7 font-semibold text-warning-ink sm:text-lg">
            {ILLUSTRATIVE_SAMPLE_LABEL}
          </p>
        </div>

        <header className="border-b border-line py-12 sm:py-16">
          <p className="text-sm font-semibold tracking-[0.14em] text-accent-text uppercase">
            {sample.context}
          </p>
          <h1 className="mt-5 max-w-4xl text-4xl leading-[1.05] font-semibold tracking-[-0.05em] text-ink sm:text-6xl">
            {sample.title}
          </h1>
          <p className="mt-6 max-w-2xl text-lg leading-8 text-muted sm:text-xl sm:leading-9">
            A reusable page pattern for your product&apos;s detail pages.
          </p>
        </header>

        <div className="grid gap-5 py-10 sm:py-14">
          {sample.sections.map((section, index) => (
            <section
              aria-labelledby={`sample-section-${index}`}
              className="rounded-[2rem] border border-line bg-surface p-6 sm:p-9"
              key={section.heading}
            >
              <div className="flex items-start gap-4">
                <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-ink text-sm font-semibold text-page">
                  {index + 1}
                </span>
                <h2
                  className="pt-1 text-2xl leading-tight font-semibold tracking-[-0.035em] text-ink sm:text-3xl"
                  id={`sample-section-${index}`}
                >
                  {section.heading}
                </h2>
              </div>
              <div className="mt-6 space-y-4 pl-0 text-base leading-8 text-muted sm:pl-13 sm:text-lg sm:leading-9">
                {section.paragraphs.map((paragraph) => (
                  <p key={paragraph}>{paragraph}</p>
                ))}
              </div>
            </section>
          ))}
        </div>

        <footer className="border-t border-line py-8 text-sm leading-6 text-muted">
          {SAMPLE_PAGE_FOOTER}
        </footer>
      </div>
    </main>
  );
}
