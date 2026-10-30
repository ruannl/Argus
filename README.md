# NextJS and C# Argus

A production-ready starting point for a SaaS product: secure customer
accounts, provider-agnostic billing, an admin surface, structured logging
with an audit trail, and a hardened CI pipeline — a Next.js frontend with an
enterprise-grade ASP.NET Core backend.

Customers receive this kit through the StarterFoundry CLI. This repository
is the kit source; Git-based clone-and-rename is not a customer path.

## What's included

- **Customer accounts** — registration, email verification, login, logout,
  password reset, account lockout, breached-password screening, opaque
  hashed sessions with absolute and idle expiry, non-enumerating responses.
- **Billing** — a provider-agnostic billing provider contract with a mock
  implementation and HMAC-signed webhooks driving subscription entitlements.
  It is not a payment integration.
- **Admin** — an admin-only page that renders account state; capability
  comes from the backend, not from the page.
- **Operations** — structured JSON logging, an `audit.*` event stream,
  request-ID correlation, security headers, per-IP rate limiting, a
  non-root backend container image with a health check.
- **CI** — backend formatting, tests, release build, and dependency
  checks; frontend audit, lint, unit tests, build, and browser end-to-end
  tests.

## Stack

| Layer | Technology |
| --- | --- |
| Frontend | Next.js 16 App Router (TypeScript, Tailwind 4, npm) |
| Backend | ASP.NET Core on .NET 10 (PostgreSQL, structured logging) |
| Auth (frontend) | Auth.js v5 (JWT session wrapping the backend session token) |
| Database | PostgreSQL |

One git repository, two independently buildable trees. The frontend talks to
the product only through the backend REST API.

## Prerequisites

None of these is installed for you.

- **.NET 10 SDK** — builds and runs the backend.
- **Node 22 and npm** — runs the first-run bootstrap and the Next.js frontend.
- **PostgreSQL** — either a running Docker container named `postgres_db`, or a
  local PostgreSQL with `psql` and `createdb` on `PATH`.
- **openssl** — the bootstrap uses it to generate local secrets.

## Quickstart

After the StarterFoundry CLI has placed this kit:

1. Run the bootstrap:

    ```text
    node scripts/bootstrap-dev.mjs
    cd backend
    dotnet run

    cd frontend
    npm ci
    npm run dev
    ```

   It creates the development and isolation-test databases, writes aligned
   `.env` files, applies migrations, and seeds two verified accounts
   (`dev@example.test` admin, `user@example.test` subscriber).

2. Rewrite `frontend/src/lib/landing-content.ts` for your product.

Kit maintainers developing *this* repository clone it with Git as usual.
That is kit development, not customer instantiation.

## Verification

The full battery is also what CI runs:

```bash
# Backend
cd backend
dotnet format --verify-no-changes
dotnet test tests/argus.Server.Tests
dotnet build -c Release

# Frontend
cd frontend
npm ci
npx audit-ci@^7 --config ./audit-ci.jsonc
npm run lint
npm run test
npm run build
npm run test:e2e
```

## Documentation

- [docs/repository-layout.md](docs/repository-layout.md) — full tree,
  commands, HTTP surface, CI
- [docs/bootstrap-runbook.md](docs/bootstrap-runbook.md) — first-run after
  placement → verify
- [docs/secret-rotation.md](docs/secret-rotation.md) — secrets inventory and
  rotation procedures
- [docs/tls-deployment-contract.md](docs/tls-deployment-contract.md) — the
  deployment contract that must hold before anything public
- [docs/repo-hygiene.md](docs/repo-hygiene.md) — what must not be committed
- [docs/divergences.md](docs/divergences.md) — documented C# divergences from
  the Rust sibling kit

## License

MIT — see [LICENSE](LICENSE).
