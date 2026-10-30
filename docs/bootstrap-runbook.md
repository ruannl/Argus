# Bootstrap runbook

First run in a placed project, through verified local development. This is the
same sequence printed by the CLI and by the bootstrap itself.

## Prerequisites

None of these is installed for you.

- **.NET 10 SDK (dotnet)** — builds and runs the backend.
- **Node 22 and npm** — runs the bootstrap and the Next.js frontend.
- **PostgreSQL** — a running Docker container named `postgres_db`, or a local
  PostgreSQL with `psql` and `createdb` on `PATH`.
- **openssl** — used to generate local secrets.

## Step 1: run the bootstrap

```text
node scripts/bootstrap-dev.mjs
```

The bootstrap:

1. verifies `openssl`, `dotnet`, and PostgreSQL availability;
2. creates the development database and the isolation-test database;
3. writes `backend/.env` and `frontend/.env`, generating the application
   secret, the billing webhook secret, and the Auth.js secret, and writing the
   development Resend placeholder;
4. applies migrations and seeds two verified accounts.

It is idempotent. Existing keys are never overwritten, and re-running it
preserves your secrets and accounts. To use pinned seed passwords, set
`ARGUS_SEED_DEV_PASSWORD` and `ARGUS_SEED_USER_PASSWORD` in
`backend/.env` (or the shell environment) before the first run.

## Step 2: start the servers

```text
cd backend
dotnet run

cd frontend
npm ci
npm run dev
```

The backend serves on `http://127.0.0.1:3001`; the frontend on
`http://127.0.0.1:3000`.

## Step 3: sign in and verify

The bootstrap prints the seeded account passwords. Sign in at
`http://127.0.0.1:3000/login`:

- `dev@example.test` — admin and lifetime account;
- `user@example.test` — verified subscriber.

Registration, verification, and reset links are written to the backend log
instead of being delivered while the Resend key is the development
placeholder.

## Step 4: verify the installation

```bash
cd backend
dotnet test tests/argus.Server.Tests
dotnet build -c Release

cd ../frontend
npm run lint
npm run test
npm run build
```

`dotnet test` uses `ARGUS_TEST_DATABASE_URL`, created by the bootstrap.

## Troubleshooting

- **`dotnet` not found** — install the .NET 10 SDK so `dotnet --version`
  works.
- **PostgreSQL not found** — start a Docker container named `postgres_db`, or
  install the PostgreSQL client tools so `psql` and `createdb` resolve.
- **Port already in use** — stop the process on 3000/3001, or change
  `ARGUS_PORT` (backend) and the frontend dev port together, then update
  `ARGUS_FRONTEND_URL`/`ARGUS_BACKEND_URL` so the origin check
  still matches.
- **Backend refuses to start with a configuration error** — it is
  fail-closed: a missing or short `ARGUS_API_SECRET`, an invalid
  `ARGUS_FRONTEND_URL`, or a non-local database URL without a password
  and TLS is rejected on purpose.
