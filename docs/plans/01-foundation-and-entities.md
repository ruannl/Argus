# Plan 1: Foundation, App Shell, and Financial Entities

- **Revision:** 1 (`2026-10-30`)
- **Status:** Specialist Plan Review Passed (`GO`) — Operator-approved; executed direct (Partner) — final acceptance review gate **PASS** (0 open findings)
- **Specialist Review Provenance:** Request `f70c4037-e38a-49a3-8153-4e616278495d` · Verdict `GO` (0 CRITICAL, 0 HIGH, 3 MEDIUM notes, 8 LOW notes disposed below) · Reviewed baseline `commit:0e7f1c8ab773a66c99c2e0ae96f4fe314c321a03/blob:5bcfb3122c484536d615819d1a5978e4c5c1a5c6` · Review artifact SHA-256 `6309ac5320a400effde300cf50ebcf6bbf7390183ad8dd1e5f57f9df8557d4ce`
- **Governing Programme:** HiveMind Thread #1 (`306f43f0-6ed8-43ea-8f75-0019027f8b06`, Requirement `e53f52af-c2ed-4098-ac75-2902426ee59a` Ready revision 1 `134e0580-8e55-4216-b883-9e9c6c466d9b`)
- **Governing Child Thread:** HiveMind Thread #2 (`780e38b1-7e15-420a-93f6-1f91f41f4ab2`, Requirement `eccf36da-2e35-4677-b4c5-ef2a51981343` Ready revision 1 `e0e2ef0f-0887-4932-bcf4-1a5acc79850c`)
- **Authoritative Product Brief:** `docs/product-brief.md` (commit `0e7f1c8`, Sections 1.1, 4, 6.1, 6.5, 6.8–6.10, 7.1, 8, 9.2, 9.4–9.5, 12.3–12.4, 13.1)

---

## 1. Desired Outcome and Current Behavior / Problem

### Current Behavior and Problem
Live-source inspection of `backend/` and `frontend/` confirms that the repository is currently an instantiated SaaS starter shell without any financial domain implementation:

- **Public identity and `/sample-page` scaffold (`MVP-18`):** `frontend/src/lib/landing-content.ts` and `frontend/src/app/page.tsx` still present starter-kit marketing copy (*"A complete starting point for your next SaaS product"*), link the primary call to action to `/sample-page` (`frontend/src/app/sample-page/page.tsx`), and `frontend/src/app/layout.tsx` sets `metadata.description` to *"Explore researched SaaS opportunities for people who want to build but do not know what to build."* Existing unit tests (`frontend/tests/app/page.test.tsx`, `frontend/tests/app/sample-page.test.tsx`) and Playwright smoke tests (`frontend/tests/e2e/smoke.spec.ts`) assert those scaffold strings and `/sample-page` navigation.
- **No authenticated app shell or navigation (`Section 6.1`, `SET-3`):** `frontend/src/app/home/page.tsx` renders a single customer account card (`AccountDetails`) with no navigation to `Overview`, `Transactions`, `Imports`, or `Settings`. `frontend/src/proxy.ts` only matches `["/home", "/home/:path*", "/admin", "/admin/:path*"]`.
- **No financial database schema, backend API, or Settings UI (`MVP-1`, `MVP-7`, `MVP-14`, `MVP-22` foundation):** `backend/Data/Db.cs` defines only four migrations (`0001_customer_accounts` through `0004_failed_login_lockout`) for `customers`, `sessions`, `email_verification_tokens`, and `password_reset_tokens`. `backend/Http/AccountEndpoints.cs` maps only `/health`, `/v1/me`, auth routes, and `/v1/billing/webhook`. No tables, store methods, endpoints, server actions, or UI screens exist for ZAR financial accounts, categories, or merchant tags.

### Desired Outcome
After executing Plan 1:

1. **Product Identity & Public Landing (`MVP-18`):** `frontend/src/lib/landing-content.ts`, `frontend/src/app/page.tsx`, and `frontend/src/app/layout.tsx` present Argus as a ZAR personal finance insights application featuring the tagline **"See where your money goes"** and the core value proposition **"Make sense of your money. See the patterns. Understand your financial behaviour."** The primary call to action routes to `/register` (with a unique accessible label distinct from the header navigation link). `/sample-page` and its dedicated test file are retired, and unit and Playwright tests verify the new landing experience and 404 retirement of `/sample-page`.
2. **Authenticated App Shell & Navigation (`Section 6.1`, `SET-3`, `UX-1`, `UX-2`, `UX-5`):** Authenticated users see a persistent, responsive, keyboard-accessible app shell (`Overview` at `/home`, `Transactions` at `/transactions`, `Imports` at `/imports`, and `Settings` at `/settings`) protected by `frontend/src/proxy.ts` and server-side session verification. Customer profile details (`AccountDetails`, email, verification status, subscription status, lifetime status, admin link when applicable, theme preference control, and logout) live in the **Profile** section of `Settings` (`SET-3`), while `Overview`, `Transactions`, and `Imports` render structured first-run empty states guiding the user to set up ZAR financial accounts and import statements.
3. **Financial Entities — Schema, Backend API, and Settings UI (`MVP-1`, `MVP-7`, `MVP-14`, `MVP-22` foundation, `SET-1`, `SET-2`, `CAT-1..7`, `TAG-1`, `TAG-7`, `SEC-1..5`, `AC-CAT-5..6`, `AC-TAG-1`, `AC-TAG-8`, `AC-SEC-1..3`):**
   - **PostgreSQL Schema (`0005_finance_foundation`):** Creates customer-scoped tables for `financial_accounts`, `categories`, `merchant_tags`, and the relational foundation (`statement_imports`, `transactions`, `transaction_merchant_tags`) plus `customers.categories_seeded_at` so category transaction counts, reassignment to `Uncategorised` on category deletion (`AC-CAT-6`), and tag unassignment on merchant-tag deletion (`AC-TAG-8`) operate on real relational tables and foreign keys from day one.
   - **ZAR Financial Accounts (`MVP-1`, `SET-1`):** Customers can create, list, rename (and update optional institution), archive, and restore financial accounts with fixed `ZAR` currency.
   - **Categories (`MVP-7`, `CAT-1..7`, `SET-2`, `AC-CAT-6`):** First use idempotently seeds the 19 default categories (`Groceries`, `Dining and takeaways`, `Transport`, `Housing and rent`, `Home loan`, `Levies`, `Utilities`, `Health`, `Education and school fees`, `Shopping`, `Entertainment`, `Subscriptions`, `Fees and charges`, `Travel`, `Other expenses`, `Salary`, `Other income`, `Transfers`, `Excluded`) with their respective visible kinds (`Expense`, `Income`, `Transfer`, `Excluded`) plus a non-deletable, immutable system `Uncategorised` bucket. Customers can create custom categories, rename categories (enforcing case-insensitive, whitespace-trimmed uniqueness per customer), change a category's kind after an explicit warning and confirmation (`CAT-3`), archive and restore categories, and delete non-system categories with confirmation that atomically reassigns any linked transactions to `Uncategorised` (`AC-CAT-6`).
   - **Merchant Tags (`MVP-22` foundation, `TAG-1`, `TAG-7`, `SET-2`, `AC-TAG-1`, `AC-TAG-8`):** Customers can create, list, rename, archive, restore, and delete workspace merchant tags with case-insensitive, whitespace-trimmed uniqueness per customer (`AC-TAG-1`), displaying assigned transaction counts (`TAG-7`) and atomically removing tag assignments from transactions on delete without deleting transactions (`AC-TAG-8`).
   - **Security & Privacy (`SEC-1`, `SEC-2`, `SEC-4`, `SEC-5`, `AC-SEC-1..3`):** Every `/v1/finance/*` route requires the trusted frontend application-secret header, origin check, and an active verified-subscriber session, scopes every SQL read and write by `customer_id`, returns an identical `404 {"error":"not_found"}` for unknown and foreign-owned entity IDs (`AC-SEC-1`), never logs user-defined financial account, institution, category, or merchant-tag names (`SEC-4`), and leaves `/admin` strictly customer-account-state only (`SEC-5`, `AC-SEC-3`).

---

## 2. Confirmed Decisions

### Observed Codebase Facts (from Live-Source Inspection)
1. **Backend architecture (`backend/Program.cs`, `backend/Data/Db.cs`, `backend/Data/Store.cs`, `backend/Http/HttpSupport.cs`, `backend/Http/AccountEndpoints.cs`):**
   - ASP.NET Core on `net10.0` using raw SQL over `Npgsql` (`10.0.3`) with embedded migrations in `Migrations.All` inside `backend/Data/Db.cs`.
   - JSON serialization uses `JsonNamingPolicy.SnakeCaseLower`. Request parsing via `JsonBody.ReadAsync<T>` enforces `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`, `PropertyNameCaseInsensitive = false`, and `JsonUnmappedMemberHandling.Disallow`.
   - `Program.cs` currently configures CORS with `.WithMethods("GET", "POST")` and logs `http_request method={Method} path={Path} status={Status} elapsed_ms={ElapsedMs}` on every request.
   - `backend/tests/argus.Server.Tests/ApiTestBase.cs` truncates tables between xUnit isolation tests via `TRUNCATE customers, sessions, email_verification_tokens, password_reset_tokens CASCADE` and provides `Get` and `Post` request helpers.
