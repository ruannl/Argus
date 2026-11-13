---
name: argus-security-review
description: Assess, review, or verify Argus security boundaries and public-launch evidence, including OWASP ASVS 5.0.0 Level 2 checks.
---

Use this skill when work touches authentication, sessions, authorization, finance or import data, user-content rendering, uploads, logging, secrets, deployment configuration, or a security assessment.

Read first: the approved Requirement and current decisions on Thread #9, the product boundary in [AGENTS.md](../../../AGENTS.md), `docs/product-brief.md` (`SEC-*`, `AC-SEC-*`), `docs/tls-deployment-contract.md`, `docs/secret-rotation.md`, and the affected route in `docs/repository-layout.md`.

## Authority

- Thread #9 owns security findings, coverage, evidence, and risk decisions. This skill is guidance, not approval, a Plan, or a substitute for the independent assessment.
- Do not infer deployment permission, production probing, PDF re-enablement, feature expansion, or credential access from a security task. Each needs explicit operator authorization.
- The agreed assessment baseline is `origin/main` at `1faa727` in a local synthetic environment, using the dedicated `argus_security` database and its two synthetic verified accounts on Node 26.10.0 (a recorded operator-accepted deviation from the CI Node 22 pin). Name the baseline, database, and runtime versions in every evidence item. Do not collect evidence against the populated `argus` development database or the `argus_test` isolation suite. Deployment security is UNVERIFIED. If the baseline moves, re-check evidence before reusing it.

## Boundary invariants

- The browser never receives the backend origin or `ARGUS_API_SECRET`; it talks only to the Next.js origin. Server actions and `frontend/src/lib/server/backend-client.ts` call the backend server-side.
- Every protected backend call rejects a mismatched `Origin` or frontend-origin header when present, proves the application secret in constant time, validates an opaque session token, and requires a verified active subscriber (`FinanceGuard`, `Trust`). An absent origin header passes that check, so the binding caller proofs are the application secret and the session token. CORS, cookies, hidden controls, and origin headers alone are not authorization.
- Every finance read and write is scoped by `customer_id`. Missing and foreign identifiers return the same `404 {"error":"not_found"}` and mutate nothing.
- `/admin` is account-state only and never exposes financial data. The frontend session cookie is not backend authorization.

## Data and privacy

- Financial descriptions, amounts, statement contents, user category and tag names, statement metadata, and raw filenames never enter application logs, `audit.*` events, analytics, URLs, or browser-visible payloads.
- Log identifiers, statuses, counts, and `SqlState` only. Never log a whole `PostgresException` or user-supplied values; use sanitized canaries in tests.
- Import bounds are frozen in `backend/Imports/ImportLimits.cs`: file and decompressed size, rows, pages, extracted characters, parser output, multipart and JSON bodies, page size, review quotas, and parser concurrency. Do not raise them at the proxy or in code to make a test pass.
- Archives reject multiple, nested, encrypted, traversal, symlink, and lying-expansion entries. Parsed content is never executed. Raw uploads are discarded after commit or abandoned review. PDF import remains deferred; do not re-enable it here.
- Use synthetic accounts, records, uploads, and canaries. Never put real credentials, customer data, or private statement content in prompts, logs, evidence, or commits.

## Rendering and transport

- React escaping is the default. Do not add raw HTML for untrusted content; the constant theme bootstrap is the only inspected `dangerouslySetInnerHTML` use.
- Keep the CSP and security headers in `frontend/next.config.ts`, the production-only removal of `unsafe-eval`, and `logging.serverFunctions: false`. Changing these can reopen known injection or disclosure paths.
- Production cookies are `httpOnly`, `SameSite=Lax`, and `secure`; keep `ARGUS_AUTH_TRUST_HOST` off outside trusted proxies. Non-local database URLs require a password and `sslmode=require` or `verify-full`.

## Query and input handling

- Prefer EF Core parameterization. Raw SQL is an explicit review boundary: every `ExecuteSql*` or `FromSql*` call that includes data must use the interpolated API or parameters, and every customer-scoped statement must still predicate on `customer_id`.
- JSON bodies reject unknown fields and malformed shapes. Duplicate keys are not explicitly rejected (System.Text.Json is last-value-wins), so assess them rather than assuming rejection. Treat a new endpoint or field as untrusted input until its size, type, ownership, and rejection behavior are covered.
- Keep the per-IP, per-customer, and per-token rate limits; they are in-process and per-instance by design, so edge protection remains a deployment responsibility.

## Verification

- Use `argus-verify` for command selection. Prefer focused tests while changing code; run the full pre-push battery before publication.
- Security evidence must name the baseline commit, database, runtime versions including Node, environment, exact check, and a result of PASS, FAIL, NOT RUN, or UNVERIFIED. A green scanner, a framework default, or code inspection is not an acceptance verdict.
- Useful starting points: `FinanceContractTests` (`CrossCustomer_ProbesReturnIdenticalNotFoundAndMutateNothing`, `FinanceRoutes_RequireTrustedCallerSessionAndActiveSubscription`, the log-canary tests), `StatementSafetyTests`, `PersistenceBoundaryTests`, `StatementImportContractTests`, and `TransactionMutationTests`.
- Add a regression test for every corrected finding where feasible. If that is infeasible, record the limitation and the strongest alternative evidence; do not claim equivalent coverage.

## Review and completion

- The independent assessment is performed by the Specialist, not the implementer. The Generalist may prepare the baseline, run supporting checks, and implement authorized corrections.
- Record each finding on Thread #9 with evidence, practical impact, applicable authority, and disposition. Blocking findings must be corrected and re-verified against the same baseline before completion; nonblocking residual risks need an explicit operator disposition.
- Do not claim public-launch readiness while deployment evidence is unavailable or blocking findings remain. Completion does not authorize deployment.
- Preserve the approved product contracts: import lifecycle, exact amounts, customer isolation, and admin exclusion. Flag a conflict with the governing Requirement or an accepted plan instead of quietly changing meaning.

## ASVS 5.0.0 Level 2 scope

Before collecting evidence, record the coverage map on Thread #9. It must name cross-site request forgery and dependency and integration checks explicitly, alongside the other agreed categories. Map the agreed categories to the applicable ASVS chapters: encoding and sanitization, validation and business logic, web frontend security, API and web service, file handling, authentication, session management, authorization, configuration, data protection, secure communication, and security logging and error handling. For dependency evidence, use the CI gates (`dotnet list package --vulnerable --include-transitive` and `npx audit-ci@^7 --config ./audit-ci.jsonc`) and record their exact output. Record justified exclusions and compensating controls. Do not imply certification or complete compliance.
