# Statement import runbook

This runbook covers the Plan 2 import surface: supported statements, service
bounds, review and commit lifecycle, duplicate policy, parser deployment, and
migration recovery. It is operational guidance, not a replacement for the
product brief or `docs/plans/02-statement-import.md`.

## Supported statements

**Current scope (2026-11-06, operator-directed): CSV-only.** Plan 2 revision 5
defers Standard Bank PDF import to Thread #6. The product exposes FNB CSV/ZIP
and generic mapped CSV only. A `standard_bank_pdf` request is rejected before
parsing with `400 pdf_import_deferred` and creates no review, statement,
transaction or receipt. The PDF parser remains in the tree but is dormant. The
last independent PDF verdict remains NO-GO
(`963d287e-c3e0-4214-ba9d-1aac0750ac9f`) and must be resolved in Thread #6
before PDF is re-enabled. Proposed revision 4 / C-PDF-2 and the pending brief
amendment are deferred, not approved.

| Source | Accepted shape | Notes |
| --- | --- | --- |
| FNB CSV | Six-line preamble, seventh-line `Date,Amount,Balance,Description` header, four fields per row | The balance column is ignored. Preamble and balance never persist or display. |
| FNB ZIP | Exactly one root-level `.csv` entry | No nested paths, directories, extra entries, traversal, or expansion beyond 10 MB. |
| Standard Bank PDF | **Deferred to Thread #6 — not accepted in the current release.** | The API rejects this source kind with `pdf_import_deferred`. The parser material below is retained for the deferred fix and is not current operating behaviour. See [Standard Bank PDF extraction](#standard-bank-pdf-extraction). |
| Generic CSV | Customer-confirmed header line, delimiter, columns, date format, decimal/thousands separators, and sign mode | Columns are addressed by zero-based index. Mapping and coverage changes require a new review. The FNB preamble shape is excluded independently of the selected delimiter, including quoted metadata labels; a header line inside it is rejected. A boundary requires a recognized transaction header or an explicit FNB title, so ordinary description-first data is not misclassified. |

### Standard Bank PDF extraction

**Dormant in the current CSV-only scope.** The following describes the
**required contract for the deferred PDF fix**, not current product behaviour.
The 2026-11-05 independent review of the first C-PDF-1 implementation returned
NO-GO. The root-cause work order is C-PDF-1 Section I in
`docs/plans/02-statement-import.md`; the corrected implementation and its
evidence are recorded below. That verdict remains the last independent review
and is not being relabelled as acceptance. Thread #6 owns the residual fix.

The parser does not read text in reading order and does not skip rows above a
header, below a footer, or below a fixed Y coordinate. Every non-whitespace
extracted line on every page is classified:

- **Candidates.** Any line with a populated date or flow cell becomes a
  candidate before value validation. Malformed dates, malformed or missing
  amounts, flow-only rows, and printed zero remain visible; Balance is never an
  amount fallback. Multiple populated flow cells are invalid. A dated row whose
  description is exactly `Summary` or `Total payments` is still a transaction.
- **Continuations.** Description-only text attaches only when it occupies the
  description band with the established alignment and spacing and has one
  preceding candidate. Orphans, competing attachments, and a continuation
  sequence closed by a cover, summary, blank page, or incompatible table reject
  the document. Cross-page continuation is limited to one bounded leading
  bridge on the immediately following table with compatible column bands; the
  bridge is consumed on first attachment, and every later continuation must
  meet the ordinary positive-gap check. A distant first fragment or below-footer
  residue rejects. A footer ends same-page continuation eligibility without
  preventing a trailing row from continuing on the next page.
- **Recognised non-transaction structures.** Exact synthetic cover lines, a
  complete printed statement period, `Summary`/`Statement summary`/`Account
  summary` headings, complete `Page n`, `Page n of N`, and `Continued on page n`
  footers in the footer position, exact `Opening balance`/brought-forward rows
  before transactions, exact `Closing balance`/carried-forward rows in the
  terminal suffix, and exact `Total payments`/`Total deposits`/`Total bank fees`
  rows with one well-formed amount in their own flow band and only permitted
  terminal totals, closing/carried-forward balances and footers afterward. A
  later recognized Summary/cover/period/header also prevents total exclusion;
  checking only for later transactions is insufficient. Cover and summary
  headings on a headerless page must sit in the approved top-of-page region.
  A repeated complete same-page header is consumed only when its boundaries stay
  within the fixture tolerance and no word changes semantic columns; a
  conflicting header rejects before any row is interpreted. Account, Customer
  and Branch metadata in the preamble rejects before row promotion. A heading
  does not authorise unrelated text; any other unexplained text rejects.
