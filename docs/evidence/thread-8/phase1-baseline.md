# Thread #8 Phase 1 — protected baseline and old-path measurements

**Status:** Phase 1 baseline captured before any package, migration, or persistence change. The conversion has not started on this evidence.
**Date:** 2026-11-10
**Branch:** `feature/ef-core-persistence`, from clean `main` at `a4162066353a1d8ccffe040a7a3539183f340965`
**Governing Plan:** native Plan `cbb81edc-02ad-46a3-8856-9183f812be51`, semantic revision 2, approved content_version 5. Requirement `cd55a2f8-a42c-4d00-877c-a8c7c032b52c`, Ready revision 2 at requirement_version 4.
**Execution session:** `ses_e3bc0c8d3ffeDCoXpx1HPpJ1Aa`

## Toolchain and runtime

| Item | Observed value |
| --- | --- |
| .NET SDK | 10.0.114 |
| .NET runtime reported by the test host | 10.0.14 |
| Node | v26.10.0 |
| Npgsql driver | 10.0.3 (application and test projects) |
| PostgreSQL server | 16.15 (Debian 16.15-1.pgdg13+2) |
| Host | WSL2, Linux 6.18.33.2-microsoft-standard-WSL2 |
| EF Core packages | not installed in this baseline |

## Protected data copies

The development database `argus` was at legacy schema migrations 0001–0008 with the canonical backfill marker present. The isolation database `argus_test` was at 0001–0010 with the same marker. Both were copied with `pg_dump` before any test reset. The dumps and schema-only SQL are outside the repository at `/tmp/argus-thread8-baseline/`.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `argus_dev.dump` | 120765 | `aafb7f36f010be34710fc80c406062291dba356c9ab62d92568bf0765cb4ebf2` |
| `argus_dev_schema.sql` | 41024 | `46383c5b1db808595114278275e30dd7774f5ebff9c005b0bd8bdd23aa231029` |
| `argus_test.dump` | 67271 | `3f7404b6eb734b1fd8e03b0a7778d388f983052c4235c69d2b73a4adfd4946f8` |
| `argus_test_schema.sql` | 41432 | `add64f38710d10a17842c4142238f7118062b73c71659a459410a638dcfd1439` |

Restore check: both dumps were restored into disposable databases `argus_t8_dev_restore` and `argus_t8_test_restore` on the local PostgreSQL 16 instance. Every per-table row count and ordered row digest, every `schema_migrations` name/timestamp and the `data_migrations` marker matched the protected manifest. The restore-check result is `True`.

## Schema and ledger inventory

- Application tables: 17, plus `schema_migrations`, `data_migrations`, and the generated `argus_merchant_key` function. Object counts: development copy 63 indexes, 94 constraints, 1 function; isolation copy 64 indexes, 94 constraints, 1 function.
- Full schema DDL is in the protected schema-only SQL files. The migration source remains the ten in-code migrations in `backend/Data/Db.cs`; no EF migration files exist in this baseline.
- Development schema ledger: 0001 through 0008. Isolation schema ledger: 0001 through 0010. The canonical marker `0006_canonical_identity_backfill` is present in both.
- The 0009 review-category columns and 0010 transaction-category-confirmation column/index are present only in the isolation copy, matching the current code's migration set.

### Development copy per-table manifest

| Table | Rows | Ordered row digest |
| --- | ---: | --- |
| `categories` | 31 | `a60313814df4f100fa1f1f956aef3619` |
| `customers` | 2 | `7c2181748d5ec9a0e9fa722cf9ebed02` |
| `data_migrations` | 1 | `8c5fbf054ecf5b70c594ac854e49cfaf` |
| `email_verification_tokens` | 0 | `EMPTY` |
| `financial_accounts` | 2 | `2bd5305f3ef30f29bfd76ad02f672f89` |
| `import_commit_receipts` | 3 | `85222227dfc2f11f896eb73af7ebb09e` |
| `import_quota_locks` | 1 | `39897f754b25cc7a6089ca720b12cc34` |
| `import_review_rows` | 0 | `EMPTY` |
| `import_reviews` | 0 | `EMPTY` |
| `merchant_aliases` | 0 | `EMPTY` |
| `merchant_tags` | 78 | `3b75ca6b230d1c6ab9a0761476a5d243` |
| `password_reset_tokens` | 0 | `EMPTY` |
| `schema_migrations` | 8 | `eeaa7c86750c52f519606f46cc609207` |
| `sessions` | 20 | `9a74c0bffd6c629d66a66eab51f6cc77` |
| `statement_imports` | 3 | `f22353e8d80ad47040ab8843f0f05866` |
| `transaction_merchant_tags` | 297 | `1ae5e7add4d2e9f6f9e3f0fb02c67fd6` |
| `transaction_operation_items` | 0 | `EMPTY` |
| `transaction_operations` | 7 | `03054eaa743dca0c95e1f72cc2861873` |
| `transactions` | 388 | `73dadb65cdf2135cddf092eb87cee802` |

