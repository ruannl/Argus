# Customer isolation runbook

Database-enforced customer isolation for the shared PostgreSQL database. It
supplements, and does not replace, the trusted frontend-to-backend boundary,
independent session authorization, verified/subscriber checks and application
ownership filters.

## Protected tables

Row-level security is enabled and forced on these thirteen relations:

- finance reference data: `financial_accounts`, `categories`, `merchant_tags`,
  `merchant_aliases`;
- committed finance: `statement_imports`, `transactions`,
  `transaction_merchant_tags`;
- import lifecycle: `import_quota_locks`, `import_reviews`,
  `import_review_rows`, `import_commit_receipts`;
- transaction workspace: `transaction_operations`,
  `transaction_operation_items`.

The authoritative owner is each row's `customer_id`. No request field supplies
database authority. Customers, sessions and verification/reset tokens are the
authentication control plane and do not carry a customer-GUC policy; migration
ledgers are global operational metadata.

## Roles

| Role | Login | Purpose |
| --- | --- | --- |
| `argus_owner` | no | Owns application tables, policies and functions; has the only cross-customer maintenance policy. |
| `argus_migrator` | yes | Separate maintenance login; may `SET ROLE argus_owner`; never a runtime credential. |
| `argus_runtime` | yes | Normal web login; no object ownership, no `SET ROLE`, no role membership, no schema `CREATE`. |

`argus_owner` and `argus_runtime` are `NOSUPERUSER` and `NOBYPASSRLS`.
`argus_migrator` and `argus_runtime` are `NOINHERIT`; the migrator is a member of
the owner only for explicit `SET ROLE`. The runtime has explicit table/column
grants and no broad maintenance grant.

## Configuration and secret delivery

- `backend/.env` is runtime-only. `ARGUS_DATABASE_URL` must be the
  `argus_runtime` login with `search_path` naming the application schema.
- `backend/.env.test` is ignored and holds only the test-maintenance URL used by
  the isolation suite. The normal host never reads it.
- `backend/.env.maintenance` is ignored and holds the migrator URL used by
  `dotnet run -- migrate` and `dotnet run -- seed`. The normal host never reads
  it.
- Bootstrap and maintenance processes use explicit per-child environment
  allowlists. Administrator, migrator and test-maintenance values are never
  spread into the web host or frontend.
- Existing privileged values in a runtime `.env` are not silently retained:
  relocate them under authorized operator control before starting the host.

Example files are `backend/.env.example` (runtime only),
`backend/.env.test.example` and `backend/.env.maintenance.example`. They contain
placeholders, never real secrets.

## Startup and maintenance

Normal startup is read-only validation, not migration:

```text
cd backend
dotnet run
```

It fails closed, before routes and hosted cleanup start, if the schema revision,
completed canonical backfill, RLS flags, exact policy expressions, normalized
function definitions that preserve literal case and internal spacing,
function security/search paths, owners, effective grants, role
attributes/memberships or the authenticated session identity do not match the
contract. A superuser/owner URL, or a privileged login that merely assumes the
runtime role, is rejected even on localhost.

Maintenance is explicit and offline:

```text
cd backend
dotnet run -- migrate   # apply pending migrations and global backfill
dotnet run -- seed      # migrate, then idempotently seed the two local accounts
```

Both commands require a migrator login acting as `argus_owner`. They exit
without starting routes or cleanup services. The bootstrap runs them in
separate children and passes only the migrator URL.

## Customer authority and RLS

Each customer-facing database unit is a `CustomerDbScope`:

1. one explicit transaction at the store's isolation level;
2. lock the customer row, then the matching session row;
3. validate session ownership, revocation, absolute/idle expiry, email
   verification and subscriber activation under the lock;
4. bind transaction-local `argus.customer_id`, `argus.session_hash` and
   `argus.transaction_id` (the full `xid8`) with parameterized `set_config`;
5. run the protected work;
6. re-check authority and database wall-clock expiry immediately before commit;
7. commit or roll back, then dispose before the connection returns to the pool.

The policy helper `argus_current_customer_id()` is a `SECURITY INVOKER`,
`VOLATILE`, `PARALLEL UNSAFE` function with a fixed trusted search path. It
rejects missing, empty, malformed and stale settings, requires the current
transaction identifier, and checks the customer/session/entitlement state using
database time. A missing or invalid result makes protected reads return no rows
and protected writes fail.

Each protected relation has exactly two permissive policies: a runtime
`ALL` policy with `USING`/`WITH CHECK` customer equality, and the owner-only
maintenance policy. Admin status adds no finance override.

List/detail use `RepeatableRead` without `SET TRANSACTION READ ONLY` because
admission takes tuple locks; their finance reads remain read-only. Serializable
bulk retries recreate the whole scope. Missing/foreign identifiers keep the
existing indistinguishable error behavior.

## Relationships, snapshots and cleanup

