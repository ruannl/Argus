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

## Working agreement

These rules apply to every change in this repository, whoever makes it.

1. Work on a non-main branch. `main` is protected: no direct commits or
   pushes; changes land only through pull-request merges.
2. Merge only after an independent reviewer other than the author approves,
   all required checks pass, and blocking findings are resolved. Failed,
   skipped, or missing required checks do not qualify. The pull request must
   record what changed and why, the exact verification commands and results,
   known risks, out-of-scope findings, and the disposition of nonblocking
   notes.
3. Commit or change remote state only when explicitly authorized, including
   through a named workflow. Inspection is read-only. Never reactivate
   user-closed items or rewrite published history without explicit permission.
   Ask when authorization is unclear.
4. Do not delete, skip, weaken, or mock away tests to hide defects. Correct an
   obsolete or incorrect test only against the intended contract, and explain
   why. A bug fix needs a regression test that fails before and passes after;
   if that is infeasible, report the limitation and the strongest alternative
   evidence without claiming equivalent coverage.
5. Reviewed rules and accepted artifacts define intended behavior; code may be
   wrong. Flag conflicts and follow the governing authority; ask if it is
   unclear. Treat `NeedsReview` as unconfirmed. Keep facts durable, current,
   and nonduplicated; record progress and task decisions on Threads. Verify
   factual claims when they are material or uncertain.
6. Challenge the user's assumptions and your own. Verify consequential
   premises; explain errors, risks, and better options with evidence. State
   uncertainty and distinguish defects from preferences. Aim for verified
   working software, not agreement or contrarianism. Raise concerns early.
   After an informed user decision, proceed within the required safeguards and
   record material concerns without relitigating.
7. When a primary peer request targets the Specialist slot, capture the
   `requestId` returned by `primary_transfer prepare` and relay it to the
   Specialist or operator immediately. The Specialist publishes the completed
   review through `primary_transfer` action `reply` using that active
   `requestId`. A completed request cannot be reused. If the `requestId` is
   missing, obtain the active one; do not rerun the review.

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

## Code style

- Use `backend/.editorconfig` for C#. Use Prettier for frontend source and test
  formatting and `frontend/eslint.config.mjs` for Next.js linting.
- Prefer explicit types over `var`, including `out` variables and tuple
  deconstructions. The `.editorconfig` records this at suggestion level, so the
  IDE applies it but `dotnet format --verify-no-changes` does not enforce it.
- In C# declarations with four or more parameters, keep the first parameter on
  the declaration line and align the remaining parameters under it, one per
  line. Shorter lists can stay inline when readable. Wrap call sites as needed
  for readability.
- C# has no fixed maximum line length. Join statements when the result is at
  most 240 characters. When a boolean condition or return expression spans
  lines, break after `&&` or `||` and align the continuation under the first
  operand.
- Put a blank line after a guard clause that returns or continues, and before a
  final `return`.
- In a `catch` block, pass the caught exception as the first argument to the
  logger. Do not pass exceptions that can embed user-supplied data, notably
  `PostgresException` on finance and import paths; log `SqlState` and the
  error class instead.
- Remove unused C# usings and sort the rest with `System` first.
- Format and lint the code you touch. Keep a repository-wide formatting pass
  separate from feature changes. Load `argus-code-style` for cleanup and
  `argus-verify` when selecting verification. Use `argus-api-change` for
  coordinated REST API and frontend changes.

## Verification

Run these before you push. CI runs the same battery.

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
npm run format:check
npm run lint
npm run test
npm run build
npm run test:e2e
```

`dotnet test` needs `ARGUS_TEST_DATABASE_URL` for the isolation tests;
the bootstrap creates that database and writes it to `backend/.env`. Export
the value before running the tests. The suite truncates finance tables and
creates its own rows, so the URL must name the dedicated `argus_test`
database — never the development `argus` database or production.
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