### Isolation copy per-table manifest

| Table | Rows | Ordered row digest |
| --- | ---: | --- |
| `categories` | 20 | `32a673879a615ed2395cabf7d0d10278` |
| `customers` | 1 | `30de2d21bc798aaad5567c620f613478` |
| `data_migrations` | 1 | `4a7ec129b06229c66ef467baa79da840` |
| `email_verification_tokens` | 1 | `9b19845433cee6bd48b743b489d10948` |
| `financial_accounts` | 1 | `1b3b008f2b871a3b2e8c2711ebfd1ff7` |
| `import_commit_receipts` | 1 | `cde769389dc07a2132f715e7fd63d01d` |
| `import_quota_locks` | 1 | `8f3bb6d55989972d3a637a8e0915a451` |
| `import_review_rows` | 0 | `EMPTY` |
| `import_reviews` | 0 | `EMPTY` |
| `merchant_aliases` | 0 | `EMPTY` |
| `merchant_tags` | 0 | `EMPTY` |
| `password_reset_tokens` | 0 | `EMPTY` |
| `schema_migrations` | 10 | `d4dd4110f63a69db13693b5fbe751e47` |
| `sessions` | 1 | `1b885a8ee3b4aba93500c9bcf82c6722` |
| `statement_imports` | 1 | `6d8d7cd4b672ba10249762b9bf9ad5a5` |
| `transaction_merchant_tags` | 0 | `EMPTY` |
| `transaction_operation_items` | 0 | `EMPTY` |
| `transaction_operations` | 0 | `EMPTY` |
| `transactions` | 3 | `e9034c9ff2ba4f1fed3de1e0d7f411ee` |

### Development ledger

| Ledger entry | Recorded at |
| --- | --- |
| `0001_customer_accounts` | `2026-10-29 15:32:37.74171+00` |
| `0002_email_verification_tokens` | `2026-10-29 15:32:37.764205+00` |
| `0003_password_reset_tokens` | `2026-10-29 15:32:37.774486+00` |
| `0004_failed_login_lockout` | `2026-10-29 15:32:37.786232+00` |
| `0005_finance_foundation` | `2026-10-31 11:16:09.832446+00` |
| `0006_statement_import` | `2026-11-04 11:24:20.754318+00` |
| `0007_transaction_workspace` | `2026-11-07 07:21:40.923005+00` |
| `0008_suggestion_learning` | `2026-11-09 07:47:49.536061+00` |

| Ledger entry | Recorded at |
| --- | --- |
| `0006_canonical_identity_backfill` | — |

### Isolation ledger

| Ledger entry | Recorded at |
| --- | --- |
| `0001_customer_accounts` | `2026-11-09 13:24:08.171788+00` |
| `0002_email_verification_tokens` | `2026-11-09 13:24:08.202179+00` |
| `0003_password_reset_tokens` | `2026-11-09 13:24:08.223019+00` |
| `0004_failed_login_lockout` | `2026-11-09 13:24:08.238151+00` |
| `0005_finance_foundation` | `2026-11-09 13:24:08.241286+00` |
| `0006_statement_import` | `2026-11-09 13:24:08.306254+00` |
| `0007_transaction_workspace` | `2026-11-09 13:24:08.370208+00` |
| `0008_suggestion_learning` | `2026-11-09 13:24:08.3929+00` |
| `0009_review_category_suggestion` | `2026-11-10 07:35:27.004434+00` |
| `0010_transaction_category_confirmation` | `2026-11-10 07:35:27.011364+00` |

| Ledger entry | Recorded at |
| --- | --- |
| `0006_canonical_identity_backfill` | — |

## Measurement method