Existing customer-inclusive finance foreign keys are retained. Merchant aliases
also carry customer-inclusive `(customer_id, merchant_tag_id)` and
`(customer_id, category_id)` foreign keys after a fail-closed ownership
preflight; the single-column lifecycle actions remain. Category deletion
explicitly clears owned alias category references in the same transaction.

Operation manifests, receipts and durable snapshots deliberately keep stale
identifiers after target deletion; do not add live cascading foreign keys there.
RLS confines the containing rows and all dereferencing queries.

Two no-argument owner-owned `SECURITY DEFINER` functions perform the existing
cleanup duties:

- `argus_cleanup_import_reviews()` — expire up to 1,000 prepared reviews,
  purge payload rows, prune terminal reviews after thirty days, return
  count-only JSON;
- `argus_cleanup_transaction_operations()` — expire up to 1,000 prepared
  operations with composite customer/operation identity, return count-only
  JSON.

They have a fixed trusted search path, references resolved through that
controlled path, no dynamic SQL, `PUBLIC EXECUTE` revoked, runtime `EXECUTE`
only, and no caller-chosen ids.
The hosted cleanup services run immediately at startup and every fifteen
minutes.

## Provisioning and legacy adoption

A fresh installation:

1. creates the three roles under administrator authority;
2. creates the database and application schema;
3. grants schema usage/create and runs the owner migration;
4. applies table/function grants in the isolation migration;
5. prepares a runtime URL and runs the normal-start validation.

For an existing installation, a separately authorized previous-owner or
administrator process first runs the bounded adoption tool with a reviewed
manifest:

```text
ARGUS_ADMIN_DATABASE_URL=postgres://previous-owner:... \
ARGUS_ADOPT_MANIFEST=./adopt-manifest.json \
ARGUS_ADOPT_OWNER=argus_owner ARGUS_ADOPT_SCHEMA=public \
node scripts/adopt-db-objects.mjs
```

The manifest must list exactly the tables, sequences, views, materialized
views and function signatures to transfer. Set `"transfer_schema_owner": true`
to also transfer schema ownership; it defaults to false because shared schemas
such as `public` need explicit review. The tool inventories the schema, refuses
missing or unexpected objects, and runs only transactional `ALTER ... OWNER`
statements. It never uses cluster-wide `REASSIGN OWNED` or touches row data.
The tool has been executed against a small populated synthetic schema in the
authorized test target (schema/table ownership transferred, row preserved).
It has **not** been executed against a full populated Argus installation; a
real cutover must exercise it there first. Then:

1. inventory only this application schema's tables, dependent sequences,
   functions and ledgers against a reviewed object manifest;
2. transfer those SQL objects transactionally to `argus_owner` with
   `ALTER ... OWNER`, never cluster-wide `REASSIGN OWNED`;
3. compare customer ids and row digests before and after;
4. then run the non-superuser migrator and grants.

SQL-object ownership transfer never changes a customer's row owner and is not a
recovery shortcut. Refuse unexpected objects and stop on drift.

## Real-data cutover (not executed by this kit)

A real cutover needs its own authorization:

1. verified backup and tested restore path;
2. stop and drain old privileged web/worker instances (no rolling mixture);
3. ownership/foreign-key/alias preflight and approved object-manifest transfer;
4. role provisioning and runtime/test/maintenance secret-layout cutover;
5. leased migration/backfill/grants under the owner;
6. runtime-role probes and an ordinary-host startup validation;
7. only then new web traffic.

Deployment evidence remains UNVERIFIED until separately obtained.

## Recovery

On failure, keep the target offline, preserve evidence and prefer a forward
fix. Do **not** disable RLS, grant runtime owner access, reassign customer data,
change SQL owners as an unplanned workaround, rewrite migration history or
delete receipts to restore availability. Backup restoration, destructive schema
rollback and credential rotation require explicit authorization.

Normal startup detects drift that exists before it starts; it cannot detect a
hostile database operator changing objects after startup. Protection is against
unauthorized supported requests and accidentally omitted ownership predicates
in normal execution paths, not against arbitrary malicious whole-backend SQL or
a fully privileged database operator.

## Adding a protected table

A new customer-owned table is not protected automatically. Update, in one
change: the protected-table inventory, the migration's RLS/policies/grants,
the validator's expected inventory, the authority/scope use in its stores, the
database-boundary tests, and this runbook. The startup validator fails on an
unclassified relation until the table is added deliberately.

## Verification

```bash
cd backend
dotnet format argus.Server.csproj --verify-no-changes
dotnet format style argus.Server.csproj --diagnostics IDE0005 --severity info --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet format style tests/argus.Server.Tests/argus.Server.Tests.csproj --diagnostics IDE0005 --severity info --verify-no-changes
dotnet test tests/argus.Server.Tests -warnaserror
dotnet build -c Release -warnaserror
```

From the repository root, `node scripts/run-backend-tests.mjs` supplies the
ignored test-maintenance URL to the test process. The database-boundary suite
must run through the restricted runtime login in the disposable `argus_test`
database; HTTP or policy-text checks alone are not equivalent evidence.
