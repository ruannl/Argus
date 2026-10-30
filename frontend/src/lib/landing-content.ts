/**
 * Static landing-page copy for the kit. Replace this content when you
 * instantiate the kit for a real product; the layout, the auth entry
 * points, and the illustrative sample page all keep working with new copy.
 */

export type SamplePageContent = Readonly<{
  title: string;
  context: string;
  sections: Readonly<{
    heading: string;
    paragraphs: readonly string[];
  }[]>;
}>;

export const ILLUSTRATIVE_SAMPLE_LABEL =
  "Illustrative example—not part of your product.";

export const STATUS_EXAMPLE = "Example: replace this with your product's live status.";

export const PRIMARY_CTA_LABEL = "View the sample page";

export const AUDIENCE_OUTCOME_LEAD =
  "A complete starting point for your next SaaS product.";

export const AUDIENCE_OUTCOME_DETAIL =
  "This kit ships with secure customer accounts, provider-agnostic billing, an admin surface, structured logging, and a hardened CI pipeline. Rename it, build on it, and ship.";

export const FEATURE_HIGHLIGHTS = [
  {
    heading: "Customer accounts",
    description:
      "Registration, email verification, login, password reset, lockout protection, and breach screening — opaque sessions, argon2id hashes, and non-enumerating responses.",
  },
  {
    heading: "Billing and admin",
    description:
      "A provider-agnostic billing trait with a mock implementation, signed webhooks, and an admin-only page that renders account state.",
  },
  {
    heading: "Production posture",
    description:
      "Structured logging with an audit trail, security headers, per-IP rate limiting, supply-chain gates, and a non-root Docker image with a healthcheck.",
  },
] as const;

export const HOW_IT_WORKS_STEPS = [
  "Start from this kit",
  "Run the bootstrap script to set up local development",
  "Rename the product identity via the instantiation guide",
  "Build your product features",
  "Verify and deploy behind TLS",
] as const;

export const SAMPLE_PAGE_CONTENT: SamplePageContent = {
  title: "A sample content page",
  context: "Kit demo",
  sections: [
    {
      heading: "What this page demonstrates",
      paragraphs: [
        "This is a labelled sample showing the page layout and content structure you can reuse for your product's detail pages. It is deliberately generic.",
        "When you instantiate the kit, replace this page with your own content — or delete it entirely.",
      ],
    },
    {
      heading: "How the layout works",
      paragraphs: [
        "A header with context and title, then numbered sections with a heading and supporting paragraphs. The design system is Tailwind 4 with a small set of shared primitives.",
        "The back-link and the label banner are part of the page, not the layout, so they travel with any page you copy from this one.",
      ],
    },
    {
      heading: "What to replace before launch",
      paragraphs: [
        "Every string on this page lives in `landing-content.ts`, so a rename is mechanical. The label banner must stay clearly marked as illustrative until you swap the content.",
        "Keep the accessibility structure (headings, labelled sections, link text) as you replace the copy.",
      ],
    },
  ],
};

export const SAMPLE_PAGE_FOOTER =
  "Replace or delete this page when you instantiate the kit.";

export const FORBIDDEN_UI_PHRASES = [
  // Marketing-claim rules for the placeholder landing. Remove or extend for
  // your product; the e2e suite asserts these never appear on public pages.
  "Subscribe",
  "Pricing",
  "get rich",
  "guaranteed",
  "10/month",
] as const;
