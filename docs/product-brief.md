# Argus product brief

**Status:** Approved for architecture planning (REQUIREMENTS READY)  
**Date:** 2026-10-30  
**Scope:** Product behaviour and outcomes. This is not an architecture plan.  
**Evidence level:** Source inspection of the working tree. No runtime observation, analytics, or user research was available.

> **Scope amendment (2026-11-06, operator-directed):** Standard Bank PDF import
> is deferred to Thread #6 while the FNB CSV/ZIP and generic mapped CSV path is
> completed and reviewed. Until that thread lands, the product rejects
> `standard_bank_pdf` requests with `pdf_import_deferred` and the Imports UI
> offers only FNB CSV/ZIP and generic mapped CSV. The PDF requirements and
> criteria below remain future scope, not current release scope. The pending
> production-PDF amendment at the end is deferred, not accepted.
>
> **Behavior amendment (2026-11-09, operator-approved):** The following replace
> the earlier whole-token-only suggestion and ±3-day near-duplicate wording.
> Suggestions may use explicit customer aliases, the customer's own historical
> tag/category choices keyed by a generic merchant key, and truncated-prefix
> token matching with exact tokens ranked first; they remain advisory and are
> never auto-applied. Transaction detail may show an advisory suggested
> category. Near-duplicate warnings require the exact transaction date, exact
> signed amount and normalized-description token-set similarity of at least 0.5.
> The finance IP limit is 1000 requests per 15 minutes. Applied bulk outcomes
> auto-dismiss the client receipt identity and show a persistent status line.
> Detailed authority is in Plan 1 revision 2, Plan 2 revision 7 and Plan 3
> revision 4; Thread #1 Decision `e69aabc8-c55b-4e83-b7a9-95a782423ecb` records
> approval. The four-plan topology, CSV-only delivery, PDF deferral and
> no-deployment state are unchanged.

## 1. Executive summary

Argus is intended to be a personal finance insights application. The repository does not yet contain that product.

The working tree is a branded, production-oriented SaaS starter. It provides customer registration, email verification, login, password reset, logout, opaque server-side sessions, an admin account view, a mock billing webhook, a trusted frontend-to-backend boundary, a Tailwind design system, and CI. Nothing in the tree models a bank statement, a financial transaction, a spending category, a financial account, or a chart.

The finance domain is therefore greenfield inside a working SaaS shell. That is good news for delivery: authentication, session security, the frontend/backend boundary, the visual language, and the test harness can be reused. It is bad news for any assumption that Argus already imports statements or reports on spending.

The smallest coherent Argus product is:

> One authenticated workspace where a user creates one or more financial accounts in ZAR, imports FNB CSV, Standard Bank PDF, and generic mapped CSV statements with a confirmed statement date range, reviews parsed transactions and skips any invalid rows, assigns a category and optional store/merchant tags individually or in bulk, and sees a period-based overview and bar-first chart view that reconcile to the underlying transactions.

ZAR is the confirmed initial currency. Multi-currency and foreign exchange are future capabilities. Categories answer what the money was for; merchant tags answer where it went.

Two real FNB CSV exports and two Standard Bank PDF statements were supplied after the first draft. They confirm the intended first two automatic import presets—FNB CSV (ZIP or bare CSV) and text-based Standard Bank PDF—alongside generic CSV column mapping and format disambiguation in the initial release. Standard Bank PDF is currently deferred by the scope amendment above; FNB CSV and generic mapped CSV remain active.

The product stops at insight. It does not budget, advise, aggregate bank feeds, track investments, or chat with the user.

**Tagline:** See where your money goes.

**Core value proposition:** Make sense of your money. See the patterns. Understand your financial behaviour.

**Most important finding:** Every finance capability in the requested product is an MVP gap. The brief must not describe imaginary current behaviour. Section 4 records what exists and what does not; Section 12 records the resolved product decisions; Section 13 defines the four-plan architecture programme.

### 1.1 Authoritative four-part requirement

> **Here is the problem.**  
> The repository provides authenticated customer accounts, session management, and a starter UI, but has no financial domain model, statement import pipeline, transaction workspace, or spending insights, and its public landing page still describes a generic software starter kit. Users with South African bank statements cannot import their statements, classify transactions by purpose and merchant, or see reconciled period summaries and charts without maintaining manual spreadsheets.
>
> **Here is the desired outcome.**  
> Argus is an authenticated ZAR personal finance insights web application where each customer creates financial accounts, imports FNB CSV/ZIP statements, text-based Standard Bank six-column PDF statements, and generic mapped CSV statements with confirmed statement start and end dates, manages categories and merchant tags individually or in bulk, hides or restores transactions and statements (or deletes a committed statement import to re-import cleanly), and explores reconciled period overviews and bar-first charts that drill down to the exact underlying transactions.
>
> **Here are the important constraints.**  
> - **Customer isolation and boundary:** Every finance API read and write uses the existing trusted frontend-to-backend boundary and opaque session token, scoped strictly to the authenticated customer (`SEC-1`). Foreign identifiers never reveal whether a record exists, and the admin surface (`SEC-5`) never exposes financial data.
> - **Privacy and logging:** Financial descriptions, amounts, statement contents, user-defined category and tag names, statement preamble metadata, and raw filenames never appear in logs, audit events, or analytics (`SEC-4`), and raw uploaded files are discarded after commit or abandoned review (`SET-6`).
> - **Immutability and lifecycle:** Source transaction `date`, `description`, and `amount` are immutable both in pre-commit review and after commit; invalid rows in review must be marked to be skipped (`IMP-5`). Post-commit edits may change only `category`, `merchant tags`, and `hidden` state (`TXN-4`). Individual transactions are never deleted (hide and restore only), whereas a committed statement import may be hidden, restored, or permanently deleted with its transactions (`IMP-9`). Hiding and restoring a statement preserves individual transaction hide flags.
> - **Exact arithmetic and reconciliation:** All monetary values are stored and aggregated as exact ZAR amounts without pre-aggregation rounding. Headline metrics use gross directional flows (`Money in`, `Money out`, and `Net`), excluding `Transfer`, `Excluded`, and `Hidden` transactions. Category breakdowns are mutually exclusive and sum to `Money in` and `Money out`; merchant-tag totals overlap when a transaction carries multiple tags and are never summed to headline totals (`OVR-11`).
> - **Idempotency and scope boundaries:** Imports are atomic (`IMP-7`) and occurrence-aware idempotent per financial account (`DUP-12`), including hidden transactions unless their parent import was deleted. The MVP excludes OCR for scanned PDFs, multi-currency conversion, balance importing or reconciliation, automatic categorisation or auto-applied merchant tags, split transactions, budgets, financial advice, bank feeds, and whole-customer account deletion.
>
> **Here is observable success.**  
> - **Positive behaviour:** A verified subscriber can create ZAR financial accounts, import the FNB CSV/ZIP and generic mapped CSV structures after confirming ZAR and statement start/end dates (Standard Bank PDF is deferred per the scope amendment), bulk-assign categories and multiple merchant tags across pages (`AC-CAT-2`), and view Overview summary cards, bar-first charts, period comparisons, and coverage warnings that reconcile to the filtered transaction list and drill down with shared filters.
> - **Negative and failure behaviour:** Archive uploads with zero or multiple CSVs, nested archives, encrypted archives, path traversal, or oversized payloads, as well as scanned, password-protected, or unrecognized-layout PDFs, are rejected before commit with plain-language reasons and zero persisted records (`AC-IMP-10`, `AC-IMP-15`, `AC-IMP-20`). Invalid rows block commit until marked skipped (`AC-IMP-2`). Overlapping imports and retried commits create zero duplicate transactions (`AC-IMP-21`, `AC-IMP-23`). Stale concurrent edits are rejected without silent overwrite, and deleting a committed import removes only that statement's transactions and restores clean re-import eligibility.

## 2. Product vision and principles

Argus turns exported FNB CSV and Standard Bank PDF statements into a clear picture of financial behaviour. Categories classify what the money was for, such as groceries, home loan, levies, or school fees. Merchant tags record where it went, such as Woolworths, Checkers, or Uber Eats. A user should be able to answer four questions without opening a spreadsheet:

1. What came in and what went out during this period?
2. Where did the money go, by category?
3. How does this period compare with the one before it?
4. Which transactions explain a total I do not understand?

The product tagline is **"See where your money goes."** It is the primary external line; the longer value proposition explains the behaviour behind it.

The product should feel like a modern insights tool. It should not feel like accounting software, a ledger, or a budgeting system that scolds the user.

### Product principles

| Principle | What it means in Argus | What it rules out |
| --- | --- | --- |
| **Clear** | Plain-language totals and categories; every metric has a short definition. | Accounting jargon, unexplained adjustments, ambiguous percentages. |
| **Insightful** | Every chart answers a question a user actually has. | Decorative charts, chart-only pages, vanity metrics. |
| **Efficient** | Import and categorisation are fast; bulk actions handle the common case. | One-transaction-at-a-time drudgery, repeated manual entry. |
| **Trustworthy** | Totals reconcile to transactions; uncertainty is stated; no silent guesses. | Auto-correcting data, hiding uncategorised amounts, invented conclusions. |
| **Progressive** | Start with a period overview, then move into categories, then transactions. | Dense dashboards that show everything at once. |
| **Consistent** | Reuse the existing design tokens, form patterns, and accessibility conventions. | A second visual language or a parallel navigation model. |
| **Responsive** | Core review, categorisation, and overview tasks work on a phone and a desktop. | Desktop-only tables or charts that cannot be read on a narrow screen. |

### Non-goals

Argus is not a budgeting app, an accounting system, a tax tool, a financial adviser, an investment tracker, a bill-payment service, a credit-score product, or a bank aggregator. Direct bank connections, AI chat, and financial recommendations are outside the initial product. Adding any of them requires explicit product evidence that the insight workflow cannot succeed without it.

## 3. Target users and problems

### Primary user hypothesis

An individual with one to three bank or credit-card accounts who wants to understand their spending without maintaining a spreadsheet or a rigid budget. They are willing to export a statement and review transactions, but they do not want to become an amateur accountant. They care about privacy and want to see the transactions behind every total.

### Secondary user hypothesis

Someone who has tried a budgeting app and abandoned it because of manual entry, envelope discipline, or a poor categorisation workflow. They want observation and explanation, not enforcement.

### Jobs to be done

| Job | When it matters | Desired outcome |
| --- | --- | --- |
| Understand a statement | After a statement arrives | "I can see where the money went without retyping it." |
| Find the change | When spending feels higher than usual | "I can see which categories changed and by how much." |
| Classify the purpose | After an import | "I can mark transactions as groceries, home loan, levies, or school fees." |
| See where I shop | When reviewing a statement | "I can see how much went to Woolworths, Checkers, or Uber Eats." |
| Categorise and tag efficiently | After an import | "I can assign categories and store tags to the bulk of my transactions in a few actions." |
| Verify a total | When a number looks wrong | "I can drill into the exact transactions behind it." |
| Return monthly | At the next statement | "My previous categories and accounts are still there, and importing again is quick." |

### Problems Argus solves

- Raw statements are long, repetitive, and opaque.
- Manual spreadsheet upkeep is slow and error-prone.
- Budgeting apps demand ongoing discipline and often force a budgeting model the user did not ask for.
- Bank apps show individual transactions but offer weak cross-period, cross-category, and cross-merchant insight.
- Store names appear in raw descriptions but are inconsistent, so users cannot compare Woolworths, Checkers, or Uber Eats across statements.
- Categories and merchants answer different questions, and most tools make users choose one or the other.
- Many finance tools present conclusions without showing the transactions or definitions behind them.
- Bank aggregation raises privacy and connectivity concerns that a manual import avoids.

### Verified evidence versus hypotheses

| Claim | Status | Basis or validation needed |
| --- | --- | --- |
| The application has implemented customer accounts, sessions, verification, reset, and logout. | **Verified** | Backend and frontend source; see Section 4. |
| The application has no statement import, transaction model, categories, dashboard, or finance settings. | **Verified** | No finance routes, tables, UI, parser dependency, or chart dependency. |
| The application has no user research, analytics, or product requirements. | **Verified** | No product docs, analytics code, or research files. |
| FNB provides credit-card and cheque-account exports as a ZIP containing one CSV. | **Verified** | Two provided samples: one credit-card export and one cheque-account export. |
| Standard Bank provides text-based PDF statements with a common six-column layout. | **Verified** | Two provided samples, 4 and 18 pages, have the same `Date`, `Description`, `Payments`, `Deposits`, `Bank fees`, and `Balance` header. |
| PDF transaction rows are structurally extractable. | **Verified at source inspection** | Local text extraction and row/column analysis; no PDF import is implemented. |
| FNB CSV and Standard Bank PDF are viable MVP inputs for at least one target user. | **Partially verified** | Two banks and several account types are proven; other banks, users, and formats remain unvalidated. |
| Scanned or image-only PDFs can be handled. | **Unknown** | No scanned sample has been tested; OCR is outside the MVP. |
| Users will export statements as CSV or PDF rather than demand direct bank connections. | **Hypothesis, partially supported** | The samples prove CSV and PDF are available from two banks; target-user preference is still untested. |
| Standard Bank PDF import is in the MVP scope. | **Confirmed by operator** | Added to the product scope after inspecting the samples. |
| Transactions need a category (for example groceries, home loan, levies, school fees) and one or more optional merchant tags (for example Woolworths, Checkers, Uber Eats). | **Confirmed by operator** | Stated product requirement. |
| Imported date, description, and amount are immutable in review and after import; only category, merchant tags, and hidden state change after import. | **Confirmed by operator** | Stated product requirement. |
| Individual transactions are never deleted (hide and restore only); committed statement imports can be hidden, restored, or permanently deleted. | **Confirmed by operator** | Stated product requirement. |
| Balances are ignored; the transaction amount is the critical field. | **Confirmed by operator** | Stated product requirement. |
| A bar-first Chart view is in MVP scope. | **Confirmed by operator** | Stated product requirement. |
| Users want merchant-level insight, not only category totals. | **Hypothesis** | Validate with interviews and prototype tests. |
| Users want insight more than budgeting. | **Hypothesis** | Given in the product vision; not yet tested with users. |
| Users will categorise and tag transactions if bulk tools are efficient. | **Hypothesis** | Prototype test the review, bulk-categorisation, and bulk-tagging flow. |
| Usage is monthly, around statement arrival. | **Hypothesis** | Validate with interviews and early product analytics. |
| The initial workspace currency is ZAR. | **Confirmed by operator** | ZAR-only MVP; multi-currency is future. |
| Privacy is a meaningful reason users choose manual import. | **Hypothesis** | Validate in interviews; do not use as marketing copy until tested. |
| Users will pay for the product. | **Unknown** | The existing billing seam is a mock; there is no pricing or payment UI. |

## 4. Current state and implementation evidence

### 4.1 Existing capabilities

| Capability | Current state | Evidence |
| --- | --- | --- |
| Customer accounts | Implemented: register, verify email, login, logout, password reset. Login requires a verified email and an activated subscription flag. | `backend/Http/AccountEndpoints.cs`; `frontend/src/app/register`, `login`, `verify`, `reset`, `forgot-password`; `frontend/src/lib/server/account-actions.ts` |
| Sessions | Implemented: opaque tokens, 12-hour absolute expiry, 30-minute idle window, revocation on logout and password reset. | `backend/Data/Store.cs` (`SessionAbsoluteTtl`, `SessionIdleWindow`, `ValidateAndTouchSessionAsync`) |
| Trusted frontend boundary | Implemented: application secret plus origin check on backend calls; the browser never holds the backend origin or application secret. | `backend/Http/HttpSupport.cs` (`Trust`); `frontend/src/lib/server/backend-client.ts`; `frontend/src/auth.ts` |
| Customer data isolation | Foundation only: account queries are keyed by the authenticated customer. There is no finance data yet to isolate. | `backend/Data/Store.cs`; `backend/Data/Db.cs` |
| Admin surface | Implemented: an admin-only page renders account state; admin capability comes from the backend, not the page. | `frontend/src/app/admin/page.tsx`; `backend/Http/AccountEndpoints.cs` (`/v1/me`) |
| Billing | Mock only: HMAC-signed webhook changes subscription entitlement. No payment integration and no pricing UI. | `backend/Billing/Billing.cs`; `backend/Data/Store.cs` (`ApplyBillingEventAsync`) |
| Preferences | Theme only: dark by default, light persisted in browser storage. No currency, date, account, or notification preferences. | `frontend/src/app/layout.tsx`; `frontend/src/components/theme-toggle.tsx` |
| Design system | Implemented: Tailwind 4 tokens, dark/light themes, accessible form labels, focus styles, responsive page shells. | `frontend/src/app/globals.css`; `frontend/src/app/login/login-form.tsx`; `frontend/src/components/account-details.tsx` |
| Tests and CI | Present: backend isolation tests against PostgreSQL, frontend unit/component tests, Playwright smoke tests, formatting, build, audit, and dependency checks. | `backend/tests/argus.Server.Tests`; `frontend/tests`; `.github/workflows/ci.yml` |
| Documentation | Operational only: bootstrap, deployment, secrets, repository layout, divergences. This brief is the first product artifact. | `docs/*.md`; `docs/product-brief.md` |
| Product identity | Partly applied: `Argus` is the canonical name in `backend/Brand.cs` and `frontend/src/lib/brand.ts`, but the landing page and metadata still describe a SaaS starter. | `frontend/src/app/page.tsx`; `frontend/src/lib/landing-content.ts`; `frontend/src/app/layout.tsx` |