- New test-only `PersistencePerformanceTests` and `PersistencePerformanceSupport` in the backend test project. They use a public in-process `ActivityListener` on the Npgsql source and the real HTTP handlers through the test host and real PostgreSQL.
- Dedicated disposable performance PostgreSQL instance `argus_t8_perf` on port 55432 (PostgreSQL 16.15); database `argus_perf2` was fresh for the final run. The isolation `argus_test` remains separate.
- Reads: 3 matched rounds × 30 samples per scenario, with warm-ups. Detail had 40 additional warm-up calls because Npgsql/JIT tier-up was visible in earlier runs.
- Writes: 3 matched rounds × 10 samples per scenario, with restored fixture state between samples where the path mutates rows. The 10,000-row import and lean 50,000-member bulk fixtures were measured at those sizes; the inherited dense 150,000-link test was not run or unskipped.
- Activity calibration: explicit `SELECT`, `BEGIN`, `SAVEPOINT`, `ROLLBACK TO SAVEPOINT`, `COMMIT`, multi-statement command and binary `COPY` produced observable activities and counts: select=2, copy=1, begin=1, commit=1, rollback=1, savepoint=1, batch=1, other=1, opens=0 (the other activity was `CREATE TEMP TABLE`).
- Each measurement resets the activity list immediately before the call, so fixture setup is excluded. Background cleanup services run at startup and then every 15 minutes; no second run occurred during the measured windows.
- `begin`/`commit`/`rollback` counts are zero for endpoint calls because Npgsql's internal `NpgsqlTransaction` control messages are not emitted as activities, although explicit transaction-control commands are observable in calibration. Endpoint transaction-control counts are therefore **UNVERIFIED / unavailable from this activity source**, not evidence of zero transactions. Physical connection opens are likewise **unavailable** from the observed activities.
- No raw parameters, financial values, tokens, passwords, spans or exceptions are stored in this report. Only aggregates and query counts are recorded.

## Results

Correctness: every measured sample returned the expected response and state (`correct` equals `samples` for all rows below).

| Scenario | Samples | Correct | Median ms | p95 ms | Min ms | Max ms | Stdev ms | SELECT | INSERT | UPDATE | DELETE | COPY | BEGIN | COMMIT | ROLLBACK | SAVEPOINT | Batch | Other | Opens |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `auth.me` | 90 | 90 | 2.64 | 3.36 | 1.93 | 5.10 | 0.49 | 90 | 0 | 90 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `finance.accounts` | 90 | 90 | 3.61 | 4.74 | 2.81 | 5.55 | 0.56 | 180 | 0 | 90 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `finance.categories` | 90 | 90 | 4.12 | 5.48 | 3.30 | 7.31 | 0.73 | 270 | 0 | 90 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `transactions.query25` | 90 | 90 | 12.52 | 14.44 | 10.87 | 21.11 | 1.25 | 450 | 0 | 90 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 90 | 0 |
| `transactions.query100` | 90 | 90 | 14.18 | 16.09 | 12.16 | 18.50 | 1.00 | 450 | 0 | 90 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 90 | 0 |
| `transactions.search` | 90 | 90 | 35.37 | 39.19 | 32.25 | 59.68 | 3.09 | 450 | 0 | 90 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 90 | 0 |
| `transactions.detail` | 90 | 90 | 24.88 | 27.76 | 22.77 | 30.99 | 1.51 | 720 | 0 | 90 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 90 | 0 |
| `imports.history` | 90 | 90 | 6.20 | 7.07 | 5.34 | 7.70 | 0.50 | 180 | 0 | 90 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `bulk.50k.prepare` | 30 | 30 | 548.20 | 744.84 | 458.51 | 754.85 | 106.51 | 240 | 30 | 30 | 0 | 30 | 0 | 0 | 0 | 0 | 30 | 0 | 0 |
| `bulk.50k.apply` | 30 | 30 | 2381.38 | 2932.28 | 1748.60 | 2977.86 | 291.51 | 300 | 0 | 90 | 30 | 0 | 0 | 0 | 0 | 0 | 30 | 0 | 0 |
| `bulk.50k.replay` | 30 | 30 | 4.47 | 6.20 | 3.58 | 6.84 | 0.72 | 60 | 0 | 30 | 0 | 0 | 0 | 0 | 0 | 0 | 30 | 0 | 0 |
| `import.dense.refresh` | 30 | 30 | 230.27 | 268.30 | 214.85 | 271.10 | 12.33 | 390 | 30 | 12090 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `transactions.edit` | 30 | 30 | 9.22 | 11.52 | 8.04 | 18.98 | 1.92 | 270 | 0 | 60 | 0 | 0 | 0 | 0 | 0 | 0 | 30 | 0 | 0 |
| `transactions.confirm` | 30 | 30 | 9.02 | 10.27 | 7.61 | 10.40 | 0.77 | 240 | 0 | 60 | 0 | 0 | 0 | 0 | 0 | 0 | 30 | 0 | 0 |
| `cleanup.import` | 30 | 30 | 4.56 | 6.11 | 3.43 | 6.21 | 0.74 | 30 | 0 | 30 | 60 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `cleanup.transactions` | 30 | 30 | 3.82 | 4.47 | 3.13 | 6.42 | 0.63 | 30 | 0 | 30 | 30 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `auth.login` | 30 | 30 | 155.73 | 193.04 | 148.72 | 197.17 | 11.97 | 60 | 30 | 30 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `import.small.create` | 30 | 30 | 16.84 | 29.83 | 14.27 | 34.67 | 4.64 | 363 | 120 | 33 | 0 | 30 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `import.small.patch` | 30 | 30 | 64.19 | 76.77 | 60.87 | 87.91 | 5.48 | 390 | 30 | 3090 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `import.small.refresh` | 30 | 30 | 63.97 | 70.94 | 60.88 | 72.05 | 2.98 | 390 | 30 | 3090 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `import.small.commit` | 30 | 30 | 20.43 | 27.98 | 18.43 | 54.79 | 6.34 | 510 | 60 | 60 | 0 | 30 | 0 | 0 | 0 | 0 | 30 | 0 | 0 |
| `import.small.replay` | 30 | 30 | 6.75 | 9.77 | 5.88 | 10.66 | 1.10 | 210 | 0 | 30 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `import.medium.create` | 30 | 30 | 320.04 | 375.18 | 279.21 | 381.72 | 28.98 | 364 | 140 | 34 | 0 | 30 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `import.medium.commit` | 30 | 30 | 563.25 | 610.61 | 472.15 | 628.60 | 41.93 | 510 | 60 | 60 | 0 | 30 | 0 | 0 | 0 | 0 | 30 | 0 | 0 |
| `import.medium.replay` | 30 | 30 | 9.48 | 11.23 | 8.12 | 17.80 | 1.66 | 210 | 0 | 30 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Existing legacy measurements, unchanged and run in the same command:

