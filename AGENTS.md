# Argus — Agent Instructions

This tree is a reusable SaaS starting point: secure customer accounts,
provider-agnostic billing, an admin surface, structured logging with an audit
trail, and a hardened CI pipeline. It has two situations: a **placed project**
delivered by the StarterFoundry CLI, where your product identity is already
applied, and the **kit source** repository that produces that project. This
file guides work in the placed project. The final section covers
kit-source-only work.

## Kit identity

- The product is a SaaS application with two independently buildable trees:
  a Next.js frontend and an ASP.NET Core (C#) backend. What it includes at a
  functional level is listed under `Exploring this project`.
- Canonical product identity lives in `backend/Brand.cs` and
  `frontend/src/lib/brand.ts`: display name, protocol header names, and cookie
  slug. Structural strings must derive from those modules. Do not rename
  structural identifiers to change a display name.
- Landing copy lives in `frontend/src/lib/landing-content.ts`. After
  placement, rewrite it for the product; the export names are used by tests.
- Environment variables carry one prefix. The kit source writes
  `ARGUS_`; a placed project uses the prefix chosen at instantiation.
  `.env` files are local secrets and are never committed.
- Kit-level variation is declared by the kit itself. Shared tooling (the CLI,
  the packager, the website) must never learn stack-specific behaviour, so
  product changes belong in this tree, not in those tools.

## Prerequisites

Required before the first run. None of these is installed for you.

- **.NET 10 SDK (dotnet)** — builds and runs the backend. The exact C# project
  target is `net10.0`; the SDK is pinned by the container image and CI.
- **Node 22 and npm** — runs the first-run bootstrap and the Next.js frontend.
- **PostgreSQL** — either a running Docker container named `postgres_db`, or a
  local PostgreSQL with `psql` and `createdb` on `PATH`.
- **openssl** — the bootstrap uses it to generate local secrets.

These are the same prerequisites the CLI prints after placing the kit.

## Customer inputs

Already applied by the CLI at instantiation — you do not supply these again:
the product display name, the HTTP header token, the lowercase hyphenated
slug, and the container image owner.

You supply these after placement:

- `ARGUS_RESEND_API_KEY` and `ARGUS_RESEND_FROM`, in
  `backend/.env`, are required for real email delivery. The first-run
  bootstrap writes a development placeholder key, so local development starts
  without a real one; real delivery needs your own key and a verified sender.
- Optionally, `ARGUS_SEED_DEV_PASSWORD` and
  `ARGUS_SEED_USER_PASSWORD` pin the seeded account passwords instead of
  using random ones. Set them in `backend/.env` (or the shell environment)
  before the first run; re-running the bootstrap never resets existing
  accounts.

Never commit a real secret. `.env` files are git-ignored; `.env.example`
templates are committable and carry no secrets.

## First-time setup

Read this section before the first setup action in a placed project. It gates
that action, and it applies to any setup script you discover: finding a script
does not authorize executing it.

The CLI applied the product display name, the HTTP header token, the slug, and
the container image owner at instantiation. Do not collect those again.

No additional required kit-specific input is needed before local setup. The
values listed under `Customer inputs` are supplied after placement; the
optional ones can be set later, and local development starts without a real
Resend key because the bootstrap writes a placeholder.

The setup action is `node scripts/bootstrap-dev.mjs`. It creates the
development and isolation-test databases, writes `backend/.env` and
`frontend/.env` without overwriting values that already exist, and seeds two
local accounts. Because it changes the project, summarize the collected
values and ask the user to proceed first; run the action only after the user's
explicit approval.

## First run

From the project root:

```text
node scripts/bootstrap-dev.mjs
cd backend
dotnet run

cd frontend
npm ci
npm run dev
```

With every prerequisite present, these commands are all it takes to start
local development: the backend serves on `http://127.0.0.1:3001` and the
frontend on `http://127.0.0.1:3000`. The bootstrap is idempotent and safe to
re-run; it creates the development and isolation-test databases, writes
aligned `backend/.env` and `frontend/.env` files without overwriting values
that already exist, applies the migrations, and seeds two verified accounts.
Re-running it preserves your secrets and accounts. Real email delivery
additionally needs `ARGUS_RESEND_API_KEY` and `ARGUS_RESEND_FROM`
replaced with your own values in `backend/.env`.

## Exploring this project

**Repository shape.** One git repository with two sibling, independently
buildable trees, `frontend/` and `backend/`. There is no root `package.json`
and no root solution file; build commands run with the working directory
inside the relevant tree.

**Product boundary.** The frontend communicates with the product **only**
through the backend REST API. Never allow direct database access from the
frontend. The backend authenticates the trusted application caller with a
constant-time application secret and independently authorizes the acting user
through an opaque session token. CORS and origin headers are not
authentication.

**Backend** (`backend/`).

- ASP.NET Core on `net10.0`, PostgreSQL through Npgsql, one web project at
  `backend/argus.Server.csproj`. Isolation tests live in
  `backend/tests/argus.Server.Tests`.
- It owns customer records, opaque hashed sessions with absolute and idle
  expiry, email-verification and password-reset tokens, argon2id passwords,
  rate limiting, the billing webhook, Resend email, and structured logging
  with the `audit.*` event stream.
- Configuration comes from non-secret defaults plus prefixed environment
  variables only; `backend/.env` is auto-loaded for local development and real
  environment variables win. Unprefixed aliases are ignored, and a non-local
  database URL must carry a password and require TLS.
- Isolation tests require exactly `ARGUS_TEST_DATABASE_URL` and fail
  closed without it. Unit tests never need a database.
- `dotnet run -- seed` creates the two local dev accounts. It is idempotent
  and never resets existing passwords.

**Frontend** (`frontend/`).

- Next.js App Router (pinned), React 19, Tailwind 4, npm. The lockfile is
  committed: use `npm ci`, not `npm install`.
- Server actions and the server-only backend client are the only route to the
  backend. The browser never holds the backend origin or the application
  secret, and the frontend session cookie is not an authorization authority —
  the backend authorizes every call independently.
- Environment values are server-only `ARGUS_*` names. Never introduce
  `NEXT_PUBLIC_*` values that leak the backend origin or a secret.
- Tests use Vitest under `tests/`, mirroring `src/`; `tests/e2e/` is
  Playwright, which builds and serves the app on :3100 itself and needs
  chromium.

**Read more.** `README.md` is the front door; `docs/repository-layout.md` has
the full tree and HTTP surface; `docs/bootstrap-runbook.md` is the first-run
runbook; `docs/secret-rotation.md` and `docs/tls-deployment-contract.md` cover
secrets and public deployment; `docs/divergences.md` records intentional
differences from the Rust sibling kit.

## Verification

Run these before you push. CI runs the same battery.

```bash
# Backend
cd backend
dotnet format argus.Server.csproj --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet test tests/argus.Server.Tests
dotnet build -c Release

# Frontend
cd frontend
npm run lint
npm run test
npm run build
npm run test:e2e
```

`dotnet test` needs `ARGUS_TEST_DATABASE_URL` for the isolation tests;
the bootstrap creates that database and writes it to `backend/.env`.
`npm run test:e2e` needs chromium installed. `.github/workflows/ci.yml` runs
this battery on push and pull request to `main`.

## Kit source only (maintainers)

This section applies only when working on the **kit source** repository, the
tree that produces the kit. It is not applicable in a placed project.

- `docs/instantiation-guide.md` is kit-source-only and is **not present** in a
  placed project; it explains what a kit version is and which paths are
  kit-only. Do not expect it in an instance.
- `kit.json` at the repository root is the kit describing itself: identity,
  version, skip rules, prerequisites, inputs, instantiation operations, and the
  printed next steps. It is omitted from placed projects.
- `documentation/` is the published developer documentation source. It is
  excluded from placed projects and published separately to the website. It is
  not the operational documentation in `docs/`, which does ship.
- `scripts/verify-kit-declaration.mjs` and its test are kit-only tooling. Run
  both before packing a kit version:

  ```bash
  node scripts/verify-kit-declaration.mjs
  node scripts/verify-kit-declaration.test.mjs
  ```

  They check the descriptor facets, that no script writes the origin record,
  that the published documentation carries its required heading profile, and
  that every first-run guidance surface expresses the same command sequence.
- Do not add a root package manifest or a root solution file.
  `.github/workflows/kit-verify.yml` runs the kit checks and is itself
  kit-only.
