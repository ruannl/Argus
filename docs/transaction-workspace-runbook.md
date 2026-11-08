# Transaction workspace runbook

Operational guidance for the Plan 3 transaction workspace: list and detail
reads, versioned classification, visible/hidden layers, bulk preparation and
apply, recovery, limits, and migration 0007. It describes the shipped
behaviour; the governing contract remains `docs/plans/03-transactions-and-classification.md`
and the approved product brief.

## Capability summary

- `/transactions` lists owned transactions with description-or-tag substring
  search, inclusive date bounds, account, statement, category, single-tag or
  Untagged, category-kind, direction and visibility filters. Sorting is by
  date, exact numeric amount, or description, always with transaction id as the
  tie-breaker. Page sizes are 25, 50 and 100.
- Counts are inventory and row counts, never money totals: `inventory_count`
  ignores filters; visible and hidden matching counts apply every filter except
  visibility; `filtered_total` applies visibility and drives pagination.
- `/transactions/{id}` and the list drawer show source identity as read-only
  text. Category, merchant tags and the individual hide flag are editable.
- Tag suggestions reuse the import whole-token matcher. They are never applied
  automatically; accepting a chip sends an explicit versioned add-tag edit.
- Hiding keeps the record and duplicate identity. Parent statement hiding wins;
  individual restore is refused with `parent_hidden` until the statement is
  restored in Imports.
- Bulk changes use an explicit selection (up to 1,000 rows) or all rows matching
  one filter snapshot (up to 50,000). Preparation freezes exact ids and
  versions; later imports are not included. Apply is all-or-nothing and writes a
  durable receipt.
- There is no individual transaction delete, no source-field edit, and no
  reporting/overview computation in this surface.

## Limits and bounds

| Concern | Value |
| --- | --- |
| Search | one literal substring, at most 200 Unicode scalars |
| Query body | 16 KiB, checked by actual read length |
| Edit/prepare/apply body | 256 KiB, checked by actual read length |
| Page size | 25 (default), 50, 100 |
| Explicit selection | 1–1,000 unique id/version tuples |
| All-matching selection | at most 50,000; 50,001 returns `selection_too_large` |
| Tags per request | at most 100 ids in each add/remove set, disjoint |
| Tags per transaction | at most 100 after an add; no-add removals may reduce a legacy set |
| Open prepared operations | 5 per customer |
| Live manifest entries | 100,000 per customer |
| Preparation rate | 20 per customer per 15-minute window |
| Operation expiry | 15 minutes absolute from capture |
| Workspace statement bound | 30 seconds per statement |
| Lock wait bound | 5 seconds |

## Lifecycle and recovery

`Prepared → Applied | Abandoned | Expired` is terminal after the first terminal
state. An applied receipt is checked before expiry or member validation, so a
lost response, a reload, a new host, or a later statement deletion can replay
the same result without re-applying effects. A differing confirmation hash or
selected count is refused. Expired and abandoned operations cannot apply.

The current `/transactions` URL may carry a nonsensitive `operation_id` for
recovery. On reload the page fetches status and never applies automatically.
Applied receipts and terminal refusals are shown; a still-Prepared operation can
be abandoned and prepared afresh, but not applied after the original private
search/filter context is lost. Cleanup expires Prepared operations at startup
and every fifteen minutes even if no request arrives.

A failed apply leaves the operation Prepared for retry, abandon or expiry; it
does not imply any row changed. Serialization, deadlock, lock-timeout and
statement-timeout failures roll back and return `409 operation_busy`; retry the
same operation rather than preparing a new one. A lost or uncertain response
must be resolved through status/receipt before describing the outcome.

## Errors

| Status | Codes |
| --- | --- |
| 400 | `invalid_request`, `empty_selection`, `category_unavailable`, `tag_unavailable`, `tag_limit_exceeded`, `selection_too_large` |
| 401 / 403 | `unauthorized` / bodyless origin rejection |
| 404 | `not_found` for unknown or foreign ids, filters, targets and operations |
| 409 | `version_conflict`, `selection_stale`, `classification_changed`, `parent_hidden`, `idempotency_conflict`, `operation_busy` |
| 410 | `operation_expired`, `operation_abandoned` |
| 413 | `request_too_large` |
| 429 | `rate_limited`, `operation_quota` |
| 500 | `internal_server_error`; no internal detail is returned or logged with user data |

Stale failures may include owned `stale_count`/`missing_count` values and a
bounded list of owned statement links for parent-hidden restores. They never
include amounts, descriptions, foreign ids or SQL detail.

