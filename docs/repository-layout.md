# Repository layout

One git repository, two independently buildable trees. There is no root
package manifest, no root solution file, and no monorepo orchestrator: run
commands with the working directory inside the relevant tree.

## Tree

```text
.
├── .github/workflows/        ci.yml, kit-verify.yml, publish-backend.yml
├── AGENTS.md                 installed agent guidance (ships in instances)
├── LICENSE                   MIT
├── README.md                 front door
├── backend/
│   ├── Brand.cs              canonical brand/header/env constants
│   ├── Configuration/        fail-closed prefixed configuration
│   ├── Data/                 Npgsql connection, migrations, Store, FinanceStore,
│   │                         StatementImportStore, TransactionStore, CustomerDbScope,
│   │                         isolation contract/validators, cleanup services
│   ├── Transactions/         filter/action/limit contracts, fault seam, shared DTOs
│   ├── Finance/              merchant-key, alias/history and tag-token suggestion helpers shared with imports
│   ├── Imports/              bounded FNB/generic CSV parsers, identity/fingerprint
│   │                         code, and the dormant PDF parser/child runner
│   ├── Security/             argon2id, tokens, policy, rate limiter, HIBP
│   ├── Http/                 error bodies, trust checks, account, finance, import
│   │                         and transaction endpoints, bounded bodies
│   ├── Mail/                 Resend mailer
│   ├── Billing/              HMAC webhook verification and event parsing
│   ├── Seed.cs               idempotent dev-account seeding under an owner context
│   ├── MaintenanceProgram.cs offline migrate/seed entry point
│   ├── Program.cs            host, logging, middleware, routes, startup validation
│   ├── data/common-passwords.txt
│   ├── argus.Server.csproj
│   ├── Dockerfile            non-root image with health check
│   ├── .env.example          runtime-only template
│   ├── .env.test.example     test-maintenance template
│   ├── .env.maintenance.example  migrator template
│   └── tests/argus.Server.Tests/   xunit isolation tests
├── frontend/
│   ├── src/app/              App Router routes (landing, auth, workspace)
│   ├── src/app/settings/     Accounts, Categories, Merchant tags, Profile
│   ├── src/app/imports/      upload/confirm workspace, review pages, statement detail
│   ├── src/app/transactions/ list/filter/bulk workspace, detail drawer/standalone page
│   ├── src/components/       app shell, account details, session clearing
│   ├── src/lib/server/       server-only backend client, finance/import/transaction actions
│   ├── src/lib/brand.ts      canonical brand constants
│   ├── tests/                Vitest; tests/e2e is Playwright
│   ├── package.json / package-lock.json
│   └── .env.example
├── scripts/
│   ├── bootstrap-dev.mjs / .test.mjs      first-run databases, roles, env, migrate, seed
│   ├── provision-db-roles.mjs / .test.mjs role provisioning and drift checks
│   ├── adopt-db-objects.mjs / .test.mjs   bounded previous-owner transfer
│   └── run-backend-tests.mjs / .test.mjs  scoped test-maintenance launcher
├── docs/                     operational documentation (ships)
└── documentation/            published developer documentation (kit-only)
```

## Commands

```bash
# Backend (from backend/)
dotnet run                    # validate prepared schema/role, then serve on 127.0.0.1:3001
dotnet run -- migrate         # explicit owner migration/backfill
dotnet run -- seed            # migrate + seed (idempotent)
dotnet test tests/argus.Server.Tests
dotnet build -c Release

# Repository root
node scripts/bootstrap-dev.mjs
node scripts/run-backend-tests.mjs

# Frontend (from frontend/)
npm ci
npm run dev                   # serve on 127.0.0.1:3000
npm run test
npm run build
npm run test:e2e
```

