# Persistence migration runbook

Operational guidance for the Thread #8 migration from hand-written Npgsql to
Entity Framework Core. The governing contract is the native Plan
`cbb81edc-02ad-46a3-8856-9183f812be51` and Requirement
`cd55a2f8-a42c-4d00-877c-a8c7c032b52c`; this file is operational guidance, not a
second Plan.

**Current state (2026-11-11, Phase 6 complete):** The EF Core model, ten guarded
migrations, the common initialization coordinator and lease, the explicit
canonical backfill, and the synchronous migration-only command are in place.
Every production persistence path uses EF: account authority, finance,
suggestion, seed, statement import and the transaction workspace. No production
file opens a raw connection or executes a driver command, and there is no bare
migration apply outside the coordinator. `Data/Db.cs` and
`Transactions/TransactionSql.cs` are gone. EF history is the schema-progress
authority; existing `schema_migrations` rows remain immutable adoption evidence,
and the `data_migrations` marker remains the only canonical-backfill authority.

## Supported entry points today

- `dotnet run` from `backend/` starts the backend, runs the complete
  initialization protocol asynchronously, and only then maps endpoints and
  starts the reapers.
- `dotnet run -- seed` runs the same protocol and then the explicit account
  seed. It never resets existing account passwords.
- `node scripts/bootstrap-dev.mjs` from the repository root provisions the
  development and isolation databases, writes non-overwriting local env files,
  and invokes the existing migrate/seed path. Its provisioning code is
  unchanged.
- `dotnet run -- migrate` is the internal synchronous migration-only command.
  It runs the same coordinator, applies pending EF migrations and the mandatory
  canonical backfill, then exits without listening, starting reapers or creating
  accounts.

Bare `dotnet ef database update`, migration bundles and externally applied
generated SQL are **not** supported full-initialization paths. The design-time
factory installs public connection and command interceptors that reject database
access in both sync and async variants. Offline model, pending-model and
`migrations script --idempotent` generation remain usable.

No deployment, production migration, mixed old/new initialization or
destructive database reset is authorized by this runbook.

## Target and safety rules

- Use a disposable local PostgreSQL instance or database for adoption and
  performance work. The isolation test database must be a dedicated local
  `argus_test`-equivalent database; the suite truncates it. Never point
  `ARGUS_TEST_DATABASE_URL` at the development `argus` database or production.
- Keep protected copies outside the repository: `pg_dump` data and schema-only
  files, plus the restore-check manifests, belong in a disposable operator
  directory, not in Git, logs or shared memory.
- Do not print URLs, passwords, seed passwords, tokens, financial rows or raw
  statement content in evidence. Counts, digests, durations, query shapes and
  status codes are the intended evidence.
- Initialization requires a session-affine PostgreSQL endpoint. Client-side
  pooling or multiplexing settings do not prove compatibility with a
  transaction-pooling proxy. The approved endpoint/proxy topology must be
  verified before any runtime adoption claim.
- Adoption needs a maintenance window: stop old and new writers and reapers,
  take a restorable backup, migrate a protected clone first, and retain the old
  binary/source and original ledger export. The later coordinator's advisory
  lock serializes conforming initializers only; it does not exclude
  nonconforming old writers or reapers.
- Use forward repair or restore the verified backup after an unresolvable
  initialization failure. Do not rewrite or delete `schema_migrations` rows,
  invent `__EFMigrationsHistory` rows, generate destructive Down operations for
  adoption/history changes, or use generated SQL as complete initialization.
- If a restore would discard later writes, stop and obtain the operator's
  recovery decision.

## Phase 1 baseline protocol

The Phase 1 evidence was captured on a clean tree at
`a4162066353a1d8ccffe040a7a3539183f340965` before any package or persistence
change.

1. Copy the current development database and isolation schema with `pg_dump`
   into a protected directory outside the repository. Record SHA-256 and byte
   size for each dump and schema-only file.
2. Restore both dumps into disposable databases. Compare every per-table row
   count and ordered row digest, every `schema_migrations` name/timestamp and the
   `data_migrations` marker against the protected manifest. Record any
   mismatch; do not proceed on a mismatch.