## Concurrency and lock order

New workspace writes use the order: **owned accounts ascending → operation
header (bulk only) → chosen category → referenced tags ascending → transactions
ascending**. Chosen category/tag rows are locked `FOR SHARE`, so renaming,
archiving, deleting or changing a target fails the confirmation with
`classification_changed` instead of reinterpreting it. Category and tag deletion
lock their affected transaction rows in ascending id order before the broad
reassignment/removal; their existing target-entity-first order is unchanged.
Statement import, statement visibility and statement deletion keep their account
first lock prefix. Cleanup locks only operation headers.

## Migration and rollback

Migration `0007_transaction_workspace` adds `transaction_operations` and
`transaction_operation_items`, plus transaction indexes on
`(customer_id, transaction_date, id)` and `(customer_id, amount, id)`. It is
additive: it does not backfill or change source identity values. Manifest rows
deliberately have no live transaction/statement foreign key, so a deleted member
makes apply stale rather than silently shrinking the frozen scope; the parent
header cascades.

Before applying to valuable data, take a database backup with the existing
recovery process. A populated upgrade from `0006` is covered by
`TransactionMigrationTests`. Application rollback may leave the additive schema,
indexes and compact receipts in place; older code does not use them. Do not drop
receipts while an operation may be retried. Restoring a backup needs explicit
operator approval.

## Verification evidence (corrected baseline, 2026-11-07)

The following historical battery ran after the Plan 3 implementation was
corrected and the in-scope whole-tree review findings were addressed (W2,
M1–M3, M5 and the M6 evidence record). The whole-tree review's W1 (pre-existing
login/password-reset race), M4 (shared account preflight) and L1 (import expiry
policy) were outside that correction's scope. They were subsequently corrected
in Plan 3 revision 3 and Plan 2 revision 6 and merged in PR #3 at 63815ee;
PR #5 at 1b80a66 fixed the expiry-wait instability. The operator accepted
Plan 3 as Validated on 2026-11-08. This runbook does not re-label the historical
battery as evidence for the merged tree. No deployment occurred.

- Backend: `dotnet format argus.Server.csproj --verify-no-changes` and the test
  project equivalent are clean. The full isolation suite passed **298 tests**
  against the dedicated `argus_test` database (2m 11s). `dotnet build -c Release`
  completed with 0 warnings and 0 errors. Coverage includes query/filter/count/
  sort, single and bulk mutation, tag algebra, parent-hidden refusal, stale and
  classification conflicts, abandon/expiry/quota, migration upgrade, fault
  rollback at transaction/tag/receipt boundaries, post-commit lost-response
  restart replay, composite cleanup pairs with overlapping operation UUIDs,
  post-lock expiry, the foreign-owner route matrix, and the 50,000-member
  boundary. The whole-tree review independently reran the non-database
  configuration tests, frontend Vitest/lint and TypeScript checks, and the live
  health/route checks. It did not rerun the full isolation suite, Playwright or
  the browser walkthrough, so the 298/17 results remain implementer-reported
  execution, not an independently repeated full battery.
- Frontend: `npm run lint` is clean; Vitest passed **229 tests** in 28 files;
  `npm run build` succeeds for both transaction routes; Playwright passed all
  **17** existing smoke tests, including the unauthenticated nested detail
  route.
- 20,000-row measurement (21,000 owned rows plus another customer's 5,000,
  dense tags and a 1,000-row hidden parent): visible list 81 ms, search 31 ms,
  category+amount filter 69 ms, hidden view 45 ms on the local PostgreSQL
  instance. The recorded execution plan is a limit over index-driven nested
  loops, not a sequential scan. The 20,000-row pass is in
  `/tmp/opencode/measurement-corrected.log`; that combined capture also contains
  a failed 50,000-row fixture setup, so its 50,000-row result is not the
  passing measurement. These are local synthetic observations, not a production
  service-level guarantee.
- 50,000-member bulk measurement: prepare 990 ms, apply 1,936 ms, receipt replay
  7 ms; 25,000 rows changed and 25,000 already matched, committed atomically.
  The public 50,000 bound was exercised as a whole, not as independent pages.
  This result is in the separate passing capture
  `/tmp/opencode/measurement-50k-corrected.log`, not the earlier combined
  capture whose fixture setup timed out. These are local synthetic
  observations, not a capacity promise.