- **Images.** Any raster image on any page rejects as `text_pdf_required` before
  text classification. A text header does not bypass the check, and a small
  image is not assumed to be decoration.

This is a conservative text-only subset. Real-bank logos, unusual metadata, and
layouts outside the synthetic fixtures may be rejected; that is a safe
rejection, not a claim of broad real-bank certification. No OCR, image
interpretation, vector or annotation analysis, new parser dependency, or
balance use is added.

**User-facing copy limitation (CPDF-N2).** The frontend currently displays
`That PDF has no text layer` for `text_pdf_required`
(`frontend/src/app/imports/import-workspace.tsx:33` and
`frontend/src/lib/server/import-actions.ts:45`). A text-bearing PDF with a
raster logo is also rejected, so that copy is not proof that no text layer
existed. The frontend wording is unchanged in this correction.

The importer does not support OCR, scanned PDFs, password-protected PDFs,
multi-currency values, balances, bank feeds, split transactions, or
auto-categorisation. Dates and ZAR must be confirmed before preparing a review.
Source date, description, signed amount, account, and source are immutable
after preparation.

## Service bounds

| Bound | Value |
| --- | --- |
| File size / decompressed ZIP payload | 10,000,000 bytes each |
| Multipart envelope | 10,100,000 bytes; options metadata at most 64 KiB |
| Data rows | 20,000 including invalid rows |
| PDF pages | 200 |
| Extracted PDF characters | 2,000,000 |
| Parser deadline | 30 seconds for CSV and PDF parsing |
| Concurrent parsers | 2 per backend process, shared by CSV and PDF |
| Description length | 4,096 Unicode scalars |
| Open reviews per customer | 5 |
| Sanitized review payload per customer | 50 MiB |
| Review lifetime | 24 hours absolute |
| Abandoned/Expired header retention | 30 days |
| JSON review mutation body | 256 KiB; at most 200 row decisions per request |

A request that exceeds a bound fails with a fixed safe error and no partial
finance records. Parser exhaustion is a safe failure, not silent truncation of
the duplicate policy. Full rejected files never enter a review or history.

## Raw retention and privacy

Raw uploads are read request-locally and discarded after inspect, prepare, or
failure. The browser keeps the selected `File` only in component memory and
re-sends it with confirmed options. Reviews store sanitized mapped rows,
warnings, duplicate annotations, and decisions; they never store raw bytes,
filenames, preambles, balances, account metadata, or parser diagnostics.

Logs, audit events, URLs, and error response bodies never contain transaction
descriptions, amounts, statement contents, user-defined category or tag names,
filenames, or preambles. Authorized inspect, review, and statement responses do
contain the mapped descriptions and amounts the customer is reviewing; that is
the product content, not an error or log surface. Audit entries use generated identifiers and counts
only. The original filename is not displayed in the UI.

## Review lifecycle

1. **Inspect** parses request-local bytes and returns detection, mapping
   choices, proposed dates, required confirmations, and at most 20 preview
   rows. It persists nothing.
2. **Prepare** re-sends the file with confirmed ZAR, coverage dates, and
   mapping. It creates a `Prepared` review with a version, summary hash,
   duplicate snapshot, and 24-hour expiry. No transactions exist yet.
3. **Review** reads the durable rows. Invalid rows must be skipped. Repeated
   identical rows require a keep-all, skip-all, or per-row decision. A row
   that matches an existing occurrence is suppressed unless explicitly
   overridden. Tag suggestions are unselected and only use the customer's
   active tags whose normalized name is a complete token sequence in the
   description.
4. **Refresh** recomputes duplicate state under the account lock. It preserves
   skips and selected tag IDs, but resets overrides and group decisions that
   depend on changed duplicate state; the customer must renew confirmation.
5. **Commit** atomically creates the statement, included transactions, tag
   links, reconciled counts, account version bump, and a receipt, then purges
   the review payload. A new review is required for any later source change.
6. **Abandon or expiry** purges the review rows and configuration and retains
   only a minimal terminal header for 30 days. Neither action can undo a
   committed import. Expiry cleanup locks the review header before deleting its
   child rows, matching the commit/edit lock order.