3. Record the toolchain: .NET SDK/runtime, Node, Npgsql driver, PostgreSQL
   server, host, EF package state and the exact Git commit.
4. Run the test-only performance harness on a dedicated performance database,
   not on the development database and not on the isolation database used by
   behavioral tests. The harness uses real HTTP handlers, real PostgreSQL and a
   public `ActivityListener` on the `Npgsql` source.
5. Use 3 matched rounds. Read paths use 30 samples per round plus warm-ups;
   detail needs additional warm-ups because tiered JIT was visible. Write paths
   use 10 samples per round with restored fixture state. Keep fixture setup
   outside the measured activity window.
6. Calibrate the activity source with explicit `SELECT`, `BEGIN`, `SAVEPOINT`,
   `ROLLBACK TO SAVEPOINT`, `COMMIT`, a multi-statement command and binary
   `COPY` before trusting endpoint counts. Npgsql's internal
   `NpgsqlTransaction` control messages do not emit activities in this version,
   so endpoint transaction-control and physical-open counts are **unavailable**
   from this source, not zero.
7. Retain the raw harness log and protected manifest outside the repository.
   Publish only the sanitized Phase 1 report under
   `docs/evidence/thread-8/phase1-baseline.md`.
8. Do not start Phase 2 if protected-copy access, a safe target, a reproducible
   baseline or a required hot-path measurement is missing.

### Baseline scenarios and sizes

- Authentication: login and session-touch reads.
- Finance inventory: account and category lists.
- Transaction reads: 20,000-row dataset at page sizes 25 and 100, literal
  search, and detail with category/tag suggestion fan-out.
- Import history list.
- Small import lifecycle: create, PATCH, refresh, commit and receipt replay at
  100 rows.
- Medium import: create, commit and receipt replay at 10,000 rows.
- Dense duplicate review refresh.
- Single category edit and explicit category confirmation.
- Lean 50,000-member bulk prepare, apply and replay without the inherited dense
  150,000-link setup.
- Import and transaction cleanup against expired prepared headers.

The existing `TwentyThousandRowMeasurementRecordsTimingsAndPlan` and
`TenThousandRowGenericImportPreparesWithoutCombinatorialWork` measurements run
in the same command. Historical runbook timings are not the baseline.

### Reproduce the baseline

From `backend/`, with `ARGUS_TEST_DATABASE_URL` pointing at a dedicated
disposable performance database:

```text
dotnet test tests/argus.Server.Tests --filter "FullyQualifiedName~PersistencePerformanceTests|FullyQualifiedName~TwentyThousandRowMeasurementRecordsTimingsAndPlan|FullyQualifiedName~TenThousandRowGenericImportPreparesWithoutCombinatorialWork" --logger "console;verbosity=detailed" -warnaserror
dotnet test tests/argus.Server.Tests -warnaserror
dotnet format argus.Server.csproj --verify-no-changes
dotnet format style argus.Server.csproj --diagnostics IDE0005 --severity info --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet format style tests/argus.Server.Tests/argus.Server.Tests.csproj --diagnostics IDE0005 --severity info --verify-no-changes
dotnet build -c Release -warnaserror
```

The filtered command above is the performance run. The default `dotnet test`
command excludes tests marked `Category=Performance`.

## Phase 2 initialization protocol

The coordinator is the only production schema/data apply entry.

1. It creates one EF context with client pooling and multiplexing disabled and
   explicitly opens that connection once. It records `pg_backend_pid()`.
2. It acquires the fixed session advisory lock `(0x41524755, 0x53494E49)` with
   `pg_try_advisory_lock`, waiting briefly with cancellation while another
   conforming initializer holds it. This is not the provider's transaction-scoped
   history lock.
3. Under the continuously held lock it validates raw `schema_migrations` and
   `__EFMigrationsHistory` rows as known ordered prefixes, rejects unknown or
   non-prefix entries, and validates the effective application schema before any
   effect. The validator filters the design-time model by each object's
   introduction migration, so a prefix-0005 database is checked for its full
   0005 schema before EF 0006 runs. It compares types, nullability, defaults,
   generated expressions, index columns/uniqueness/filters/order/validity,
   check expressions, foreign-key columns/targets/delete rules, primary and
   unique constraints, and the merchant-key function. The validator always runs,
   including fully migrated and marker-present re-entry.