- Composed browser walkthrough (run `20261107T1630`, synthetic dev customer,
  fresh dev frontend on :3000 and current backend on :3001): a synthetic FNB CSV
  was imported through the real upload/review/commit flow into a canary
  account; settings categories and tags were created (including an archived
  tag); description search, inline category plus multi-tag edit, explicit
  suggestion acceptance, confirmed hide/restore, a second browser session
  conflicting on a stale version and refreshing, all-matching bulk category
  apply with a receipt that survived reload, prepare/reload/abandon recovery,
  a parent-hidden bulk restore refused at preparation with an Imports link,
  parent restore, drawer Tab/Shift-Tab containment and Escape focus return at
  320 px, no horizontal overflow at 320/375/1280 px, and both themes were
  exercised. Search text stayed out of the URL and browser storage.
  **Evidence limit:** the retained
  `/tmp/opencode/walkthrough-20261107T1630/report.json` is an accumulated
  per-step artifact across interrupted runs. It ends with a rate-limited
  sign-in failure and contains repeated skipped entries, so it is not one clean
  end-to-end run; the successful checks are live per-step observations, not a
  single completed run. The retained dark screenshot shows the loaded list
  after the drawer closed, and the 320 px light screenshot shows the
  rate-limited/incomplete state; they do not visually prove every claimed
  loaded or drawer state. This is live local runtime observation, not CI,
  production or a substitute for the automated suites.
- The existing Playwright smoke suite is unauthenticated and does not establish
  authenticated finance composition; the walkthrough above is the composed
  evidence, and it used synthetic values only.

## Private Server Action logging

`frontend/next.config.ts` sets `logging: { serverFunctions: false }`. Next.js
16.4.3 captures Server Function invocation arguments in development before the
action authenticates or validates them, and writes invocation lines after the
request. The supported setting prevents that capture at its source. It applies
globally to development Server Functions: function names, arguments, duration
and source locations are no longer printed. Incoming request lines,
fetch/browser logging, application diagnostics, and backend structured logs and
`audit.*` events are separate and unchanged. `logging: false`, console
suppression, post-capture redaction, a transport change or a dependency patch
are not part of this contract. Removing the setting while private Server
Actions remain reachable reopens the disclosure, so it is not a safe
troubleshooting rollback.

Pre-correction evidence remains at
`/tmp/opencode/frontend-walkthrough.log:38` (retained, not edited): a
development invocation line containing the synthetic private search
`{"filters":{"search":"Cafe",...}}`.

The corrected privacy run used fresh, separate captures
`/tmp/opencode/frontend-canary-20261107-1640.log` and
`/tmp/opencode/backend-corrected-20261107-1615.log`, with Next.js 16.4.3 in
development and the current backend. It exercised a successful description
search that returned its row, a malformed search over 200 Unicode scalars
rejected inside action validation, a valid search whose unknown owned
statement filter was rejected by the backend with `404`, and a session-expiry
attempt after the session cookie was cleared. The session-expiry request never
reached the action: the proxy redirected the action POST to `/login`, which is
recorded as a proxy boundary rather than action-validation evidence. The
canary script itself recorded only that the backend-rejection attempt was made;
the expected `404` is visible in the backend capture for that run, so that case
is inspected-log evidence rather than a script assertion.

Not established by this run: an all-matching preparation carrying private
search text, malformed financial-field inputs, and an action-level
session-expiry rejection were not exercised. The screenshots retained from the
walkthrough do not cover every claimed loaded/drawer state. These cases are
**NOT RUN** for the privacy claim and must not be inferred from the covered
searches.

The complete unsanitized captures contained **0** matches for the synthetic
search, description, account, institution, category and tag names and the
amount canary, and **0** framework Server Function invocation lines. Positive
controls in the same run: the frontend capture contains ordinary GET/POST
request lines, and the backend capture contains `audit.*` events and
`http_request` lines. No canary appears in a URL or in browser storage.

Tooling note: an earlier intermediate capture
(`/tmp/opencode/frontend-corrected-20261107-1615.log`) recorded a React
hydration-mismatch diagnostic after Playwright's screenshot caret-hiding
injected a transient `caret-color` style during hydration. That development
diagnostic printed rendered component props, including synthetic names, and is
a screenshot-injection artifact rather than the Server Function logger. The
clean canary run above took no screenshots and recorded no hydration-mismatch
line. Do not take a screenshot during a privacy canary; use
`page.screenshot({ caret: "initial" })` if one is required.

## Revision 3: credential-bound login (W1)