2. **Frontend architecture (`frontend/src/auth.ts`, `frontend/src/proxy.ts`, `frontend/src/lib/server/backend-client.ts`, `frontend/src/app/home/page.tsx`):**
   - Next.js `16.4.3` App Router, React `19.2.8`, Tailwind 4 (`frontend/src/app/globals.css`), Auth.js `5.0.0-beta.32` wrapping the backend opaque session token in `session.user.backendToken`.
   - `frontend/src/lib/server/backend-client.ts` is `"server-only"` and attaches `X-Request-Id`, `X-${PRODUCT_TOKEN}-Application-Secret`, `X-${PRODUCT_TOKEN}-Session`, and `X-${PRODUCT_TOKEN}-Frontend-Origin`.
   - In `.github/workflows/ci.yml` and `frontend/playwright.config.ts`, the `frontend` CI job and Playwright `webServer` run `npm run build && npm run start -- --hostname 127.0.0.1 --port 3100` with `ARGUS_AUTH_SECRET` only and **without** a running backend or PostgreSQL instance. Therefore, Playwright tests in `smoke.spec.ts` must test public routes, security headers, `/sample-page` retirement, and unauthenticated route redirect guards (`proxy.ts`), while authenticated workspace and Settings interactions are verified through Vitest component/page/action tests and backend xUnit PostgreSQL isolation tests.

