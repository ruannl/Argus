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
3. provisions the non-login owner, migrator and restricted-runtime roles;
4. writes a runtime-only `backend/.env`, an ignored `backend/.env.test` for the
   test-maintenance URL, and an ignored `backend/.env.maintenance` for the
   migrator URL, plus `frontend/.env`; it generates the application secret, the
   billing webhook secret and the Auth.js secret, and writes the development
   Resend placeholder;
5. runs `dotnet run -- migrate` and `dotnet run -- seed` in separate maintenance
   children that receive only the migrator URL.

It is idempotent. Existing keys are never overwritten, and re-running it
preserves your secrets, role passwords and accounts. To use pinned seed
passwords, set `ARGUS_SEED_DEV_PASSWORD` and `ARGUS_SEED_USER_PASSWORD` before
the first run; the bootstrap forwards them only to the seed child.

## Step 2: start the servers

```text
cd backend
dotnet run

cd frontend
npm ci
npm run dev
```

The backend validates the prepared schema and the restricted runtime role
before serving; it refuses to start if maintenance is pending or the role
configuration drifted. The backend serves on `http://127.0.0.1:3001`; the
frontend on `http://127.0.0.1:3000`.

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
node scripts/run-backend-tests.mjs

cd frontend
npm run lint
npm run test
npm run build
```

`run-backend-tests.mjs` reads the ignored `backend/.env.test` and supplies only
`ARGUS_TEST_DATABASE_URL` to the test process. An already scoped environment
value wins. Never place that test-maintenance URL in `backend/.env`.

Maintenance commands are explicit:

```text
cd backend
dotnet run -- migrate
dotnet run -- seed
```

They read `backend/.env.maintenance` or a process-scoped `ARGUS_DATABASE_URL`
and never start the web host.

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
