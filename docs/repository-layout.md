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
│   ├── Data/                 Npgsql connection, migrations, repositories
│   ├── Security/             argon2id, tokens, policy, rate limiter, HIBP
│   ├── Http/                 error bodies, trust checks, account endpoints
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
│   ├── src/app/              App Router routes
│   ├── src/lib/server/       server-only backend client
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

Account routes require the trusted-frontend application-secret header and
independently validate the acting user's session. Error bodies are
`{"error":"<kind>"}`; origin rejection and webhook failures are bodyless.

## CI

`.github/workflows/ci.yml` runs on push and pull request to `main`:
backend format checks, isolation tests against PostgreSQL 16, and a release
build; a transitive dependency vulnerability scan; frontend audit, lint,
unit tests, production build, and Playwright end-to-end tests.
`.github/workflows/kit-verify.yml` runs the kit-declaration checks.
`.github/workflows/publish-backend.yml` is manual (`workflow_dispatch`) and
never publishes on push.