4. It calls EF `Migrate`/`MigrateAsync` exactly once. EF owns every migration
   transaction, history row and ordering decision. The ten EF migrations map
   one-to-one to the legacy names; each guard validates and skips an
   already-applied legacy change and lets EF record its own history row.
5. It validates the resulting full schema and checks that the original backend
   session still owns the advisory lock. It then runs the explicit canonical
   backfill in its own EF transaction on the same session, validates again and
   releases the lock exactly once.
6. Connection loss, cancellation, an ambiguous acquire or a failed unlock aborts
   the attempt. The context is disposed and the non-pooled physical session ends;
   protected work never continues on a replacement connector.
7. Normal startup, `dotnet run -- seed` and `dotnet run -- migrate` all use the
   same protocol. The synchronous command blocks on the common asynchronous
   protocol and exits before endpoint mapping, listening, reapers or account
   creation.

The final model validator compares the effective catalog with the design-time
EF model: tables, columns, types, nullability, defaults, generated expressions,
index definitions, check constraints, foreign-key definitions and the
merchant-key function. The model carries both the plain
`categories_customer_id_idx` and the filtered unique
`categories_customer_system_uidx` on `customer_id` as distinct named indexes.

## Phase 2 evidence (2026-11-10)

All commands ran from `backend/` unless stated otherwise. Disposable PostgreSQL
16 databases were used; protected copies remained outside the repository.

| Check | Result |
| --- | --- |
| `dotnet tool restore`; `dotnet restore argus.Server.csproj` | PASS |
| `dotnet ef migrations has-pending-model-changes` | PASS; no pending model changes |
| `dotnet ef migrations list` | PASS; ten migrations listed offline |
| `dotnet ef migrations script --idempotent` | PASS; generated 37,876 bytes offline, not applied |
| Bare `dotnet ef database update` | PASS rejection; design-time interceptor blocked the connection before effects |
| Fresh database + repeated `dotnet run -- migrate` | PASS; EF history 10, `schema_migrations` empty, canonical marker present |
| Legacy development copy `0001`–`0008` adoption | PASS; EF history 10, ledger names/timestamps and all 19 common table row digests unchanged |
| Isolation copy `0001`–`0010` adoption | PASS; EF history 10, ledger names/timestamps and all 19 common table row digests unchanged |
| Focused Phase 2 verification filter (pre-split historical) | PASS 37/37; includes fresh/legacy catalog equality, semantic drift rejection, older-prefix drift rejection, async/sync/mixed lock serialization, ambiguous acquire/unlock, cancellation, interrupted staging, forced session loss, restart and design-time rejection |
| Full backend suite (pre-split historical, includes performance tests) | PASS 366 passed, 1 skipped; the skip is the inherited dense `FiftyThousandMemberBulkCommitsAtomically` fixture |
| Focused Phase 2 migration/model filter (current) | PASS 34/34; includes the quoted-literal and cast/placeholder drift regressions |
| Transaction and statement-import migration classes (current) | PASS 3/3 |
| Default backend suite (performance category excluded) | PASS 363 passed, 1 skipped, 364 total, 6m28s; the skip is the inherited dense `FiftyThousandMemberBulkCommitsAtomically` fixture |
| Performance category | NOT RERUN after the split; 10 tests selected by `--filter "Category=Performance"`; CI runs the separate required step |
| Focused independent review (current baseline) | GO; 0 findings; 37/37 focused migration/model plus import/transaction; direct binary rejects the cast-chain, placeholder-collision, minimal `::text`, and literal-marker altered functions; performance category not rerun |
| Model DDL inspection | PASS; credential foreign keys have no `ON DELETE CASCADE`, and both `categories_customer_id_idx` and `categories_customer_system_uidx` are present |
| Four backend format checks | PASS |
| `dotnet build -c Release -warnaserror` | PASS, zero warnings |
| Async `dotnet run` startup | PASS; endpoint listening only after initialization |
| `dotnet run -- seed` on a fresh disposable database | PASS; two accounts, EF history 10, canonical marker present |

Phase 2 does **not** include the store/query/import/workspace conversion or the
matched before/after performance comparison. Those are Phases 3–5. The frontend
battery is unchanged and was not run in this phase.