**Boundary evidence.** `backend/Http/AccountEndpoints.cs` maps only `/health`, `/v1/me`, `/v1/login`, `/v1/logout`, `/v1/register`, `/v1/verify`, `/v1/verification/resend`, `/v1/password/reset/request`, `/v1/password/reset`, and `/v1/billing/webhook`. The migrations in `backend/Data/Db.cs` create the migration bookkeeping table plus only customers, sessions, email-verification tokens, and password-reset tokens. The frontend dependencies contain no CSV, spreadsheet, PDF, archive, or chart library. There is no finance boundary to assess.

### 4.2 Current screen inventory

| Route | Screen | Access | What it does | Product note |
| --- | --- | --- | --- | --- |
| `/` | Landing | Public | Starter-kit marketing page with a call to action to the sample page. | Copy and primary action contradict the finance product. |
| `/login` | Sign in | Public | Email and password form; successful sign-in redirects to `/home`. | Reusable. |
| `/register` | Create account | Public | Email and password form; sends a verification email. | Reusable. |
| `/verify` | Verify email | Public | Consumes a token and activates the account. | Reusable. |
| `/forgot-password` | Request reset | Public | Non-enumerating reset request. | Reusable. |
| `/reset` | Reset password | Public | Consumes a token and sets a new password. | Reusable. |
| `/home` | Customer home | Authenticated | Renders account state, admin link when applicable, and logout. | No finance content. |
| `/admin` | Admin | Authenticated admin | Renders account state only. | No finance content. |
| `/sample-page` | Sample content | Public | Illustrative layout example, explicitly labelled as not part of the product. | Scaffold. |

There is no screen for overview, transactions, imports, categories, financial accounts, or settings.

### 4.3 Finance capability assessment

| Capability | Importance | Current state | Desired MVP behaviour |
| --- | --- | --- | --- |
| Bank statement imports | Critical | **Absent as product code.** No upload route, parser, statement record, or import UI. Two FNB CSVs and two Standard Bank PDFs exist outside the app and define the first formats. | Import FNB CSV (including a single CSV inside its ZIP), text-based Standard Bank PDF, and generic mapped CSV into a chosen financial account; confirm ZAR and statement start/end dates, review read-only parsed rows, skip invalid rows, resolve duplicates, and commit atomically. |
| Transaction management | Critical | **Absent.** There is no transaction table, list, filter, search, or detail view. | A searchable, filterable, paginated transaction list with a detail view. Imported date, description, and amount are immutable; category, merchant tags, and hidden state can change. |
| Categorisation | Critical | **Absent.** There is no category model, no default set, no assignment workflow, and no bulk action. | Assign, change, and review categories such as groceries, home loan, levies, and school fees; bulk-assign selected rows or all rows matching a filter; keep corrections efficient. |
| Merchant tagging | High | **Absent.** There is no store/merchant tag model, no tag filter, and no merchant-level view. | Create and reuse merchant tags such as Woolworths, Checkers, and Uber Eats; assign one or more, clear one or all, individually or in bulk; filter and compare by tag. |
| Financial overview | Critical | **Absent.** No cards, summaries, charts, trends, or period comparison. | A period overview and bar-first Chart view showing money in, money out, net, category breakdowns, merchant tags, comparison, and data coverage. |
| Financial exploration | High | **Absent.** No drill-down from a summary to transactions, no category detail, no merchant tag detail, no trend view. | Move from any card, chart segment, category, or merchant tag into the filtered transactions that explain it; compare periods, categories, and merchants. |
| Data quality and trust | Critical | **Foundation only.** The existing account system is careful about security and explicit errors, but there are no financial metric definitions or reconciliation rules. | Exact amounts, immutable imported records, explicit metric definitions, duplicate prevention, idempotent imports, coverage warnings, and totals that reconcile to transactions. |
| User settings and preferences | High | **Minimal.** Only theme. No financial accounts, categories, or merchant tags. | Manage financial accounts, categories, merchant tags, and profile/account details; committed statement imports can be hidden, restored, or deleted from Imports. |
| Authentication and isolation | Existing foundation | **Implemented for accounts.** The finance domain must extend the same authority model; it must not create a parallel one. | Every finance read and write is scoped to the authenticated customer; foreign identifiers do not reveal existence; admin does not see finance data. |
| Responsive and accessible experience | High | **Foundation only.** Existing pages are responsive and forms are labelled; no data-heavy table or chart has been designed. | Core import, review, categorisation, tagging, and overview flows work at narrow and wide widths with keyboard and screen-reader access. |

### 4.4 Journey assessment: gaps, friction, and opportunities

| Journey stage | Current experience | Gap or friction | Opportunity |
| --- | --- | --- | --- |
| Import | There is no import screen. The authenticated home is an account-status card. | The core value cannot begin. | Make import the primary action from the first-run empty state and from persistent navigation. |
| Review | No parsed-row review, validation, duplicate handling, or import history. | Users cannot see what will be imported before it is committed. | A review step with statement start/end date confirmation, counts, row-level validation errors, skip controls for invalid rows, duplicate warnings, and an atomic commit. |
| Categorise and tag | There are no transactions, categories, or merchant tags. | No way to turn raw rows into meaningful insight. | Default categories, inline category and merchant-tag assignment, multi-select, "select all matching," and visible uncategorised and untagged queues. |
| Understand | There is no overview, chart, or summary. | No answer to "where did my money go?" | Period cards, directional category breakdowns, top merchant tags, a trend, and a comparison with honest coverage notes. |
| Discover | There is no category, merchant tag, account, or period exploration. | Users cannot move from a total to the transactions behind it. | Drill-down from every visual, plus category and merchant-tag detail views with trend and transactions. |
| Return | Login and session handling exist, but there is no finance data to return to and no app navigation. | The shell feels like an account page, not a product. | Persistent app navigation, a default period based on the latest data, and preserved filters. |
| Trust | The account system is security-conscious, but there are no financial definitions or reconciliation guarantees. | Users cannot tell what a total includes or how category and merchant totals relate. | A visible metric dictionary, category and tag reconciliation, coverage warnings, exact arithmetic, and consistent empty/error states. |

### 4.5 Identity, navigation, and terminology gaps

- **Landing copy is not Argus.** `frontend/src/app/page.tsx` and `frontend/src/lib/landing-content.ts` describe "a complete starting point for your next SaaS product," customer accounts, billing, and a sample page. The primary call to action opens `/sample-page`, not the product. The agreed tagline is "See where your money goes"; the current copy does not use it.
- **Metadata is inconsistent.** `frontend/src/app/layout.tsx` describes "researched SaaS opportunities for people who want to build but do not know what to build." That is not the finance product.
- **There is no authenticated app shell.** `/home` is a single account card. There is no persistent navigation to transactions, imports, or settings, and no obvious path back after leaving the page.
- **"Account" is overloaded.** The existing product uses "account" for the customer login identity. Argus also needs "accounts" for bank and credit-card accounts. The UI must distinguish **Profile** (customer account) from **Accounts** (financial accounts).
- **The sample page is scaffolding.** `/sample-page` is explicitly labelled illustrative and is not part of the product. Keeping it as the primary destination would confuse users and weaken trust.
- **Tests encode the scaffold copy.** The landing unit test and Playwright smoke test assert the current starter-kit strings and the sample-page call to action. Changing the landing page is an intentional product change and requires updating those tests; it is not a regression.
- **Existing account details are duplicated by design.** `/home` and `/admin` both render `AccountDetails`. That reuse is good and should be preserved when account details move to a profile/settings area.

### 4.6 Defects versus enhancements

The product-coherence issues in Section 4.5 are defects or inconsistencies, not finance feature gaps:

- the landing page and metadata describe a different product;
- there is no authenticated navigation;
- "account" is ambiguous between the login identity and a bank account;
- the sample page is presented as the primary outcome;
- the tests encode the scaffold copy.

Everything in Section 4.3 is a new capability, not a defect. The absence of finance features is not a regression; the mismatched identity, navigation, and terminology are corrections.

### 4.7 Reusable foundation

The architecture should extend, not replace:

- the existing customer account, session, verification, reset, and logout contracts;
- the trusted server-side frontend client and application-secret boundary;
- the backend's authority over data and admin capability;
- the PostgreSQL migration and isolation-test approach;
- the Tailwind tokens, form patterns, error messaging, focus states, and dark/light themes;
- the restrained card-based visual language: generous spacing, rounded surfaces, a small accent palette, and a strong type hierarchy;
- the Vitest/MSW and Playwright harness for finance flows;
- the existing admin and billing behaviour, unless a product decision explicitly changes them.

### 4.8 Evidence limitations

- The working tree has no commits; every file is untracked. There is no history to distinguish scaffold from product work.
- Two FNB samples were supplied after the first draft: a credit-card export and a cheque-account export, each a ZIP containing one CSV. They establish one real format; they do not validate other banks, other account types, or user demand.
- The samples are real financial data. This brief records only structural characteristics; it does not repeat names, account numbers, balances, or transaction descriptions.
- Two Standard Bank PDF statements (4 and 18 pages) were inspected locally after the first draft. Both are digital, text-based PDFs, not scans, and both use the same six-column transaction layout. PDF import is not implemented.
- No format specification, user interviews, support tickets, analytics, or product requirements exist in the tree.
- The application was not run for this brief. Claims about current behaviour come from source inspection.
- The finance absence was verified across routes, migrations, dependencies, UI files, and documentation, not inferred from a single search.
- No TODO/FIXME markers, feature flags, or partial finance code were found.
- The only "transactions" in the backend are database transactions. They are not financial transactions.

### 4.9 Provided FNB statement samples

Two real FNB exports were provided outside the repository:

- a Private Wealth Credit Card export;
- a Platinum Cheque Account export.

Each download is a ZIP containing exactly one CSV. The samples establish the first concrete import contract. Only structural characteristics are recorded here; no personal names, account numbers, balances, or transaction descriptions are repeated in this brief.

| Characteristic | Credit-card sample | Cheque-account sample |
| --- | --- | --- |
| Container | ZIP with one CSV | ZIP with one CSV |
| Encoding | UTF-8 with BOM | UTF-8 with BOM |
| Line endings | LF | LF |
| Preamble | Six lines before the header: title, blank, Name, Account, Balance, blank | Same |
| Header | `Date, Amount, Balance, Description`; labels carry leading spaces | Same |
| Date format | `YYYY/MM/DD` | `YYYY/MM/DD` |
| Amount format | Signed dot-decimal with a leading space; no thousands separators in the sample | Same |
| Amount direction | Negative purchases; positive credits | Negative outflows; positive inflows |
| Balance column | `0` for every row | Running balance |
| Description | Padded with trailing spaces; fixed 41 characters in the sample; no commas | Variable length; no trailing spaces; no commas |
| Data rows | 150 | 199 |
| Currency | Not present | Not present |
| Account identity | Preamble contains a cardholder/account name and a masked account number | Preamble contains an account name and a full account number |
| Account type | In a bracketed preamble label | In a bracketed preamble label |

**Product implications from the samples:**

1. Accept the bank's ZIP directly when it contains exactly one CSV; also accept a bare CSV. Reject multiple files, nested archives, encrypted archives, and unsafe paths.
2. Detect the FNB preamble and header instead of assuming the first row is data. Accept the UTF-8 BOM without showing it in the first header label. Preamble lines are metadata, never transactions.
3. Trim header labels and amount fields. Ignore the Balance column entirely. Trim trailing spaces from descriptions for display and duplicate matching; preserve the meaningful description text.
4. Ignore the `Balance` column entirely: do not import, display, or use it. The credit-card sample's balance is zero for every row, and the cheque sample's running balance is not a trusted transaction value. The transaction amount is the sole monetary source.
5. The file has no currency column and no statement start/end date range. The MVP currency is ZAR; show it prominently during import and require the user to confirm the statement is in ZAR and to confirm or enter the statement `start_date` and `end_date` (pre-filled from the earliest and latest valid transaction dates). Do not infer a currency from the bank name or account label.
6. Do not store, display, or log the preamble's name/account number or the raw ZIP/CSV filename. Use the user's account label and a generated statement label.
7. Treat a row whose field count differs from the header as invalid. The CSV is unquoted in the sample; a comma in a future description would create an extra field, so flag it rather than shifting columns.
8. The FNB layout is an automatic P0 preset alongside a generic CSV mapping and format-disambiguation path for other CSV exports. Do not claim other banks' fixed presets are pre-validated.

### 4.10 Provided Standard Bank PDF samples

Two Standard Bank PDF statements were provided: one 4-page statement and one 18-page statement. Both are PDF 2.0 files with a text layer, not scans. Only structural characteristics are recorded here; no names, account numbers, balances, or transaction descriptions are repeated.

| Characteristic | 4-page sample | 18-page sample |
| --- | --- | --- |
| Format | PDF 2.0 with selectable text | PDF 2.0 with selectable text |
| Pages | 4 | 18 |
| Transaction rows | 49 | 297 |
| Description continuation lines | 66 | 356 |
| Table header | `Date`, `Description`, `Payments`, `Deposits`, `Bank fees`, `Balance` | Same |
| Transaction date | `DD Mon YY` in the left column | Same |
| Flow columns | One value in Payments or Deposits | Same |
| Payments | Negative amounts; outflow | Same |
| Deposits | Positive amounts; inflow | Same |
| Bank fees | No populated values in the sample | No populated values in the sample |
| Balance | Separate right-hand column | Same |
| Amount format | Comma thousands, dot decimals | Same |
| Currency label | No explicit ZAR label found in the extracted text | Same |
| Account metadata | Present in the statement preamble; must not be stored or logged | Same |
| OCR | Not required | Not required |

**What this establishes:** the two samples use the same six-column layout, a text layer is present, and every transaction row has one flow amount plus a balance column that the MVP ignores. A single Standard Bank PDF parser can handle both samples in principle. The difference is volume: the 18-page sample is roughly six times the size of the 4-page sample.

**Product implications from the PDF samples:**

1. Standard Bank PDF import is in the MVP scope. The MVP supports text-based Standard Bank PDFs only; scanned or image-only PDFs require OCR and are a future capability.
2. Detect the Standard Bank six-column layout from the header and column positions. If the layout is not recognized, stop with a clear unsupported-format message rather than guessing.
3. Reconstruct rows by position, not by reading order. Join description continuation lines to the preceding transaction.
4. Map the populated Payments column to outflow, the populated Deposits column to inflow, and Bank fees to outflow. Ignore the Balance column entirely: do not import, display, or use it. The transaction amount is the sole monetary source.
5. Transaction dates use two-digit years. Resolve them against the statement period printed in the PDF; if the period is missing or ambiguous, require the user to confirm the year.
6. Skip page headers, page footers, statement metadata, account details, and summary/balance rows. They are not transactions.
7. Reject password-protected or image-only PDFs with a clear message. Do not silently run OCR or send the PDF to an external service in the MVP.
8. Show the page number and source context for each extracted row in the review. Do not retain the raw PDF or log its contents after the import or an abandoned review.

## 5. Core user journeys

### 5.1 First run

1. The user registers, verifies their email, and signs in through the existing flow.
2. The first authenticated session shows an empty overview explaining what Argus does and prompting the first import.
3. The user creates a financial account: a display name and an optional institution. The account uses the ZAR workspace currency.
4. The user starts an import from the empty state.

**Success:** the user understands the next action and reaches the import screen without guessing.

**Failure and recovery:** if the user has no financial account, the import screen offers to create one inline. If the session expires, the user signs in and returns to the import starting point.

### 5.2 Import and review