Login verifies the password against an encoded hash before entering a database
transaction. Session issuance then locks that customer's credential row,
compares the **same encoded hash**, clears successful-login failure counters,
inserts the hashed session token and commits as one unit. Password reset updates
that row and revokes sessions transactionally. If reset commits first, issuance
returns the existing `invalid_credentials` without clearing counters, inserting
a session or logging success. If issuance commits first, reset revokes that
session before reset succeeds. No credential-generation column or schema change
was added; the former customer-ID-only production issuance method was removed.
The unverified-session finance-guard test now inserts its intentionally invalid
test fixture directly in the isolated test database.

**Focused evidence (2026-11-07, dedicated `argus_test`):** the actual HTTP
old-hash/read → reset-commit → issuance regression failed before the correction
(`401` expected, `200` observed; `/tmp/opencode/w1-before.txt`) and passes
afterward. The inverse issuance-first test observes the real PostgreSQL
advisory/credential-row wait, then verifies reset revocation. A cancellation
test observes a credential-row wait and verifies no session or success-counter
effect. All three tests passed. The Phase 5 account/finance/billing filter
passed **33/33**; both backend format checks and Release build passed with zero
warnings. This is real HTTP/PostgreSQL test evidence, not a live development
service observation. The combined full battery subsequently completed; the
revision-3 corrections merged in PR #3 at 63815ee and the operator accepted
Plan 3 as Validated on 2026-11-08. The focused result above remains focused
evidence, not the combined run.

## Revision 3: transaction account preflight (M4)

The two transaction routes check the server-held session with `/v1/me` before
loading account or transaction data. A canonical `401` with error
`unauthorized` retains the existing sign-in and session-clearing path. A `429`
asks the user to wait and retry. A transport failure, unexpected status or body,
unreadable response, or bodyless `403` displays an account-check failure and an
explicit Retry without clearing the session. A retry refreshes the same route,
including its filters, operation ID or detail ID; it neither applies nor retires
an operation. Errors do not expose backend bodies, tokens, origins or exceptions.
The shared account client accepts the expected fields plus additional response
fields. Seven other page callers keep their prior catch behavior; a broader
authentication change remains outside this correction.

**Evidence (2026-11-08):** focused client and transaction-page tests passed
**113/113** across five files. Lint passed; the full frontend suite passed
**249/249** across 28 files; the production build passed; Playwright passed all
**17/17** existing smoke tests. New compatibility regressions for unexpected
`401` bodies and extra success fields failed against the interrupted code before
the correction and passed afterward. These client and component tests use mocks;
the Playwright suite is unauthenticated. They do not establish a live backend
outage or actual browser-cookie persistence.

## Revision 3: import expiry reference (L1)

Plan 2 revision 6 owns the import expiry policy. Its bounded correction now
rechecks the persisted review deadline after tag validation and immediately
before commit, using fresh PostgreSQL time. An expired attempt rolls back;
previously committed receipts remain replayable. The statement-import runbook
records the full behavior and the **69/69** focused import results.

Two earlier full backend runs each passed **307/308**, with different existing
transaction tests timing out. Those tests passed when rerun separately. A third,
monitored full run passed **308/308**, and a fourth full run repeated
**308/308** on 2026-11-08. A later isolated run of the existing three-variant
transaction expiry-wait test failed one variant, so repeated full-suite passes
did not establish that the test was stable at that time. PR #5 at 1b80a66
subsequently fixed the expiry-wait instability; Thread #7 is resolved. The
historical observation above remains as recorded.

During investigation, a bounded clock probe observed the host and PostgreSQL
wall clocks move backward by about **1.37 seconds** while the host monotonic
clock advanced roughly **1.0–1.2 seconds**. The system time service reported an
offset of approximately **−2.48 seconds**. A backward wall-clock adjustment can
extend an expiry test's wall-clock wait relative to its monotonic timeout, but
the probe does not prove the cause of either full-run failure. In particular,
the 50,000-row fixture timeout remains unexplained. No system clock, time
service, development database or development service was changed.

The 2026-11-08 repeat battery also passed frontend lint and build, **249/249**
Vitest tests and **17/17** unauthenticated Playwright smoke tests. Those remain
synthetic/component and unauthenticated smoke evidence, not a live outage or
cookie-persistence observation.

## Known boundaries

- Search cost is a bounded literal substring scan per customer; trigram
  extensions, full-text services and background bulk jobs are out of scope.
- Live manifests are capped and expire; terminal receipts remain compact for
  idempotency. A receipt-retention policy change would alter replay guarantees.
- Overview, charts, category/tag money totals and coverage analytics belong to
  Thread #5. Standard Bank PDF remains deferred to Thread #6.