## Specialist Phase 2 review corrections

The first independent Phase 2 review returned NO-GO with four HIGH findings and two
MEDIUM notes. The correction pass addressed them as follows:

- Schema validation now checks semantic definitions at every supported prefix,
  not only object names at the final prefix. It rejects changed index
  definitions, defaults, generated expressions, foreign-key delete rules,
  check expressions, unique constraints and the merchant-key function before
  migration effects. Negative tests cover each drift class and older-prefix
  drift.
- The EF model now preserves the non-cascading credential foreign keys and
  models both category indexes with distinct model names. Offline model DDL and
  catalog tests verify the corrected metadata.
- The forced-session-loss test captures the initializer's own backend ID,
  scopes lock observations to the test database, terminates exactly that
  session, and includes a sibling-database negative case.
- The initialization matrix now exercises contention at the migration-commit,
  final-schema/backfill, staging and pre-marker boundaries; no-pending and
  marker-present re-entry; async/sync/mixed entrants; ambiguous acquisition and
  unlock; cancellation; session loss; and restart. Contenders must reach a
  failed lock attempt before the test releases the holder.
- Migration target metadata is decoupled from the generated snapshot into
  `ArgusPhase2MigrationModel` so an ordinary scaffold does not delete the helper the
  designers use. The test-only legacy fixtures are frozen from the public
  `a416206` baseline and no longer reuse the production migration definitions.
- A later independent review found that quoted SQL text was rewritten before
  tokenization: cast and function-header regular expressions could strip text
  inside a string literal. `SchemaValidator` now masks quoted text before any
  rewrite and restores it after tokenization. Real-provider regressions cover a
  merchant-key regex literal containing `::text` and `CREATE OR REPLACE`, and a
  check-constraint literal containing `::text`.
- A further review found two more normalization errors: arbitrary cast removal
  changed executable function behavior, and predictable quote placeholders
  could collide with input identifiers. The validator now tokenizes `::` casts,
  removes only string-type casts applied directly to a single-quoted literal
  with no type modifier, array suffix, or further cast, and does not rewrite
  function-body casts or parentheses. Quote placeholders contain a control
  character and a random GUID, are never case-folded, and are restored only
  from the generated dictionary. Regressions cover a `description::jsonb::text`
  cast chain, a bare `__argus_quoted_1__` identifier, the minimal
  `[^a-z0-9]+::text` literal, and literal text that resembles a placeholder.
- The test-suite split was reviewed separately and accepted. The default run
  excludes the 10 performance tests; CI runs them in a separate required step.
  The performance category has not been rerun locally; its selection is not a
  pass result.

## Phase 3 evidence (2026-11-11)

- Converted account authority, finance entities, suggestion loading and explicit
  seed to EF. `Store`, `FinanceStore`, `SuggestionLoader` and `Seed` execute no
  raw driver commands. The temporary suggestion bridge noted here was removed
  in Phase 5.
- Account, billing, policy, finance and boundary suites: PASS 49/49.
- Migration/model, statement matching and transaction-query suites: PASS 52/52.
  The explicit filter includes the 20,000-row measurement test; the other nine
  performance tests remain separate.
- Framework log privacy: the entire `Microsoft.EntityFrameworkCore` logging
  namespace is suppressed at the host boundary, because framework error events
  can carry provider primary text before the sanitized application catch runs.
  Application and audit logs remain. Regressions cover both an account-insert
  SaveChanges error and a session-touch query error; the sanitized 500 body is
  returned, no row changes persist, and no captured log contains the canary.
- Ordering regression: account, category and merchant-tag lists retain their
  archived and stable display/sort ordering.
- Repeated disposable `dotnet run -- seed`: the first run created both accounts;
  the second kept both. Both accounts had a password hash, a non-null
  `categories_seeded_at`, and 20 categories.
- Full default backend suite: PASS 368 passed, 1 skipped, 369 total, 3m45s.
  The skip is the inherited dense 50,000-member test.