1. The user selects the financial account and uploads an FNB ZIP/CSV, a Standard Bank PDF, or another CSV statement.
2. Argus detects the format. For an FNB CSV, it extracts the single CSV, skips the preamble, and recognizes the header. For a Standard Bank PDF, it detects the six-column layout and reconstructs transaction rows. For an unfamiliar CSV, it asks the user to confirm the header row, delimiter, column mapping, date format, decimal separator, and sign convention.
3. The user confirms the statement currency is ZAR and confirms or enters the statement coverage `start_date` and `end_date` (pre-filled from the PDF header period or the CSV's earliest and latest valid transaction dates).
4. Argus previews the read-only parsed rows and validates every row.
5. The user marks any invalid rows to be skipped and reviews duplicate warnings. Source fields (`date`, `description`, `amount`) are never edited in review.
6. The user confirms the import and sees a summary: total reviewed, imported, already imported (suppressed), and skipped by the user.
7. Argus shows the imported statement in the import history and links to the new transactions.

**Success:** the import finishes with counts the user can understand, and every imported row is traceable to the statement.

**Failure and recovery:** no partial import. A ZIP with zero or multiple CSVs, an encrypted archive, an unsafe path, or an unreadable archive is rejected before parsing. A password-protected, image-only, or unrecognized-layout PDF is rejected with a clear message. A file that overlaps an earlier import is not a failure; it proceeds with already-imported rows suppressed and new rows available for review. If validation fails, the user marks invalid rows to be skipped or fixes the source file and re-uploads. If the backend is unavailable, the user sees a retry path and no half-saved statement.

### 5.3 Categorise and tag

1. The user opens the uncategorised or untagged queue, or the full transaction list.
2. They filter to a meaningful set, such as one account, one month, one description, or one merchant tag.
3. They assign a category to selected rows, or to all rows matching the filter. They can assign a merchant tag in the same action or separately.
4. Argus confirms how many rows changed and updates the overview.
5. Repeating the action for the remaining rows brings category and merchant-tag coverage close to 100%.

**Success:** the bulk of a statement is categorised and tagged in a few actions. A Woolworths transaction can be both category "Groceries" and merchant tag "Woolworths"; the two dimensions stay independent.

**Failure and recovery:** a failed save changes nothing and reports which rows were not updated. Archived categories and tags cannot be assigned. Assigning "Uncategorised" clears a category, and clearing a merchant tag removes only that tag; neither deletes the transaction.

### 5.4 Understand

1. The user lands on the overview for the latest period with data, or the current month to date.
2. They see money in, money out, net, and a prominent uncategorised indicator.
3. They see where money came from and where it went, plus a trend and a comparison with the previous comparable period. They can switch to the Chart view for bar-first views of categories, merchant tags, and period comparisons.
4. They select a different period or account and the cards, charts, and coverage notes update together.

**Success:** the user can describe their financial position for the period without leaving the overview.

**Failure and recovery:** no data in the period produces a clear empty state with a period change or import action, not a wall of zeros. Missing prior-period data produces "No comparison available," not a misleading percentage.

### 5.5 Discover

1. The user clicks a category, a merchant tag, a chart bar, or a summary card.
2. Argus opens the transaction list or category detail with the same filters applied.
3. The user sees the trend for that category, the transactions that make it up, and the change from the previous period.
4. They go back without losing the overview period or filters.

**Success:** every total is explainable by the transactions behind it.

**Failure and recovery:** if a filter returns no rows, the screen says so and offers to clear the filter. If a category is archived, its history remains viewable.

### 5.6 Categorise, hide, and restore

1. The user opens a transaction. The imported date, description, and amount are read-only; the imported record is the truth.
2. The user can change the category and merchant tags, or hide the transaction.
3. Argus saves the change and recalculates the affected totals.
4. The user can hide a transaction or hide/delete an entire committed import after confirmation. Hidden transactions disappear from default lists, charts, and calculations, but they are not deleted. Deleting a committed import removes that statement and its transactions so it can be re-imported cleanly.
5. The user can open the Hidden view and restore a transaction or a hidden import (preserving individual transaction hide flags); the totals recalculate.

**Success:** category, tag, and hidden-state changes are possible without altering the imported record. Hiding is reversible, individual transactions are never deleted, and committed imports can be hidden, restored, or deleted cleanly.

**Failure and recovery:** hide, restore, and statement delete are explicit. A stale category, tag, or hidden-state change in another session does not silently overwrite a newer change. A hidden transaction still counts for duplicate prevention, so re-importing it does not create a second copy unless its parent statement import was deleted. There is no edit of an imported date, description, or amount.

## 6. Desired experience and functional requirements

### 6.1 Information architecture

The authenticated product should have a small, persistent navigation:

- **Overview** — the period summary and chart view, and the entry point after sign-in. Summary and Charts are tabs within Overview.
- **Transactions** — the searchable, filterable list and the bulk-categorisation workspace.
- **Imports** — committed import history (including statement hide, restore, and delete) and the start of a new import.
- **Settings** — profile, financial accounts, categories, and merchant tags.

The existing `/home` route is the natural default overview because login already redirects there and the proxy protects it. Account details should move to a profile/settings area rather than disappear. The final route names are an architecture decision; the screen set and the persistent navigation are product requirements.

On small screens, the same four destinations must remain reachable. Tables should become readable cards or a horizontally scrollable table with a clear primary column. Filters should be usable without a mouse.

### 6.2 Import

- **IMP-1:** The user can create or select a financial account before importing. A financial account has a display name and an optional institution, and uses the ZAR workspace currency.
- **IMP-2:** The MVP supports the automatic FNB CSV layout (bare CSV or a ZIP containing exactly one CSV), text-based Standard Bank six-column PDF statements, and generic CSV mapping with format disambiguation in P0. Other PDF layouts, scanned PDFs, and unrecognized non-CSV formats are rejected with a clear message.
- **IMP-3:** For an FNB export, Argus skips the six-line preamble, recognizes the `Date, Amount, Balance, Description` header, and confirms the mapping with a preview; the Balance column is ignored. For a Standard Bank PDF, Argus detects the `Date | Description | Payments | Deposits | Bank fees | Balance` layout and previews the reconstructed rows; the Balance column is ignored. For a generic CSV, the user confirms the header row, delimiter, date format, decimal separator, sign convention, and column mapping.
- **IMP-4:** Ambiguous dates, decimal separators, and sign conventions require explicit confirmation in P0. Argus never silently chooses an interpretation.
- **IMP-5:** Every data row is validated and is strictly read-only. Invalid rows show the row number and a plain-language reason, and each row shows the raw source values alongside the interpreted date, description, and amount. PDF-derived rows also show the page number and source context. Source fields (`date`, `description`, `amount`) cannot be edited in review; the user must mark invalid rows to be skipped before committing (or fix the source file and re-upload).
- **IMP-6:** The review step shows total data rows, rows ready to import, rows suppressed as already imported, rows marked to be skipped by the user, and possible duplicates included. At commit, total equals imported plus suppressed-as-already-imported plus skipped-by-user; no invalid row is left unaccounted for.
- **IMP-7:** The commit is atomic. Either the confirmed statement and all its included transactions are saved, or nothing is saved. Retrying a failed commit must not duplicate data.
- **IMP-8:** Import history shows the financial account, confirmed statement coverage period (`start_date` to `end_date`), import date, and imported/suppressed-as-already-imported/skipped/hidden counts for each committed import. A committed import can be opened to review its counts, hidden, restored, or permanently deleted. An abandoned or failed review is not listed as an import; the user retries from the review.
- **IMP-9:** A user can hide or restore a committed import, or permanently delete a committed import, after a confirmation that states how many transactions will be affected. Hiding an import hides its transactions and keeps the import in history with a Hidden state; restoring a hidden import restores its transactions except those that were hidden individually before the import was hidden. Deleting a committed import permanently removes that statement, its transactions, and its duplicate-protection identity records so the statement can be re-imported cleanly. Individual transactions are never deleted on their own.
- **IMP-10:** File size, page count, and row count are bounded, including the uncompressed content of a ZIP and the page count of a PDF. Exceeding a limit produces a clear error before processing. Processing shows progress or a busy state and does not leave the user without feedback.
- **IMP-11:** Re-importing the same statement is blocked or prominently flagged with a link to the existing import. An overlapping statement is allowed; transaction-level idempotency suppresses already-imported transactions and presents only new occurrences. An override requires explicit confirmation.
- **IMP-12:** The MVP supports ZAR only and requires an explicit statement coverage period (`start_date` and `end_date`) on every import. Show ZAR prominently and require the user to confirm the statement is in ZAR and to confirm or enter the statement's `start_date` and `end_date` (pre-filled from the Standard Bank PDF header period when present, or from the earliest and latest valid transaction dates in a CSV). A non-ZAR statement is unsupported and blocks the import with an explanation.
- **IMP-13:** The import flow clearly shows the current step (mapping, review, or committing), the selected file, and the target financial account, so the user always knows what will happen next.
- **IMP-14:** A ZIP is accepted only when it contains exactly one CSV. Argus rejects multiple files, nested archives, encrypted archives, unsafe paths, and archives whose decompressed content exceeds the limit.
- **IMP-15:** Argus does not store, display, or log the FNB preamble's name/account number, the Standard Bank PDF's account metadata, or the raw ZIP/CSV/PDF filename. The user sees the account label and a generated statement label.
- **IMP-16:** A Standard Bank PDF must have a selectable text layer. Image-only or scanned PDFs are rejected with a clear message; OCR is a future capability.
- **IMP-17:** Password-protected or encrypted PDFs are rejected with a clear message.
- **IMP-18:** The Standard Bank PDF layout is recognized by its six-column header and column positions. An unrecognized PDF layout is rejected; there is no generic PDF mapping in the MVP.
- **IMP-19:** PDF descriptions that wrap are joined to the preceding transaction. Page headers, page footers, statement metadata, and summary or balance-only rows are skipped.
- **IMP-20:** PDF transaction dates use two-digit years. Resolve them against the confirmed statement period (`start_date` to `end_date`, pre-filled from the period printed in the PDF when present); if the period is missing or ambiguous, require the user to confirm the statement start/end dates and year.
- **IMP-21:** PDF review shows the page number and source context for each extracted row. The raw PDF is not retained after the import or an abandoned review.
- **IMP-22:** The import review may suggest merchant tags from the transaction description using the customer's active tag names, explicit per-customer aliases, and the customer's own historical tag choices keyed by a generic merchant key. Exact tokens rank before truncated-prefix matches. A suggestion is never applied automatically; the user accepts, changes, or ignores it. Import review does not suggest or assign a category; category suggestions are detail-only and advisory.
- **IMP-23:** A user can hide or restore individual transactions and bulk-hide selected or all matching transactions. Hidden transactions are excluded from calculations and default lists but retained for duplicate prevention and idempotency unless their parent statement import is deleted.

### 6.3 Duplicate prevention and transaction idempotency

- **DUP-1:** Argus identifies exact duplicates across imports using the same financial account, transaction date, signed amount, and normalised description.
- **DUP-2:** Argus identifies statement-level repeats using the same account and a stable statement fingerprint, such as the same file content or the same period, row count, and total.
- **DUP-3:** Duplicates within one file are flagged. Identical rows can be legitimate, so the user decides whether to include them; Argus does not silently drop them.
- **DUP-4:** Exact matches against already-imported transactions default to suppressed-as-already-imported; the user can include a legitimate repeat with an explicit override. Exact duplicates within the incoming file are flagged and the user decides. Near duplicates are flagged and default to included, because a false skip loses real money data.
- **DUP-5:** Existing transactions are never deleted or merged automatically. Duplicate handling only affects what the current import will add.
- **DUP-6:** Imported date, description, and amount are immutable, so a post-import edit cannot create a duplicate. Category and tag changes do not affect duplicate identity.
- **DUP-7:** A retried or repeated commit is idempotent. The user never gets two copies of the same confirmed import.
- **DUP-8:** FNB descriptions carry trailing spaces. Trim them for display and collapse whitespace for duplicate matching, but preserve the meaningful description text.
- **DUP-9:** Standard Bank PDF descriptions are reconstructed from wrapped lines. Collapse whitespace for matching and display, but preserve the meaningful description text.
- **DUP-10:** Transaction creation is idempotent. Re-importing an overlapping statement, or retrying a commit, must never create a second copy of a transaction already recorded for the same financial account.
- **DUP-11:** The fallback identity key is financial account, transaction date, signed amount, and normalised description. When the source provides a stable transaction identifier, that identifier is included in the key.
- **DUP-12:** When an identity key legitimately occurs more than once, compare the existing occurrence count with the incoming count. Suppress only the occurrences already present and present the excess as new; do not skip a genuine additional occurrence.
- **DUP-13:** The import review and history show how many rows were suppressed as already imported, separately from rows skipped by the user and possible duplicates.
- **DUP-14:** Idempotency is account-scoped. The same transaction arriving from a CSV, a ZIP-contained CSV, or a PDF for the same account is a duplicate; a transfer between two different accounts is not.
- **DUP-15:** If a re-export changes the description enough that the identity key no longer matches, the row is a possible duplicate, not an exact one. The user decides; Argus does not silently skip it.

### 6.4 Transaction management

- **TXN-1:** The transaction list shows date, description, financial account, category, merchant tags, direction, amount, and import source. The amount is displayed in ZAR with an unambiguous sign.
- **TXN-2:** The user can search descriptions and merchant tags, and filter by date range, financial account, category (including Uncategorised), merchant tag (including Untagged), category kind, direction, and visible/hidden state. Sorting and pagination are available. The default excludes hidden transactions; a Hidden view shows them. The MVP filters by one merchant tag at a time; combined tag filters are future.
- **TXN-3:** The list shows the number of matching visible transactions, the hidden count, and the total for the current filter. Changing a filter updates all three.
- **TXN-4:** A transaction detail view shows the imported date, description, amount, account, and source as read-only. The user can change the category and merchant tags, and hide or restore the transaction. Suggested tags are labelled and require confirmation. There is no in-place edit of source fields.
- **TXN-5:** The user can select individual rows, select all rows on the current page, and select all rows matching the current filter. The interface states the count and makes clearing the selection easy.
- **TXN-6:** Bulk assignment changes the category and adds or removes merchant tags for every selected transaction or every matching transaction, confirms the number changed, and updates the overview. Adding or removing tags does not replace unrelated tags.
- **TXN-7:** The UI distinguishes income, expense, transfer, excluded, and uncategorised transactions. Category and merchant tags are independent: a Woolworths transaction can be Groceries or Clothing, and both can carry the Woolworths tag.
- **TXN-8:** No transaction is silently hidden. Transfers, excluded transactions, uncategorised transactions, and untagged transactions remain visible and are excluded or flagged only according to the published metric definitions.
- **TXN-9:** Empty, loading, partial, and error states are explicit. A filter with no results offers a way to clear it.
- **TXN-10:** A transaction can be hidden or restored. Hidden transactions are not deleted, are excluded from default lists and calculations, and remain available in the Hidden view.
- **TXN-11:** A user can bulk-hide or bulk-restore selected transactions or all transactions matching a filter. The confirmation states the count.
- **TXN-12:** A hidden transaction still counts for duplicate prevention and idempotency. Re-importing it does not create a second copy.

### 6.5 Categories, merchant tags, and categorisation

- **CAT-1:** Argus seeds a default category set on first use and provides a system Uncategorised bucket. The set includes Groceries, Dining and takeaways, Transport, Housing and rent, Home loan, Levies, Utilities, Health, Education and school fees, Shopping, Entertainment, Subscriptions, Fees and charges, Travel, Other expenses, Salary, Other income, Transfers, and Excluded. Default categories can be renamed or archived; Uncategorised is a system concept and cannot be deleted.
- **CAT-2:** A user can create, rename, archive, and restore categories. Deleting a category is not the same as archiving it: deletion requires confirmation and moves its transactions to Uncategorised.
- **CAT-3:** Every category has a visible kind: Income, Expense, Transfer, or Excluded. The kind determines whether the category participates in the overview and how it is grouped. Changing a kind changes reported totals and requires a warning.
- **CAT-4:** Archived categories remain visible on historical transactions but cannot be chosen for new assignments.
- **CAT-5:** Category and merchant-tag assignment are available inline in the list and in the transaction detail view. Bulk assignment is available from the list.
- **CAT-6:** The MVP does not auto-categorise or auto-apply tags. Suggestions may use explicit customer aliases and the customer's own historical choices, but they remain advisory and require confirmation; there is no global merchant dictionary or cross-customer learning. The manual and bulk workflow must stand on its own.
- **CAT-7:** Category management shows the transaction count and kind for each category so the user can see the effect of archiving or deleting it.
- **TAG-1:** Merchant tags are workspace-wide user labels for stores or merchants such as Woolworths, Checkers, and Uber Eats. A user can create, rename, archive, restore, and delete them. Tag names are case-insensitive unique.
- **TAG-2:** A transaction can have zero, one, or many merchant tags. The category remains single-select; multiple categories require split transactions, which are future.
- **TAG-3:** Tags can be assigned or removed individually in the transaction detail. Suggested tags are labelled and require confirmation.
- **TAG-4:** Bulk actions can add or remove tags on selected transactions or all transactions matching a filter. Bulk edits do not replace unrelated tags.
- **TAG-5:** The transaction list can be filtered by one merchant tag in the MVP, with an Untagged option. Combined tag filters are future.
- **TAG-6:** A merchant-tag detail view shows its transactions, total, count, trend, comparison, and category mix. Because a transaction can have multiple tags, tag totals overlap and are not additive.
- **TAG-7:** Tag management shows the assigned transaction count and allows archive, restore, and delete. Deleting a tag removes it from transactions; it never deletes transactions.
- **TAG-8:** Argus may suggest tags from the description using active tag names, explicit customer aliases, and the customer's own historical tag choices. Exact tokens rank before truncated-prefix matches. There is no external merchant directory or cross-customer learning, and no suggestion is applied automatically. Transaction detail may show an advisory suggested category from the same sources; it changes nothing until the user saves it.
- **TAG-9:** Untagged transactions remain visible in the transaction list and tag views, and the product reports tag coverage.
- **TAG-10:** Global merchant dictionaries, cross-customer learning, automatic rule application, tag merge, and combined tag filters are future capabilities.

Category and merchant tag are independent dimensions. A category describes the purpose of the money; a merchant tag describes where it went. A Woolworths purchase can be categorised as Groceries or Clothing, and both transactions can carry the Woolworths tag.

### 6.6 Financial overview

- **OVR-1:** The overview has a period selector with sensible presets and a custom range. The default is the most recent calendar month with data; if the current month has data, the default is the current month and is labelled "Month to date."
- **OVR-2:** The overview shows money in, money out, net, and a prominent needs-categorisation indicator. Money in and money out are gross directional totals excluding transfers and excluded transactions; uncategorised amounts are included but identified.
- **OVR-3:** The overview shows where money came from and where it went, grouped by category in each direction. Uncategorised is its own row. The two breakdowns reconcile to money in and money out.
- **OVR-4:** The overview shows money in versus money out over time; this is the product's income-versus-expenditure view. The granularity adapts to the selected range, for example weekly for a single month and monthly for a multi-month range. The granularity is labelled.
- **OVR-5:** The overview shows the largest categories for outflows and, separately, for inflows, with amount, share of the relevant directional total, transaction count, and change versus the comparison period.
- **OVR-6:** Period comparison follows the rules in Section 7.3. If the prior period has no data, the overview says so and does not display a percentage change.
- **OVR-7:** A data coverage note states the statement coverage for the selected period, evaluated against the union of committed, non-hidden statement `[start_date, end_date]` periods for the selected financial account(s), and warns when the selected period extends beyond covered dates, has a statement gap, or includes a partial month.
- **OVR-8:** Every card, chart segment, and category row is a path into the transaction list or category detail with the same period, account, category, and direction filters applied.
- **OVR-9:** A "How this is calculated" disclosure explains money in, money out, net, transfers, excluded, and uncategorised in plain language.
- **OVR-10:** The overview has a distinct empty state for no data at all and a distinct empty state for no data in the selected period.
- **OVR-11:** The overview shows the largest merchant tags for outflows and, separately, for inflows, with amount, transaction count, and change versus the comparison period. It states that a transaction can have multiple tags, so tag totals overlap.
- **OVR-12:** The overview shows merchant-tag coverage and the untagged value. Tag views are a secondary lens; category breakdowns remain the additive reconciliation to money in and money out.
- **OVR-13:** Hidden transactions are excluded from every card, chart, comparison, and coverage denominator. The overview reports the hidden count. If all transactions in the selected period are hidden, it shows the empty state with a restore action rather than zero spending.

**Chart choices.** Use summary cards for the headline figures; horizontal bars for directional category and merchant-tag breakdowns because labels and amounts remain readable; grouped bars or lines for money in versus money out over time; and a line or bar for category or tag detail. Avoid pie or donut charts with many categories, 3D effects, dual axes, and colour-only encoding. Every chart has a text or table alternative. Tag charts must disclose that a transaction can appear under more than one tag.

**Chart view.** The Chart view is a visual lens over the same data and filters, not a separate reporting system. It should be a tab within the Overview rather than a fifth top-level destination; the final route structure is an architecture decision.

- **CHT-1:** The Chart view opens with bar charts as the default visualization. It covers outflows by category, inflows by category, top merchant tags, and money in versus money out over time.
- **CHT-2:** Use horizontal bars for category and merchant-tag rankings because the labels stay readable. Use grouped vertical bars for money in versus money out over time. Use bar charts first for category and tag detail trends; line or area charts are optional secondary views, not the default.
- **CHT-3:** Every chart uses the same period, account, category, merchant-tag, and direction filters as the Overview and transaction list. Changing a filter updates all charts and the summary together.
- **CHT-4:** Every bar is a drill-down. Selecting a category, tag, or period bar opens the transaction list with the equivalent filters applied.
- **CHT-5:** Category and tag bars are sorted by amount where ranking applies. Show the top categories or tags plus an Other bucket; show Uncategorised and Untagged explicitly. Every bar shows its label and value.
- **CHT-6:** Category bars are additive. Merchant-tag bars overlap when a transaction has multiple tags, and the chart states that tag totals must not be summed. The Chart view never implies that tag bars reconcile to the headline totals.
- **CHT-7:** Charts have a keyboard-reachable text or table alternative, readable labels, and sufficient contrast in both themes. On narrow screens, bars stack vertically or scroll horizontally without hiding the amount.
- **CHT-8:** The Chart view has empty, partial-data, and no-comparison states. It does not draw an empty chart or imply that missing statement coverage is zero.

### 6.7 Financial exploration

- **EXP-1:** A category detail view shows the category's transactions, gross inflows and outflows, net for the category, transaction count, and change versus the comparison period.
- **EXP-2:** The user can filter the overview and transaction list by financial account. The "all accounts" view is valid because the MVP is ZAR-only.
- **EXP-3:** A period comparison view lists the categories with the largest absolute change between the selected period and its comparison. It appears only when both periods have data.
- **EXP-4:** Search is by the raw transaction description and by merchant tag. External or cross-customer automatic merchant normalisation is future; a customer-local derived merchant key may group suggestion history, and merchant-level insight uses the user's tags.
- **EXP-5:** Filters and charts share one state. A chart never shows a different period, account, category, or tag scope from the list it links to.
- **EXP-6:** A merchant-tag detail view shows its transactions, total, transaction count, trend, comparison, and category mix. It makes clear that tag totals overlap when a transaction has multiple tags.
- **EXP-7:** Top merchant tags link to the tag detail and to a transaction list filtered by that tag. The Untagged view is available from the same place.
- **EXP-8:** Tag totals are never summed with category totals. They answer a different question and are reconciled only against the transactions carrying that tag.

### 6.8 Settings, financial accounts, and data

- **SET-1:** The user can create, rename, archive, and restore financial accounts. All accounts use the ZAR workspace currency; there is no currency choice in the MVP.
- **SET-2:** The user can manage categories and merchant tags, including defaults, custom entries, kinds, and archived state.
- **SET-3:** The profile/settings area preserves the existing account details: email, verification status, subscription status, lifetime status, admin link where applicable, theme, and logout.
- **SET-4:** Individual transactions are never deleted; they are hidden and restored. Committed statement imports can be hidden, restored, or permanently deleted from Imports (removing that statement, its transactions, and its duplicate-protection identity records). There is no whole-customer account-deletion or data-export action in the MVP; account deletion and data export are future. The workspace remains ZAR.
- **SET-5:** The admin surface does not expose any user's financial data in the MVP. It may continue to show account state only.
- **SET-6:** The product states whether the original uploaded ZIP/CSV/PDF is retained. The MVP behaviour is to discard the raw file after a successful import or an abandoned review and keep only the parsed transactions and statement metadata.
- **SET-7:** The MVP does not add a pricing page, checkout, or plan management. The existing verified-subscriber gate remains the access model unless a separate product decision changes it.

### 6.9 Cross-cutting experience

- **UX-1:** Import, review, categorisation, and overview work at 320–375 px and at desktop widths. Data tables adapt without hiding the amount or category.
- **UX-2:** Core flows target WCAG 2.1 AA. Controls have accessible names, keyboard operation, visible focus, and sufficient contrast in both themes. Charts have a text or table alternative; colour is never the only encoding.
- **UX-3:** Financial labels use plain language and show the ZAR code where the symbol could be ambiguous. A short definition is available wherever a metric could be misread.
- **UX-4:** Every asynchronous action has a loading, success, empty, and failure state. Errors say what happened and what the user can do next.
- **UX-5:** New screens reuse the existing Tailwind tokens, card treatment, form controls, spacing, and focus conventions. The theme toggle remains available.
- **UX-6:** Destructive actions require confirmation, state the scope, and are not the default action.
- **UX-7:** The product should remain interactive for a typical personal dataset. Proposed validation targets are an overview response under 2 seconds at the 95th percentile and a transaction filter response under 1 second for 20,000 transactions; these are targets, not measured baselines.
- **UX-8:** Merchant-tag selection supports multiple tags, keyboard operation, and clearing one tag without clearing the others. Suggested tags are visibly labelled and are never pre-applied.

### 6.10 Security, privacy, and trust

- **SEC-1:** Every finance read and write is scoped to the authenticated customer. A foreign statement, transaction, category, or financial-account identifier must not reveal existence or content.
- **SEC-2:** Finance calls use the existing trusted frontend boundary and session authority. The browser must not receive the backend origin or application secret, and the frontend must not access the database directly.
- **SEC-3:** Uploads are validated for type, size, and content before parsing. Archive uploads reject multiple files, nested archives, encrypted archives, path traversal, and oversized decompression. PDF uploads reject password protection and image-only files in the MVP; no external OCR service is used. Parsed content is never executed. Malformed files produce a safe error, not an internal trace.
- **SEC-4:** Financial descriptions, amounts, statement contents, user-defined category and merchant-tag names, the FNB preamble's name/account number, Standard Bank PDF account metadata, and raw ZIP/CSV/PDF filenames do not appear in application logs, audit events, or third-party analytics. Operational events may record counts, status, and non-sensitive identifiers.
- **SEC-5:** The admin surface does not expose financial data. Any future support-access feature requires an explicit, audited product decision.
- **SEC-6:** Hiding a statement hides its transactions, and restoring it restores any transactions not hidden individually before the statement was hidden; totals recalculate. Deleting a committed statement removes all associated transactions and duplicate-protection identity records with no orphans. Routine transaction management never deletes an individual transaction.
- **SEC-7:** Import retries are idempotent. A network failure after a successful commit must not create a second copy on retry.
- **SEC-8:** Import and expensive read endpoints are rate-limited or otherwise bounded so a single session cannot exhaust the service.

## 7. Financial rules and data integrity

### 7.1 Vocabulary

| Term | Meaning in Argus |
| --- | --- |
| **Customer account** | The login identity: email, password, verification, subscription. Called "Profile" in the product UI. |
| **Financial account** | A bank or credit-card account whose statements the user imports. The FNB samples cover both. Called "Account" in the product UI. |
| **Statement** | One committed import and its metadata: financial account, source file type, confirmed statement coverage period (`start_date` to `end_date`), import date, and imported/suppressed/skipped/hidden counts. The source may be a bare FNB CSV, a ZIP containing one FNB CSV, a text-based Standard Bank PDF, or a generic mapped CSV. |
| **Transaction** | One row from a statement, with a date, description, signed amount, one category, zero or more merchant tags, and an import source. |
| **Category** | A single user-visible classification for the purpose of the money, with a kind: Income, Expense, Transfer, or Excluded. Examples: Groceries, Home loan, Levies, School fees. |
| **Merchant tag** | A workspace-wide user label for a store or merchant, such as Woolworths, Checkers, or Uber Eats. A transaction can have zero, one, or many merchant tags. |
| **Uncategorised** | The system bucket for transactions with no category. It is visible and measurable, not a silent default. |
| **Untagged** | The view of transactions with no merchant tag. It is visible and measurable, not a silent default. |
| **Hidden transaction** | A retained transaction excluded from default lists and all calculations. It is not deleted, can be restored, and still participates in duplicate prevention and idempotency. |
| **Transfer** | Money moving between the user's own accounts. Excluded from money in/out and net. |
| **Excluded** | A transaction the user has deliberately excluded from all totals. It remains visible in the list. |
| **Period** | The date range used for analysis. A period is independent of the statement's own date range. |

The conceptual relationships are:

- one customer account owns zero or more financial accounts;
- one financial account owns zero or more statements and transactions;
- one statement owns the transactions created by that import;
- one transaction belongs to one financial account, one statement, and at most one category;
- one transaction can have zero or more merchant tags;
- one category and one merchant tag belong to one customer;
- category and merchant tag are independent dimensions: a category answers what the money was for, and a tag answers where it went;
- a hidden transaction remains linked to its statement and identity; hiding changes visibility, not the imported record.

A transaction remains linked to its statement. Hiding a statement hides its transactions; restoring it restores them except for transactions that were hidden individually before the statement was hidden; deleting a committed statement removes its transactions and duplicate-protection records. Individual transactions are never deleted on their own. This is a product relationship, not a database design.

### 7.2 Metric definitions

The recommended MVP model uses **gross directional flows** as the headline, with transfers and excluded transactions excluded. This keeps every chart reconcilable to the transaction list and avoids double counting.

| Metric | Definition | Transfers | Uncategorised |
| --- | --- | --- | --- |
| **Money in** | Sum of positive transaction amounts in Income, Expense, and Uncategorised categories. | Excluded. | Included, and shown as its own breakdown row. |
| **Money out** | Sum of the absolute value of negative transaction amounts in Income, Expense, and Uncategorised categories. | Excluded. | Included, and shown as its own breakdown row. |
| **Net** | Money in minus money out. | Excluded. | Included through the two directional totals. |
| **Income group** | Inflows minus outflows for categories with kind Income. Shown as a secondary breakdown, not a headline total. | Not applicable. | Not applicable. |
| **Expense group** | Outflows minus inflows for categories with kind Expense. Shown as a secondary breakdown and in category detail. | Not applicable. | Not applicable. |
| **Transfers** | Absolute total and count of Transfer-kind transactions. | Reported separately; never in money in, money out, or net. | Not applicable. |
| **Excluded** | Absolute total and count of Excluded-kind transactions. | Not applicable. | Not applicable. |
| **Needs categorisation** | Count and directional totals of uncategorised transactions. | Not applicable. | The metric itself. |
| **Categorisation coverage** | The share of included transaction value that has a category, with the uncategorised count alongside. | Transfers and excluded are outside the coverage denominator. | The numerator gap. |
| **Category share** | A category's directional amount divided by money in or money out, depending on direction. Rounding is disclosed. | Not applicable. | Uncategorised has its own share. |
| **Merchant-tag total** | Sum of transactions carrying a merchant tag, in the relevant direction. A transaction with multiple tags appears in each tag total. | Excluded. | Included if the uncategorised transaction carries the tag. |
| **Merchant-tag share** | A tag's directional total divided by money in or money out. Shares overlap and do not sum to 100% when transactions have multiple tags. | Not applicable. | Untagged has its own view. |
| **Tag coverage** | The share of included transaction value carrying at least one merchant tag, with the untagged count alongside. | Transfers and excluded are outside the coverage denominator. | The numerator gap. |
| **Hidden** | Count and directional value of hidden transactions. Hidden transactions are excluded from every other metric. | Hidden transactions are outside all calculation denominators. | Shown separately with a restore path. |
| **Comparison** | Current value minus the value for the comparison period, with a percentage only when the prior value is non-zero. | Excluded consistently. | Included consistently. |

**Why this model:** it answers "how much came in," "how much went out," and "what is the net" directly. It keeps refunds as money in and reversals as money out, rather than hiding them inside a net category number. Category detail can show gross inflows, gross outflows, and net for that category. The product must not present a single "spending" number that silently nets refunds unless the label and definition say so.

**Category and merchant tag are different dimensions.** Category rows are mutually exclusive and additive. Merchant-tag rows overlap because a transaction can have several tags. Never sum tag totals to reconcile to money in or money out; tag views reconcile only against the transactions carrying that tag. The UI must state the overlap wherever tag totals appear.

**Hidden transactions are outside every calculation.** They are excluded from money in, money out, net, category and tag totals, coverage, and comparisons. They remain in the data for duplicate prevention and idempotency, and the product reports their count with a restore path.

**Balances are ignored in the MVP.** The FNB credit-card sample's `Balance` column is zero for every row, and the cheque sample's running balance is not a trusted transaction value. Do not import, display, or use a balance for money in, money out, net, or coverage. The signed transaction amount is the sole monetary source and is critical. Opening and closing balances are outside the MVP.

### 7.3 Period and comparison rules

1. Transaction dates are calendar dates. They are not shifted by timezone. A transaction dated 31 October belongs to October regardless of when it was imported.
2. The default period is the most recent calendar month with at least one included transaction. If the current calendar month has data, the default is the current month, labelled "Month to date."
3. Presets include the current month to date, the previous month, the last three months, the last six months, and a custom range.
4. A full calendar month compares with the previous calendar month.
5. A month-to-date period compares with the same day-of-month range in the previous month, labelled "vs same period last month."
6. A multi-month or custom period compares with the immediately preceding period of equal length, ending the day before the selected start. The comparison dates are shown.
7. If the comparison period has no included transactions, the product shows "No comparison available." It does not show 0%, +100%, or an infinite change.
8. If the comparison period has data but the current period is partial, the comparison is labelled as partial and the date ranges are shown.
9. If the union of committed, non-hidden statement coverage periods (`start_date` to `end_date`) for the selected account(s) does not cover the full selected period, the overview shows a coverage warning naming the uncovered dates. It does not imply that missing data is zero spending.
10. Date-range filters are inclusive of the start and end dates.

### 7.4 Amount, currency, and date rules

1. The MVP uses ZAR as the single workspace currency. There is no currency choice. Multi-currency is a future capability.
2. There is no foreign-exchange conversion in the MVP. A non-ZAR statement is unsupported and is blocked with a clear explanation.
3. Amounts are exact at import. The product does not round transaction amounts before aggregation. Display rounds to ZAR cents; the exact value remains available.
4. A positive amount means money in; a negative amount means money out. When a statement uses separate debit and credit columns, the mapping determines the direction. When a statement uses an inverted credit-card convention, the user confirms the convention from a preview that shows the interpreted direction.
5. Thousands separators, decimal separators, parentheses for negatives, trailing minus signs, and CR/DR suffixes are supported in P0 generic CSV mapping, and an ambiguous interpretation requires explicit user confirmation.
6. Zero-amount transactions are imported and visible but do not change totals. They still count in transaction counts.
7. Dates use four-digit years. Ambiguous day/month and month/day formats require the user to choose. Two-digit years require an explicit format choice or statement-period resolution, not a silent pivot.
8. Future-dated transactions are allowed with a warning; they are included if they fall inside the selected period.
9. Amounts with more precision than ZAR cents are flagged as invalid during review. Because source fields are read-only, the user must mark such rows to be skipped (or fix the source file); they are never silently rounded.
10. Every import requires a confirmed statement coverage period (`start_date` and `end_date`). For Standard Bank PDFs, it is pre-filled from the statement period printed in the PDF when present; for CSVs, it is pre-filled from the earliest and latest valid transaction dates. The user confirms or adjusts `start_date` and `end_date` during review. It is stored on the statement for coverage warnings and shown separately from the analysis period without overriding transaction dates.
11. A transaction cannot be imported without a valid date and amount. A missing description is allowed with a fallback label and is flagged in review; a missing category becomes Uncategorised.
12. The FNB file has no currency column and no statement period header. The MVP currency is ZAR; show it prominently during import and require the user to confirm the statement is in ZAR and to confirm or enter the statement `start_date` and `end_date`. Never infer currency from the bank name or account label.
13. The FNB amount format is a signed dot-decimal with a leading space; the leading space is formatting, not a sign. Dates are `YYYY/MM/DD` and are unambiguous.
14. Ignore the FNB `Balance` column entirely. The credit-card sample's balance is zero for every row, and the cheque sample's running balance is not a trusted transaction value. The transaction amount is the sole monetary source.
15. The FNB preamble and raw filenames contain account identifiers. Skip the preamble, do not store or display its contents, and do not log the raw filename.
16. The FNB header is on the seventh line; the six preamble lines are metadata. Rows whose field count differs from the header are invalid; do not shift columns.
17. Standard Bank PDF transaction dates use `DD Mon YY`. Resolve the two-digit year against the confirmed statement period (`start_date` to `end_date`, pre-filled from the PDF header when present); if the period is missing or ambiguous, require the user to confirm the statement dates and year.
18. Standard Bank PDF columns map as follows: Payments is an outflow, Deposits is an inflow, Bank fees is an outflow. Ignore Balance entirely: do not import, display, or use it. The transaction amount is the sole monetary source.
19. PDF descriptions that wrap across lines are joined to the preceding transaction. Page headers, page footers, statement metadata, and summary or balance-only rows are not transactions.
20. A Standard Bank PDF must contain a selectable text layer. Image-only or scanned PDFs are unsupported in the MVP, and password-protected PDFs are rejected.
21. PDF-derived rows retain their page number and source context for review. The raw PDF is not retained after the import or an abandoned review.
22. If a PDF transaction row has values in more than one flow column, flag it invalid in review (requiring the user to skip it) rather than summing the columns silently.
23. If a PDF row has no flow amount, treat it as a summary or metadata row and skip it unless it is clearly a transaction with a missing amount, in which case flag it invalid.
24. Transaction descriptions are unstructured merchant text. Argus may use them for search, explicit aliases and advisory suggestions; tags and categories remain user-assigned and are never recorded as fact until the user explicitly saves them.
25. Suggested tags and suggested categories are suggestions only. They are never applied automatically. Accepting a tag suggestion writes only that tag; a suggested category is staged and saved only by an explicit category change.
26. A transaction's date, description, amount, account, and source are strictly read-only both during the pre-commit import review and after commit. Invalid rows in the pre-commit review must be marked to be skipped (or fixed in the source file before re-uploading). After commit, only category, merchant tags, and hidden state can change.

### 7.5 Duplicate rules

1. **Exact duplicate:** same financial account, same transaction date, same signed amount, and same normalised description. Normalisation collapses whitespace and case but does not rewrite the description.
2. **Statement repeat:** same financial account and the same stable statement fingerprint. The product blocks or prominently flags the repeat and links to the existing import.
3. **Within-file duplicate:** two rows in the same file that match on the exact key. They are flagged, but the user decides; two identical coffees on the same day are legitimate.
4. **Near duplicate:** same account, exact transaction date and exact signed amount, with normalized-description token-set similarity of at least 0.5. Near duplicates are warnings, not automatic exclusions.
5. Exact matches against already-imported transactions default to suppressed. Exact duplicates within the incoming file are flagged for a user decision. Near duplicates default to included. All three are visible in the review with a reason.
6. An override is explicit, recorded in the import summary, and visible after import.
7. A statement-level repeat can only be imported after the user confirms an override or deletes/hides the prior import.
8. Permanently deleting a committed import removes that statement, its transactions, and its duplicate protection for future imports of the same data. Hiding a statement or transaction retains its duplicate protection.
9. Imported date, description, and amount are immutable in review and after commit. No edit can create a duplicate, and category or tag changes do not affect identity.
10. Retrying a failed commit must detect the already-saved statement and return the existing result rather than importing again.
11. FNB descriptions are padded with trailing spaces. Trim them for display and duplicate matching, but preserve the meaningful description text.
12. Standard Bank PDF descriptions are reconstructed from wrapped lines. Collapse whitespace for matching and display, but preserve the meaningful description text.
13. **Transaction-level idempotency:** re-importing an overlapping statement, or retrying a commit, must never create a second copy of a transaction already recorded for the same financial account.
14. **Identity key:** the fallback key is financial account, transaction date, signed amount, and normalised description. A stable source transaction identifier is used when the source provides one.
15. **Occurrence-aware suppression:** when the same identity key legitimately occurs more than once, compare the existing count with the incoming count. Suppress only the occurrences already present and present the excess as new.
16. **Account scope:** idempotency is per financial account. The same transaction arriving from a CSV, a ZIP-contained CSV, or a PDF for the same account is a duplicate; a transfer between two different accounts is not.
17. **Visibility:** the import review and history report suppressed-as-already-imported rows separately from rows skipped by the user and possible duplicates.
18. **Metadata:** category and merchant-tag assignments are user metadata. They are not part of the transaction identity and do not affect duplicate detection or idempotency.
19. **Hidden transactions:** hidden transactions remain in the identity set unless their parent statement import is permanently deleted. Re-importing a hidden transaction does not create a visible copy; it is reported as already imported and can be restored.

### 7.6 Reconciliation invariants

These must hold for every selected period and account filter:

1. **Directional reconciliation:** money in equals the sum of positive included amounts; money out equals the sum of the absolute values of negative included amounts; net equals money in minus money out.
2. **Category reconciliation:** the sum of the inflow category breakdown equals money in; the sum of the outflow category breakdown equals money out, within display rounding.
3. **Single bucket:** every included transaction appears in exactly one category row and contributes to exactly one directional total, money in or money out. Transfers and excluded transactions appear only in their own summaries. No transaction is counted twice.
4. **Coverage visibility:** every uncategorised transaction is visible in the uncategorised row and the coverage indicator. It is never silently allocated to another category.
5. **Transfer exclusion:** Transfer-kind transactions never appear in money in, money out, or net. They appear only in the transfers summary and the transaction list.
6. **Excluded exclusion:** Excluded-kind transactions never appear in any total. They remain visible in the transaction list.
7. **Statement reconciliation:** for each import, imported transactions plus suppressed-as-already-imported rows plus explicitly skipped rows equals the number of data rows reviewed. The import summary states all counts.
8. **Deletion reconciliation:** deleting an import removes exactly its transactions. Totals after deletion equal totals before deletion minus those transactions.
9. **Metadata reconciliation:** changing a transaction's category, merchant tags, or hidden state updates every affected total and comparison consistently. The imported date, description, and amount are immutable. There is no cached total that disagrees with the list.
10. **Rounding disclosure:** displayed category shares may not sum to exactly 100% because of rounding. The product says so rather than adjusting a category to force the total.
11. **Idempotency:** re-importing an overlapping statement or retrying a commit does not change the count of already-imported transactions; only genuinely new occurrences increase the count.
12. **Category reconciliation:** category rows are mutually exclusive and additive; category totals plus Uncategorised equal money in or money out.
13. **Tag reconciliation:** each merchant-tag total equals the transactions carrying that tag. Tag rows overlap when a transaction has multiple tags and must never be summed to reconcile to money in or money out. The Untagged view contains transactions with no tags.
14. **Hidden exclusion:** hidden transactions contribute to no calculation total, category or tag total, coverage denominator, chart, or comparison. They remain in the identity set for duplicate prevention and are reported separately with a restore path.

### 7.7 Insight honesty rules

- Never label an inference as a fact. "Spending is up 18%" is allowed when the comparison is valid; "You are overspending" is not.
- Never compare periods when one period has no data. Show "No comparison available" and explain why.
- Never treat missing statement coverage as zero spending. Show a coverage warning.
- Never present a total as complete when uncategorised transactions exist without saying so. When uncategorised value exceeds a material share of money in or money out (proposed 10%), show a prominent warning that the totals include uncategorised transactions and may change after review.
- Merchant-level insight must state that it uses user-assigned tags, not an automatic merchant directory. Tag totals overlap when a transaction has multiple tags.
- Never claim a recurring expense, anomaly, or saving opportunity without a published, testable rule. Those are future capabilities.
- Every calculated insight must state its period, account scope, and whether transfers, excluded transactions, or uncategorised transactions are included.
- A "How this is calculated" disclosure must use the same definitions as the metric dictionary in this brief. Suggested tags and suggested categories are clearly suggestions, not confirmed facts.
- Hidden transactions are not deleted. Never describe them as deleted; show the hidden count and the restore path, and state that they are excluded from the displayed totals.

## 8. Prioritised scope

### 8.1 Existing — preserve

No finance capability is in this category. These are the account and platform capabilities to preserve:

- Customer registration, email verification, login, logout, password reset, and session semantics.
- The trusted frontend-to-backend boundary, application secret, origin checks, and backend authority over account state.
- The admin account-state view and its backend-derived capability check.
- The mock billing webhook and subscription entitlement behaviour, unless a separate decision changes the access model.
- The visual language: Tailwind tokens, dark/light themes, form controls, focus conventions, card treatment, and responsive page shells.
- Existing account details for the profile/settings area.
- The existing test and CI structure; extend it rather than replacing it.

### 8.2 Improve — existing functionality that needs refinement

These are product-coherence corrections, not finance features:

| Area | Improvement | Reason |
| --- | --- | --- |
| Landing page and metadata | Rewrite for the finance product; lead with the tagline "See where your money goes"; replace the starter-kit story; point the primary action at registration or the product, not the sample page. | The current page contradicts the product and is the first thing a user sees. |
| Authenticated home | Make `/home` the financial overview; move account details to profile/settings; keep logout and admin access. | The current home has no product value. |
| Navigation | Add persistent Overview, Transactions, Imports, and Settings destinations. | Users currently have no way to move around the product. |
| Sample page | Remove or repurpose it as a genuine product detail page; do not keep it as the primary call to action. | It is explicitly illustrative scaffolding. |
| Empty states | Replace account-only views with first-run guidance that leads to the first import. | The core workflow must start without guesswork. |
| Error and loading states | Extend the existing form-message pattern to import, validation, and data-loading flows. | Finance flows have more failure modes than authentication. |
| Tests | Update the landing and home tests intentionally when the product copy and screen purpose change; add finance tests. | Existing tests encode the scaffold, not the product. |
| Documentation | Keep operational docs accurate, treat this brief as the product source of truth, and update the README/AGENTS product descriptions when the identity changes. | The repository currently has no product documentation. |

### 8.3 MVP gaps — essential for a coherent initial product

| ID | Capability | Priority | Why it is MVP |
| --- | --- | --- | --- |
| MVP-1 | Financial account setup for bank and credit-card accounts: name, optional institution, ZAR workspace currency, archive, and restore. | P0 | The FNB samples cover a cheque account and a credit card; import needs a destination, and duplicate detection is per account. |
| MVP-2 | FNB ZIP/CSV import: structure detection, preamble handling, statement start/end date confirmation, read-only preview, validation, skip controls for invalid rows, and atomic commit. | P0 | This is the product's entry point, and the provided samples define the first format. |
| MVP-3 | Committed import history with confirmed statement period, imported/suppressed/skipped/hidden counts, hidden state, hide/restore, and committed-import deletion. | P0 | Users need to know what happened and recover from mistakes by hiding, restoring, or deleting and re-importing a statement. |
| MVP-4 | Duplicate detection and transaction idempotency: exact, statement-level, within-file, near-duplicate, overlapping imports, retries, occurrence-aware suppression. | P0 | Duplicate data destroys trust in every total, and overlapping statements are normal. |
| MVP-5 | Transaction list with search, date/account/category/direction filters, sorting, pagination, and totals. | P0 | This is the working surface for review and classification. |
| MVP-6 | Transaction detail with read-only imported fields, category and merchant-tag changes, hide and restore. | P0 | The imported record is the truth; only metadata and visibility may change after import. |
| MVP-7 | Category model, default categories including Groceries, Home loan, Levies, and School fees, kinds, create/rename/archive/delete, system Uncategorised. | P0 | Categories are the vocabulary of the insight. |
| MVP-8 | Single and bulk categorisation and merchant-tag add/remove, including select-all-matching-filter. | P0 | Manual one-by-one categorisation and tagging is too slow to be a product. |
| MVP-9 | Overview with money in, money out, net, needs-categorisation, directional category breakdowns, trend, and comparison. | P0 | This is the core value proposition. |
| MVP-10 | Drill-down from every overview element to the matching transactions or category detail. | P0 | Totals must be explainable. |
| MVP-11 | Data coverage: confirmed statement period coverage, partial periods, missing comparison data, uncategorised disclosure. | P0 | Trust depends on not overstating incomplete data. |
| MVP-12 | Customer-scoped isolation, admin exclusion, hide/restore for transactions and imports, and committed-import deletion (no whole-customer account deletion in MVP). | P0 | Financial data is sensitive and customer isolation is mandatory. |
| MVP-13 | Responsive, accessible core flows; empty, loading, error, and partial states. | P0 | The product must be usable, not just correct. |
| MVP-14 | Profile/settings area for existing account details, financial accounts, categories, and merchant tags. | P0 | The product needs a home for account, category, and tag configuration alongside the app shell. |
| MVP-15 | Category detail with trend, gross in/out, net, count, and comparison. | P1 | This is the bridge between overview and transactions. |
| MVP-16 | Period comparison view of the largest category changes. | P1 | It directly answers "what changed?" but the overview comparison is the minimum. |
| MVP-17 | Performance targets for overview, filtering, and import. | P1 | Personal datasets are small, but the experience must stay responsive. |
| MVP-18 | Landing page and metadata rewrite; sample page retirement. | P0 (launch-blocking) | The product cannot launch with a starter-kit identity. |
| MVP-19 | Generic CSV mapping and format disambiguation for other bank exports (header row, delimiter, date format, decimal separator, and debit/credit or sign-inversion confirmation). | P0 | Included in initial launch so users can import non-FNB CSVs and confirm ambiguous date, decimal, or sign conventions. |
| MVP-20 | Standard Bank text-based PDF import: six-column layout, multi-line descriptions, two-digit-year resolution via statement period, page metadata skipping, and source context. | P0 | PDF is confirmed MVP scope; the two provided samples share one layout. |
| MVP-21 | PDF format safety: reject scanned/image-only, password-protected, and unrecognized-layout PDFs with clear messages. | P0 | PDF parsing must fail safely and must not imply OCR support. |
| MVP-22 | Merchant tags: multiple tags per transaction, suggestions with confirmation, create/rename/archive/delete, bulk add/remove, single-tag filter, tag detail, top tags, and untagged coverage. | P0 | Store-level insight is a confirmed requirement, and categories alone cannot answer where the money went. |
| MVP-23 | Chart view: bar-first category, merchant-tag, and period charts with filters, drill-down, and accessible table alternatives. | P0 | The product needs a visual lens over the same reconciled data. |
| MVP-24 | Hide, restore, and statement delete: transaction and import hide/restore (preserving individual transaction hide flags), bulk hide, Hidden view, exclusion from calculations, retention for idempotency, and committed-import deletion. | P0 | Users need to remove noise without losing records, and delete a bad import when needed. |

### 8.4 Future enhancements — not MVP

| Enhancement | Why it is not MVP | Trigger to reconsider |
| --- | --- | --- |
| Additional bank-specific CSV presets and dialects | FNB is the only validated layout; other banks need their own presets and fixtures. | More banks are added to the target market, or generic mapping proves too slow. |
| Scanned or image-only PDFs and OCR | Requires OCR, confidence scoring, and a larger correction workflow. The MVP handles text-based PDFs only. | A scanned PDF is the only statement format a target user has. |
| Other banks' PDF statement layouts | Each bank and statement layout needs its own parser and fixtures. Standard Bank is the only validated PDF layout. | A target user's bank issues PDFs in a different layout. |
| OFX, QFX, and XLSX imports | Each adds a parser, format variance, and validation burden. CSV proves the workflow first. | Users cannot export CSV, or a target segment requires a specific format. |
| Direct bank connections | Adds providers, consent, connectivity, regulatory, and support complexity; conflicts with the privacy-first manual import hypothesis. | Strong evidence that manual import is the main adoption blocker. |
| Auto-categorisation and remembered tag rules | Useful, but the manual and bulk workflow must work first; rules can create invisible errors. | Categorisation or tagging volume is high enough that bulk actions are insufficient. |
| Undo for bulk edits | Helpful, but re-assignment is the basic correction path; undo adds history and concurrency complexity. | Usability testing shows bulk mis-assignment is common. |
| External merchant directories or cross-customer automatic merchant normalisation | The MVP uses user-assigned tags plus a customer-local derived merchant key for advisory history; an external directory adds maintenance, coverage and privacy questions. | Descriptions are consistent enough to build a validated external normaliser, or users need automatic enrichment. |
| Combined tag filters and tag merge | One-tag filtering and manual tags are enough for the first release; combined filters and merging add UI and reconciliation complexity. | Users have many overlapping tags and routinely need AND/OR filters or duplicate-tag cleanup. |
| Line and area chart variants | Bar charts are the default and first priority; line charts add a second chart language and interaction model. | Bar charts prove insufficient for trend or distribution questions. |
| Recurring transaction detection | Requires a defensible definition and several periods of data; false positives damage trust. | Users have enough history and repeatedly ask for subscription/recurring views. |
| Budgets and envelopes | A different product model; the vision is insight, not enforcement. | Explicit user demand and a deliberate product strategy change. |
| Multi-currency and FX | Requires exchange-rate sourcing, policy, and rounding decisions. | The target user regularly imports statements in more than one currency. |
| Shared or household workspaces | Adds roles, permissions, and attribution. | A validated household use case. |
| Split transactions | One transaction across multiple categories adds splitting and reconciliation complexity. | Users frequently report mixed-purpose transactions. |
| Manual transaction entry | The MVP is statement-driven; manual entry adds validation, duplicate, and source-attribution questions. | Users need to track cash spending that never appears on a statement. |
| Account balances and reconciliation | Balances are ignored in the MVP; a balance view adds opening/closing and reconciliation complexity. | Users need to verify a statement balance or track net worth. |
| Account deletion, data export, and whole-customer data removal | The MVP hides and restores transactions and allows deleting committed statement imports; whole-customer account deletion and export are future privacy features. | Users or regulators require whole-account deletion or export. |
| Transfer linking between accounts | Nice for net-worth views, but the MVP excludes transfers from insight totals. | The product adds balance or net-worth tracking. |
| Exports and scheduled reports | Useful distribution, not core insight. | Users ask to take data out or receive a monthly summary. |
| Anomaly and unusual-change detection | Needs a defined baseline and a high bar for false positives. | Enough history exists to validate the rule. |
| Investments, advice, credit scores, bill pay, tax | Outside the product vision and carry regulatory or advisory risk. | Not planned. |
| AI chat or LLM-generated financial conclusions | Conflicts with the trust requirement unless every claim is grounded and testable. | Not planned without a separate evidence base. |
| Native mobile app | The responsive web product covers the core workflow first. | Mobile usage dominates and the web experience is the bottleneck. |

### 8.5 Explicit non-goals for the initial release

- No budgets, envelopes, savings goals, or spending limits.
- No investment, pension, crypto, or net-worth tracking.
- No financial advice or personalised recommendations.
- No direct bank integrations or account aggregation.
- No AI chat, generated advice, or opaque scoring.
- No accounting, invoicing, tax, or receipt-OCR workflow.
- No OCR for scanned or image-only statements in the MVP.
- No multi-currency conversion.
- No balance import, balance display, or balance reconciliation. Transaction amounts are the source of truth.
- No automatic merchant enrichment, external merchant directory, or auto-applied merchant tags in the MVP.
- No combined tag filters or tag merge in the MVP.
- No editing of a transaction's date, description, or amount either during import review or after commit.
- No individual transaction deletion (hide and restore only). Committed statement imports may be hidden, restored, or permanently deleted; whole-customer account deletion and data export are future.
- No shared/household accounts.
- No pricing page, checkout, or plan management.
- No financial data in the admin surface.

## 9. Acceptance criteria for priority workflows

### 9.1 Import and review

- **AC-IMP-1:** Given a financial account in ZAR and a CSV with mapped date, description, and amount columns and confirmed ZAR and statement start/end dates, when the user confirms the import, then a statement is recorded as imported with its confirmed coverage period, the transaction count equals the valid included rows, and each transaction's date, description, and amount match the source row.
- **AC-IMP-2:** Given a file with invalid rows, when the review is shown, then every invalid row has a row number, raw source values, and a plain-language reason, source fields are read-only, and the import cannot commit until each invalid row is marked to be skipped.
- **AC-IMP-3:** Given an ambiguous date, decimal separator, delimiter, or sign format in a CSV, when the user reaches the preview, then Argus requires an explicit format choice and shows the interpreted values; no ambiguous value is silently accepted.
- **AC-IMP-4:** Given a backend failure during commit, when the failure is reported, then no statement or transactions from that commit are visible, and retrying does not create duplicates.
- **AC-IMP-5:** Given a file that repeats a previously imported statement, when the user uploads it, then Argus blocks or prominently flags the repeat, links to the existing statement, and requires an explicit override to proceed.
- **AC-IMP-6:** Given a file containing two identical rows, when the review is shown, then both are flagged as possible duplicates, the user can include both, and neither is silently dropped.
- **AC-IMP-7:** Given a completed import, when the user opens import history, then the account, confirmed statement coverage period (`start_date` to `end_date`), import date, and total/imported/suppressed-as-already-imported/skipped/hidden counts are visible and consistent with the review.
- **AC-IMP-8:** Given a completed import, when the user hides it and confirms, then its transactions are hidden, the import remains in history with a Hidden state, the overview totals exclude them, and restoring the import restores its transactions except those that were hidden individually before the import was hidden. When the user permanently deletes a committed import and confirms, that statement, its transactions, and its duplicate-protection identity records are removed so the statement can be re-imported cleanly.
- **AC-IMP-9:** Given an FNB ZIP containing one CSV, when the user uploads it, then Argus extracts the CSV, skips the six preamble lines, recognizes the header, and previews every data row without importing any preamble line as a transaction.
- **AC-IMP-10:** Given a ZIP containing zero or multiple CSVs, a nested archive, an encrypted archive, or an unsafe path, when the user uploads it, then Argus rejects it before parsing with a clear reason.
- **AC-IMP-11:** Given the FNB credit-card sample, when the overview is calculated, then the `Balance` column is not imported or displayed and has no effect on money in, money out, net, or coverage; the signed transaction amounts are the only source.
- **AC-IMP-12:** Given FNB descriptions with trailing spaces, when duplicate matching runs, then trailing whitespace is ignored; when the description is displayed, the trailing whitespace is removed.
- **AC-IMP-13:** Given a data row with more or fewer fields than the header, when the review is shown, then the row is invalid and is not shifted into other columns.
- **AC-IMP-14:** Given a text-based Standard Bank PDF, when the user uploads it, then Argus recognizes the six-column layout, extracts transaction rows, joins continuation lines to the preceding transaction, skips page headers, footers, metadata, and summary rows, and shows the extracted count.
- **AC-IMP-15:** Given an image-only or scanned PDF, when the user uploads it, then Argus rejects it with a clear message that text-based PDFs are supported and OCR is not yet available.
- **AC-IMP-16:** Given a password-protected PDF, when the user uploads it, then Argus rejects it without attempting to bypass the protection.
- **AC-IMP-17:** Given a Standard Bank transaction with a two-digit year, when the row is parsed, then the year is resolved from the statement period; if the period is missing or ambiguous, the user must confirm the year before the row can be imported.
- **AC-IMP-18:** Given a Standard Bank row with exactly one populated flow column, when the transaction is created, then Payments and Bank fees become outflows, Deposits becomes an inflow, and the Balance column is not imported, displayed, or used in totals.
- **AC-IMP-19:** Given an 18-page Standard Bank PDF, when the import completes, then no transaction is duplicated or lost at a page boundary, and each extracted row retains its page number for review.
- **AC-IMP-20:** Given a PDF whose layout is not the recognized Standard Bank six-column layout, when the user uploads it, then Argus rejects it with a clear unsupported-layout message and imports nothing.
- **AC-IMP-21:** Given a statement already imported, when the user imports another file containing some of the same transactions plus new ones, then the already-imported occurrences are suppressed, only the new occurrences are proposed for import, and the review shows both counts.
- **AC-IMP-22:** Given an identity key that legitimately occurs twice and one occurrence was already imported, when a new statement contains both occurrences, then only the unseen occurrence is proposed for import; the user can still override if needed.
- **AC-IMP-23:** Given a successful import, when the same commit is retried or the same statement is submitted again, then no additional transactions are created.
- **AC-IMP-24:** Given the same account transaction arriving once from a CSV or ZIP-contained CSV and once from a PDF, when the second file is imported, then it is recognized as already imported; a transfer between two different accounts is not treated as a duplicate.
- **AC-IMP-25:** Given a re-export of an overlapping statement with the same transactions in a different row order, when it is imported, then idempotency still holds; row order does not create duplicates.
- **AC-IMP-26:** Given a source row with a valid date and description but a missing or unparseable amount, when the review is shown, then the row is invalid and cannot be imported; the balance column is never used as a fallback.
- **AC-IMP-27:** Given a committed transaction, when its detail view is open, then the imported date, description, amount, account, and source are read-only. No save path changes them; only category, merchant tags, and hidden state are editable.

### 9.2 Categorisation and merchant tags

- **AC-CAT-1:** Given N selected transactions, when the user assigns a category, then all N receive it, the confirmation states N, and the overview and category counts update.
- **AC-CAT-2:** Given a filter with matches beyond the current page, when the user chooses "select all matching," then every matching transaction is selected, and the displayed count matches the filter total.
- **AC-CAT-3:** Given an inflow assigned to an Expense-kind category, when the overview is calculated, then the inflow appears in money in and in the category's inflow detail, and the category's secondary net reflects the refund; it is not silently removed from money in.
- **AC-CAT-4:** Given a Transfer-kind transaction, when the overview is calculated, then it appears in the transfers summary and not in money in, money out, or net.
- **AC-CAT-5:** Given an archived category, when a historical transaction is viewed, then the category is still visible; when a new assignment is attempted, then the archived category is not selectable.
- **AC-CAT-6:** Given a category deletion, when the user confirms, then its transactions move to Uncategorised and no transactions are deleted.
- **AC-CAT-7:** Given a Woolworths transaction, when it is categorised as Groceries and tagged Woolworths, then the category breakdown shows Groceries and the tag view shows Woolworths. Categorising another Woolworths transaction as Clothing does not change the tag.
- **AC-TAG-1:** Given a new merchant tag, when the user creates it, then it is available for assignment; creating the same name with different case or surrounding whitespace is rejected as a duplicate.
- **AC-TAG-2:** Given a transaction with two merchant tags, when the user removes one, then the other remains and the category is unchanged.
- **AC-TAG-3:** Given N selected transactions, when the user bulk-adds or bulk-removes a tag, then the confirmation states N, unrelated tags remain, and the tag views update.
- **AC-TAG-4:** Given a merchant tag, when the user opens its detail view, then the total and count equal the transactions carrying that tag.
- **AC-TAG-5:** Given a transaction with multiple tags, when tag totals are displayed, then the view states that tag totals overlap and does not sum them to reconcile to money in or money out.
- **AC-TAG-6:** Given a tag suggestion from the customer's description, aliases or own history, when the suggestion is shown, then it is labelled as suggested and is not applied until the user confirms it.
- **AC-TAG-7:** Given transactions with no tags, when the tag view is displayed, then they appear in Untagged and the tag-coverage indicator includes them.
- **AC-TAG-8:** Given a tag deletion, when the user confirms, then the tag is removed from its transactions and no transaction is deleted.

### 9.3 Overview and reconciliation

- **AC-OVR-1:** Given a selected period and account filter, when the overview is displayed, then money in, money out, net, and the sum of the category breakdowns reconcile to the filtered transaction list within display rounding.
- **AC-OVR-2:** Given a chart segment or category row, when the user activates it, then the transaction list opens with the same period, account, category, and direction filters, and the list total matches the segment.
- **AC-OVR-3:** Given a comparison period with no included transactions, when the overview is displayed, then the comparison shows "No comparison available" and no percentage.
- **AC-OVR-4:** Given a current month with data, when the overview defaults to it, then the heading says "Month to date" and the comparison uses the same day range in the previous month.
- **AC-OVR-5:** Given uncategorised transactions, when the overview is displayed, then the needs-categorisation indicator shows the count and directional amounts, and the category breakdowns show an Uncategorised row.
- **AC-OVR-6:** Given a selected period that extends beyond the union of committed, non-hidden statement coverage periods (`start_date` to `end_date`) for the selected account(s) or contains an uncovered gap between statements, when the overview is displayed, then a coverage warning names the uncovered dates and does not imply that missing data is zero.

### 9.4 Access, isolation, and deletion

- **AC-SEC-1:** Given user A's authenticated session, when user A requests user B's financial account, statement, transaction, or category by identifier, then the response does not reveal whether the record exists and no data is returned.
- **AC-SEC-2:** Given no valid session, when a finance screen or finance data request is made, then the user is sent to sign in and the backend rejects the request as unauthorised.
- **AC-SEC-3:** Given an admin session, when the admin surface is opened, then it shows account state only and no financial data.
- **AC-SEC-4:** Given a successful or failed import, when logs and audit events are inspected, then they do not contain transaction descriptions, amounts, or file contents.
- **AC-DEL-1:** Given a statement hide, when it completes, then that statement's transactions are hidden and no transaction is deleted; restoring the statement restores its transactions except those hidden individually before the statement was hidden. Given a committed statement delete, when confirmed, then exactly that statement, its transactions, and its duplicate-protection identity records are removed.
- **AC-DEL-2:** Given the MVP, then individual transactions cannot be deleted (hide and restore only), and there is no whole-customer account-deletion or data-export action; whole-account deletion and data export are future.

### 9.5 Responsive and accessible experience

- **AC-UX-1:** Given a 375 px viewport, when the user imports, reviews, categorises, and reads the overview, then the primary controls and amounts are reachable and readable without horizontal page scrolling.
- **AC-UX-2:** Given keyboard-only operation, when the user reviews an import or bulk-categorises transactions, then selection, assignment, and confirmation are possible without a pointer.
- **AC-UX-3:** Given a chart, when it is read by a screen reader, then the same information is available as text or a table, and colour is not the only way to distinguish categories.

### 9.6 Chart view

- **AC-CHT-1:** Given a period with data, when the user opens the Chart view, then bar charts render by default for category outflows, category inflows, merchant tags, and money in versus money out over time.
- **AC-CHT-2:** Given the same filters as the transaction list, when the Chart view is displayed, then each bar's amount matches the total of the transactions it represents; changing a filter updates the chart and list together.
- **AC-CHT-3:** Given a chart bar, when the user activates it, then the transaction list opens with the matching period, account, category, tag, and direction filters.
- **AC-CHT-4:** Given merchant-tag bars, when they are displayed, then the view states that tags overlap when a transaction has multiple tags and does not sum tag bars to the headline totals. Category bars remain additive.
- **AC-CHT-5:** Given keyboard-only use or a narrow viewport, when the user reads the Chart view, then every chart has a text or table alternative and all bar values remain reachable without colour-only encoding.

### 9.7 Hide and restore

- **AC-HIDE-1:** Given a visible transaction, when the user hides it, then it disappears from the default transaction list, all cards, charts, category and tag totals, coverage, and comparisons; it remains in the Hidden view and can be restored.
- **AC-HIDE-2:** Given a committed import, when the user hides it, then all of its transactions are hidden, the import remains in history with a Hidden state, totals recalculate, and restoring the import restores its transactions except any that were hidden individually before the import was hidden.
- **AC-HIDE-3:** Given a hidden transaction, when an overlapping statement containing the same transaction is imported, then it is suppressed as already imported and no visible duplicate is created. The user can restore the hidden transaction instead.
- **AC-HIDE-4:** Given hidden transactions, when categorisation, tagging, or coverage is calculated, then hidden transactions are excluded from the denominator and shown separately as a hidden count.
- **AC-HIDE-5:** Given all transactions in a period are hidden, when the overview is displayed, then it shows the empty state with the hidden count and a restore action, not zero spending.

## 10. Edge cases

| Area | Case | Expected behaviour |
| --- | --- | --- |
| File | Empty file or header-only file | Reject with a clear message; no import record with zero transactions. |
| File | Unsupported extension or content | Reject before parsing; list the supported format. |
| File | File above the size or row limit | Reject with the limit and a suggestion to split the statement. |
| File | Undecodable characters | Explain the encoding issue; do not replace characters silently. |
| File | Preamble rows before the header | Let the user choose the header row; preview the result. |
| File | FNB six-line preamble | Skip it automatically; never import a preamble line as a transaction. |
| File | ZIP with zero or multiple CSVs | Reject before parsing; state that exactly one CSV is required. |
| File | Nested archive, encrypted archive, or unsafe path | Reject before parsing; do not extract to an unsafe location. |
| File | Compressed content above the limit | Reject before decompression exceeds the limit; protect against archive expansion. |
| File | PDF has no selectable text layer | Reject as a scanned/image-only PDF; OCR is not in the MVP. |
| File | PDF is password-protected | Reject with a clear message; do not attempt to bypass protection. |
| File | PDF layout is not the Standard Bank six-column layout | Reject as an unsupported layout; import nothing. |
| File | PDF page header, footer, metadata, or summary row | Skip it; never import it as a transaction. |
| File | PDF description wraps across lines | Join it to the preceding transaction; do not create a separate transaction. |
| File | PDF transaction crosses a page break | Join it carefully; do not duplicate or lose it. |
| File | PDF above the page or size limit | Reject with the limit and a suggestion to split the statement. |
| Date | PDF transaction year is two digits | Resolve from the statement period; require confirmation if ambiguous. |
| Amount | PDF row has Payments, Deposits, Bank fees, and Balance | Map Payments and Bank fees to outflows and Deposits to inflows; ignore Balance entirely and do not import or display it. |
| Amount | PDF row has more than one populated flow column | Flag for review; do not sum the columns silently. |
| Amount | PDF row has no flow amount | Skip summary/metadata rows; flag a genuine transaction with a missing amount. |
| File | FNB filename contains an account number | Do not display or log the raw filename; use the user's account label and a generated statement label. |
| File | Missing or duplicate column headers | Require mapping; do not guess duplicate columns. |
| Date | Ambiguous `03/04/2026` | Require a format choice with a preview. |
| Date | Two-digit year | Require an explicit century/format choice. |
| Date | Future date | Allow with a warning. |
| Amount | `1.234,56` versus `1,234.56` | Detect and preview; require confirmation when ambiguous. |
| Amount | Parentheses, trailing minus, CR/DR suffix | Parse according to the confirmed convention; never guess silently. |
| Amount | Separate debit and credit columns | Map both; direction is explicit. |
| Amount | Credit-card sign inversion | Require confirmation from a preview showing money in/out. |
| Amount | FNB leading space before the amount | Trim it; the space is formatting, not a negative sign. |
| Amount | FNB credit-card `Balance` is zero for every row | Ignore and do not import the balance; use the signed amount only. |
| Amount | FNB file has no currency column or statement period | Use ZAR; show it and require the user to confirm ZAR and the statement `start_date` and `end_date` (pre-filled from earliest and latest transaction dates); never infer currency from the bank or account label. |
| Amount | Zero amount | Import and display; no effect on totals. |
| Amount | More decimal places than ZAR cents | Flag as invalid; require the row to be marked skipped (or fixed in the source file); never round silently. |
| Row | Missing description | Allow with a fallback label; do not drop the row silently. |
| Row | FNB description has trailing spaces | Trim for display and duplicate matching; preserve the meaningful text. |
| Row | Unmapped columns present in the header | Ignore them after the user confirms the mapping. |
| Row | More fields than the header (unquoted comma) | Mark the row invalid; do not shift values into other columns. |
| Row | Balance-only or summary row | Skip it; never import it as a transaction. |
| Duplicate | Two legitimate identical rows | Use occurrence-aware suppression: import only the excess occurrences; never auto-delete; allow an explicit override. |
| Duplicate | Re-import after deleting the prior import | Allow; duplicate protection no longer applies to the deleted rows. |
| Duplicate | Overlapping statement periods | Suppress already-imported occurrences by default; import only new occurrences; show both counts. |
| Duplicate | Re-export contains new transactions plus old ones | Suppress the old occurrences; import only the new ones; do not block the whole file. |
| Duplicate | Same transaction appears in CSV/ZIP and PDF for the same account | Recognize it as already imported; do not create a second copy. |
| Duplicate | Transfer appears on two different accounts | Not a duplicate; keep both account-side transactions and let the Transfer category exclude them from totals. |
| Duplicate | Retry after a successful import | Idempotent; create no additional transactions. |
| Duplicate | Re-export changes the description | Treat it as a possible duplicate, not an exact one; the user decides. |
| Duplicate | Re-export changes row order | Match by transaction identity, not row order; idempotency still holds. |
| Category | Archive a category with history | Keep history visible; block new assignments. |
| Category | Delete a category | Reassign to Uncategorised after confirmation; never delete transactions. |
| Tag | Create a tag with the same name in different case or surrounding whitespace | Reject as a duplicate; tag names are case-insensitive unique. |
| Tag | A transaction has multiple tags | Show it in each tag view; tag totals overlap and are never summed to reconcile to headline totals. |
| Tag | Same tag spans different categories | Allow it; tag detail shows the category mix (for example Woolworths Groceries and Woolworths Clothing). |
| Tag | Rename or archive a tag | Keep the assignments; archived tags stay visible on history but are not selectable for new assignments. |
| Tag | Delete a tag | Remove it from its transactions after confirmation; never delete a transaction. |
| Tag | Suggested tag is wrong | Leave it unconfirmed; nothing changes. The user can assign a different tag or none. |
| Tag | Bulk add or remove a tag | Add or remove only the named tag; do not replace unrelated tags. |
| Tag | No tags exist | Show the Untagged view and tag-coverage prompt; do not hide the feature. |
| Category | Change a category kind | Warn that reported totals will change; apply consistently. |
| Transfer | Only one side of a transfer is imported | Exclude the known side from totals; show it in transfers; do not invent the counterpart. |
| Transfer | An uncategorised transfer | Include it in money in/out until categorised; surface it in the needs-categorisation indicator. |
| Refund | Refund assigned to an expense category | Show it as money in and as an inflow against that category; category net reflects it. |
| Split | One transaction spans two categories | Not supported in MVP; the user chooses one category; note the limitation. |
| Period | No transactions in the selected period | Show a period empty state with a change-period action. |
| Overview | Net is negative | Display a minus sign and neutral styling; do not label it as an error or failure. |
| Overview | Net is zero while transactions exist | Show zero and explain that inflows and outflows offset; do not show an empty state. |
| Overview | Period contains only transfers | Money in/out/net are zero; explain that transfers are excluded and link to the transfers list. |
| Overview | Period contains only uncategorised transactions | Money in/out show values, coverage is 0%, and the user is prompted to categorise; do not claim category insight. |
| Chart | Many categories or merchant tags | Show the top N plus an Other bucket; do not use a pie or donut chart to avoid an unreadable legend. |
| Chart | Long category or tag labels | Use horizontal bars, wrapping, or a scrollable list so labels and amounts remain readable. |
| Chart | No data in the period | Show the chart view's empty state; do not render empty bars. |
| Chart | Partial statement coverage | Show the coverage warning in the Chart view; do not imply missing data is zero. |
| Chart | Merchant tag bars overlap | State that a transaction can have multiple tags and do not sum tag bars. |
| Chart | Net is negative | Show signed values and labels; do not treat a negative bar as an error. |
| Chart | Narrow screen | Stack or scroll bars without hiding labels or amounts; keep the table alternative available. |
| Period | Current month only partly covered by statements | Label partial coverage; do not imply a full month. |
| Period | No prior-period data | Show "No comparison available." |
| Account | Archive a financial account with data | Remove it from new-import choices; keep its transactions in history and in the all-accounts view; allow filtering to the archived account and restoring it later. |
| Account | Archive the last financial account | Keep its history visible; new imports prompt the user to restore it or create another account. |
| Period | Statement gap inside the selected range | Compare the selected period against the union of committed, non-hidden statement `[start_date, end_date]` periods for the selected account(s) and show a coverage warning naming any uncovered dates. |
| Edit | User wants to change a row's date, description, or amount in review or after import | Not allowed. Source fields are read-only in review and after commit; mark invalid rows skipped in review, or hide a committed transaction / delete and re-import the statement if needed. |
| Edit | User changes category, merchant tags, or hidden state | Allow; update the affected totals. The imported record is unchanged. |
| Concurrency | Two sessions change the same transaction's category, tags, or hidden state | Warn on a stale change; do not silently overwrite a newer change. |
| Concurrency | Two imports for the same account at once | Serialise or show one in progress; do not interleave counts. |
| Session | Session expires during import | Redirect to sign in; do not lose the confirmed import if it already committed. |
| Backend | Backend unavailable during review | Show a retry path; do not show a successful import. |
| Deletion | Hide or delete a statement while a transaction is open | Return the user to the list or show the transaction as hidden/removed; restore remains available when hidden. |
| Correction | A committed transaction is wrong | Hide it, or delete the committed statement import and import a corrected statement. Do not edit the imported record. |
| Hide | All transactions in a period are hidden | Show the empty state with the hidden count and a restore action; do not show zero spending. |
| Hide | A hidden transaction is re-imported | Suppress it as already imported; do not create a visible duplicate; allow restore. |
| Currency | Statement is not in ZAR | Block the import with an explanation that the MVP supports ZAR only. |
| Currency | User wants a non-ZAR currency | Not supported in MVP; multi-currency is future. |
| Admin | Admin opens a finance screen | Admin sees only their own data as a customer; the admin surface shows account state only. |

## 11. Recommended success metrics

These are initial hypotheses, not commitments. Establish a baseline before setting targets.

| Metric | Definition | Why it matters | Initial target hypothesis |
| --- | --- | --- | --- |
| **Import completion rate** | Imports reaching "imported" divided by imports started, for supported ZIP/CSV inputs. | The product is unusable if statements do not land. | 85% or higher. |
| **FNB first-import success** | FNB ZIP uploads reaching "imported" with zero invalid rows skipped. | Validates the first concrete format contract. | 90% or higher. |
| **Standard Bank PDF extraction accuracy** | Correct transaction rows extracted divided by the expected rows in the sample fixtures. | A PDF parser that loses or invents rows is worse than no PDF support. | 99% or higher on the validated samples. |
| **PDF first-import success** | Standard Bank PDF uploads reaching "imported" with zero invalid rows skipped. | Validates the second concrete format contract. | 85% or higher. |
| **Time to first import** | Median time from first sign-in to first successful import. | Measures onboarding friction. | Under 10 minutes. |
| **Import error rate** | Failed or abandoned imports divided by attempts. | Reveals format and validation problems. | Under 10%. |
| **Duplicate rate** | Duplicate transactions imported per 1,000 transactions. | Duplicate data destroys trust. | Fewer than 1 per 1,000. |
| **Idempotency violations** | Duplicate transactions created by a retry or an overlapping import, per 1,000 imported transactions. | Idempotency is a data-integrity guarantee, not a best effort. | Zero. |
| **Categorisation completion** | Share of included transaction value categorised within 7 days of import. | The overview is only meaningful when categorisation is high. | 80% or higher. |
| **Uncategorised residual** | Share of included value still uncategorised after 30 days. | Measures whether bulk tools are sufficient. | Under 5% for active users. |
| **Bulk efficiency** | Median categorisation actions per 100 transactions. | Measures workflow efficiency. | Fewer than 10 actions per 100. |
| **Bulk adoption** | Share of categorisation changes made in bulk. | Indicates whether the efficient path is discoverable. | 50% or higher. |
| **Drill-down rate** | Share of overview sessions that open a filtered transaction list or category detail. | Measures whether insight leads to explanation. | 30% or higher. |
| **Comparison usage** | Share of overview sessions that select a comparison period or view a category change. | Measures whether trend insight is used. | 20% or higher. |
| **Merchant-tag adoption** | Share of included outflow value carrying at least one merchant tag within 7 days of import. | Tags are only useful when coverage is high. | 70% or higher. |
| **Untagged residual** | Share of included value with no merchant tag after 30 days. | Measures whether bulk tagging and suggestions are sufficient. | Under 10% for active users. |
| **Tag suggestion acceptance** | Suggested tags accepted divided by suggested tags shown. | Shows whether advisory description/history suggestions are useful. | 60% or higher. |
| **Chart view usage** | Share of overview sessions that open the Chart view. | Measures whether the visual lens is discoverable. | 40% or higher. |
| **Chart drill-down rate** | Share of Chart view sessions that open a filtered transaction list or detail view. | Measures whether charts lead to explanation, not just display. | 25% or higher. |
| **Trust defects** | Confirmed reconciliation or calculation defects reported by users or found in QA. | A finance product cannot survive wrong totals. | Zero in QA; under 0.5% of active users reporting. |
| **Return rate** | Share of importers who return within 30 days and within 90 days. | Measures whether the monthly job is retained. | 40% at 30 days; 25% at 90 days. |
| **Performance** | p95 overview response; filter response for 20,000 transactions; import time for 10,000 rows. | Slow finance tools are abandoned. | Under 2s; under 1s; under 30s. |
| **Accessibility** | Critical automated accessibility violations on core flows; keyboard task completion. | Core flows must be usable by everyone. | Zero critical violations; all core tasks keyboard-completable. |
| **Comprehension** | In usability testing, share of participants who can correctly answer "where did the money go?" and "what changed?" | Measures insight, not just feature use. | 80% or higher. |

## 12. Limitations, assumptions, and decisions required

### 12.1 Known limitations

- The repository contains no finance product. This is the defining limitation.
- The two FNB samples cover one bank and two account types. Other FNB account types are unvalidated.
- The samples have no explicit currency label, and FNB CSVs have no statement period header; the MVP is ZAR-only, so show ZAR and require the user to confirm the statement is in ZAR and to confirm or enter the statement `start_date` and `end_date`.
- Balances are ignored entirely in the MVP; the credit-card sample's `Balance` column is zero for every row, and no balance is imported, displayed, or used for reconciliation.
- The samples are real financial data and must not be committed. Use synthetic fixtures that reproduce the structure; record only structural characteristics in documentation.
- No user research, analytics, support data, or product requirements exist.
- The two Standard Bank PDF samples are text-based and share one six-column layout. Scanned/image-only PDFs and other banks' PDF layouts are unvalidated.
- No PDF parser exists in the application yet. The structural feasibility is established from the samples, not from implemented code.
- No chart, data-table, file-upload, CSV, archive, or PDF capability exists in the current dependencies or UI.
- The MVP is ZAR-only and does not convert foreign transactions.
- Source transaction `date`, `description`, and `amount` are strictly read-only both during the pre-commit import review and after commit. Invalid rows in the pre-commit review must be marked to be skipped (or fixed in the source file and re-uploaded); there is no in-review or post-import editing of source fields.
- Individual transactions are never deleted (they are hidden and restored only). Committed statement imports can be hidden, restored, or permanently deleted (removing that statement, its transactions, and its duplicate-protection identity records). There is no whole-customer account-deletion or data-export action in the MVP; account deletion and data export are future.
- Split transactions, external/cross-customer automatic merchant normalisation, recurring detection, auto-categorisation, and transfer linking are not in the MVP.
- Merchant tags are user-assigned, with advisory suggestions from descriptions, explicit aliases and the customer's own history that require confirmation. There is no external merchant directory and no auto-applied tag.
- A transaction can have multiple merchant tags, so tag totals overlap and are not additive. The MVP filters by one tag at a time; combined tag filters, tag merge, and bulk tag replacement are future.
- The Chart view is bar-first. Line and area chart variants are future.
- Whole-customer account deletion is outside the MVP. Individual transactions are hidden and restored; committed imports may be hidden, restored, or deleted.
- The existing login requires email verification and an activated subscription flag. This is existing behaviour, not a new product decision.
- The billing integration is a mock. There is no pricing, checkout, or plan management.
- The landing page, metadata, README, and docs still describe a SaaS starter.
- No performance baseline exists for finance data volumes.

### 12.2 Assumptions requiring validation

1. Target users beyond the provided samples can export statements as CSV or PDF from their banks.
2. FNB's credit-card and cheque-account layouts are representative of the first target market.
3. The two Standard Bank PDFs are representative of the target users' statements, and the layout remains stable enough to parse.
4. Text-based PDFs are the common case; scanned/image-only PDFs can be deferred to OCR.
5. The target user's accounts are all in ZAR; the samples do not state an explicit currency.
6. Target users prefer a manual, privacy-preserving import over a bank connection.
7. Users will tolerate an email-verification step before first use.
8. Users will categorise transactions if bulk selection and select-all-matching are available.
9. A single ZAR workspace covers the initial target user.
10. One to three financial accounts is the common case.
11. Monthly or statement-driven usage is the primary cadence.
12. Users care more about understanding spending than about setting budgets.
13. A raw description is sufficient for search; a generic derived merchant key may group the customer's own suggestion history, but no external merchant directory or cross-customer normalisation is required.
14. Users will trust a total more when they can see the transactions and the calculation basis.

### 12.3 Confirmed decisions

- The initial workspace currency is ZAR. The MVP has no currency selection and no FX conversion. Multi-currency is future.
- Launch formats in P0 are FNB CSV (bare or a ZIP containing exactly one CSV), text-based Standard Bank six-column PDF, and generic CSV mapping with format disambiguation (delimiter, header row, date format, decimal separator, and debit/credit or sign-inversion confirmation). Scanned/image-only PDFs and OCR are future.
- Every import requires the user to confirm ZAR currency and confirm or enter the statement coverage period (`start_date` and `end_date`), pre-filled from the PDF header period or the CSV's earliest and latest valid transaction dates. Statement coverage and gap warnings (`OVR-7`, `AC-OVR-6`) are evaluated against the union of committed, non-hidden statement `[start_date, end_date]` periods for the selected account(s).
- The MVP supports multiple ZAR financial accounts.
- Balances are ignored entirely in the MVP: not imported, not displayed, and not used in calculations. The transaction amount is the critical monetary field.
- Transactions have one category and zero or more merchant tags. Categories include Groceries, Home loan, Levies, and School fees; merchant tags include Woolworths, Checkers, and Uber Eats.
- Source transaction `date`, `description`, and `amount` are strictly read-only both during pre-commit review and after commit. Invalid rows in pre-commit review must be marked to be skipped (or fixed in the source file before re-uploading). Only category, merchant tags, and hidden state can change after import.
- Individual transactions are never deleted (hide and restore only). Committed statement imports can be hidden, restored (preserving individual transaction hide flags), or permanently deleted (removing that statement, its transactions, and its duplicate-protection identity records so it can be re-imported cleanly). Whole-customer account deletion and data export are future.
- Merchant tags are user-assigned with advisory suggestions from descriptions, explicit aliases and the customer's own history; suggestions require confirmation and are never auto-applied.
- The MVP includes a bar-first Chart view over the same data and filters.
- Transaction creation is idempotent across overlapping statement imports and retries. Exact overlaps are suppressed by default; occurrence counts preserve legitimate repeats; an explicit override is available.
- The metric model is gross directional money in, money out, and net, with additive category breakdowns, overlapping merchant-tag views, transfers and excluded separate, and uncategorised and untagged flagged.
- Duplicate policy: exact overlaps are suppressed, same-date/same-amount near duplicates with similar descriptions are warned, statement repeats are blocked with an explicit override.
- Raw-file retention: the uploaded ZIP/CSV/PDF is discarded after import or an abandoned review.
- Access model: the existing verified-subscriber gate, with no pricing or checkout UI in the MVP.

### 12.4 Planning defaults and open questions

| # | Decision | Recommendation | Why it matters |
| --- | --- | --- | --- |
| 1 | Is the finance product to be built on this working tree, or is there another branch/instance with finance code? | Build on this tree. No finance module is present or referenced. | The entire scope depends on this premise. |
| 2 | What are the file and row limits? | Proposed 10 MB and 20,000 data rows per statement, including uncompressed ZIP content. The PDF samples are 4 and 18 pages, with 49 and 297 transactions. Proposed PDF page limit: 200. | Affects UX, performance targets, and abuse controls. |
| 3 | How are refunds and transfers represented? | Refunds are inflows assigned to categories; transfers use a category kind that is excluded from money in, money out, and net. The credit-card sample uses negative purchases and positive credits; the user decides whether a credit is a payment, refund, or income. | Wrong treatment silently corrupts every total. |
| 4 | What is the default period and comparison rule? | Latest month with data; current month as month-to-date; prior comparable period; no comparison when prior data is absent. | All comparisons and the first screen depend on it. |
| 5 | Are default categories seeded, and do merchant tags start empty? | Seed categories including Groceries, Home loan, Levies, and School fees; system Uncategorised; tags start empty and are user-created. Kind changes allowed with a warning. | Category and tag behaviour affects every report. |
| 6 | May admin users see financial data? | No. Admin remains account-state only. | Privacy and isolation are non-negotiable. |
| 7 | Do we support credit-card sign conventions? | Yes. The FNB credit-card sample uses negative purchases and positive credits; confirm the convention in preview. No separate credit-card accounting model. | Many users' spending is on credit cards. |
| 8 | How do we handle concurrent metadata changes and imports? | Warn on stale category, tag, or hidden-state changes; serialise imports per account. | Silent overwrites and double imports damage trust. |
| 9 | What happens to the landing page and sample page? | Rewrite the landing page and metadata for Argus finance using the tagline "See where your money goes"; retire or repurpose the sample page; update the tests intentionally. | The product cannot launch with a starter-kit identity. |
| 10 | Is target-user research done before or alongside architecture? | Run 5–8 interviews or prototype tests in parallel; do not let it block reversible architecture decisions. | The user hypotheses are currently untested. |
| 11 | Should the MVP filter by one merchant tag at a time or support combined tag filters? | One tag at a time; combined filters are future. | Combined filters add query and empty-state complexity. |
| 12 | Should bulk tag actions add and remove tags or replace all tags? | Add and remove; replace-all is future. | Replacing all tags risks destructive edits. |
| 13 | Should the Chart view be a tab within Overview or a separate navigation destination? | Tab within Overview. | Keeps the four-destination navigation small. |

### 12.5 Questions for the next conversation

- Which user segment is the first target: privacy-conscious manual importers, budgeting-app refugees, or a broader "anyone with a bank account" audience?
- Are other FNB account types, such as savings or business accounts, needed at launch?
- Does the first release need a sample/demo dataset, or is a real import the only onboarding path?
- Is there an existing brand or visual direction beyond the current design tokens?

### 12.6 Readiness for architecture planning

**Status: REQUIREMENTS READY for architecture planning.**

Fixed decisions:
- Single workspace currency: ZAR (no currency picker, no FX conversion).
- P0 import formats: FNB CSV (bare or single-CSV ZIP), text-based Standard Bank six-column PDF, and generic CSV mapping with format disambiguation (delimiter, header row, date format, decimal separator, debit/credit columns, and sign-inversion confirmation).
- Statement coverage period: every import requires confirming ZAR and confirming or entering the statement `start_date` and `end_date` (pre-filled from the PDF header period or the CSV's earliest and latest valid transaction dates). Coverage and gap warnings (`OVR-7`, `AC-OVR-6`) are evaluated against the union of committed, non-hidden statement `[start_date, end_date]` periods for the selected financial account(s).
- Multiple ZAR financial accounts.
- Balances ignored entirely; signed transaction amount is the sole monetary source.
- One category and zero or more merchant tags per transaction.
- Source transaction `date`, `description`, and `amount` are read-only both during pre-commit review and after commit; invalid rows in review must be marked to be skipped (no in-review editing of source fields).
- Individual transactions are never deleted (hide and restore only). Committed statement imports may be hidden, restored (preserving individual transaction hide flags), or permanently deleted (removing that statement, its transactions, and its duplicate-protection identity records). No whole-customer account deletion or data export in the MVP.
- Uploaded ZIP/CSV/PDF files are discarded after commit or abandoned review.
- Gross directional `Money in`, `Money out`, and `Net` metric model with additive category breakdowns and overlapping merchant-tag views.
- Occurrence-aware transaction-level idempotency across overlapping imports and retries; exact overlaps suppressed by default, same-date/same-amount near duplicates warned, statement repeats blocked with explicit override.
- Existing verified-subscriber access gate; admin surface shows account state only and never exposes financial data.
- Bar-first Chart view tab within Overview with accessible table alternatives.
- Tagline: "See where your money goes".
- Excluded from MVP: OCR for scanned PDFs, budgets, investments, financial advice, bank feeds, AI chat, split transactions, auto-categorisation, auto-applied merchant tags, combined tag filters, and whole-customer account deletion.

Working defaults for the plan: file and page limits (10 MB, 20,000 rows, 200 PDF pages), refund and transfer representation, default period, default seeded categories and empty initial tags, admin restriction, credit-card sign handling, concurrency warnings on stale metadata edits, the landing-page rewrite, single-tag filter scope, additive/subtractive bulk tag behaviour, and Chart view placement as a tab within Overview. Section 12.4 records each default.

## 13. Programme structure, documentation, and next step

This brief is stored at `docs/product-brief.md` as the authoritative product requirement and continuity brief for Argus.

### 13.1 Approved four-plan delivery programme

Architecture planning and implementation are divided into four sequential, repository-governed plans under `docs/plans/`:

1. **Plan 1 — Foundation, App Shell, and Financial Entities (`docs/plans/01-foundation-and-entities.md`)**
   - **Scope:** Rewrite landing page and metadata with tagline *"See where your money goes"* and retire `/sample-page` (`MVP-18`); build the authenticated app shell and persistent navigation (`Overview`, `Transactions`, `Imports`, `Settings`), moving `AccountDetails` into `Settings` (`SET-3`); implement PostgreSQL migrations, customer-isolated backend endpoints, and Settings UI for ZAR Financial Accounts (`MVP-1`, `SET-1`), Categories with seeded defaults, system `Uncategorised`, and kinds (`MVP-7`, `CAT-1..7`, `SET-2`), and Merchant Tags (`MVP-22` entity/management foundation, `TAG-1`, `TAG-7`, `SET-2`), plus customer-scoped merchant aliases and the 1000-request/15-minute finance IP bound (Plan 1 revision 2).
   - **Dependencies:** None (root foundation).
2. **Plan 2 — Statement Import, Parsers, Review, and Idempotency (`docs/plans/02-statement-import.md`)**
   - **Scope:** FNB CSV/ZIP parser (`MVP-2`, `IMP-14..15`), text-based Standard Bank six-column PDF parser (`MVP-20`, `MVP-21`, `IMP-16..21`), and generic CSV mapping with format disambiguation (`MVP-19`, `IMP-3..4`, `AC-IMP-3`); ZAR and statement `start_date`/`end_date` confirmation (`IMP-12`, `IMP-20`); read-only row validation and skip workflow (`IMP-5..6`, `AC-IMP-2`); exact, statement-level, within-file, and exact-date near-duplicate detection with occurrence-aware idempotency (`MVP-4`, `DUP-1..19`); advisory merchant-tag suggestions in review from active tags, explicit aliases and the customer's own history (`IMP-22`); atomic commit, Import History, statement hide/restore (preserving individual transaction hide flags), and committed-import deletion (`MVP-3`, `MVP-24` statement scope, `IMP-7..9`, `AC-IMP-7..8`, `AC-DEL-1`).
   - **Dependencies:** Requires Plan 1.
3. **Plan 3 — Transaction Workspace, Bulk Classification, and Hide/Restore (`docs/plans/03-transactions-and-classification.md`)**
   - **Scope:** Searchable, filterable, sortable, paginated Transaction List with visible count, hidden count, and filtered total (`MVP-5`, `TXN-1..3`, `TXN-7..9`, `TAG-5`); read-only Transaction Detail with category, multi-tag, advisory tag and category suggestions, and hide/restore controls (`MVP-6`, `TXN-4`, `TAG-2..3`, `TAG-8`); page-level and filter-wide (`select all matching`) bulk category assignment, non-destructive bulk tag add/remove, and bulk hide/restore with stale-edit concurrency protection and an auto-dismissed Applied status (`MVP-8`, `MVP-24` transaction scope, `TXN-5..6`, `TXN-10..12`, `TAG-4`, `AC-CAT-1..7`, `AC-TAG-1..8`, `AC-HIDE-1..5`).
   - **Dependencies:** Requires Plans 1 and 2.
4. **Plan 4 — Financial Overview, Bar-First Chart View, and Exploration (`docs/plans/04-overview-and-charts.md`)**
   - **Scope:** Overview Summary tab (`MVP-9`, `MVP-11`, `OVR-1..13`, `AC-OVR-1..6`) with period presets/custom ranges, gross directional `Money in`, `Money out`, `Net`, `Needs categorisation`, additive category breakdowns, overlapping merchant-tag rankings, prior-period comparisons (`7.3`), statement-period coverage/gap warnings (`OVR-7`), and metric dictionary disclosure; Overview Chart tab (`MVP-23`, `CHT-1..8`, `AC-CHT-1..5`) with bar-first horizontal and grouped vertical charts and accessible table alternatives; drill-down from every visual plus Category Detail (`MVP-15`), Merchant-Tag Detail (`MVP-22`, `TAG-6`), and Period Comparison (`MVP-16`) views (`MVP-10`, `EXP-1..8`).
   - **Dependencies:** Requires Plans 1, 2, and 3.

**Programme completion condition:** All four plans are implemented and verified against the backend battery (`dotnet format`, `dotnet test`, `dotnet build -c Release`) and frontend battery (`npm run lint`, `npm run test`, `npm run build`, `npm run test:e2e`) with customer isolation and reconciliation invariants holding across all views.

### 13.2 Instructions for Architect

The architecture agent should treat Section 1.1 and Sections 6, 7, and 9 as the product contract, Sections 4.9 and 4.10 as the sample-derived format contracts, Section 12.3 and 12.4 as the confirmed decisions and planning defaults, and Section 13.1 as the delivery programme topology. Preserve the existing account, session, admin, billing, and visual foundations described in Section 4.

## Pending production-PDF amendment (2026-11-05)

**DEFERRED — not approved (2026-11-06).** The operator deferred Standard Bank
PDF import to Thread #6. This amendment and Plan 2 revision 4 / C-PDF-2 remain
future planning input; they are not current release scope and must not be
implemented or treated as accepted until Thread #6 is explicitly activated.
The approved body above and the 2026-11-06 scope amendment remain authoritative.
This appendix does not claim implementation, private product acceptance, a new
review verdict or publication authority.

### Purpose and production inputs

The operator confirms that the two Standard Bank statements described in
Section 4.10 are intended production inputs, not test-only examples. Their
structural expectations remain 4 pages / 49 transactions / 66 continuation
lines and 18 pages / 297 transactions / 356 continuation lines. All pages,
including page 1, have the six-column table. They are 612×792 PDF 2.0 documents,
use Standard AES-256 V=5/R=6 encryption with an empty user password, carry the
same masthead raster asset on each page, and have complete first/later-page
account metadata and a terminal statement summary. The real source files and
all financial values remain private and are never repository fixtures.

The Plan's earlier synthetic-only/no-raster/all-encryption rejection policy
does not meet that production intent. Proposed revision 4 broadens the accepted
container/template, not the transaction meanings, ledger policy or privacy
rules. No actual date, amount, description, account value, address or filename
is included in this amendment.

### Proposed replacement protection wording

Replace **IMP-17** with:

> Reject PDFs requiring a nonempty **user password**, and unsupported or
> malformed protection, without prompting for passwords, guessing passwords,
> using owner access, external decryption or changing the original PDF.
> Support unencrypted PDFs and the recognized Standard Bank AES-256
> Standard-handler V=5/R=6 template only when the empty user password is
> independently verified before page extraction. Merely opening the document
> is insufficient because a library may accept an owner password first.
> No password input or password storage is introduced.

For this preset, the words **password-protected** in Section 1.1's negative
outcome, Section 4.10 item 7, Section 5.2 recovery, SEC-3, financial rule 20,
MVP-21, AC-IMP-16 and the password-protected error-table row mean a document
requiring a nonempty user password or outside the permitted protection profile.
They do not prohibit the approved empty-user container simply because an
encryption dictionary exists. Encrypted ZIP policy is unchanged.

Replace **AC-IMP-16** with:

> Given a PDF requiring a nonempty user password, accepting only empty-owner
> access, or carrying unsupported/malformed protection, upload rejects safely
> before any prepared review or committed financial effect. An approved
> empty-user AES-256 R6 production-template control succeeds without supplying
> a nonempty or owner password. No protection diagnostic or source value leaks.

**Separate explicit decision:** the two target documents disable the PDF
text-copy permission bit although they open with the empty user password.
Supporting them therefore permits the account import despite that owner-set
advisory restriction. It does not change permission bits, recover an owner
password, claim bank authenticity or grant backend ownership from PDF metadata.
Approval must explicitly include this decision; otherwise these exact files
remain unsupported. Customer/session/account authorization stays unchanged.

### Proposed replacement image/text and metadata wording

Clarify **IMP-16**, Section 4.10 items 1/6/7, financial rule 20 and
**AC-IMP-15** as follows:

> Every transaction in the supported template is supplied by its selectable,
> readable text layer. Image-only/scanned bodies and missing/unreadable or
> ambiguous text reject; no OCR or external service is used. A text-bearing
> page may carry only the specific privately confirmed nonfinancial masthead
> asset, with fixed encoded identity/interpretation and approved placement
> outside the transaction and summary areas. Size or apparent logo shape alone
> is not evidence. Unknown/replaced/relocated/additional images reject, even
> beneath a valid text header. The operator confirms the allowed asset and
> fixed boilerplate privately; customer images and values never become a
> learned allowlist.

> The real first-page and later-page metadata forms, Available balance and
> terminal statement-summary production are recognized by their complete
> labels, typed field slots, positions and order. Each production consumes only
> its own text. A title or Account label never permits skipping an arbitrary
> preamble, extra line or body text. Metadata and all Balance/summary values
> are neither returned, normalized into a description, persisted, logged nor
> used as transaction amount, fingerprint or account/currency authority.

Section 4.10's position rule is clarified to use the actual complete header and
table gutters on 612×792 pages, not midpoints between short header words. Long
description text may extend beyond the old midpoint boundary while remaining
inside its real description cell. Amounts are taken only from unambiguous flow
cells. Numeric description fragments are not amount candidates by value alone;
true overlap/uncertain separation rejects rather than borrowing Balance.
Payments and Bank fees remain outflows, Deposits inflows, and all exact-money,
sign, zero/multiple-flow and immutable-invalid-row rules remain unchanged.

### Private evidence and authority

Keep Section 12.1's no-real-sample-commit rule unchanged. Synthetic repository
fixtures reproduce the real protection, all-page layout, forms, decorative
asset policy and description geometry with invented values. Actual source
files, filenames, field/value manifests and diagnostic CSVs stay outside Git,
logs and HiveMind. Private comparisons must verify every row and continuation
through the published product child; row counts or earlier heuristic CSVs do
not establish fidelity. Durable evidence uses sample aliases, structural
counts, boolean comparisons, product/test identities and limits only.

All other approved outcomes and the four-child programme remain unchanged.
Thread #3 owns the correction; Thread #6 tracks the residual findings and is
not a newly scheduled programme child. The latest implementation NO-GO remains
in force. This proposal cannot close those findings or authorize work on
Threads #4/#5, a new review, implementation or publication by itself.