### Absolute commit expiry (revision 6)

The initial expiry check rejects an already-expired review; it does not reserve
the right to commit. After selected tag locks and validation, the commit checks
the persisted deadline again using PostgreSQL `clock_timestamp()`. It checks
once more immediately before committing, after tentative writes and the existing
test seam. A database time equal to or later than the deadline returns
`410 review_expired` and rolls back the attempt, including the statement, rows,
tag links, receipt, account version change and review-payload purge. These
checks also apply without tags and when every incoming row is suppressed. The
final check admits the commit; it does not promise that disk flush or the HTTP
response finishes before the deadline.

The original review payload remains until normal cleanup; it cannot authorize
new effects after expiry. A receipt from an earlier successful commit is
resolved before these checks, so the same request can still replay that result
after the original review deadline. Do not extend the deadline or delete a
receipt to recover an operation; prepare a new review for new work.

**Focused evidence (2026-11-08):** the selected-tag lock regression observed an
actual PostgreSQL wait and deadline crossing. Before the fix it returned `201`
where `410` was required; after the fix it returns `review_expired` without
durable effects. Four additional post-tag/final-commit scenarios also returned
`201` before correction; the corrected suite covers both boundaries, tagged and
untagged reviews, and fully suppressed imports. A new test replays the original
receipt after its deadline through a fresh test host. The three import test
classes passed **69/69** against the dedicated `argus_test` database. This is
test-host and PostgreSQL evidence, not a live development-service exercise.

### Counts

`total_rows = imported_count + suppressed_count + skipped_count` on every
review and committed statement. Invalid rows are included in total and count
as skipped for reconciliation, but commit still requires each invalid row to
be explicitly skipped. A nonempty valid review may commit with zero new
transactions when every valid row is an existing occurrence; that is an honest
suppressed statement, not an empty file. Possible-duplicate and override counts
are overlapping annotations, not extra buckets.

### Duplicate policy

- Exact fallback identity is the account-scoped date, signed cents, and
  normalized description. A versioned SHA-256 key and occurrence slots make
  repeated legitimate rows possible.
- Cross-import matching consumes existing occurrences once. Stable source IDs
  are paired first, then compatible ID/anonymous rows, then anonymous rows;
  the matcher uses maximum-compatible pairing rather than first-fit.
- Within-file repeats require a deliberate decision; the importer never
  silently drops identical rows.
- Near matches use the same account and amount with a date within ±3 days and
  token-set Jaccard similarity of at least 0.5. They are warnings and default to
  included; they never suppress a row by themselves.
- The statement fingerprint protects whole-statement repeats. A repeat commit
  is refused unless the customer explicitly confirms a statement override.
  Hiding does not release fingerprint protection.
- Hidden transactions still own identity slots and still suppress duplicates.
  Deleting a statement releases only its own slots.

## Commit recovery and uncertain responses

The review UUID is the commit operation ID. The receipt binds the expected
review version and summary hash to one result.

- After a durable commit, retrying the same operation with the same body returns
  the same statement and counts with `replayed: true`, including after restart.
- Reusing the operation with different version or hash returns
  `409 idempotency_conflict`.
- A connection loss around commit is an unknown outcome until the receipt is
  read. Retry the same commit; do not create a new review.
- A failure before commit rolls back every statement, transaction, tag link,
  and receipt. The review is retryable only while it remains valid.
- Deleting a statement marks its receipt `Deleted`. An old commit retry returns
  `410 import_deleted`; it never recreates data. A fresh review can import the
  deleted occurrences again.

## Statement lifecycle

- **History** lists committed statements only, including hidden ones, with
  stable `(created_at DESC, id DESC)` cursor pagination. Failed, abandoned, and
  draft reviews are not history.
- **Hide/restore** changes only the parent statement flag and increments the
  statement and child transaction versions. It never changes an individual
  transaction hide flag. Effective visibility is
  `statement.is_hidden OR transaction.is_hidden_by_user`.
- **Delete** permanently removes the statement and exactly its owned
  transactions and tag links. It does not adopt or delete another import's
  suppressed rows.
- Individual transaction deletion is not exposed. Hiding is the reversible
  transaction-level operation; permanent deletion is statement-level only.

## PDF parser child deployment

**Dormant in the current CSV-only scope.** The import endpoints reject
`standard_bank_pdf` before launching this child. The deployment contract is
retained for the deferred PDF thread.

