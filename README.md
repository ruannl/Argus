# Argus

**See where your money goes.**

Argus is a personal finance workspace for South African ZAR bank statements.
It turns exported statements into a reviewed transaction record you can
classify by category and merchant tag, then search, filter, and correct
without changing the imported date, description, or amount.

Argus is not a budgeting app, an accounting system, or a bank aggregator.
Statements are imported manually.

## What it does

- **Profile and sign-in** — registration, email verification, login, logout,
  password reset, account lockout, breached-password screening, and opaque
  hashed sessions with absolute and idle expiry.
- **ZAR financial accounts** — create an account with a display name and an
  optional institution, rename it, and archive or restore it.
- **Categories** — use the seeded defaults or create your own; rename, change
  kind after confirmation, archive, restore, or delete. Deleting a category
  reassigns its transactions to Uncategorised.
- **Merchant tags and aliases** — create tags such as Woolworths or Uber Eats,
  assign several to one transaction, and define aliases that map a description
  to a tag and an optional category.
- **Statement import** — upload an FNB CSV export (a bare CSV or the ZIP that
  contains one CSV), or map a generic CSV. Argus inspects the file, lets you
  confirm the mapping, and prepares a durable review. Every row is validated
  with duplicate, category, and merchant-tag decisions before commit. Category
  defaults are preselected but stay unconfirmed until you accept them, and
  tags remain advisory. The commit is atomic and idempotent, and a committed
  statement can be hidden, restored, or deleted with exactly the transactions
  it owns.
- **Transactions** — search and filter the owned list, open a transaction, and
  change its category or tags. Category assignments can be confirmed, and
  individual transactions can be hidden or restored. Bulk edits work on an
  explicit selection or on every row matching one filter snapshot, with a
  saved scope and a durable receipt.
- **Settings** — manage the profile, financial accounts, categories, merchant
  tags, and aliases.

Sign-in and the finance routes require a verified email and an active
subscription entitlement. The local bootstrap seeds `dev@example.test`
(admin) and `user@example.test` (subscriber) with both.

## Platform foundations

The workspace runs on a trusted frontend-to-backend boundary: the browser
never holds the backend origin or the application secret, and the backend
authorizes every call independently. The backend also provides an admin
account view, a mock billing webhook that drives subscription entitlement, a
structured `audit.*` event stream, request-ID correlation, security headers,
per-IP rate limiting, and a non-root container image with a health check. The
billing seam is a mock, not a payment integration.

## Stack

| Layer | Technology |
| --- | --- |
| Frontend | Next.js 16 App Router (TypeScript, Tailwind 4, npm) |
| Backend | ASP.NET Core on .NET 10 (PostgreSQL, structured logging) |
| Auth (frontend) | Auth.js v5 (JWT session wrapping the backend session token) |
| Database | PostgreSQL |

One git repository, two independently buildable trees. There is no root
package manifest or solution file; run commands inside `backend/` or
`frontend/`. The frontend talks to the backend only through its REST API.

## Prerequisites

None of these is installed for you.

- **.NET 10 SDK** — builds and runs the backend.
- **Node 22 and npm** — runs the bootstrap and the Next.js frontend.
- **PostgreSQL** — either a running Docker container named `postgres_db`, or a
  local PostgreSQL with `psql` and `createdb` on `PATH`.
- **openssl** — the bootstrap uses it to generate local secrets.

## Quickstart

From the repository root:

```text
node scripts/bootstrap-dev.mjs
```

The bootstrap creates the development and isolation-test databases, writes
`backend/.env` and `frontend/.env` without overwriting existing values,
applies the migrations, and seeds the two local accounts. It is idempotent and
safe to re-run.

Start the backend:

```text
cd backend
dotnet run
```

The backend serves on `http://127.0.0.1:3001`.

Start the frontend in another terminal:

```text
cd frontend
npm ci
npm run dev
```

The frontend serves on `http://127.0.0.1:3000`.

## Configuration

`backend/.env` is loaded for local development, and real environment
variables win. Only `ARGUS_`-prefixed names are read. A non-local database URL
must carry a password and require TLS.

- `ARGUS_RESEND_API_KEY` and `ARGUS_RESEND_FROM` enable real email delivery.
  The bootstrap writes a development placeholder, so local development starts
  without a real key; real delivery needs your own key and a verified sender.
- `ARGUS_SEED_DEV_PASSWORD` and `ARGUS_SEED_USER_PASSWORD` pin the seeded
  account passwords. Set them before the first bootstrap run. Re-running the
  bootstrap never resets an existing account.

Never commit a real secret. `.env` files are git-ignored; `.env.example`
templates are committable and carry no secrets. Frontend environment values
are server-only `ARGUS_*` names; do not add `NEXT_PUBLIC_*` values that expose
the backend origin or a secret.

## Verification

Run the full battery before you push. CI runs these checks plus a backend
transitive dependency scan.

```bash
# Backend
cd backend
dotnet format argus.Server.csproj --verify-no-changes
dotnet format style argus.Server.csproj --diagnostics IDE0005 --severity info --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet format style tests/argus.Server.Tests/argus.Server.Tests.csproj --diagnostics IDE0005 --severity info --verify-no-changes
dotnet test tests/argus.Server.Tests -warnaserror
dotnet build -c Release -warnaserror

# Frontend
cd frontend
npm ci
npx audit-ci@^7 --config ./audit-ci.jsonc
npm run format:check
npm run lint
npm run test
npm run build
npm run test:e2e
```

The default `dotnet test` run excludes tests marked `Category=Performance`,
which seed up to 50,000 rows. CI runs them separately; run them locally with
`dotnet test tests/argus.Server.Tests -warnaserror --filter "Category=Performance"`.
`dotnet test` needs `ARGUS_TEST_DATABASE_URL` for the isolation tests. The
bootstrap creates that database and writes the value to `backend/.env`; export
it before running the tests. The suite truncates finance tables and creates its
own rows, so the URL must name the dedicated `argus_test` database, never the
development `argus` database or production. `npm run test:e2e` needs chromium
installed.

## Documentation

- [docs/product-brief.md](docs/product-brief.md) — product intent, target
  users, and finance domain rules
- [docs/statement-import-runbook.md](docs/statement-import-runbook.md) —
  import limits, privacy, parser deployment, and recovery
- [docs/transaction-workspace-runbook.md](docs/transaction-workspace-runbook.md)
  — filters, classification, bulk operations, and receipts
- [docs/repository-layout.md](docs/repository-layout.md) — full tree,
  commands, HTTP surface, and CI
- [docs/bootstrap-runbook.md](docs/bootstrap-runbook.md) — first run and
  verification
- [docs/secret-rotation.md](docs/secret-rotation.md) — secrets inventory and
  rotation procedures
- [docs/tls-deployment-contract.md](docs/tls-deployment-contract.md) — the
  deployment contract that must hold before anything public
- [docs/repo-hygiene.md](docs/repo-hygiene.md) — what must not be committed
- [docs/divergences.md](docs/divergences.md) — documented C# divergences from
  the Rust sibling kit

## License

MIT — see [LICENSE](LICENSE).