## HTTP surface

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/health` | Liveness; origin-checked, no secret required |
| GET | `/v1/me` | Current account from the presented session |
| POST | `/v1/login` | Password sign-in; returns session token + expiry |
| POST | `/v1/logout` | Revokes the presented session (idempotent) |
| POST | `/v1/register` | Non-enumerating account creation |
| POST | `/v1/verify` | Consumes an email-verification token |
| POST | `/v1/verification/resend` | Non-enumerating verification resend |
| POST | `/v1/password/reset/request` | Non-enumerating reset request |
| POST | `/v1/password/reset` | Consumes a reset token and revokes sessions |
| POST | `/v1/billing/webhook` | Provider HMAC-signed entitlement events |
| GET | `/v1/finance/accounts` | List the customer's ZAR financial accounts |
| POST | `/v1/finance/accounts` | Create a ZAR financial account |
| PATCH | `/v1/finance/accounts/{id}` | Rename or update an account's institution |
| POST | `/v1/finance/accounts/{id}/archive` | Archive an account (idempotent) |
| POST | `/v1/finance/accounts/{id}/restore` | Restore an account (idempotent) |
| GET | `/v1/finance/categories` | List categories; seeds defaults on first use |
| POST | `/v1/finance/categories` | Create a custom category |
| PATCH | `/v1/finance/categories/{id}` | Rename or change kind (confirmation required) |
| POST | `/v1/finance/categories/{id}/archive` | Archive a category |
| POST | `/v1/finance/categories/{id}/restore` | Restore a category |
| DELETE | `/v1/finance/categories/{id}` | Delete a category and reassign to Uncategorised |
| GET | `/v1/finance/merchant-tags` | List merchant tags |
| POST | `/v1/finance/merchant-tags` | Create a merchant tag |
| PATCH | `/v1/finance/merchant-tags/{id}` | Rename a merchant tag |
| POST | `/v1/finance/merchant-tags/{id}/archive` | Archive a merchant tag |
| POST | `/v1/finance/merchant-tags/{id}/restore` | Restore a merchant tag |
| DELETE | `/v1/finance/merchant-tags/{id}` | Delete a tag and remove its assignments |
| GET | `/v1/finance/merchant-aliases` | List the customer's suggestion aliases |
| POST | `/v1/finance/merchant-aliases` | Create an alias that maps a description to a tag and optional category |
| PATCH | `/v1/finance/merchant-aliases/{id}` | Update an alias |
| DELETE | `/v1/finance/merchant-aliases/{id}` | Delete an alias |
| POST | `/v1/finance/imports/inspect` | Inspect a statement file; stateless preview/mapping |
| POST | `/v1/finance/imports/reviews` | Prepare a durable, expiring review |
| GET | `/v1/finance/imports/reviews` | List the customer's open reviews |
| GET | `/v1/finance/imports/reviews/{id}` | Read a paged review with decisions |
| PATCH | `/v1/finance/imports/reviews/{id}` | Update skip, override, group, tag, or category decisions |
| POST | `/v1/finance/imports/reviews/{id}/refresh` | Recompute duplicate state under the account lock |
| POST | `/v1/finance/imports/reviews/{id}/commit` | Atomically commit or replay the same receipt |
| POST | `/v1/finance/imports/reviews/{id}/abandon` | Purge a review without importing |
| GET | `/v1/finance/imports` | Committed-only history, filterable and cursor-paged |
| GET | `/v1/finance/imports/{id}` | Statement summary and paged owned rows |
| POST | `/v1/finance/imports/{id}/hide` | Hide the parent statement layer |
| POST | `/v1/finance/imports/{id}/restore` | Restore the parent statement layer |
| DELETE | `/v1/finance/imports/{id}` | Delete the statement and exactly its owned transactions |
| POST | `/v1/finance/transactions/query` | Owned list with filters, counts and pagination |
| GET | `/v1/finance/transactions/{id}` | Owned detail with unapplied tag and category suggestions and category provenance |
| PATCH | `/v1/finance/transactions/{id}` | Versioned category/tag/hide/confirm-category metadata edit |
| POST | `/v1/finance/transactions/operations/prepare` | Freeze an explicit or all-matching bulk scope |
| GET | `/v1/finance/transactions/operations/{id}` | Safe operation status and receipt recovery |
| POST | `/v1/finance/transactions/operations/{id}/apply` | Apply a saved scope atomically once |
| POST | `/v1/finance/transactions/operations/{id}/abandon` | Abandon a prepared operation |

Account routes require the trusted-frontend application-secret header and
independently validate the acting user's session. Error bodies are
`{"error":"<kind>"}`; origin rejection and webhook failures are bodyless.

Finance and import routes apply the same caller proof plus a verified active
subscriber session, scope every SQL statement by `customer_id`, and return the
same `404 {"error":"not_found"}` for unknown and foreign-owned ids. User-defined
account, institution, category, merchant-tag and transaction-description text
is never written to logs, audit events, or URL path segments. CORS allows
`GET`, `POST`, `PATCH`, and `DELETE` for the workspace.

Statement imports bound every file and request before buffering: 10 MB per file
and decompressed ZIP payload, 20,000 rows, 200 PDF pages, a 10.1 MB multipart
envelope, a 30-second parser deadline, and two concurrent parsers per process
shared by CSV and PDF. Standard Bank PDF import is currently deferred: the API rejects
that source kind with `pdf_import_deferred`, and the PDF parser and child runner
remain dormant until the follow-up thread re-enables them. Raw uploads and real bank preamble metadata are request-local and
never persisted. The durable review stores only sanitized mapped rows and
expires after 24 hours; committed receipts retain identifiers and counts for
retry safety. See `docs/statement-import-runbook.md`.

Transaction queries and edits reuse the same caller/session/subscriber guard and
owner scoping. Search text travels only in bounded request bodies; list URLs
carry nonsensitive ids, enums, dates and page choices. Bulk scope is frozen in
PostgreSQL operation manifests; apply, abandon, expiry and receipts are
recoverable after reload or restart. See
`docs/transaction-workspace-runbook.md`.

## CI

`.github/workflows/ci.yml` runs on push and pull request to `main`:
backend format checks, isolation tests against PostgreSQL 16, and a release
build; a transitive dependency vulnerability scan; frontend audit, lint,
unit tests, production build, and Playwright end-to-end tests.
`.github/workflows/kit-verify.yml` runs the kit-declaration checks.
`.github/workflows/publish-backend.yml` is manual (`workflow_dispatch`) and
never publishes on push.
