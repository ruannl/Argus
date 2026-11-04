/**
 * Static landing-page copy for Argus. The layout, auth entry points, and the
 * workspace shell keep working with new copy, but the export names are used by
 * the unit and end-to-end tests.
 */

export const PRODUCT_TAGLINE = "See where your money goes";

export const AUDIENCE_OUTCOME_LEAD = "See where your money goes.";

export const AUDIENCE_OUTCOME_DETAIL =
  "Make sense of your money. See the patterns. Understand your financial behaviour. Import your South African ZAR bank statements, classify transactions by category and merchant tag, and explore period overviews and bar charts that reconcile to every transaction.";

export const PRIMARY_CTA_LABEL = "Start with your statements";

export const STATUS_EXAMPLE =
  "ZAR workspace · Manual statement import · Reconciled to every transaction";

export const FEATURE_HIGHLIGHTS = [
  {
    heading: "Import your statements",
    description:
      "Bring in FNB CSV or ZIP statements and Standard Bank PDF statements, or map a generic CSV. Every row is validated and reviewed before it is committed.",
  },
  {
    heading: "Classify every transaction",
    description:
      "Group transactions by category and merchant tag. A system Uncategorised bucket means nothing is ever lost, and a kind change asks for confirmation first.",
  },
  {
    heading: "Totals that reconcile",
    description:
      "Period overviews and bar-first charts for Money in, Money out, and Net, reconciled to every transaction in your ZAR workspace.",
  },
] as const;

export const HOW_IT_WORKS_STEPS = [
  "Create your account",
  "Add your ZAR financial accounts",
  "Import a bank or credit-card statement",
  "Classify transactions by category and merchant tag",
  "Explore period overviews and bar charts",
] as const;

export const FORBIDDEN_UI_PHRASES = [
  // Marketing-claim rules for the public landing page. The unit and e2e suites
  // assert these never appear on public pages.
  "Subscribe",
  "Pricing",
  "get rich",
  "guaranteed",
  "10/month",
  "SaaS starter",
  "sample page",
] as const;