- Complete performance category: PASS 10/10 in 5m06. Query counts match
  Phase 1 for the account/finance/me paths, with no extra round trips or N+1
  growth. Measured medians: `auth.me` 3.31 ms versus 2.64 ms,
  `finance.accounts` 5.84 ms versus 3.61 ms, and `finance.categories` 7.03 ms
  versus 4.12 ms. The remaining difference is EF context/materialization
  overhead. The operator reviewed the measured delta and accepted it on
  2026-11-11; Phase 3 is closed.
- Four format checks, Release build and pending-model: PASS.
- At Phase 3 the raw command files were limited to
  `Data/StatementImportStore.cs`, `Data/TransactionStore.cs` and
  `Transactions/TransactionSql.cs`, with the temporary bridge, facade and
  cleanup callers listed below. That state was superseded by Phases 4-6; the
  final boundary appears later in this document.

### EF-owned SQL escape hatches

The following EF-owned SQL is deliberate; each shape preserves a semantic or
observer boundary that typed EF cannot express safely:

- `Store` keeps `SELECT password_hash ... FOR UPDATE` for the credential-row
  lock and `UPDATE customers SET password_hash` for the reset observer, and
  uses CTE `UPDATE ... RETURNING` for failed-login, session touch/revoke and
  token consumption so the conditional result stays atomic.
- `Store` and `Seed` use `INSERT ... ON CONFLICT ... RETURNING` to preserve
  conflict-safe customer creation.
- `FinanceStore` uses `FOR UPDATE`, `FOR SHARE`, the customer advisory lock and
  ordered transaction-lock queries to preserve the frozen lock order.
- Category, tag and alias normalized-name updates use EF-owned `UPDATE`
  because those columns are alternate keys; EF refuses tracked key changes.
- Seed category inserts use `ON CONFLICT (customer_id, normalized_name) DO
  NOTHING` to preserve seed-once behavior.
- `StatementImportStore` keeps `FOR UPDATE`/`FOR SHARE` lock reads for the
  account, quota row, review header, tags, categories and statement, because
  the frozen lock order is an observer-visible boundary typed EF cannot state.
- Import review rows, committed transactions and tag links are inserted with
  bounded EF-owned `INSERT ... SELECT FROM unnest(...)` statements. Each batch
  is one parameterized command inside the caller's EF transaction, so no
  driver COPY or per-row round trip is used.
- Import row paging and review-header aggregates stay as EF-owned SQL because
  they depend on `NULLS LAST` ordering, jsonb containment and correlated
  counters; import history keeps the tuple cursor comparison.
- Clock authority is explicit per operation. Database `clock_timestamp()` is
  used for commit expiry and transaction admission/terminal timestamps.
  Database `NOW()` is used for cleanup scrubbing, abandon, statement
  visibility, the legacy fallback Deleted receipt, the existing-receipt Deleted
  update and the account import-version bump. The tracked review, statement and
  commit-receipt inserts keep the application-supplied `createdAt` and
  `updated_at` values they always used; those are the explicit exception.
- `TransactionStore` keeps `FOR UPDATE`/`FOR SHARE` lock reads for the customer,
  account, header, members, categories and tags because the frozen lock order
  and wait scope are observer-visible. The conditional header read stays
  EF-owned SQL for the same reason.
- Transaction list/count/all-matching capture share one typed filter and one
  ordered query builder. Visibility counts remain one aggregate scan with two
  filtered counts, matching the former single-statement shape.
- Transaction manifests are frozen with bounded `unnest` inserts and tag links
  mutate with `unnest` joins, all inside the caller's EF transaction. Terminal
  header writes and the applied receipt use `clock_timestamp()`; the applied
  `UPDATE ... RETURNING` runs through `FromSql` because a data-modifying CTE
  cannot be nested inside `SqlQuery`.

## Remaining raw callers

No production file creates driver commands, readers or COPY. The account,
finance, suggestion, seed, statement import and transaction workspace all use
EF. `Transactions/TransactionSql.cs` is replaced by the typed
`Transactions/TransactionQuery.cs`, and the raw `SuggestionLoader` bridge is
gone. `Data/Db.cs` is deleted; its connection-string builder moved to
`PersistenceOptions`, and the test harness owns a test-only `TestDatabase`
helper. Only `InitializationCoordinator` applies migrations, and the boundary
tests enforce both the empty raw-caller inventory and the absence of direct
connection, command, reader or COPY construction in production sources.