PDF extraction runs in a child mode of the backend assembly:

```bash
dotnet argus.Server.dll --parse-statement-pdf
```

The child starts before configuration, migrations, logging, or web services.
It reads a bounded binary request frame from standard input and writes bounded
sanitized JSON to standard output. The parent enforces the 30-second deadline,
drains and discards standard error, bounds resident memory at 384 MiB, and uses
the two process-wide parser admission slots shared with CSV. The child has no
database or network responsibilities and does not execute document actions,
embedded files, links, or scripts.

On Linux the child joins its own process group (`setpgid`). The parent
terminates that group on timeout, cancellation, or an output bound, and also
when the root has already exited while a descendant still holds an inherited
pipe. `Process.Kill(entireProcessTree)` alone does nothing once the root is
gone, so group termination is the cleanup that reaches those descendants. If a
parent process disappears, the child still exits on input closure or its own
30-second watchdog; the parent is not required to reap it.

The process tests exercise both launch forms: the apphost-free build output and
the published release output (`dotnet argus.Server.dll`). They also cover a
descendant that outlives its root and an abrupt parent death with the input pipe
closing.

The process tests exercise a descendant that outlives its root, an abrupt parent
death with the input pipe closing (the child is confirmed alive on a valid
incomplete frame before the kill), production process-group membership, and the
apphost-free and published launch forms. Bounded cleanup waits attach a
late-fault observation to unfinished tasks instead of blocking indefinitely.

Deployment requirements:

- The published backend output must contain the server assembly and runtime
  configuration so the child can run without `dotnet run`.
- The runtime must allow child processes, process-group signalling, and bounded
  pipe I/O. A platform that forbids child processes cannot use the Standard
  Bank PDF preset.
- The backend request timeout and proxy body limits must admit the 10.1 MB
  multipart envelope. Do not raise the global trust boundary to compensate.
- The child is not a kernel sandbox. The bounds are service controls; use the
  platform's process isolation for stronger isolation.

## Migration 0006 recovery

`0006_statement_import` adds import reviews, receipts, identity and fingerprint
constraints, statement/account consistency, and tag-link ownership. It is
forward-only and must not be edited after execution.

Before applying it to a database with customer data:

1. Take a verified backup and record the restore procedure.
2. Confirm no transaction references a statement owned by another customer or
   account, and no tag link joins rows from different customers. The migration
   fails closed with an operator-safe diagnostic on either condition.
3. Run the backend migration through normal startup or `dotnet run -- seed` in
   a controlled environment. The migration is transactional per migration.
4. Verify populated `0005` upgrades with `StatementImportMigrationTests` and
   check that counts and historical rows are unchanged.

If the migration fails, leave the prior schema and data intact, inspect the safe
diagnostic, and reconcile the ownership mismatch through the operator. Do not
drop finance tables or restore by deleting new schema/data. After imports are
accepted, prefer a forward fix; reverting application code must not remove new
schema, receipts, or transactions. Destructive restoration requires operator
authorization.

## Verification battery

From `backend/`:

```bash
dotnet format argus.Server.csproj --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet test tests/argus.Server.Tests
dotnet build -c Release
dotnet publish argus.Server.csproj -c Release --no-restore
dotnet list argus.Server.csproj package --vulnerable --include-transitive
```

From `frontend/`:

```bash
npx audit-ci@^7 --config ./audit-ci.jsonc
npm run lint
npm run test
npm run build
npm run test:e2e
```

`dotnet test` requires exactly `ARGUS_TEST_DATABASE_URL` against an isolated
PostgreSQL database. Never point it at a development or production customer
database. Parser-only tests need no database.

The parser evidence includes genuinely encrypted PDF fixtures (RC4-128 with an
empty user password and AES-128 with a non-empty user password), parent-death
and inherited-pipe process cases, and the apphost-free and published-output
launch forms. The import evidence includes concurrent commit/abandon/delete/tag/
account races, review quota growth at creation and PATCH, dense identical-row
matching and refresh, and a 10,000-row commit followed by receipt replay.

### C-PDF-1 first-pass evidence (2026-11-05, superseded)

**This first C-PDF-1 implementation pass was reviewed NO-GO and is retained
only as history. The root-cause correction and its evidence follow later in this
section.**