| Existing test | Result |
| --- | --- |
| `TwentyThousandRowMeasurementRecordsTimingsAndPlan` | PASS; owned dataset 21,000 + other 5,000; visible 30 ms, search 102 ms, filtered amount 80 ms, hidden 24 ms; plan captured in test output |
| `TenThousandRowGenericImportPreparesWithoutCombinatorialWork` | PASS; commit 459 ms, receipt replay 9 ms |

## Verification of the baseline phase

- Focused harness and existing measurement command: **11/11 tests passed**, 4.0 minutes.
- Full backend suite after adding the harness: **339 passed, 1 skipped** in 7m36s. The skip is the inherited `FiftyThousandMemberBulkCommitsAtomically` dense fixture, which remains owned by Thread #4 and is not claimed as covered.
- Four backend format checks: PASS.
- `dotnet build -c Release -warnaserror`: PASS, zero warnings.
- Protected-copy restore and manifest comparison: PASS for both copies.
- Frontend: unchanged; no frontend command was needed for this phase.

## Limits and stop conditions

- Endpoint `BEGIN`/`COMMIT`/`ROLLBACK`/savepoint counts are unavailable from Npgsql activities because internal transactions do not emit activity spans in this version. The report does not treat the zero counts as complete transaction parity.
- Physical connection-open counts are unavailable. The later EF phases must add safe transaction/connection evidence or preserve this explicit limitation.
- The performance host and the test host share one process; background cleanup did not run during measured windows, but process-level JIT/GC effects remain possible and were reduced rather than eliminated by warm-ups.
- The protected copies cover one local development/isolation state. Production topology, session-affine proxy behavior, permissions and backups remain unverified and are not claimed by this baseline.
- The inherited dense 50,000-member/150,000-link stress test remains skipped and UNVERIFIED. The lean 50,000-member measurement is different evidence.
- The frontend battery is not part of this phase because no frontend file changed. It remains required at final acceptance.

## Reproduce

From `backend/` with `ARGUS_TEST_DATABASE_URL` pointing at a dedicated disposable PostgreSQL database (never development or production):

```text
dotnet test tests/argus.Server.Tests --filter "FullyQualifiedName~PersistencePerformanceTests|FullyQualifiedName~TwentyThousandRowMeasurementRecordsTimingsAndPlan|FullyQualifiedName~TenThousandRowGenericImportPreparesWithoutCombinatorialWork" --logger "console;verbosity=detailed" -warnaserror
dotnet test tests/argus.Server.Tests -warnaserror
dotnet format argus.Server.csproj --verify-no-changes
dotnet format style argus.Server.csproj --diagnostics IDE0005 --severity info --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet format style tests/argus.Server.Tests/argus.Server.Tests.csproj --diagnostics IDE0005 --severity info --verify-no-changes
dotnet build -c Release -warnaserror
```

The raw harness log and protected manifest remain outside the repository at `/tmp/argus-thread8-baseline/`. They are not published and contain no raw financial records.
