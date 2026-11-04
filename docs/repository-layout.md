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
│   ├── Data/                 Npgsql connection, migrations, Store, FinanceStore
│   ├── Security/             argon2id, tokens, policy, rate limiter, HIBP
│   ├── Http/                 error bodies, trust checks, account + finance endpoints
│   ├── Mail/                 Resend mailer
│   ├── Billing/              HMAC webhook verification and event parsing
│   ├── Seed.cs               idempotent dev-account seeding
│   ├── Program.cs            host, logging, middleware, routes, seed command
│   ├── data/common-passwords.txt
│   ├── argus.Server.csproj
│   ├── Dockerfile            non-root image with health check
│   ├── .env.example
│   └── tests/argus.Server.Tests/   xunit isolation tests
├── frontend/
│   ├── src/app/              App Router routes (landing, auth, workspace)
│   ├── src/app/settings/     Accounts, Categories, Merchant tags, Profile
│   ├── src/components/       app shell, account details, session clearing
│   ├── src/lib/server/       server-only backend client and finance actions
│   ├── src/lib/brand.ts      canonical brand constants
│   ├── tests/                Vitest; tests/e2e is Playwright
│   ├── package.json / package-lock.json
│   └── .env.example
├── scripts/
│   ├── bootstrap-dev.mjs     first-run databases, env, migrations, seed
│   ├── verify-kit-declaration.mjs / .test.mjs   kit-only checks
│   └── bootstrap-dev.test.mjs                   kit-only checks
├── docs/                     operational documentation (ships)
└── documentation/            published developer documentation (kit-only)
```

## Commands

```bash
# Backend (from backend/)
dotnet run                    # serve on 127.0.0.1:3001
dotnet run -- seed            # migrate + seed (idempotent)
dotnet test tests/argus.Server.Tests
dotnet build -c Release

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

Account routes require the trusted-frontend application-secret header and
independently validate the acting user's session. Error bodies are
`{"error":"<kind>"}`; origin rejection and webhook failures are bodyless.

Finance routes apply the same caller proof plus a verified active subscriber
session, scope every SQL statement by `customer_id`, and return the same
`404 {"error":"not_found"}` for unknown and foreign-owned ids. User-defined
account, institution, category, and merchant-tag names are never written to
logs, audit events, or URL path segments. CORS allows `GET`, `POST`, `PATCH`,
and `DELETE` for the workspace.

## CI

`.github/workflows/ci.yml` runs on push and pull request to `main`:
backend format checks, isolation tests against PostgreSQL 16, and a release
build; a transitive dependency vulnerability scan; frontend audit, lint,
unit tests, production build, and Playwright end-to-end tests.
`.github/workflows/kit-verify.yml` runs the kit-declaration checks.
`.github/workflows/publish-backend.yml` is manual (`workflow_dispatch`) and
never publishes on push.