Source baseline: branch `plan/02-statement-import`, HEAD
`496f34ab39a792b090753ed4e2215562fe0ea15d`, with the existing Plan 2 working
tree preserved. Parser blob
`fdcd2e1dbdf48888ad89fe46b5ebe97821f270ad` replaces the pre-correction
`af33c4c716de9844bda6279b502b90cac7c8832a`. Test-source blobs:
`StatementParserTests.cs` `2de428dc43e91816861de65684a45f1eef73b26b`,
`StatementSafetyTests.cs` `bebbaaff23bab16ca9ae7c74eb30b9d810567ad4`,
`StatementParserProcessTests.cs` `bba5f5e1172d4609708812df828080e16ccfa57b`,
`StatementFixtureBuilder.cs` `515c7b3a4b5c30571f5f0dc4edbc2b50ed542121`, and
`StatementImportContractTests.cs` `cf5c622f6e1c9be63cfe5e3beab3ce9d4947e360`.
The governing Plan and public contracts were not changed by this correction.

Before the correction, the bounded readiness probes accepted `-1000.00` and
`20` while omitting the payment from a headerless page, and accepted a text
header with a large raster body unless coverage reached 25%. After the
correction, synthetic PDF bytes through the real parser and the published child
reject all three amount probes, period-plus-headerless residue, last-page
residual text, and any raster image, with fixed safe errors. Authenticated HTTP
inspect and prepare on the isolated test database create no review, statement,
transaction, row, or receipt when the parser rejects; a PDF with a missing-date
payment remains preparable, commit is refused with `invalid_rows` until the row
is explicitly skipped, and the committed counts reconcile.

Evidence strength: parser and process results are unit/synthetic integration
through the real child boundary. HTTP results are integration over the isolated
PostgreSQL test database. No real customer statement, browser import journey, or
deployment observation is claimed.

Full battery on the recorded baseline: backend `dotnet format` both projects
PASS; `dotnet test` **197/197 PASS**; Release build 0 warnings/0 errors;
`dotnet publish` PASS; vulnerability scan reports no vulnerable packages.
Frontend `audit-ci` PASS; lint PASS; Vitest **169/169 PASS**; production build
PASS; Playwright **17/17 PASS**.

**Review status (2026-11-05):** the first-pass baseline was independently
reviewed **NO-GO** with 6 HIGH and 2 MEDIUM findings. The root-cause correction
replaced the implementation baseline; it did not supersede that independent
verdict. A fresh independent review of the corrected baseline also returned
**NO-GO**; see the fresh review status below.

**Architect disposition (2026-11-05):** the eight findings are covered by the
existing approved revision 3; no acceptance criterion, invariant, phase scope,
supported bank layout, frontend or schema contract changes. C-PDF-1 Section I
defines the corrective processing order and actual-PDF/process/authenticated
regression matrix. The work order has since been implemented and verified below.
The earlier 197/169/17 passes remain historical evidence only. No publication or
additional review was requested.

### C-PDF-1 root-cause correction evidence (2026-11-05)

Source baseline: branch `plan/02-statement-import`, HEAD
`496f34ab39a792b090753ed4e2215562fe0ea15d`, with the existing Plan 2 working
tree preserved. Parser blob
`5215c496da50e33da59778eb444c011e13fdc166` replaces the reviewed first-pass
`fdcd2e1dbdf48888ad89fe46b5ebe97821f270ad`. Test-source blobs:
`StatementParserTests.cs` `94d57040b3defaf639a9e69e16346f439f58079e`,
`StatementSafetyTests.cs` `63f460a5b36bfada6e1758ef4c25b2d34c1f7605`,
`StatementParserProcessTests.cs` `3ead5976000acf6689bf5176939e807073b999b2`,
`StatementFixtureBuilder.cs` `515c7b3a4b5c30571f5f0dc4edbc2b50ed542121`, and
`StatementImportContractTests.cs` `82a75afc85ed815d4da4683ae23107a369d328fb`.
The governing Plan, public contracts, frontend and schema were not changed by
this correction.