### Confirmed Operator and Brief Decisions
- **Workspace currency:** Fixed `ZAR` across all financial accounts (`MVP-1`, `SET-1`, `Section 12.3`). No currency selector or FX conversion exists in the MVP.
- **Terminology separation (`Section 4.5`, `Section 7.1`):** Customer login/subscription identity is called **Profile** in the UI; bank and credit-card accounts are called **Accounts** (or **Financial accounts**).
- **Four-destination navigation (`Section 6.1`):** `Overview` (`/home`), `Transactions` (`/transactions`), `Imports` (`/imports`), and `Settings` (`/settings`). Summary and Charts are tabs within `Overview` (`/home`).
- **Default categories and `Uncategorised` (`CAT-1..7`):** The 19 default categories (`Groceries`, `Dining and takeaways`, `Transport`, `Housing and rent`, `Home loan`, `Levies`, `Utilities`, `Health`, `Education and school fees`, `Shopping`, `Entertainment`, `Subscriptions`, `Fees and charges`, `Travel`, `Other expenses`, `Salary`, `Other income`, `Transfers`, `Excluded`) are seeded idempotently on first use per customer, alongside a non-deletable system `Uncategorised` bucket.
- **Uniqueness rules (`TAG-1`, `AC-TAG-1`, Thread #2 Requirement):** Category names and merchant-tag names are case-insensitive, whitespace-trimmed unique per customer.
- **Deletion vs. archiving (`SET-1`, `CAT-2`, `TAG-1`, `TAG-7`, `AC-CAT-6`, `AC-TAG-8`):**
  - Financial accounts support create, rename/update institution, archive, and restore (never delete).
  - Categories support create, rename, change kind (with explicit warning and confirmation), archive, restore, and delete (for non-system categories only; confirmation required; atomically reassigns any transactions in that category to the customer's `Uncategorised` bucket).
  - Merchant tags support create, rename, archive, restore, and delete (confirmation required; atomically removes tag assignments from transactions without deleting transactions).

### Assumptions and Constraints
- **Constraint (`SEC-4`):** No user-defined financial account name, institution name, category name, or merchant-tag name may ever be written to application logs or `audit.*` events, nor placed in URL path segments (since `Program.cs` logs `context.Request.Path.Value`).
- **Constraint (`SEC-1`, `AC-SEC-1`):** Every finance query and mutation must filter by `customer_id = @customer_id` in SQL. Missing and foreign-owned entity IDs must return the identical `404 {"error":"not_found"}` response.
- **Constraint (`SEC-5`, `AC-SEC-3`):** `/admin` remains strictly customer-account-state only (`AccountDetails` for the admin's own `/v1/me` record) and never queries or displays financial entities.

---

## 3. Explicit Scope and Non-Goals

### In Scope for Plan 1
1. Database migration `0005_finance_foundation` in `backend/Data/Db.cs` defining `financial_accounts`, `categories`, `merchant_tags`, and the relational foundation tables `statement_imports`, `transactions`, and `transaction_merchant_tags`, plus `customers.categories_seeded_at`.
2. Backend persistence (`backend/Data/FinanceStore.cs`) and HTTP surface (`backend/Http/FinanceEndpoints.cs`) for `/v1/finance/accounts`, `/v1/finance/categories`, and `/v1/finance/merchant-tags`, including idempotent first-use category seeding and `Seed.cs` integration.
3. Public landing page and metadata rewrite (`frontend/src/lib/landing-content.ts`, `frontend/src/app/page.tsx`, `frontend/src/app/layout.tsx`) with tagline *"See where your money goes"*, retirement of `frontend/src/app/sample-page/page.tsx` and `frontend/tests/app/sample-page.test.tsx`.
4. Authenticated app shell (`frontend/src/components/app-shell.tsx`), route protection in `frontend/src/proxy.ts`, first-run empty states on `/home` (`Overview`), `/transactions`, and `/imports`, and the complete `Settings` workspace (`/settings`) for **Accounts**, **Categories**, **Merchant tags**, and **Profile** (`AccountDetails`, admin link, theme toggle, logout).
5. Backend xUnit PostgreSQL isolation tests, frontend Vitest unit/component/action tests, Playwright e2e smoke test updates, and `docs/repository-layout.md` update.

### Out of Scope (Deferred to Plans 2–4 or Excluded from MVP)
- Statement file upload, FNB CSV/ZIP parser, Standard Bank six-column PDF parser, generic CSV mapping, row validation/skip review, duplicate/idempotency logic, and Import History UI (`Thread #3` / `Plan 2`).
- Transaction list search/filtering/sorting/pagination, transaction detail drawer/page, single and bulk category/tag assignment, tag suggestions, and transaction hide/restore UI (`Thread #4` / `Plan 3`).
- Overview metric calculations, bar-first Chart tab visualizations, period comparison calculations, statement coverage gap warnings, and Category/Tag Detail drill-down screens (`Thread #5` / `Plan 4`).
- Whole-customer account deletion, data export, multi-currency/FX, OCR for scanned PDFs, account balance importing/display, automatic categorisation, budgets, and billing checkout UI (`Section 8.5`).

---

## 4. Target Invariants and Architecture

### Target Invariants
1. **INV-1 (Customer Isolation & Non-Enumeration — Stop Rule 2, `SEC-1`, `AC-SEC-1`, `AC-SEC-2`):** Every finance table row is owned by a `customer_id`. Every SQL `SELECT`, `INSERT`, `UPDATE`, and `DELETE` in `FinanceStore` predicates on `customer_id = @customer_id`. Requests without a valid application secret, valid origin, and active verified-subscriber session fail closed (`401` or `403`). Requests targeting a nonexistent `Guid` or another customer's `Guid` take the same code path and return `404 {"error":"not_found"}` with zero state mutation.
2. **INV-2 (Single Canonical `Uncategorised` Identity — Stop Rule 1, `CAT-1`, `AC-CAT-6`):** Each customer has exactly one system `Uncategorised` row in `categories` (`is_system = TRUE`, `normalized_name = 'uncategorised'`, `kind = 'Uncategorised'`, `is_archived = FALSE`), enforced by a partial unique index `UNIQUE (customer_id) WHERE is_system = TRUE` and `UNIQUE (customer_id, normalized_name)`. Every row in `transactions` has a non-null `category_id UUID NOT NULL REFERENCES categories(id) ON DELETE RESTRICT`. Uncategorised transactions point directly to the customer's system `Uncategorised` category `id`—never `NULL`—eliminating any dual representation between `NULL` and a system row.
3. **INV-3 (Idempotent First-Use Category Seeding Without Resurrection — Stop Rule 3, `CAT-1`):** `FinanceStore.EnsureCategoriesSeededAsync(customerId)` locks the customer row (`SELECT categories_seeded_at FROM customers WHERE id = @customer_id FOR UPDATE`) when `categories_seeded_at IS NULL`, inserts `Uncategorised` plus the 19 default categories with `ON CONFLICT (customer_id, normalized_name) DO NOTHING`, and sets `categories_seeded_at = NOW()`. Once `categories_seeded_at` is set, subsequent renames or deletions of default categories are never overwritten or resurrected by later reads or restarts.
4. **INV-4 (Atomic Category Deletion with Reassignment & Atomic Tag Deletion — Stop Rule 4, `CAT-2`, `TAG-7`, `AC-CAT-6`, `AC-TAG-8`):**
   - Deleting a category (`is_system = FALSE`) executes inside a single PostgreSQL transaction: locks the target category row for `customer_id`, resolves the customer's `Uncategorised` category `id`, updates all transactions with `category_id = @deleted_id` to `category_id = @uncategorised_id` (incrementing `version` and `updated_at`), and deletes the category row. No transaction is ever deleted.
   - Deleting a merchant tag executes inside a single PostgreSQL transaction: locks the target tag row for `customer_id`, removes rows from `transaction_merchant_tags` for that tag (incrementing `version` and `updated_at` on affected transactions), and deletes the tag row. No transaction is ever deleted.
5. **INV-5 (Case-Insensitive, Whitespace-Trimmed Name Uniqueness — `TAG-1`, `AC-TAG-1`):** For both `categories` and `merchant_tags`, input names are trimmed of leading/trailing whitespace and internal whitespace runs are collapsed to a single space (`DisplayName`), and `NormalizedName = DisplayName.ToLowerInvariant()`. PostgreSQL enforces `UNIQUE (customer_id, normalized_name)` on both tables. Collisions on create or rename return `409 Conflict` with `{"error":"duplicate_name"}`.
6. **INV-6 (Privacy-Safe Logging & Admin Exclusion — `SEC-4`, `SEC-5`, `AC-SEC-3`):** No route URL, log message, or `audit.*` event includes `display_name` or `institution`. Audit events log only `customer_id`, entity `id`, `kind` (for categories), and integer counts. `/admin` renders only `AccountDetails` from `/v1/me`.

---

## 5. Authoritative Sources of Identity, Configuration, State, and Policy

| Concern | Authoritative Source | Details |
| --- | --- | --- |
| Product identity & protocol headers | `backend/Brand.cs` and `frontend/src/lib/brand.ts` | `PRODUCT_NAME = "Argus"`, `PRODUCT_TOKEN`, `PRODUCT_SLUG = "argus"`, `EnvPrefix = "ARGUS_"`. |
| Public marketing copy | `frontend/src/lib/landing-content.ts` | Single source for landing tagline, value proposition, feature highlights, how-it-works steps, CTA labels, and forbidden phrases. |
| Caller & session authority | `backend/Http/HttpSupport.cs` (`Trust`) + `backend/Data/Store.cs` | `Trust.OriginValid`, `Trust.ApplicationSecretValid`, `Trust.SessionToken` + `Store.ValidateAndTouchSessionAsync` + verified active subscriber check (`EmailVerifiedAt != null && (SubscriberActivatedAt != null || IsLifetime)`). |
| Financial entity state | PostgreSQL via `backend/Data/Db.cs` and `backend/Data/FinanceStore.cs` | `financial_accounts`, `categories`, `merchant_tags`, `statement_imports`, `transactions`, `transaction_merchant_tags`. |
| Category kinds | `FinanceStore.AllowedCategoryKinds` + DB `CHECK` constraint | User-assignable kinds: `"Expense"`, `"Income"`, `"Transfer"`, `"Excluded"`. System bucket kind: `"Uncategorised"` (`is_system = TRUE` only). |

---

## 6. Important Data / Control Flow and Lifecycle Transitions

### 6.1 Authenticated Session and Finance Request Guard
1. Next.js Server Component (`/home`, `/transactions`, `/imports`, `/settings`) or Server Action (`finance-actions.ts`) reads `const session = await auth()` to obtain `backendToken`. If absent, redirects to `/login` (or returns unauthorized action state).
2. `backend-client.ts` sends the HTTP request to `ARGUS_BACKEND_URL` with `X-${PRODUCT_TOKEN}-Application-Secret`, `X-${PRODUCT_TOKEN}-Session`, `X-${PRODUCT_TOKEN}-Frontend-Origin`, and `X-Request-Id`.
3. `FinanceApi.AuthenticateAsync(HttpContext, CancellationToken)` in `backend/Http/FinanceEndpoints.cs`:
   - Checks `Trust.OriginValid(context, settings)` -> returns `403` (`Errors.OriginRejected()`) on mismatch.
   - Checks `Trust.ApplicationSecretValid(context, settings)` -> returns `401` (`Errors.Unauthorized()`) on mismatch.
   - Extracts `Trust.SessionToken(context)` and calls `store.ValidateAndTouchSessionAsync(Tokens.HashToken(rawToken), cancellationToken)` -> returns `401` if null.
   - Loads `store.FindCustomerByIdAsync(customerId.Value, cancellationToken)` and verifies `customer is not null && customer.EmailVerifiedAt is not null && (customer.SubscriberActivatedAt is not null || customer.IsLifetime)` -> returns `401` if not satisfied.
   - Checks per-IP rate limit (`rateLimiter.CheckIp(peerIp)` when `peerIp` is non-null) -> returns `429` (`Errors.RateLimited()`) if exceeded.
   - Returns the authenticated `Guid customerId`.

### 6.2 Category Lifecycle and First-Use Seeding
```text
Customer created / logs in
   │
   ▼
First category read or mutation (or Seed.RunAsync)
   │
   ├─► EnsureCategoriesSeededAsync(customerId)
   │     ├─ Fast check: SELECT categories_seeded_at FROM customers WHERE id = @id
   │     └─ If NULL: BEGIN TX -> SELECT ... FOR UPDATE
   │          ├─ Insert system 'Uncategorised' (is_system=true, is_default=true, kind='Uncategorised', sort_order=0)
   │          ├─ Insert 19 default categories (is_system=false, is_default=true, sort_order=1..19)
   │          └─ UPDATE customers SET categories_seeded_at = NOW() -> COMMIT
   ▼
Active Category (is_system = false, is_archived = false)
   ├── Rename (PATCH) ──────────────────────► Active Category (updated display_name & normalized_name)
   ├── Change Kind (PATCH + confirm=true) ──► Active Category (updated kind)
   ├── Archive (POST .../archive) ──────────► Archived Category (is_archived = true, archived_at = NOW())
   │                                              │
   │                                              └── Restore (POST .../restore) ──► Active Category
   └── Delete (DELETE) ─────────────────────► Atomic TX:
                                                1. UPDATE transactions SET category_id = uncategorised_id
                                                2. DELETE FROM categories WHERE id = @id
                                                3. Category removed; transactions preserved in Uncategorised
```

### 6.3 Financial Account and Merchant Tag Lifecycles
- **Financial Account:** `Active (is_archived = false, currency = 'ZAR')` $\leftrightarrow$ `Archived (is_archived = true)` via `/archive` and `/restore`; rename and optional `institution` update via `PATCH /v1/finance/accounts/{id}` in either state. Never deleted (`SET-1`).
- **Merchant Tag:** `Active (is_archived = false)` $\leftrightarrow$ `Archived (is_archived = true)` via `/archive` and `/restore`; rename via `PATCH /v1/finance/merchant-tags/{id}` enforcing case-insensitive trimmed uniqueness; permanent removal via `DELETE /v1/finance/merchant-tags/{id}`, which atomically deletes matching `transaction_merchant_tags` rows and increments `version` on affected `transactions` rows while preserving the transactions themselves (`TAG-7`, `AC-TAG-8`).

---

## 7. Concrete Phase Boundaries, Dependencies, and Ordering

Phases are sequential and execution-agnostic.

### Phase 1: Backend Schema, Idempotent Category Seeding, and Customer-Scoped Finance API
- **Dependencies:** None.
- **Purpose:** Establish migration `0005_finance_foundation`, `FinanceStore`, `FinanceApi` endpoints (`/v1/finance/accounts`, `/v1/finance/categories`, `/v1/finance/merchant-tags`), dev seed integration, and PostgreSQL isolation tests.
- **Scope:**
  1. **Migration `0005_finance_foundation` in `backend/Data/Db.cs`:**
     - Alter `customers` to add `categories_seeded_at TIMESTAMPTZ NULL`.
     - Create `financial_accounts`:
       - `id UUID PRIMARY KEY`
       - `customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE`
       - `display_name TEXT NOT NULL`
       - `institution TEXT NULL`
       - `currency TEXT NOT NULL DEFAULT 'ZAR' CHECK (currency = 'ZAR')`
       - `is_archived BOOLEAN NOT NULL DEFAULT FALSE`
       - `archived_at TIMESTAMPTZ NULL`
       - `created_at TIMESTAMPTZ NOT NULL`
       - `updated_at TIMESTAMPTZ NOT NULL`
       - Index: `financial_accounts_customer_id_idx ON financial_accounts(customer_id)`
     - Create `categories`:
       - `id UUID PRIMARY KEY`
       - `customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE`
       - `display_name TEXT NOT NULL`
       - `normalized_name TEXT NOT NULL`
       - `kind TEXT NOT NULL`
       - `is_system BOOLEAN NOT NULL DEFAULT FALSE`
       - `is_default BOOLEAN NOT NULL DEFAULT FALSE`
       - `is_archived BOOLEAN NOT NULL DEFAULT FALSE`
       - `archived_at TIMESTAMPTZ NULL`
       - `sort_order INT NOT NULL DEFAULT 100`
       - `created_at TIMESTAMPTZ NOT NULL`
       - `updated_at TIMESTAMPTZ NOT NULL`
       - Constraint: `UNIQUE (customer_id, normalized_name)`
       - Constraint: `CHECK ((is_system = TRUE AND kind = 'Uncategorised' AND is_archived = FALSE) OR (is_system = FALSE AND kind IN ('Income', 'Expense', 'Transfer', 'Excluded')))`
       - Partial unique index: `CREATE UNIQUE INDEX categories_customer_system_uidx ON categories(customer_id) WHERE is_system = TRUE;`
       - Index: `categories_customer_id_idx ON categories(customer_id)`
     - Create `merchant_tags`:
       - `id UUID PRIMARY KEY`
       - `customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE`
       - `display_name TEXT NOT NULL`
       - `normalized_name TEXT NOT NULL`
       - `is_archived BOOLEAN NOT NULL DEFAULT FALSE`
       - `archived_at TIMESTAMPTZ NULL`
       - `created_at TIMESTAMPTZ NOT NULL`
       - `updated_at TIMESTAMPTZ NOT NULL`
       - Constraint: `UNIQUE (customer_id, normalized_name)`
       - Index: `merchant_tags_customer_id_idx ON merchant_tags(customer_id)`
     - Create relational foundation tables `statement_imports`, `transactions`, and `transaction_merchant_tags`:
       - `statement_imports`: `id UUID PRIMARY KEY`, `customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE`, `financial_account_id UUID NOT NULL REFERENCES financial_accounts(id) ON DELETE RESTRICT`, `source_type TEXT NOT NULL`, `statement_label TEXT NOT NULL`, `statement_fingerprint TEXT NOT NULL`, `start_date DATE NOT NULL`, `end_date DATE NOT NULL`, `total_rows INT NOT NULL DEFAULT 0`, `imported_count INT NOT NULL DEFAULT 0`, `suppressed_count INT NOT NULL DEFAULT 0`, `skipped_count INT NOT NULL DEFAULT 0`, `is_hidden BOOLEAN NOT NULL DEFAULT FALSE`, `hidden_at TIMESTAMPTZ NULL`, `created_at TIMESTAMPTZ NOT NULL`, `updated_at TIMESTAMPTZ NOT NULL`.
       - `transactions`: `id UUID PRIMARY KEY`, `customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE`, `financial_account_id UUID NOT NULL REFERENCES financial_accounts(id) ON DELETE RESTRICT`, `statement_import_id UUID NOT NULL REFERENCES statement_imports(id) ON DELETE CASCADE`, `transaction_date DATE NOT NULL`, `description TEXT NOT NULL`, `normalized_description TEXT NOT NULL`, `amount NUMERIC(18, 2) NOT NULL`, `category_id UUID NOT NULL REFERENCES categories(id) ON DELETE RESTRICT`, `source_type TEXT NOT NULL`, `source_page INT NULL`, `source_row_number INT NOT NULL DEFAULT 1`, `identity_key TEXT NOT NULL`, `occurrence_index INT NOT NULL DEFAULT 1`, `is_hidden_by_user BOOLEAN NOT NULL DEFAULT FALSE`, `version INT NOT NULL DEFAULT 1`, `created_at TIMESTAMPTZ NOT NULL`, `updated_at TIMESTAMPTZ NOT NULL`. Indexes on `(customer_id, category_id)` and `(customer_id, financial_account_id)`.
       - `transaction_merchant_tags`: `transaction_id UUID NOT NULL REFERENCES transactions(id) ON DELETE CASCADE`, `merchant_tag_id UUID NOT NULL REFERENCES merchant_tags(id) ON DELETE CASCADE`, `assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW()`, `PRIMARY KEY (transaction_id, merchant_tag_id)`, index on `(merchant_tag_id)`.
  2. **`backend/Data/FinanceStore.cs`:**
     - Name normalisation helper `NormalizeName(string? input, int maxRunes = 80)`: trims leading/trailing whitespace, collapses consecutive internal whitespace to a single space (`' '`), rejects empty strings, strings with control characters, or strings exceeding `maxRunes` Unicode scalar values, and returns `(string DisplayName, string NormalizedName)` where `NormalizedName = DisplayName.ToLowerInvariant()`.
     - Optional institution normalisation `NormalizeInstitution(string? input, int maxRunes = 80)`: returns `null` when null/whitespace, otherwise applies the same whitespace collapse and length/control-character validation.
     - Seed definitions (`DefaultCategories`):
       - `("Uncategorised", "Uncategorised", IsSystem: true, SortOrder: 0)`
       - `("Groceries", "Expense", false, 1)`
       - `("Dining and takeaways", "Expense", false, 2)`
       - `("Transport", "Expense", false, 3)`
       - `("Housing and rent", "Expense", false, 4)`
       - `("Home loan", "Expense", false, 5)`
       - `("Levies", "Expense", false, 6)`
       - `("Utilities", "Expense", false, 7)`
       - `("Health", "Expense", false, 8)`
       - `("Education and school fees", "Expense", false, 9)`
       - `("Shopping", "Expense", false, 10)`
       - `("Entertainment", "Expense", false, 11)`
       - `("Subscriptions", "Expense", false, 12)`
       - `("Fees and charges", "Expense", false, 13)`
       - `("Travel", "Expense", false, 14)`
       - `("Other expenses", "Expense", false, 15)`
       - `("Salary", "Income", false, 16)`
       - `("Other income", "Income", false, 17)`
       - `("Transfers", "Transfer", false, 18)`
       - `("Excluded", "Excluded", false, 19)`
     - Store operations for Financial Accounts (`ListAccountsAsync`, `CreateAccountAsync`, `UpdateAccountAsync`, `SetAccountArchivedAsync`), Categories (`EnsureCategoriesSeededAsync`, `ListCategoriesAsync`, `CreateCategoryAsync`, `UpdateCategoryAsync`, `SetCategoryArchivedAsync`, `DeleteCategoryAsync`), and Merchant Tags (`ListMerchantTagsAsync`, `CreateMerchantTagAsync`, `UpdateMerchantTagAsync`, `SetMerchantTagArchivedAsync`, `DeleteMerchantTagAsync`).
     - Catch PostgreSQL unique constraint violation (`PostgresException` with `SqlState == PostgresErrorCodes.UniqueViolation`, `"23505"`) on category and merchant-tag insert/update and map cleanly to `DuplicateName` outcome so concurrent duplicate writes never surface as `500`.
  3. **`backend/Http/HttpSupport.cs` and `backend/Http/FinanceEndpoints.cs`:**
     - Add error helpers in `Errors`:
       - `NotFound()` -> `404 {"error":"not_found"}`
       - `DuplicateName()` -> `409 {"error":"duplicate_name"}`
       - `SystemCategoryImmutable()` -> `400 {"error":"system_category_immutable"}`
       - `KindChangeConfirmationRequired()` -> `400 {"error":"kind_change_confirmation_required"}`
     - Implement `FinanceApi` in `backend/Http/FinanceEndpoints.cs` and register/map it in `backend/Program.cs` (updating CORS `.WithMethods("GET", "POST", "PATCH", "DELETE")`).
     - Update `backend/Seed.cs` to call `FinanceStore.EnsureCategoriesSeededAsync` for seeded dev accounts.
  4. **`backend/tests/argus.Server.Tests/ApiTestBase.cs` and `FinanceContractTests.cs`:**
     - Add `Patch` and `Delete` request builders to `ApiTestBase.cs`.
     - Author `FinanceContractTests.cs` testing all positive, negative, customer-isolation, concurrency, and log-privacy requirements.
  5. **Engineer-owned Phase 1 review-note dispositions (from Specialist review `f70c4037-e38a-49a3-8153-4e616278495d`):**
     - **N1 (MEDIUM — Canary log test on error paths, `SEC-4`):** Never pass raw `PostgresException` objects or user-supplied parameter values to `_logger.LogError` / `_logger.LogWarning` (log `SqlState` and error class only, because PostgreSQL `23505`/`23514` `Detail` embeds the offending normalized name). Extend Scenario 9 in `FinanceContractTests.cs` to assert zero `"Canary"` log entries across rejected duplicate creates/renames (`23505`), system-category mutation attempts (`23514`), and invalid-name `400` responses in addition to success paths.
     - **N2 (MEDIUM — Explicit under-lock recheck in `EnsureCategoriesSeededAsync`, `INV-3`):** After beginning the transaction and executing `SELECT categories_seeded_at FROM customers WHERE id = @customer_id FOR UPDATE`, explicitly re-check `categories_seeded_at IS NOT NULL` and exit/commit immediately without inserting when another concurrent request already seeded the customer. Add an isolation test that re-invokes `EnsureCategoriesSeededAsync` after renaming `"Groceries"` -> `"Supermarket"` and deleting `"Travel"` and verifies neither default is resurrected.
     - **N3 (MEDIUM — `PATCH /v1/finance/accounts/{id}` `institution` partial-update semantics):** Define and enforce one rule: omitting `"institution"` from the `PATCH` JSON object preserves the existing stored `institution`, whereas passing `"institution": null` or `"institution": ""` clears it to `NULL`. Add contract tests in `FinanceContractTests.cs` for both rename-only (`institution` omitted -> preserved) and explicit clear (`"institution": null` -> cleared).
     - **N4 (LOW — Idempotent archive/restore re-entry):** Archiving an already-archived account, category, or merchant tag, or restoring an already-active one, returns an idempotent `200 OK` with the current record (preserving `archived_at` when already archived). Add contract tests for repeated `/archive` and repeated `/restore`.
     - **N5 (LOW — Same-customer composite foreign key defense-in-depth in `0005`):** Add `UNIQUE (customer_id, id)` on `financial_accounts`, `categories`, and `statement_imports`, and reference `(customer_id, financial_account_id)`, `(customer_id, category_id)`, and `(customer_id, statement_import_id)` from `transactions` (and `(customer_id, financial_account_id)` from `statement_imports`) so cross-customer foreign-key references are impossible at the PostgreSQL constraint level as well as in `FinanceStore` queries.
     - **N6 (LOW — Map PostgreSQL `23514` check violation on `categories`):** Catch `PostgresException` with `SqlState == PostgresErrorCodes.CheckViolation` (`"23514"`) in `FinanceStore` category mutations and map to `SystemCategoryImmutable` (`400 {"error":"system_category_immutable"}`) or `InvalidRequest` (`400 {"error":"invalid_request"}`) so concurrent races never fall through to `500`, and cover with a test.
     - **N7 (LOW — Dedicated finance rate-limit bucket):** Add `rateLimiter.CheckFinanceIp(peerIp)` in `backend/Security/RateLimiter.cs` (keyed `finance_ip:{ip}` with a 300 requests / 15-minute sliding window) so multi-fetch workspace/Settings navigation does not consume or exhaust the 120-request `ip:{ip}` login/auth brute-force bucket.
     - **N8 (LOW — Explicit guard error codes across all contract table rows):** Ensure all `/v1/finance/*` routes enforce and test `401 unauthorized`, `403` (bodyless origin rejection), and `429 rate_limited` uniformly across `GET`, `POST`, `PATCH`, and `DELETE`.
     - **N10 (LOW — Align verified-subscriber gate with `LoginAsync`):** Align the finance session guard with `AccountEndpoints.LoginAsync` (`customer.EmailVerifiedAt is not null && customer.SubscriberActivatedAt is not null`, which holds for all verified active subscribers including seeded lifetime dev accounts since `Seed.cs` sets `subscriber_activated_at = NOW()`), keeping entitlement enforcement identical between login and finance access.
- **Verification Commands (Phase 1):**
  ```bash
  cd backend
  dotnet format argus.Server.csproj --verify-no-changes
  dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
  dotnet test tests/argus.Server.Tests
  dotnet build -c Release
  ```

### Phase 2: Product Identity, Landing Page Rewrite, `/sample-page` Retirement, and Authenticated App Shell
- **Dependencies:** Phase 1.
- **Purpose:** Replace starter-kit landing copy and metadata with Argus personal finance identity (`MVP-18`), retire `/sample-page`, protect all workspace routes in `proxy.ts`, and build the persistent authenticated `AppShell` with `Overview` (`/home`), `Transactions` (`/transactions`), and `Imports` (`/imports`) first-run empty states.
- **Scope:**
  1. **`frontend/src/lib/landing-content.ts`, `frontend/src/app/layout.tsx`, `frontend/src/app/page.tsx`:**
     - Export `PRODUCT_TAGLINE = "See where your money goes"`, `AUDIENCE_OUTCOME_LEAD = "See where your money goes."`, `AUDIENCE_OUTCOME_DETAIL = "Make sense of your money. See the patterns. Understand your financial behaviour. Import your South African ZAR bank statements, classify transactions by category and merchant tag, and explore period overviews and bar charts that reconcile to every transaction."`, `PRIMARY_CTA_LABEL = "Start with your statements"`, `FEATURE_HIGHLIGHTS`, `HOW_IT_WORKS_STEPS`, `STATUS_EXAMPLE = "ZAR workspace · Manual statement import · Reconciled to every transaction"`, and `FORBIDDEN_UI_PHRASES = ["Subscribe", "Pricing", "get rich", "guaranteed", "10/month", "SaaS starter", "sample page"]`.
     - Update `frontend/src/app/layout.tsx` `metadata.description` to `"See where your money goes. Make sense of your money, see the patterns, and understand your financial behaviour with ZAR bank statement insights."`
     - Update `frontend/src/app/page.tsx` hero badge, callout card, section headings, and bottom CTA section so the primary CTA (`PRIMARY_CTA_LABEL`) links to `href="/register"` and secondary CTA links to `href="/login"`.
     - Delete `frontend/src/app/sample-page/page.tsx` and `frontend/tests/app/sample-page.test.tsx`.
  2. **`frontend/src/proxy.ts` and `frontend/src/components/app-shell.tsx`:**
     - Update `frontend/src/proxy.ts` `config.matcher` to `["/home", "/home/:path*", "/transactions", "/transactions/:path*", "/imports", "/imports/:path*", "/settings", "/settings/:path*", "/admin", "/admin/:path*"]`.
     - Create `frontend/src/components/app-shell.tsx`:
       - Accepts `account: CurrentAccount`, `activeNav: "overview" | "transactions" | "imports" | "settings"`, `title: string`, `eyebrow: string`, `description?: string`, and `children: ReactNode`.
       - Renders header with `PRODUCT_NAME`, `"ZAR workspace"` pill, `<nav aria-label="Workspace">` containing links to `Overview` (`/home`), `Transactions` (`/transactions`), `Imports` (`/imports`), and `Settings` (`/settings`) with `aria-current="page"` on the active item, plus conditional `Admin` link (`href="/admin"`) when `account.admin` is `true` and `Log out` button (`form action={endSessionAction}`).
       - Responsive layout (`320px–375px` mobile wrapping nav bar without horizontal overflow; desktop top bar).
  3. **`frontend/src/app/home/page.tsx`, `frontend/src/app/transactions/page.tsx`, `frontend/src/app/imports/page.tsx`:**
     - Refactor `/home` to render `AppShell` (`activeNav="overview"`) with the Overview `Summary` and `Charts` view selector tabs (`Section 6.1`) and first-run empty state guiding the user to create a ZAR financial account in `/settings` and import a statement in `/imports`.
     - Create `/transactions` and `/imports` pages with the same fail-closed session check (`auth()` + `getCurrentAccount(backendToken)` + `<ClearSession />` on failure) and first-run empty states.
  4. **Unit and E2E tests (`frontend/tests/app/page.test.tsx`, `frontend/tests/app/home/page.test.tsx`, `frontend/tests/app/transactions/page.test.tsx`, `frontend/tests/app/imports/page.test.tsx`, `frontend/tests/e2e/smoke.spec.ts`):**
     - Update `page.test.tsx` and `home/page.test.tsx`; add unit tests for `/transactions` and `/imports`.
     - Update `frontend/tests/e2e/smoke.spec.ts` in the same phase as `landing-content.ts` (since `tsconfig.json` includes `**/*.ts` during `npm run build`) to remove retired `SAMPLE_PAGE_CONTENT` / `ILLUSTRATIVE_SAMPLE_LABEL` imports, assert the Argus finance landing page and `/register` primary CTA, assert `/sample-page` returns 404, and assert unauthenticated redirects to `/login` on `/home`, `/transactions`, `/imports`, and `/admin`.
  5. **Engineer-owned Phase 2 review-note disposition (from Specialist review `f70c4037-e38a-49a3-8153-4e616278495d`):**
     - **N9 (LOW — Honest first-run empty state copy on `/home`, `/transactions`, and `/imports`):** Keep the Phase 2 empty states truthful to current capabilities (§7.7 insight honesty): the `Summary` / `Charts` header on `/home` and the empty state on `/imports` guide the user to configure their ZAR financial accounts, categories, and merchant tags in `Settings` (`/settings`) first and state clearly that statement file upload will appear in `Imports` once accounts are ready, without rendering non-functional upload controls.
- **Verification Commands (Phase 2):**
  ```bash
  cd frontend
  npm run lint
  npm run test
  npm run build
  ```

### Phase 3: Frontend Finance Client, Server Actions, Settings Workspace (`SET-1..3`), and End-to-End Verification
- **Dependencies:** Phases 1 and 2.
- **Purpose:** Wire the server-only backend client and server actions for financial accounts, categories, and merchant tags, build `/settings` (`SET-1`, `SET-2`, `SET-3`), add `/settings` E2E redirect coverage in `smoke.spec.ts`, update operational docs (`docs/repository-layout.md`), and run the full backend and frontend verification batteries.
- **Scope:**
  1. **`frontend/src/lib/server/backend-client.ts`:**
     - Add TypeScript types (`FinancialAccount`, `CategoryKind`, `UserCategoryKind`, `Category`, `MerchantTag`, `DeleteCategoryResult`, `DeleteMerchantTagResult`, `FinanceRequestErrorKind`, `FinanceRequestResult<T>`) and server-only functions:
       - `listFinancialAccounts(sessionToken)`
       - `createFinancialAccount(sessionToken, input)`
       - `updateFinancialAccount(sessionToken, id, input)`
       - `archiveFinancialAccount(sessionToken, id)`
       - `restoreFinancialAccount(sessionToken, id)`
       - `listCategories(sessionToken)`
       - `createCategory(sessionToken, input)`
       - `updateCategory(sessionToken, id, input)`
       - `archiveCategory(sessionToken, id)`
       - `restoreCategory(sessionToken, id)`
       - `deleteCategory(sessionToken, id)`
       - `listMerchantTags(sessionToken)`
       - `createMerchantTag(sessionToken, input)`
       - `updateMerchantTag(sessionToken, id, input)`
       - `archiveMerchantTag(sessionToken, id)`
       - `restoreMerchantTag(sessionToken, id)`
       - `deleteMerchantTag(sessionToken, id)`
  2. **`frontend/src/lib/server/finance-actions.ts`:**
     - Implement `"use server"` actions that authenticate via `auth()`, invoke `backend-client.ts`, call `revalidatePath("/settings")` (and `/home`, `/transactions`, `/imports`), and return structured `{ ok: boolean; message: string; errorKind?: FinanceRequestErrorKind }` results with plain-language error messages (`UX-4`).
  3. **`frontend/src/app/settings/page.tsx` and client sections (`accounts-section.tsx`, `categories-section.tsx`, `merchant-tags-section.tsx`):**
     - `page.tsx`: authenticates via `auth()` and `getCurrentAccount(backendToken)`, fetches accounts, categories, and merchant tags in parallel, and renders within `AppShell` (`activeNav="settings"`):
       - **Accounts (`accounts-section.tsx`):** Create ZAR financial account (display name, optional institution, explicit `ZAR` badge), list active and archived accounts with transaction counts, inline rename/institution edit, archive, and restore (`SET-1`, `MVP-1`).
       - **Categories (`categories-section.tsx`):** List system `Uncategorised` bucket (marked `"System bucket — cannot be deleted"`, no edit/archive/delete buttons) plus active and archived categories with visible kind badge (`Expense`, `Income`, `Transfer`, `Excluded`) and transaction count (`CAT-1..7`, `SET-2`). Create custom category; rename; change kind with an explicit inline warning panel explaining the impact on `Money in`, `Money out`, and `Net` totals and requiring confirmation (`CAT-3`, `UX-6`); archive and restore; delete with an inline confirmation prompt stating the exact number of transactions that will move to `Uncategorised` (`CAT-2`, `AC-CAT-6`, `UX-6`).
       - **Merchant tags (`merchant-tags-section.tsx`):** List active and archived merchant tags with assigned transaction counts (`TAG-1`, `TAG-7`, `SET-2`). Create tag (with duplicate-name error feedback per `AC-TAG-1`), rename, archive, restore, and delete with an inline confirmation prompt stating the number of transactions from which the tag will be removed (`AC-TAG-8`, `UX-6`).
       - **Profile (`SET-3`):** Renders `<AccountDetails account={account} />`, conditional `Admin` link (`href="/admin"`), theme preference note, and `Log out` button (`form action={endSessionAction}`).
  4. **Tests and Documentation (`frontend/tests/lib/server/backend-client.test.ts`, `frontend/tests/lib/server/finance-actions.test.ts`, `frontend/tests/app/settings/page.test.tsx`, `frontend/tests/e2e/smoke.spec.ts`, `docs/repository-layout.md`):**
     - Add unit and component tests for backend-client finance routes, finance-actions, and the Settings page.
     - Update `frontend/tests/e2e/smoke.spec.ts` to verify the rewritten landing page, tagline *"See where your money goes"*, `/register` primary CTA, `/sample-page` 404 retirement, and unauthenticated redirects to `/login` for `/home`, `/transactions`, `/imports`, `/settings`, and `/admin`.
     - Update `docs/repository-layout.md` with the new finance HTTP endpoints and directory entries.
  5. **Engineer-owned Phase 3 review-note disposition (from Specialist review `f70c4037-e38a-49a3-8153-4e616278495d`):**
     - **N3 (Frontend side — `institution` partial-update semantics):** In `backend-client.ts` and `finance-actions.ts`, ensure `updateFinancialAccount` preserves `institution` when omitted on a rename-only call and sends `"institution": null` when cleared by the user in `accounts-section.tsx`, with unit test coverage in `backend-client.test.ts` and `finance-actions.test.ts`.
- **Verification Commands (Phase 3):**
  ```bash
  # Backend
  cd backend
  dotnet format argus.Server.csproj --verify-no-changes
  dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
  dotnet test tests/argus.Server.Tests
  dotnet build -c Release

  # Frontend
  cd frontend
  npx audit-ci@^7 --config ./audit-ci.jsonc
  npm run lint
  npm run test
  npm run build
  npm run test:e2e
  ```

---

## 8. Exact Files, Directories, and Public / Shared Contracts in Scope

### Backend Files
| Path | Action | Contract / Responsibility |
| --- | --- | --- |
| `backend/Data/Db.cs` | Modify | Add migration `0005_finance_foundation` to `Migrations.All`. |
| `backend/Data/FinanceStore.cs` | Create | Domain records (`FinancialAccountRecord`, `CategoryRecord`, `MerchantTagRecord`), `DefaultCategories`, name normalisation, idempotent transactional category seeding, and customer-scoped CRUD/archive/restore/delete queries. |
| `backend/Http/HttpSupport.cs` | Modify | Add `NotFound`, `DuplicateName`, `SystemCategoryImmutable`, `KindChangeConfirmationRequired` to `Errors`; add finance request/response DTO records. |
| `backend/Http/FinanceEndpoints.cs` | Create | `FinanceApi` mapping `/v1/finance/accounts`, `/v1/finance/categories`, and `/v1/finance/merchant-tags` with origin, app-secret, session, verified-subscriber, and rate-limit guards, plus `SEC-4` compliant logging. |
| `backend/Security/RateLimiter.cs` | Modify | Add `CheckFinanceIp(string ip)` bucket (`finance_ip:{ip}`, 300 requests per 15-minute window) so finance requests are bounded independently of the login/auth bucket (`N7`). |
| `backend/Program.cs` | Modify | Register `FinanceStore` and `FinanceApi` singletons, map `FinanceApi`, and update CORS `.WithMethods("GET", "POST", "PATCH", "DELETE")`. |
| `backend/Seed.cs` | Modify | Call `FinanceStore.EnsureCategoriesSeededAsync` for seeded dev accounts (`dev@example.test`, `user@example.test`). |
| `backend/tests/argus.Server.Tests/ApiTestBase.cs` | Modify | Add `Patch` and `Delete` request helpers and include finance tables in `ResetDatabaseAsync`. |
| `backend/tests/argus.Server.Tests/FinanceContractTests.cs` | Create | xUnit PostgreSQL isolation tests for accounts, categories, merchant tags, customer isolation, concurrency, and log privacy. |

### Frontend and Documentation Files
| Path | Action | Contract / Responsibility |
| --- | --- | --- |
| `frontend/src/lib/landing-content.ts` | Modify | Replace starter-kit copy with Argus personal finance copy, tagline `"See where your money goes"`, and finance feature highlights. |
| `frontend/src/app/layout.tsx` | Modify | Update `metadata.description` for Argus ZAR personal finance insights. |
| `frontend/src/app/page.tsx` | Modify | Rewrite landing page for Argus finance; point primary CTA to `/register` and secondary CTA to `/login`. |
| `frontend/src/app/sample-page/page.tsx` | Delete | Retire `/sample-page` scaffold route (`MVP-18`). |
| `frontend/tests/app/sample-page.test.tsx` | Delete | Remove retired sample-page test (`MVP-18`). |
| `frontend/src/proxy.ts` | Modify | Protect `/transactions`, `/imports`, and `/settings` (and subpaths) in addition to `/home` and `/admin`. |
| `frontend/src/components/app-shell.tsx` | Create | Responsive authenticated navigation shell (`Overview`, `Transactions`, `Imports`, `Settings`), ZAR badge, conditional `Admin` link, and `Log out` button. |
| `frontend/src/app/home/page.tsx` | Modify | Render `Overview` empty state and Summary/Charts tab header inside `AppShell`. |
| `frontend/src/app/transactions/page.tsx` | Create | Authenticated `Transactions` route with first-run empty state inside `AppShell`. |
| `frontend/src/app/imports/page.tsx` | Create | Authenticated `Imports` route with first-run empty state inside `AppShell`. |
| `frontend/src/lib/server/backend-client.ts` | Modify | Add server-only client functions and types for `/v1/finance/accounts`, `/v1/finance/categories`, and `/v1/finance/merchant-tags`. |
| `frontend/src/lib/server/finance-actions.ts` | Create | `"use server"` actions for financial accounts, categories, and merchant tags. |
| `frontend/src/app/settings/page.tsx` | Create | Authenticated `Settings` page rendering `Accounts`, `Categories`, `Merchant tags`, and `Profile` (`AccountDetails`) inside `AppShell`. |
| `frontend/src/app/settings/accounts-section.tsx` | Create | Client interactive section for ZAR financial accounts (`SET-1`). |
| `frontend/src/app/settings/categories-section.tsx` | Create | Client interactive section for categories, kind-change warning, and delete confirmation (`SET-2`, `CAT-1..7`). |
| `frontend/src/app/settings/merchant-tags-section.tsx` | Create | Client interactive section for merchant tags and delete confirmation (`SET-2`, `TAG-1`, `TAG-7`). |
| `frontend/tests/app/page.test.tsx` | Modify | Assert Argus finance landing copy, tagline, `/register` primary CTA, and forbidden phrases. |
| `frontend/tests/app/home/page.test.tsx` | Modify | Assert `Overview` shell navigation, first-run empty state, expired session handling, and admin link visibility. |
| `frontend/tests/app/transactions/page.test.tsx` | Create | Unit tests for `/transactions` page and session guard. |
| `frontend/tests/app/imports/page.test.tsx` | Create | Unit tests for `/imports` page and session guard. |
| `frontend/tests/app/settings/page.test.tsx` | Create | Unit and interaction tests for `/settings` (`Accounts`, `Categories`, `Merchant tags`, `Profile`). |
| `frontend/tests/lib/server/backend-client.test.ts` | Modify | Unit tests for finance client functions and error classification. |
| `frontend/tests/lib/server/finance-actions.test.ts` | Create | Unit tests for finance server actions. |
| `frontend/tests/e2e/smoke.spec.ts` | Modify | Update Playwright e2e smoke tests for landing page rewrite, `/sample-page` 404 retirement, and unauthenticated route guards. |
| `docs/repository-layout.md` | Modify | Document `/v1/finance/*` HTTP endpoints and new files. |

### HTTP Contract Summary (`/v1/finance/*`)
All routes require `X-{ProductToken}-Application-Secret`, valid origin (`Origin` / `X-{ProductToken}-Frontend-Origin`), and `X-{ProductToken}-Session` for a verified active subscriber.

| Method | Path | Request JSON (`snake_case`) | Success Response (`snake_case`) | Error Codes |
| --- | --- | --- | --- | --- |
| `GET` | `/v1/finance/accounts` | None | `200 { "accounts": [ { "id", "display_name", "institution", "currency": "ZAR", "is_archived", "transaction_count", "created_at", "updated_at" } ] }` | `401 unauthorized`, `403`, `429 rate_limited` |
| `POST` | `/v1/finance/accounts` | `{ "display_name": string, "institution"?: string \| null }` | `201` single account object | `400 invalid_request`, `401`, `403`, `429` |
| `PATCH` | `/v1/finance/accounts/{id:guid}` | `{ "display_name": string, "institution"?: string \| null }` | `200` single account object | `400 invalid_request`, `404 not_found`, `401`, `403`, `429` |
| `POST` | `/v1/finance/accounts/{id:guid}/archive` | None | `200` single account object (`is_archived: true`) | `404 not_found`, `401`, `403`, `429` |
| `POST` | `/v1/finance/accounts/{id:guid}/restore` | None | `200` single account object (`is_archived: false`) | `404 not_found`, `401`, `403`, `429` |
| `GET` | `/v1/finance/categories` | None | `200 { "categories": [ { "id", "display_name", "kind", "is_system", "is_default", "is_archived", "transaction_count", "created_at", "updated_at" } ] }` | `401 unauthorized`, `403`, `429 rate_limited` |
| `POST` | `/v1/finance/categories` | `{ "display_name": string, "kind": "Expense" \| "Income" \| "Transfer" \| "Excluded" }` | `201` single category object | `400 invalid_request`, `409 duplicate_name`, `401`, `403`, `429` |
| `PATCH` | `/v1/finance/categories/{id:guid}` | `{ "display_name": string, "kind": string, "confirm_kind_change"?: bool }` | `200` single category object | `400 invalid_request` / `system_category_immutable` / `kind_change_confirmation_required`, `404 not_found`, `409 duplicate_name`, `401`, `403`, `429` |
| `POST` | `/v1/finance/categories/{id:guid}/archive` | None | `200` single category object (`is_archived: true`) | `400 system_category_immutable`, `404 not_found`, `401`, `403`, `429` |
| `POST` | `/v1/finance/categories/{id:guid}/restore` | None | `200` single category object (`is_archived: false`) | `400 system_category_immutable`, `404 not_found`, `401`, `403`, `429` |
| `DELETE` | `/v1/finance/categories/{id:guid}` | None | `200 { "deleted": true, "reassigned_transaction_count": int, "uncategorised_category_id": guid }` | `400 system_category_immutable`, `404 not_found`, `401`, `403`, `429` |
| `GET` | `/v1/finance/merchant-tags` | None | `200 { "merchant_tags": [ { "id", "display_name", "is_archived", "transaction_count", "created_at", "updated_at" } ] }` | `401 unauthorized`, `403`, `429 rate_limited` |
| `POST` | `/v1/finance/merchant-tags` | `{ "display_name": string }` | `201` single merchant tag object | `400 invalid_request`, `409 duplicate_name`, `401`, `403`, `429` |
| `PATCH` | `/v1/finance/merchant-tags/{id:guid}` | `{ "display_name": string }` | `200` single merchant tag object | `400 invalid_request`, `404 not_found`, `409 duplicate_name`, `401`, `403`, `429` |
| `POST` | `/v1/finance/merchant-tags/{id:guid}/archive` | None | `200` single merchant tag object (`is_archived: true`) | `404 not_found`, `401`, `403`, `429` |
| `POST` | `/v1/finance/merchant-tags/{id:guid}/restore` | None | `200` single merchant tag object (`is_archived: false`) | `404 not_found`, `401`, `403`, `429` |
| `DELETE` | `/v1/finance/merchant-tags/{id:guid}` | None | `200 { "deleted": true, "unassigned_transaction_count": int }` | `404 not_found`, `401`, `403`, `429` |

---

## 9. Observable Acceptance Criteria Mapped to Verification Evidence

| ID | Acceptance Criterion | Verification Level & Concrete Evidence |
| --- | --- | --- |
| **AC-P1-1** (`MVP-18`) | Public landing page (`/`) and metadata display the Argus finance value proposition and tagline *"See where your money goes"*, primary CTA points to `/register`, starter-kit / forbidden phrases are absent, and `/sample-page` is retired (returns 404). | **Unit & Live/runtime E2E test:** `frontend/tests/app/page.test.tsx` and `frontend/tests/e2e/smoke.spec.ts`. |
| **AC-P1-2** (`Section 6.1`, `SET-3`, `UX-1`, `UX-2`) | Authenticated users see persistent, keyboard-reachable navigation (`Overview`, `Transactions`, `Imports`, `Settings`) across `/home`, `/transactions`, `/imports`, and `/settings`; `Settings` renders `Profile` (`AccountDetails`, admin link when admin, theme note, logout); unauthenticated requests to any of the four routes redirect to `/login`. | **Unit & Live/runtime E2E test:** `frontend/tests/app/home/page.test.tsx`, `frontend/tests/app/transactions/page.test.tsx`, `frontend/tests/app/imports/page.test.tsx`, `frontend/tests/app/settings/page.test.tsx`, and `frontend/tests/e2e/smoke.spec.ts`. |
| **AC-P1-3** (`MVP-1`, `SET-1`) | Authenticated customer can create, list, rename (including optional institution), archive, and restore ZAR financial accounts; currency is always `ZAR`. | **Integration & Unit test:** `FinanceContractTests` (real PostgreSQL) + `frontend/tests/app/settings/page.test.tsx` + `frontend/tests/lib/server/finance-actions.test.ts`. |
| **AC-P1-4** (`MVP-7`, `CAT-1..7`, `AC-CAT-6`) | First use idempotently seeds the 19 default categories with their kinds (`Expense`, `Income`, `Transfer`, `Excluded`) plus system `Uncategorised`; subsequent renames or deletions of default categories are not resurrected on later reads; custom categories can be created, renamed (case-insensitive trimmed unique), kind-changed (requiring `confirm_kind_change: true` and UI warning), archived, restored, and deleted (moving linked transactions to `Uncategorised` with zero deleted transactions); system `Uncategorised` cannot be deleted, archived, renamed, or kind-changed. | **Integration & Unit test:** `FinanceContractTests` (inserting real transactions and verifying `reassigned_transaction_count` and `Uncategorised` `transaction_count` after category deletion, plus `400 system_category_immutable` on `Uncategorised`) + `frontend/tests/app/settings/page.test.tsx`. |
| **AC-P1-5** (`MVP-22`, `TAG-1`, `TAG-7`, `AC-TAG-1`, `AC-TAG-8`) | Authenticated customer can create, list, rename, archive, restore, and delete merchant tags; duplicate names differing only by case or surrounding/internal whitespace are rejected (`409 duplicate_name`); deleting a tag removes its `transaction_merchant_tags` assignments while keeping all transactions intact. | **Integration & Unit test:** `FinanceContractTests` (real PostgreSQL) + `frontend/tests/app/settings/page.test.tsx`. |
| **AC-P1-6** (`SEC-1`, `SEC-2`, `SEC-4`, `SEC-5`, `AC-SEC-1..3`) | Unauthenticated or untrusted requests to `/v1/finance/*` are rejected (`401`/`403`); customer A requesting or mutating customer B's account, category, or merchant tag by UUID receives the exact same `404 {"error":"not_found"}` as a nonexistent UUID with no side effects; logs and `audit.*` events never contain user-supplied account, institution, category, or tag names; `/admin` exposes no financial data. | **Integration & Unit test:** `FinanceContractTests` (including cross-customer UUID probes and log-capture canary assertions) + `frontend/tests/app/admin/page.test.tsx`. |

---

## 10. Required Verification Commands and Stop Conditions

Every phase must pass its scoped checks, and Phase 3 must pass the full CI verification battery before handback:

```bash
# Backend verification (working directory: backend/)
dotnet format argus.Server.csproj --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet test tests/argus.Server.Tests
dotnet build -c Release

# Frontend verification (working directory: frontend/)
npx audit-ci@^7 --config ./audit-ci.jsonc
npm run lint
npm run test
npm run build
npm run test:e2e
```

**Environment note for WSL Playwright execution:** If Chromium fails to launch at `browserType.launch` due to missing host shared libraries (`libnspr4`, `libnss3`, `libasound2t64`) in WSL, extract those `.deb` packages to `/tmp/opencode/chromium-libs/root` and prefix `LD_LIBRARY_PATH=/tmp/opencode/chromium-libs/root/usr/lib/x86_64-linux-gnu npm run test:e2e`.

**Stop conditions:**
- Any failing xUnit isolation test, Vitest test, Playwright test, ESLint check, `dotnet format` check, or release build blocks phase completion.
- Any SQL query in `FinanceStore` that omits `customer_id` scoping or any log message emitting a user-supplied entity name is a Stop Rule blocker.

---

## 11. Adversarial Scenarios

1. **Cross-customer ID probing and mutation (`AC-SEC-1`):** Customer A calls `PATCH`, `POST .../archive`, `POST .../restore`, and `DELETE` using Customer B's `financial_account_id`, `category_id`, and `merchant_tag_id`. All calls must return `404 {"error":"not_found"}` (identical to a random `Guid.NewGuid()`) and leave Customer B's rows and counts untouched.
2. **Concurrent first-use category seeding:** Two parallel requests (`Task.WhenAll(Get("/v1/finance/categories"), Get("/v1/finance/categories"))`) for a newly verified customer run simultaneously. `SELECT ... FROM customers WHERE id = @id FOR UPDATE` + `ON CONFLICT (customer_id, normalized_name) DO NOTHING` must serialize seeding without throwing `23505` or creating duplicate categories, yielding exactly 20 categories (19 defaults + `Uncategorised`).
3. **Default category rename/delete non-resurrection:** Customer renames `"Groceries"` to `"Supermarket"` and deletes `"Travel"`. Subsequent calls to `GET /v1/finance/categories` and `Seed.RunAsync` must not re-create `"Groceries"` or `"Travel"`.
4. **System `Uncategorised` immutability:** Caller attempts to rename `Uncategorised`, change its kind, archive it, restore it, or delete it (`DELETE /v1/finance/categories/{uncategorisedId}`), or create another category named `"  uncategorised  "`. All mutations on `Uncategorised` return `400 {"error":"system_category_immutable"}` and creating `"  uncategorised  "` returns `409 {"error":"duplicate_name"}`.
5. **Case and whitespace collision on Categories and Merchant Tags (`AC-TAG-1`):** Creating `"Woolworths"` then creating `"  woolworths "` or `"WOOLWORTHS"` (or renaming another tag to `"woolworths"`) for the same customer is rejected with `409 {"error":"duplicate_name"}`, while another customer can independently create `"Woolworths"`. Renaming `"woolworths"` to `"Woolworths"` on the *same* tag ID succeeds.
6. **Unconfirmed category kind change (`CAT-3`):** Calling `PATCH /v1/finance/categories/{id}` with a changed `kind` and `confirm_kind_change: false` (or omitted) is rejected with `400 {"error":"kind_change_confirmation_required"}` without modifying the category.
7. **Category and Merchant Tag deletion with linked transactions (`AC-CAT-6`, `AC-TAG-8`):** Deleting a category that owns 2 transactions atomically moves both transactions to the customer's `Uncategorised` category (`reassigned_transaction_count = 2`) and preserves total customer transaction count. Deleting a merchant tag assigned to 2 transactions removes the 2 join rows (`unassigned_transaction_count = 2`) and preserves both transactions and their categories.
8. **Malformed JSON, unknown fields, and control characters:** Requests with unknown JSON properties, empty/whitespace-only names, names > 80 runes, or names containing control characters (`\u0000`, `\n`, `\r`) are rejected with `400 {"error":"invalid_request"}`.
9. **Canary log inspection (`SEC-4`):** Creating, renaming, archiving, and deleting entities named `"CanaryAccount_9921"`, `"CanaryInst_9921"`, `"CanaryCategory_9921"`, and `"CanaryTag_9921"` under a capturing `ILoggerProvider` produces zero log entries containing `"Canary"`.

---

## 12. Risks, Compatibility, Rollback, Deferred Work, and Human Decisions

- **Compatibility with existing auth and billing suites:** `AccountContractTests`, `BillingContractTests`, and `ConfigurationAndPolicyTests` remain untouched in behaviour. Extending `ApiTestBase.ResetDatabaseAsync()` and adding `0005_finance_foundation` preserves all existing tests.
- **Forward compatibility with Plans 2, 3, and 4:** By establishing `statement_imports`, `transactions`, and `transaction_merchant_tags` alongside `financial_accounts`, `categories`, and `merchant_tags` in `0005_finance_foundation`, Plan 1 tests real foreign-key reassignment (`AC-CAT-6`) and tag unassignment (`AC-TAG-8`) directly against PostgreSQL, while duplicate-identity and statement-fingerprint uniqueness constraints (`DUP-1..19`) are intentionally owned by Plan 2's migration once parser normalisation and occurrence rules are introduced (`N11`).
- **Rollback / recovery:** All schema additions live in migration `0005_finance_foundation` and new endpoints live under `/v1/finance/*`. Rolling back the commit and dropping the new tables restores the starter baseline cleanly.
- **Deferred work:**
  - Statement upload, FNB/Standard Bank/generic CSV parsing, review, idempotency, and Import History -> **Thread #3 (`docs/plans/02-statement-import.md`)**.
  - Transaction list, detail view, inline/bulk classification, and transaction hide/restore -> **Thread #4 (`docs/plans/03-transactions-and-classification.md`)**.
  - Overview summary cards, bar-first Chart tab, period comparisons, coverage warnings, and detail drill-downs -> **Thread #5 (`docs/plans/04-overview-and-charts.md`)**.
- **Decisions requiring human confirmation:**
  - Operator approval of this Plan after the Specialist plan-review gate before Orchestrator / execution begins.

---

## Decisions Log

| Date | Decision | Rationale |
| --- | --- | --- |
| `2026-10-30` | Represent `Uncategorised` as a seeded per-customer system row (`is_system = TRUE`, `kind = 'Uncategorised'`) and require `transactions.category_id NOT NULL REFERENCES categories(id) ON DELETE RESTRICT`. | Eliminates dual representation (`NULL` vs. system row ID), enforces `ON DELETE RESTRICT` at the database level so transactions can never be orphaned, and allows uniform `transaction_count` joins and `UNIQUE (customer_id, normalized_name)` enforcement. |
| `2026-10-30` | Track first-use default category seeding via `customers.categories_seeded_at TIMESTAMPTZ NULL` guarded by `SELECT ... FOR UPDATE`. | Guarantees idempotent concurrent seeding on first use while ensuring that default categories renamed or deleted by the customer are never resurrected on subsequent reads or restarts. |
| `2026-10-30` | Include `statement_imports`, `transactions`, and `transaction_merchant_tags` relational tables in migration `0005_finance_foundation`. | Enables real SQL `transaction_count` queries, real transactional reassignment to `Uncategorised` (`AC-CAT-6`), and real tag unassignment (`AC-TAG-8`) verified by PostgreSQL isolation tests in Plan 1. |
| `2026-10-30` | Keep Playwright `smoke.spec.ts` backend-independent while verifying authenticated workspace and Settings flows in Vitest + xUnit PostgreSQL isolation tests. | Matches `.github/workflows/ci.yml` where the `frontend` job runs Next.js standalone without starting the C# backend or PostgreSQL. |

---

## Direct Execution Record

- **Executed by:** Partner (direct mode), session `ses_e728ddc81ffejVieKQDAM8oGvl`
- **Date:** `2026-10-31`
- **Scope:** Phases 1–3 plus every Engineer-owned review-note disposition (`N1`–`N11`).
- **Baseline:** `commit:0e7f1c8ab773a66c99c2e0ae96f4fe314c321a03` plus the uncommitted working tree. No commit or push was requested, so publication remains the operator's decision.
- **Verification evidence:**
  - Backend: `dotnet format` (server and tests) PASS; `dotnet test tests/argus.Server.Tests` 35/35 PASS; `dotnet build -c Release` PASS (0 warnings, 0 errors).
  - Frontend: `npx audit-ci@^7 --config ./audit-ci.jsonc` PASS; `npm run lint` PASS; `npm run test` 134/134 PASS; `npm run build` PASS; `npm run test:e2e` 17/17 PASS.
  - Environment note: Playwright launched the installed Chromium directly; no shared-library workaround was needed. The test `LOG_LEVEL` is `info` so the canary capture observes audit events.
- **Review status:** **PASS** — final acceptance review gate completed. Specialist review request `438b2b1a-ba27-4d2f-8a34-f7a491c0f0d4` (verdict `GO`, 0 CRITICAL, 0 HIGH, 0 blockers; artifact SHA-256 `6309ac5320a400effde300cf50ebcf6bbf7390183ad8dd1e5f57f9df8557d4ce`, 21,570 bytes). All 3 MEDIUM and 8 LOW findings (`N1`–`N11`) assessed against the working tree: each is disposed with implementation and passing test evidence; zero open findings.