Test harnesses keep direct Npgsql for setup, independent lock/session
observation and fault injection. The test host removes the two background
cleanup hosted services so their startup pass cannot race a shared-database
reset; tests drive the cleanup stores directly.

Tracked EF writes can wrap a provider failure in `DbUpdateException`, so the
import endpoint unwraps it through `PersistenceErrors` and logs only
`error_class=postgres` and `sql_state`. A real-trigger regression prepares a
review, fails the tracked statement insert with a provider message containing
the stored description, and asserts the fixed 500 body, unchanged review and
rows, no statement, transaction or receipt rows, no success audit, and no
canary in any captured log. The same test fails without the unwrapping catch.

### Phase 4 measurements

Phase 4 was measured with the same 30-sample, three-round calibration used for
Phase 1. Medians in milliseconds, Phase 1 baseline then Phase 4:

- `import.small.create` 16.84 then 25.78 (p95 43.05); patch 64.19 then 86.25
  (p95 100.17); refresh 63.97 then 87.07 (p95 93.10); commit 20.43 then 33.02
  (p95 38.12, one round-one outlier at 193.48); replay 6.75 then 10.33
  (p95 14.16).
- `import.medium.create` (10,000 rows) 320.04 then 432.36 (p95 483.45, round
  medians 417.23 to 452.07); `import.medium.commit` 563.25 then 739.18 (p95
  825.37, round medians 677.95 to 767.23); `import.medium.replay` 9.48 then
  13.25 (p95 32.30).
- `import.dense.refresh` 230.27 then 284.71 (p95 331.65, round medians 279.12
  to 304.28); `cleanup.import` 4.56 then 5.94 (p95 7.38).

The extra time is consistent with building an EF context and serializing JSON
payloads per request, but that attribution is an inference, not a profile. The
10,000-row writes are bounded `unnest` batches rather than one COPY; across the
30 samples create reported 260 insert and 34 batch activities and commit
reported 150 insert, 60 batch, 60 delete and 120 savepoint-classified
activities, with zero COPY activities in both, while Phase 1 reported one COPY
per sample. Replay carries one extra select because the ownership probe now
checks the review and the receipt as separate EF reads. Counter figures are
observed activities, not wire-round-trip totals; internal transaction-control
and physical-open counts were unavailable before and remain unavailable. All 10
performance tests passed; functional assertions in those tests are not latency
acceptance. The operator accepted this measured trade-off against this baseline on
2026-11-11, including the EF/JSON overhead and the extra replay select.

### Phase 5 measurements

Same 30-sample, three-round calibration. Medians in milliseconds, Phase 1
baseline then Phase 5 (p95 in parentheses where it matters):

- `transactions.query25` 12.52 then 34.81 (37.37); `transactions.query100`
  14.18 then 35.80 (38.03); `transactions.search` 35.37 then 36.77;
  `transactions.detail` 24.88 then 22.82.
- `transactions.edit` 9.22 then 11.12; `transactions.confirm` 9.02 then
  10.40.
- `bulk.50k.prepare` 548.20 then 550.51; `bulk.50k.apply` 2381.38 then
  2428.79 (2%); `bulk.50k.replay` 4.47 then 4.44.

Activity counts match Phase 1 exactly for the list, search, detail, edit,
confirm, bulk apply, bulk replay and import cleanup paths. Bulk preparation
swaps the former one COPY per sample for ten bounded manifest inserts (330
insert activities versus 30) with equivalent median latency. The list-query
latency gap carries no extra statement work; a Phase 4 run of the same harness
while `TransactionStore` was still raw measured 46.57 and 46.75 ms for
query25/query100, and that run also recorded an implausible 358.51 ms detail
median, so it cannot serve as a clean A/B attribution. The remaining gap is
consistent with EF materialization and context overhead but is not profiled.
The operator explicitly accepted this trade-off on 2026-11-11.

## Known legacy ledger prefixes

At Phase 1 the protected development copy held schema migrations `0001` through
`0008`; the protected isolation copy held `0001` through `0010`. Both held the
`0006_canonical_identity_backfill` marker. The ten legacy migration names are:

```text
0001_customer_accounts
0002_email_verification_tokens
0003_password_reset_tokens
0004_failed_login_lockout
0005_finance_foundation
0006_statement_import
0007_transaction_workspace
0008_suggestion_learning
0009_review_category_suggestion
0010_transaction_category_confirmation
```

The 0006 canonical backfill reconstructs statement-row fingerprints and identity
keys from the persisted review rows, including the intentional historical
omission of stored source IDs and validation codes. The marker is the only
authority for whether that transformation has run.

The EF migration mapping preserves existing `schema_migrations` rows and
timestamps, records EF history for the mapped migrations, preserves the existing
marker, and never fabricates legacy history on a fresh database. The generated
idempotent EF SQL is a schema-inspection artifact only. It does not acquire the
coordinator's session lock, run the full validator or perform the explicit
canonical backfill.

## Recovery and rollback

- Keep the old binary/source and a restorable backup available during adoption.
- Do not declare ordinary rollback safe merely because table names match.
  Quiesce, compare schema/data, and use an operator-approved recovery path.
- Do not generate destructive Down migrations for the adoption/history/backfill
  transition. Down must refuse unsupported reversal.
- Migration and backfill retries resume from their own durable records. Never
  delete EF history rows or rewrite `schema_migrations` to force a retry.
- A failed initialization must not start endpoints, reapers or account seeding.
  Preserve the sanitized failure evidence and correct it before retrying.

## Phase 6 updates still required

## Phase 6 final evidence (2026-11-11)

- **Boundary.** `PersistenceBoundaryTests` passes three checks: no converted
  path contains raw command/COPY spelling, the raw-caller inventory is empty,
  and no production file outside `Data/InitializationCoordinator.cs` applies
  migrations. A direct-execution scan rejects `new NpgsqlConnection`,
  `NpgsqlCommand`, readers, command creation and COPY across production sources.
  `Data/Db.cs` is deleted; the test harness owns its connection helper.
- **Fresh creation.** Disposable database `argus_t8_phase6`: `dotnet run --
  migrate` exited 0 twice; the second run changed nothing. Result: ten EF
  history rows, zero `schema_migrations` rows, zero customers and zero
  categories, so no listener, reaper or seed ran.
- **Offline design time.** `dotnet ef migrations list --no-connect` lists the
  ten migrations and reports that applied status is unavailable offline;
  `migrations script --idempotent` generated 37,878 bytes without a database
  connection; `has-pending-model-changes` reports none. The design-time
  database-apply rejection test still passes.
- **Seed twice.** On the same fresh database, `dotnet run -- seed` exited 0
  twice. Both accounts were verified, each seeded 20 categories (40 total), and
  the password hashes, verification timestamps and seeding markers were
  byte-identical across runs.
- **Protected-copy adoption.** The protected development dump was restored into
  disposable `argus_t8_adopt` and `argus_t8_adopt2` (originals untouched), then
  `migrate` ran twice with the final binary, both exit 0. All eight
  `schema_migrations` names and timestamps match the Phase 1 manifest exactly,
  the canonical marker remains, and EF history has ten rows. All 388 transactions
  remain with every legacy column byte-identical under per-column digests; the
  new `category_confirmed` column is TRUE for all legacy rows, matching the
  legacy default. Counts match the manifest: 2 customers, 20 sessions, 31
  categories, 78 tags, 297 tag links, 3 receipts and 7 operations.
- **Authenticated walkthrough.** On a fresh disposable database seeded with a
  pinned password, the final binary served real HTTP: login 200, account create
  201, generic import prepare 201, commit 201 with `replayed=false`, then the
  process was stopped and restarted, and the same commit returned 200 with
  `replayed=true`. The database held exactly one transaction, one statement and
  one receipt; the review header and rows were purged. No source text or secrets
  were printed.
- **Test batteries.** Full backend suite: 372 passed, 1 inherited skip. Frontend
  unchanged battery: Prettier check, ESLint, 283 unit tests, production build and
  17 Playwright tests pass. Complete performance category: 10 of 10.
- **Not run.** `node scripts/bootstrap-dev.mjs` was not run; the CLI script is
  unchanged and its migrate/seed steps were exercised directly on disposable
  targets. The inherited dense 50,000-member stress test remains skipped and is
  not claimed as passed.