Before the parser fix, the new independent regressions failed on the reviewed
baseline: focused parser/safety **19 failed / 117 passed of 136**, and
authenticated contract **6 failed / 23 passed of 29**. The failures covered the
conflicting second header (deposit/flow reversal and Balance-as-fees), unknown
preamble metadata, negative-X residue, stale cross-page continuation, mid-page
headless headings, above-header source-line reuse, zero-day rejection, and
terminal-total suffix lookahead. After the correction, focused parser/safety
**138/138 PASS**, authenticated contract **29/29 PASS**, and full backend
**231/231 PASS**. The published child exercises the compatible/conflicting
header cases, tolerated-column ambiguity, metadata, negative-X row/total, far
and distant continuations, below-footer residue, preamble Summary closure,
mid-page heading, source-line control, zero-day boundaries and the full
terminal suffix; the direct published-child canary probe confirms no source
canary on standard error.

Recorded tolerances: top-of-page headings use the upper 120 points of the page
(the fixtures place masthead/period content at Y=780–820 on 842-point pages);
the cross-page bridge is the leading slot within 80 points below the header
(fixture header 800, first continuation 770); same-page repeated headers use
the existing 6-point compatibility tolerance plus a semantic-column check; and
ordinary continuation spacing remains a positive gap of at most 12 points.

Full battery on the corrected baseline: backend `dotnet format` both projects
PASS; `dotnet test` **231/231 PASS**; Release build 0 warnings/0 errors;
`dotnet publish` PASS; vulnerability scan reports no vulnerable packages.
Frontend `audit-ci` PASS; lint PASS; Vitest **169/169 PASS**; production build
PASS; Playwright **17/17 PASS**.

Evidence strength: parser and process results are synthetic actual-PDF unit and
process integration through the real child boundary; authenticated results are
integration over the isolated `ARGUS_TEST_DATABASE_URL` database. No real
customer statement, browser import journey, or deployment observation is
claimed.

**Fresh independent review of the corrected baseline (2026-11-05):** request
`963d287e-c3e0-4214-ba9d-1aac0750ac9f` returned **NO-GO** with 0 CRITICAL,
4 HIGH, 2 MEDIUM and 1 LOW (immutable SHA-256
`b10e23a04e326881ef402532c7b6c5ee5090f03f0bd72876c8b4119e64ff3b7b`). The
original I1–I6/N3/N4 probes are fixed, but CPDF-R1 (conflicting-header residue
and exact-tolerance bypasses), CPDF-R2 (colon-attached metadata), CPDF-R3
(aligned pre-header Summary fragment), CPDF-R4 (off-page top/footer roles),
CPDF-N5 (closing-balance suffix placement), CPDF-N6 (Phase 2 test-shape gaps)
and CPDF-N7 (wording) remain. The corrected baseline is not accepted. The
residual findings and the separate real-statement blockers are tracked in
follow-up Thread #6. No further PDF review was requested. The separate CSV-only
Plan 2 implementation was published through PR #1 at 78a43e4; Thread #4 is
operator-accepted and Thread #5 is unblocked for Plan 4 planning. PDF remains
disabled/deferred to Thread #6. No deployment occurred.

## Workspace handoff

Committed statements remain the source of the read-only row list shown on
Imports. Classification, tag changes and the individual hide flag belong to the
Transactions workspace; each statement links to its rows there and to each
owned row's detail page. Statement visibility still owns the parent layer, so a
parent-hidden row is restored from Imports first. See
`docs/transaction-workspace-runbook.md` for filters, version conflicts, frozen
bulk snapshots and recovery. This handoff does not change any import count,
fingerprint, occurrence, receipt, visibility or deletion behaviour described
above.

## Safe error codes

The deferred production-template proposal adds no route or password field. Its
private acceptance must not print real rows, filenames or diagnostics, and its
synthetic authenticated tests must cover every residual finding from Thread #6.
Until approval and new evidence, neither the source files' successful
diagnostic extraction nor a completed correction changes the recorded NO-GO.

`413 file_too_large`; `415 unsupported_format`; `400 invalid_request`,
`unsupported_encoding`, `unsafe_archive`, `encrypted_pdf`, `text_pdf_required`,
`unsupported_layout`, `processing_limit`, `format_confirmation_required`,
`pdf_import_deferred`,
`invalid_rows`, `duplicate_decision_required`, `no_included_rows`;
`429 rate_limited`, `import_busy`; `404 not_found`; `409 review_stale`,
`version_conflict`, `statement_repeat`, `idempotency_conflict`,
`account_archived`, `identity_conflict`, `tag_unavailable`; `410 review_expired`,
`import_deleted`; `500 internal_server_error`. Foreign review/import IDs return
`404`, not `410`. Error bodies never include source text, filenames, or
exception details.
