---
name: argus-verify
description: Select and run Argus verification for backend, frontend, or cross-stack changes, including PostgreSQL isolation and Playwright checks.
---

The Verification section of [AGENTS.md](../../../AGENTS.md) lists the full pre-push suite. Use the affected project and behavior to choose earlier focused checks.

- For a pure backend change, select relevant database-free tests with `dotnet test tests/argus.Server.Tests --filter <expression> -warnaserror` from `backend/`. Read the test class first. A class using the API test base or PostgreSQL needs the isolation database.
- Before running isolation tests, verify that `ARGUS_TEST_DATABASE_URL` points to a dedicated local `argus_test` database. Keep credentials out of output. If no suitable database exists, continue with database-free checks and follow the setup approval in AGENTS.md before using bootstrap.
- For frontend logic and components, run `npm run format:check`, `npm run lint`, and `npm run test` from `frontend/`. Include the production build for App Router, configuration, or server-only changes. Use `npm run test:e2e` for browser flows with Chromium installed. Playwright builds and serves the app itself.
- For a bug fix, verify the regression fails against the old behavior and passes with the fix where feasible. Report the exact checks run and any prerequisites that blocked coverage.
