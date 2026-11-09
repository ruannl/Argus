# Plan 2: Statement Import, Parsers, Review, and Idempotency

- **Current approved revision:** 7 (`2026-11-09`) — operator-approved suggestion-source and near-duplicate correction. Suggestions may use explicit aliases, the customer's own historical choices keyed by a generic merchant key, and exact/truncated-prefix token matching; near duplicates require the exact transaction date. Detailed scope is in "Revision 7 — Suggestion sources and exact-date near duplicates" below. This is a semantic authority correction for PR #8; no new plan review is claimed, and the code remains subject to a fresh acceptance review. Thread #3 Decision `a92fc81b-879b-4844-8543-1b25a51abda5` records approval.
- **Prior approved revision:** 6 (`2026-11-07`) — bounded L1 expiry correction, explicitly approved on Thread #4 in Decision `060f596f-31c3-4632-a1b3-a91e36879448` against contract `27141eaf-98da-4ba6-9378-3d84ce78c136`. The correction merged in PR #3 at 63815ee as part of the Plan 3 revision-3 branch after merge-gate review 37828a33 returned GO with zero blocking findings; PR #5 at 1b80a66 fixed the later expiry-wait test instability. It was not separately plan-reviewed, and no new plan review is claimed. The operator accepted Plan 3 as Validated on 2026-11-08. It preserves revision 5's **CSV-only delivery**: FNB CSV/ZIP and generic mapped CSV remain active; Standard Bank PDF stays disabled/deferred to Thread #6.
- **Prior approved revision:** 5 (`2026-11-06`), CSV-only delivery. Immediately preceding file blob `94efd17a193f0cb2df3321ce63d1c4347f34a006` at `eda95113b6bf29102a6ec24c2cea45058334a9c0`. Its historical approval and implementation acceptance are preserved below. Revision 6 changes only the absolute-expiry boundary in commit and its bounded tests/documentation.
- **Deferred revision 4:** **C-PDF-2 — production Standard Bank template**, proposed `2026-11-05`. It is **NOT APPROVED** and is deferred with PDF import; it is preserved as future planning input for Thread #6. No implementation or additional review is authorized by it.
- **Revision 3 authority:** The operator explicitly selected **Approve revision 3** after the bounded Specialist GO and both note dispositions. Approved annotated proposal blob: `53a63747ba2bb7e79d4a1dc5b966fce6b11c7153`; reviewed blob: `053218c3a9640fa124126db8ad8e88a625576e7f`. Its CSV/FNB/generic rules remain active. Its PDF-specific rules are dormant while Revision 5 defers PDF. Thread #3 records the final activated file blob and approval provenance.
- **Status:** **Revision 5's CSV-only implementation is historically review-accepted.** Final independent review `5524f1dd-f982-49af-a6fd-5a863fe95987` returned **GO** with zero findings against the exact baseline below; it does not review revisions 6 or 7. L1 was identified by whole-tree review `9d131c1c-58d6-449c-b22f-c88688cc24fc` against `c76c78f`; its approved correction merged in PR #3 at 63815ee after merge-gate review 37828a33 returned GO with zero blocking findings, without reopening Thread #3 or changing programme order. PR #5 at 1b80a66 fixed the expiry-wait instability. The operator accepted Plan 3 as Validated on 2026-11-08; Thread #5 is unblocked for Plan 4 planning. Revision 7 is an operator-approved semantic amendment for PR #8; it preserves the revision-5 review and revision-6 execution records and does not accept the PR #8 code, which remains subject to a fresh acceptance review. Earlier NO-GO reviews and the dormant PDF NO-GO remain historical provenance, not relabelled acceptance. No deployment occurred.
- **Revision 7 authority:** Operator instruction in the PR #8 artifact-revision request, 2026-11-09; Thread #3 Decision `a92fc81b-879b-4844-8543-1b25a51abda5`. No new Specialist review is claimed.
- **Final acceptance baseline:** branch `plan/02-statement-import`, HEAD `496f34ab39a792b090753ed4e2215562fe0ea15d`, reviewed Plan blob `901a93a59dd99e3a63316148a825dbb4632fa05c`, working-tree diff digest `de371ea2e6126b2cf2d8f8c4fb159b9e1ccb4579`, ordered untracked-file digest `d23c207b740f806dd1bd6a8fd2268faf9a04bc74`. The GO binds to those identities; this acceptance record is operational metadata.
- **Specialist provenance:** Request `9d4092ed-ce89-44e5-9e0b-fc63b572d865`; reviewed revision 1 / Git blob `f03ffe737fc669f0b7d2737e04f2d13307a64306` on the implementation base below. Immutable full review SHA-256 `3c3d227603443489af81f8ed6fc1634d0efae44735de63e294ee5d522ea1907b` (18,756 bytes). Verdict: 0 CRITICAL, 0 HIGH, 5 MEDIUM notes, 1 LOW note. Final annotated blob and acceptance provenance are recorded on Thread #3. Notes/status below are operational dispositions, not a new semantic revision or a second review.
- **Revision 2 authority:** Operator's bounded Architect correction request on Thread #3. Supersedes the `TextFieldParser` choice and explicitly names the requested helper/test-runner paths. The accepted revision 1 annotated blob was `b03f058e75833e2dff254acb788fc566b363cac7`; the immediately preceding file, including execution records, was blob `fdeee65e3989997aad92056c2d2e37de01330678`. Revision 2 is not separately Specialist-reviewed; the operator explicitly requested no new plan review. The original plan gate and its provenance remain intact, without claiming that the new tokenizer was reviewed in revision 1.
- **Implementation baseline:** `496f34ab39a792b090753ed4e2215562fe0ea15d` on `main`.
- **Governing child:** HiveMind Thread #3 (`7f9a4a3a-ccaa-41db-9064-26fb78c657c5`).
- **Governing programme:** Thread #1 (`306f43f0-6ed8-43ea-8f75-0019027f8b06`), Ready Requirement revision 1 (`134e0580-8e55-4216-b883-9e9c6c466d9b`). Programme topology and progression remain authoritative in HiveMind, not this file.
- **Selected brief:** `docs/product-brief.md`, approved body from `0e7f1c8ab773a66c99c2e0ae96f4fe314c321a03`, plus the 2026-11-06 CSV-only scope amendment and the 2026-11-09 behavior amendment. Sections 1.1, 6, 7, and 9 govern behaviour; Sections 4.9–4.10 govern documented formats; Sections 12–13 govern limitations, decisions, defaults, and this unit. The production-PDF amendment is **deferred, not accepted**, by the Revision 5 operator decision.
- **Predecessor content:** `docs/plans/01-foundation-and-entities.md` at `496f34a`, particularly the `/v1/finance/*` boundary and Section 12 note N11. Plan 1 is complete and is not amended here.
- **Authority:** This file is the editable Plan. There is no native Plan or duplicate child Requirement. The approved umbrella Requirement, selected brief, and Thread #3 scope sufficiently specify this unit. Review provenance is recorded on Thread #3 against the exact file blob; publication and implementation approval are separate operator decisions.

## 1. Desired outcome and current behaviour

An authenticated customer can select a ZAR financial account, upload an FNB CSV or single-CSV ZIP or a generic mapped CSV, and commit only the rows they have reviewed. Standard Bank PDF import is deferred by Revision 5; a PDF request is rejected before parsing with `pdf_import_deferred`. They explicitly confirm ZAR, the statement dates, ambiguous formats, and duplicate decisions. Imported source fields remain immutable. History exposes accurate counts and reversible statement visibility, plus confirmed permanent statement deletion.

The present application has financial entities and relational tables, but no import execution path. The Imports page is an authenticated placeholder. No parser, upload handler, statement API, transaction-creation API, or duplicate constraint exists. Plan 2 supplies those paths, not the transaction workspace or reporting product.

### Finished live-source inspection

Explorer report `ses_e5ab26e9affeaC0iRnqJzeRcb5` completed before drafting, against `496f34a`. Targeted direct reads then checked the schema and the auth dependency. These are **source-inspection** findings, not executed behaviour:

| Concern | Observed evidence and consequence |
| --- | --- |
| Execution path and authority | `frontend/src/app/imports/page.tsx:47–60` obtains `auth().user.backendToken` and calls `/v1/me`. `backend-client.ts:313–382` sends trusted headers and JSON only. `FinanceEndpoints.cs:24–87` maps 17 entity routes and checks origin, application secret, opaque session, verified subscription, then finance rate limit. New import routes must use the same guard. |
| Reachability | `frontend/src/proxy.ts:14–25` already protects Imports and its descendants. `auth.ts:77–92` keeps credentials in the encrypted token and redacts the public session callback. Pinned `next-auth@5.0.0-beta.32/lib/index.js`, `getSession`, reinserts `user: args[0].user ?? args[0].token` for server `auth()`; the public handler does not use that wrapper. This resolves the scan's structural ambiguity, but needs a real-library composition test. Do not expose the token in the public callback to make mocked page tests pass. |
| Inputs available | `FinanceStore.cs:7–38,102–147` supplies owned accounts, categories, tags, and system Uncategorised. `Db.cs:271–333` supplies statement/transaction tables, exact `NUMERIC(18,2)`, source position, identity and occurrence fields, user-hide flag, transaction version, and same-customer foreign keys. No parser results or review state exist. |
| Side effects | `FinanceStore.cs:745–802,1036–1101` deletes categories/tags transactionally while preserving transactions and incrementing their versions. Account archive locks its account row (`517–534`). Statement deletion already cascades to transactions, then tag links. Import code must coordinate with these existing operations, not replace them. |
| Missing safety mechanisms | No upload limits or multipart path exists. `JsonBody` reads the whole body. `Program.cs:83–100` logs paths; `FinanceEndpoints.cs:94–117` logs error classes, not exception contents. The new path must bound bodies before buffering and never put financial text in URLs or exceptions sent to logs. |
| Tests and feasibility | `FinanceContractTests.cs` has 16 database-backed entity tests; its raw SQL helper inserts foundation transactions, not imported transactions. `ApiTestBase.cs:21–33` truncates finance tables. Vitest mocks auth/backend calls; the three Imports tests currently require no upload control. Playwright starts Next.js without a backend in continuous integration. Preserve that smoke setup and be explicit about what each new test exercises. |

### Disposition of supplied gaps

| Item | Classification | Evidence / disposition |
| --- | --- | --- |
| No child Requirement | **INTENTIONAL** | The current operator selects the approved brief and umbrella Requirement. No missing outcome requires an Analyst handoff; no new Requirement or inferred approval is created. Decision recorded on Thread #3. |
| No real samples in the repository | **INTENTIONAL** | Brief Section 12.1 forbids committing them. Phase 1 uses independently specified synthetic fixtures; actual-bank accuracy remains unverified without a separately authorized private exercise. |
| Duplicate and fingerprint constraints absent | **CONFIRMED** | `Db.cs:271–323`; Plan 1 Section 12/N11 assigns them here. Add migration `0006_statement_import`; do not edit `0005`. |
| No draft from the aborted attempt | **ALREADY SATISFIED** | Thread #3 records that no draft survived. Initial authoring produced revision 1, not a continuation; the bounded revision 2 correction is recorded below. |
| Auth token reachability ambiguity | **ALREADY SATISFIED** at dependency-source level | The pinned server wrapper supplies the token. Phase 3 adds composition evidence; there is no authority to weaken public-session redaction. |
| Transaction workspace and reports | **DEFERRED** | Thread #4 owns individual/bulk transaction management. Thread #5 owns totals, charts, and coverage warnings. This plan provides their storage contracts, not their screens. Thread #4 is operator-accepted and Thread #5 is unblocked for Plan 4 planning. |

## 2. Confirmed decisions, assumptions, and constraints

### Product decisions retained

- The account is customer-selected, active, and fixed to ZAR. A bank label, statement preamble, filename, or browser cookie never establishes ownership or currency.
- Every import confirms `start_date`, `end_date`, and ZAR. Dates are inclusive calendar dates. Coverage dates do not rewrite transaction dates.
- Balances are ignored: never retained, returned, displayed, used as amount fallback, or used in fingerprints. Account metadata and filenames are likewise excluded from persisted/returned content and logs.
- Date, description, signed amount, financial account, and source are immutable in review and after commit. Review permits only skip/include decisions, explicit duplicate overrides, and deliberate merchant-tag selection. Category starts as the customer's system Uncategorised.
- Exact overlaps default to suppression, within-file repetitions require a decision, and near matches remain included unless skipped. Overrides are explicit and recorded. Hidden records still prevent duplicates. Statement deletion releases only identities owned by that statement.
- There is no auto-categorisation, automatically accepted tag, external merchant service, OCR, or generic PDF mapping. Existing login/subscription, admin, billing, and visual contracts remain unchanged.

### Architecture decisions for this unit

1. **Parse in the backend.** Use the strict custom tokenizer in `backend/Imports/CsvRecordReader.cs` for both CSV presets, `System.IO.Compression.ZipArchive` for ZIP, and pin NuGet `PdfPig` **0.1.16** for positional PDF extraction. Revision 2 approves the custom tokenizer instead of the originally selected .NET `TextFieldParser`; no switch is required. It must preserve physical record start/end lines, reject malformed quoting without repairing it into valid data, and enforce a finite record cap while reading. Strict UTF-8 decoding remains at the shared byte-input boundary. No frontend parser dependency or new service is needed. Inspect/prepare run the same parser contract; a commit never trusts client-supplied source rows.
2. **Keep raw uploads request-local.** Inspect is stateless. The browser retains the selected `File` only in component memory and re-sends it with confirmed mapping/dates to prepare a review. The backend discards raw bytes after each request, including failures. Only sanitized parsed rows enter a durable, expiring review. This avoids sticky sessions, a raw-file repository, and restart-dependent commit authority.
3. **Use PostgreSQL for review and commit authority.** A review has an opaque UUID, authenticated owner, fixed account/configuration/period, immutable parsed rows, mutable decisions, version, expiry, and duplicate snapshot. A durable receipt binds one reviewed version to one commit result. History contains committed statements only.
4. **Serialize account ledger changes.** Lock the owned financial-account row for duplicate snapshot/commit/deletion; use a new `import_version` on that row to detect stale review results. This works across backend processes. In-memory rate limiting is only an abuse control, not a correctness lock.
5. **Separate visibility layers.** Effective hidden state is `statement_imports.is_hidden OR transactions.is_hidden_by_user`. Statement hide/restore changes the parent layer, never the individual flag. Publish this predicate for later plans.
6. **Make deletion terminal for an old commit operation.** Deleting an import removes its transactions, fingerprint slots, and row identity records, but retains a non-financial commit tombstone. Retrying that old operation returns `410 import_deleted`; a new upload/review may import the data cleanly. A lost response must not resurrect deleted work.

### Limits and assumptions

- Adopt the brief defaults as exact limits: **10,000,000 bytes** per file and per decompressed ZIP payload; **20,000 data rows** including invalid rows; **200 PDF pages**. Accept exactly one root-level CSV file and no other ZIP entries. Reject directories as well as nested paths for a simple safe subset.
- Multipart request limit is **10,100,000 bytes** including metadata; metadata is at most 64 KiB. Enforce measured bytes even if `Content-Length` is absent or false. JSON mutations are capped at 256 KiB; review edits are at most 200 row decisions per request. Pages contain at most 100 rows or statements.
- Reviews expire after **24 hours**, absolute from creation. Limit each customer to five open reviews and 50 MiB of sanitized review payload. Enforce the quota transactionally under a customer lock. Background cleanup deletes expired payloads at startup and every 15 minutes; handlers enforce expiry even if cleanup is late. Revision 6 makes the commit admission boundary explicit: passing an early check grants no extension through a later tag/database wait. Section 6.2 requires post-tag and final-precommit checks for new effects; existing receipts remain replayable first.
- Retain minimal Abandoned/Expired review headers (owner, account, operation ID, terminal state, timestamps; no row/configuration/period payload) for 30 days so retries have a truthful terminal response. After that an uncommitted old operation may return 404; it can never prepare or commit again under that UUID. Committed operation receipts are not subject to this cleanup.
- Parsing gets a **30-second deadline**. Run PDF extraction in a child mode of the existing backend assembly (`--parse-statement-pdf`), before loading configuration, migrations, logging, or web services. Pass bytes through bounded standard input, return only bounded sanitized JSON through standard output, never use filenames or secrets in arguments. The parent kills the entire child tree on timeout/disconnect and reaps it. Discard standard error rather than logging it. Limit extracted characters to 2,000,000 and output to 20 MiB. Set the child's managed heap hard limit to 256 MiB and terminate it if observed resident memory exceeds 384 MiB. These are service bounds, not a claim of kernel sandboxing.
- Bound concurrent parsers to two per backend process, reject excess with `429 import_busy` without waiting in an unbounded queue, and rate-limit inspect/prepare to ten per customer per 15 minutes in addition to the existing finance guard. The parser child has no database/network responsibilities and does not execute document actions, embedded files, links, or scripts.
- CSV supports strict UTF-8, with or without BOM, LF/CRLF, quoted fields, escaped quotes, and quoted multiline descriptions. Undecodable bytes are rejected, never replaced. Other encodings require re-export as UTF-8; they are not guessed.
- Monetary values use decimal arithmetic and integer cents for canonical identity. Reject values outside `NUMERIC(18,2)` or with precision beyond cents, including trailing fractional zeros beyond two places; never rely on PostgreSQL coercion/rounding. JSON transports amounts as invariant decimal **strings**; JavaScript never parses them as a binary floating-point authority.
- Description display preserves meaningful text: trim CSV padding; collapse PDF continuation whitespace. Matching collapses all whitespace and uses invariant case folding. Empty description has a stable empty matching value and a displayed fallback `No description`, with a warning. Do not use the fallback label as identity. Bound a description to 4,096 Unicode scalars; overlong rows are invalid, never truncated into valid transactions.
- No critical product decision remains open. Local component arrangement, SQL batching, exact layout tolerances, and test helpers are implementation choices within the contracts below. Dependency incompatibility or an ambiguous PDF layout is a stop, not permission to invent another parser or silently relax recognition.

## 3. Explicit scope and non-goals

**In scope:** all three documented import formats; ZIP/PDF safety; generic mapping and disambiguation; dates/currency confirmation; durable read-only review, skip decisions and tag suggestions; four duplicate classes; occurrence-aware atomic creation; retry receipts; Import History and statement detail; parent hide/restore/delete; schema constraints and indexes; bounded transport and privacy-safe errors; synthetic fixtures; tests and operational import documentation.

**Not in scope:** individual transaction modification, classification UI or bulk actions, the main transaction list/detail, actual overview metrics/charts, coverage-gap computation, category/tag management redesign, new bank presets, OCR, balances, raw-file downloads, multi-currency, background job infrastructure, customer deletion/export, pricing, or changes to Plan 1. A read-only list of this statement's created transactions is in scope solely to make its success/detail links usable before Thread #4.

## 4. Target invariants and architecture

### 4.1 Invariants

1. **Owned authority:** All reads/writes join or predicate on the authenticated `customer_id`; owner/account constraints protect review, receipt, statement, transaction, and tag references. Missing/foreign UUIDs return identical `404 {"error":"not_found"}` without parsing the file or mutating finance data. A denied tag selection changes nothing. No browser-provided customer ID is accepted.
2. **Source truth:** Parsed rows are server-created and immutable. A mutation schema has no source-field setters. Commit reads stored rows, recalculates validation, and rejects unskipped invalid rows. All writes preserve exact dates/text/cents and never persist a balance or preamble.
3. **Account-scoped occurrence truth:** A live transaction owns exactly one identity occurrence slot. Hidden transactions own slots too. Reordering, changing container, changing tags/category, or hiding does not create a new identity. Deletion removes only slots associated with deleted transactions.
4. **Atomic reviewed effect:** A transaction commits the statement, included transactions, accepted tag links, immutable counts/override summary, and receipt together. A failure before durable commit exposes none of those effects. A successful commit with a lost response is recoverable through the same operation ID, including after restart.
5. **No silent re-entry:** Same operation and request digest returns the same result, not another import. Different content under that operation returns conflict. Cancel/expiry cannot undo a committed import. Deletion cannot be reversed by a stale commit retry.
6. **Count reconciliation:** `total_rows = imported_count + suppressed_count + skipped_count`. Invalid rows are included in total and must be skipped. Possible-duplicate and override counts are overlapping annotations, not extra buckets. History retains the committed summary; live hidden count is separate.
7. **Two hide layers:** Parent hide affects effective visibility only; restore cannot unhide an individually hidden child. Every later visible query uses the common predicate. No individual transaction delete route is introduced.
8. **Bounded private processing:** Reject unsafe content before financial persistence. Raw bytes and extracted account metadata are request-local; sanitized review rows expire or are purged on commit/cancel. Logs, telemetry, URLs, error messages, and parser diagnostics contain no financial content, amount, filename, preamble, account label, or tag/category name.

### 4.2 Parser contract

Place domain/parser code under `backend/Imports/`. A parser yields immutable row IDs, physical source line/record number, optional start page and continuation pages, raw **mapped** date/description/flow values, interpreted values or nulls, validation codes, warnings, and safe source context. Context means the transaction's mapped fields and position, not a page image, whole extracted page, balance, or account preamble. Do not retain extra CSV fields on malformed-width rows; identify expected/actual field counts and expose only safely mapped fields. A truncated raw preview must be visibly labelled and cannot become the committed value.

**Shared CSV tokenizer (revision 2 technical correction)**

- `CsvRecordReader` is the shared tokenizer for FNB parsing, generic detection/inspection, and confirmed generic parsing. It tracks physical start/end lines through LF/CRLF, blank lines, escaped quotes, and quoted multiline fields. Header selection and source-row diagnostics continue to use physical positions, not a compressed logical-record index. Retaining internal end-line information does not add a public response field.
- Decode bytes through the strict UTF-8 boundary, with optional BOM removal; malformed bytes are rejected, never replaced. Propagate malformed-record state through row validation so a value such as `"Shop"garbage` cannot become a valid repaired description. Preserve exact header-width checks, source-value immutability, FNB preamble suppression, confirmed sign conventions, and the invalid-row skip workflow. This approval does not excuse malformed headers, shifted fields, silently accepted quoting, or privacy leaks.
- Every production read path, including format detection and re-inspection, supplies and enforces a finite cap before appending records beyond that cap; an uncapped default is not permission for an uncapped call. Preserve the existing file/data-row limits and safe whole-input rejection when exhausted. Do not materialize an unbounded record list and check only afterward, or accept a truncated prefix as the statement. The choice of tokenizer changes no amount/date interpretation or duplicate policy.

**FNB preset**

- Recognize the six-line preamble shape and the seventh-line `Date, Amount, Balance, Description` header after trimming header whitespace/BOM. Do not return or persist the first six lines. Both account variants use the same preset; no account number is used to choose the destination.
- Parse `yyyy/MM/dd`, signed dot-decimal amounts after trimming, negative outflow/positive inflow, and descriptions with trailing padding removed. Balance is not passed beyond the parser. Confirm the mapping and money-in/out direction in preview.
- Field count must equal four. An unquoted comma creating a fifth field is invalid, not repaired. Empty/header-only input is rejected. A malformed FNB-looking export must not silently fall through to a different interpretation; offer explicit generic remapping with the preamble still suppressed.

**Standard Bank PDF preset**

- Use positional glyph/word extraction, not `page.Text` reading order. Recognize all six labels in order with non-overlapping column bands; derive bands from header positions on each table page. A table-like page that cannot be classified safely rejects the document, rather than dropping its possible transactions. Non-table cover/summary pages may be ignored only when clearly identified as such.
- Recognize transaction date anchors `DD Mon YY`, group visual lines by coordinates, and attach description-only continuation lines in the description band. Carry an unfinished row over a page boundary only inside a recognized continuing table. Headers, footers, account metadata, and explicit summary/balance-only rows never become transaction candidates. Orphan continuation text or ambiguous association rejects the layout instead of attaching it speculatively.
- Treat Payments and Bank fees as outflows, Deposits as inflows; parse comma grouping/dot decimal. Accept unsigned magnitudes in the indicated column and a sign agreeing with that direction; contradictory signs are invalid. Exactly one populated flow column is required, including a printed zero. Multiple populated flow columns are invalid even when one is zero. A dated/description-bearing transaction candidate missing a flow is invalid, not silently discarded. Never use Balance.
- Extract only a printed, unambiguous statement period for prefill. Resolve each two-digit year to the unique date in the confirmed inclusive period. Cross-year periods are supported. If more than one century is possible, require explicit century selection before preparing the review; never use a runtime pivot. Dates not resolvable against that period are invalid.
- Reject encrypted PDFs even if an empty password opens them (`PdfDocument.IsEncrypted`); map encrypted-open exceptions to a safe unsupported-protection reason. Do not supply passwords. Reject image-only tables and unrecognized layouts; no OCR or external service is allowed. Detect the page count before page extraction and enforce row/character bounds during extraction.

**Generic CSV mapping**

- Inspect proposes, but never confirms, a header row and dialect. The user confirms a physical header-line number, delimiter (comma, semicolon, tab, or pipe), column indexes, date format, decimal separator, thousands separator (including none or spaces), and direction/sign convention. Columns use indexes so duplicate/missing header names cannot pick the wrong field. Show the interpretation before preparing a review; ignored columns are excluded thereafter.
- Require date and description columns plus either one signed amount column or separate debit and credit columns. A missing description cell is allowed with its warning; an absent description column must be explicitly mapped to the empty-description option. Separate debit/credit maps debit to outflow and credit to inflow; both populated is invalid. A zero in one column is populated. Signed mode supports explicit normal/inverted direction, parentheses, trailing minus, or CR/DR convention. Multiple conflicting sign encodings are invalid. Validate grouping; do not remove arbitrary punctuation until a number happens to parse.
- Supported date choices include `yyyy-MM-dd`, `yyyy/MM/dd`, `dd/MM/yyyy`, `MM/dd/yyyy`, and `dd Mon yyyy`. Two-digit equivalents require a confirmed century or unique resolution in the confirmed period. Ambiguous values never choose a culture automatically. The user may change mapping and re-parse, but cannot edit a row.
- An optional stable transaction-ID column and a confirmed source namespace may be mapped when the export documents its stability. Store these as identity discriminators, not as ownership or amount authority. Empty IDs fall back to tuple matching. FNB and Standard Bank presets provide no stable ID; never invent one from row number or filename.

For all formats, future dates are allowed with a warning. A valid date outside the confirmed coverage period is invalid for this review; the user adjusts coverage before preparation or explicitly skips the row. Changing coverage/mapping after preparation means abandon and prepare a new review, not mutate source fields.

### 4.3 Duplicate semantics

**Canonical fallback identity:** versioned SHA-256 over a length-delimited encoding of calendar date, signed integer cents (zero canonicalized), and normalized description. Scope all comparisons and uniqueness to `(customer_id, financial_account_id)`. Store canonical components as well as the digest; if a digest resolves to unequal components, fail closed as `identity_conflict` instead of suppressing data. Source type, page, row order, category, tags, and visibility never enter this identity.

**Occurrence matching:** after user skips, group valid incoming rows by fallback identity. With no stable IDs, if `I` incoming occurrences and `E` existing occurrences remain, suppress `min(I,E)` and propose `max(I-E,0)` as new. Use stable source positions only to display which indistinguishable rows were paired, not as cross-import identity. Removing an incoming row causes a fresh pairing; it must not make another row consume the same existing occurrence twice.

When both sides supply a validated namespace/ID, that discriminator extends their comparison key: pair equal IDs first; different nonempty IDs in the same namespace are distinct. Remaining incoming rows with IDs may consume only existing occurrences lacking a conflicting discriminator. Anonymous incoming rows then consume remaining existing occurrences by the fallback tuple. Consume each existing slot at most once. Repeated identical IDs still use counts. A matching stable ID with changed date/amount/description is a warning requiring review, not an automatic rewrite or silent suppression. Include mixed-ID/anonymous tests so adding an ID does not duplicate a transaction previously imported without one.

New transactions allocate unused occurrence indexes greater than the current maximum for that fallback key, under the account lock. Count existing rows, not maximum index: deleting an earlier import can leave gaps. Uniqueness is `(customer_id, financial_account_id, identity_key, occurrence_index)`. A legitimate exact override allocates a fresh slot. The receipt makes retrying the same override safe; an override is not a standing exemption for later uploads.

For mixed-ID matching, different namespaces make no claim about the same identifier: compare those rows by the fallback tuple. A conflict means two different nonempty IDs in the **same** namespace. Record matched occurrence IDs in the snapshot so deterministic matching can be checked at commit and no occurrence is consumed twice.

**Within-file:** flag every member of a repeated identity group, including new excess occurrences. Require a deliberate group keep-all or per-row include/skip decision before commit. Never silently drop identical coffees. Cross-import suppression and within-file decisions are separate controls; keeping all within-file rows does not itself override existing occurrences.

**Statement fingerprint:** hash a versioned canonical multiset of all sanitized parser row signatures, including multiplicity and invalid-row signatures, plus confirmed coverage dates. Exclude raw bytes, balance, preamble, filename, source order/container, tag choices, and skip/override decisions. Include mapped stable-ID discriminators when present. Equal logical FNB bare/ZIP inputs produce the same fingerprint. Matching transactions across PDF/CSV remain protected by row identity even when statement fingerprints differ.

Store one fingerprint slot per committed statement with a positive `fingerprint_occurrence`. Uniqueness is `(customer_id, financial_account_id, statement_fingerprint, fingerprint_occurrence)`. A default commit is refused if **any** live statement has that fingerprint, with owned existing-import links and counts. An explicit statement override allocates the next slot. This is not permission to bypass row suppression; exact-row overrides require separate confirmation. Hiding does not release fingerprint protection. Deleting a statement releases only its own slot; repeats still retained continue to protect themselves.

**Near duplicate:** same account, exact transaction date and exact signed amount, but not an exact match. Flag when normalized descriptions have token-set Jaccard similarity at least 0.5; also flag a conflicting stable-ID match. Empty descriptions match only each other for this warning. Apply the same rule within the incoming file and against existing rows, including hidden rows. Show up to five strongest candidates per row, sorted deterministically, and disclose additional candidates. A warning defaults to included. It never changes another transaction or independently suppresses a row. Algorithm version and reasons belong in tests and the review contract.

**Stale duplicate state:** preparation/refresh stores the account's `import_version` and computed decisions/summary hash. Commit rechecks it under lock. If another commit/deletion changed it, return `409 review_stale` with no commit. Refresh recomputes pairing and warnings; preserve user skips and tag selections, but require renewed confirmation for changed duplicate classifications/overrides. Commit does not silently change the reviewed counts.

## 5. Sources of identity, configuration, state, and policy

| Concern | Authority |
| --- | --- |
| Product/protocol identity | Existing `backend/Brand.cs` and `frontend/src/lib/brand.ts`; all structural headers derive from them. |
| Customer and entitlement | Existing finance guard and `Store.ValidateAndTouchSessionAsync`; backend customer lookup requires verified email and active subscriber timestamp. No admin bypass. |
| Financial destination | Owned `financial_accounts` row; active state is checked before parsing and again under lock at commit. Archive during review prevents commit until restored. |
| Parsed source values | Backend parsers at preparation, then immutable review rows, then committed `transactions`. Browser-supplied row values are never authoritative. |
| Duplicate policy | This plan, versioned normalization/identity code, and live PostgreSQL occurrences; neither client counts nor in-memory locks decide suppression. |
| Mutable review decisions | Versioned PostgreSQL review; one customer, fixed account, immutable parser configuration and coverage dates. |
| Committed operation | PostgreSQL receipt keyed by review UUID and customer, with request digest and final result; retained after review payload cleanup. |
| Visibility | Parent hidden flag OR individual hidden flag; no flattened second hidden authority. |
| Parser bounds | `backend/Imports/ImportLimits.cs`; matching frontend display constants are advisory and tested against the HTTP contract. No new environment variables or `NEXT_PUBLIC_*` settings. |
| Secrets and origins | Existing server-only `ARGUS_*` settings. Browser route checks against `ARGUS_FRONTEND_URL`, not a client-supplied forwarding header. |

## 6. Data/control flow and lifecycle

### 6.1 Inspect, confirm, prepare, review

1. `/imports` loads accounts/history through the existing server boundary. With no active account, offer inline account creation through the existing action or restoring an archived account in Settings. Archived accounts retain history but are not new-import destinations.
2. The browser selects a file through an accessible control. Do not render the native filename or persist it in component state beyond the `File` object. Display `Selected CSV/ZIP/PDF statement`, type, byte size, and target account. Send a generated safe multipart filename, not `File.name`.
3. A Node-runtime Next.js route handles `POST /imports/upload` with a fixed `inspect` or `prepare` mode. Check session and exact allowed Origin before reading the body, bound streamed bytes, reject extra file parts, and forward only fixed backend routes using server-generated trust headers. Never create a generic URL proxy. Backend independently authorizes before parsing. Both sides set `Cache-Control: no-store` and never include raw filenames in errors.
4. Inspect parses request-local bytes and returns detection/mapping choices, sanitized preview (at most 20 rows), proposed coverage dates, and safe reasons. It persists no finance records. FNB/PDF metadata and all balances are omitted even from preview. Generic header selection may be re-inspected; rows before the selected header are never transaction preview content. An FNB preamble remains suppressed even in generic mode.
5. User confirms format options, ZAR, dates, and direction. Prepare reuploads and reparses with those explicit options, stores sanitized immutable rows/validation and duplicate state, and immediately releases raw bytes. Prepare is not a committed import and does not enter history. If the response is lost, the user can list their open reviews or abandon and prepare again; no transaction exists yet.
6. `/imports/review/[id]` pages through the backend review. PATCH requests accept only version, row IDs, include/skip/override choices and selected active merchant-tag IDs; they may not carry source text/date/amount. Group decisions are expanded server-side across all rows, not just the displayed page. All rows remain readable and invalid reasons visible; commit stays disabled until invalid rows are skipped and within-file groups resolved.
7. Suggestions combine the customer's explicit aliases, the customer's own historical tag choices for the generated merchant key, and active owned tag-name token matching. Alias text, history and tag names all use the existing trimmed, normalized matching rules. Exact contiguous tokens rank before a truncated-prefix match, where a statement token is at least four characters and a prefix of a longer tag token of at least five characters; a longer statement token never shortens to a tag token. Alias and history inputs are scoped to the customer and only resolve to active owned tags; category suggestions are not part of import review. Start with no selected tags. Explicitly accepting a suggestion or choosing another tag sets row decisions; ignoring it writes no link. At commit lock and validate all chosen tags in deterministic ID order; foreign, deleted, or archived tags fail the entire commit without silently dropping selections. Tag rename refreshes labels/suggestions, not previously accepted IDs.

### 6.2 Commit and recovery

Commit body contains `expected_review_version` and `confirmed_summary_hash`. Its operation ID is the review UUID, not a fresh random key per click.

Within one database transaction: resolve owner; lock the account; look for an existing receipt; lock/revalidate the review; check expiry, fixed account active/ZAR, version/hash, validation and duplicate decisions; compare account `import_version`; enforce statement repeat policy; resolve the system category; lock/validate selected tags; recheck expiry after those waits and before the first statement insert; allocate statement and occurrence slots; insert statement, included rows and tag links; write reconciled summary/overrides and receipt; increment account `import_version`; delete the review payload; make the final fresh expiry check; commit. Ensure categories are seeded before entering this lock order. Match receipt owner/account and digest before replay; replay precedes expiry/stale checks because committed review payloads no longer exist.

**Revision 6 — L1 absolute-expiry boundary:** The pre-tag check is early rejection, not an irrevocable reservation. For new effects, sample PostgreSQL `clock_timestamp()` on the same connection/transaction after all selected tag locks/validation and before the first statement insert, and again immediately before database commit after intervening writes/waits and the existing `before_commit` test seam. The initial commit check may use the same small helper. Transaction-start `NOW()`/`CURRENT_TIMESTAMP` alone cannot establish expiry after a wait. Current time equal to or later than persisted `expires_at` returns existing `410 review_expired` and rolls back the attempt. Apply the guards even with no tags or zero newly inserted transactions. Do not change review creation, expiry values, lifetime, refresh, unrelated clocks, lock order, duplicate policy or cleanup retention.

No statement, transaction, tag link, occurrence/fingerprint allocation, receipt, account version bump, review edit/purge or success audit from an expired attempt survives. Existing cleanup may later expire/purge the review normally; refusal need not persist a new terminal transition. Existing committed/deleted receipt resolution remains ahead of every new-effect expiry check. The final guard is the last application decision to admit commit, not a promise about when PostgreSQL flushing or the HTTP response finishes. Do not introduce another deliberate wait/external action after it. This is an explicit decision under this Plan's own absolute lifetime and terminal/atomic contract, not an inferred copy of Plan 3's 15-minute policy.

All review edits/refresh/abandon and statement mutations acquire the owned account before the review/statement row, then chosen tags in sorted ID order before any new transaction/tag-link writes. Review creation acquires its customer quota lock before the account lock; commit does not reacquire that customer lock. Cleanup may lock a review to purge expired payload but must not then acquire an account lock. Keep these orders explicit in concurrency tests. Existing category/tag management may lock its own row first; commit validates and locks selected tags before inserting any children that could make a lock cycle with those operations.

- After durable commit, receipt replay returns the same statement ID and counts (`200`, `replayed: true`), including after application restart. Reusing the operation with a different version/hash returns `409 idempotency_conflict`.
- If a database error/cancellation happens before commit, rollback all writes and leave the review retryable. Do not label a dropped connection as definitely rolled back: query/retry the receipt operation. A connection loss around commit has an unknown outcome until receipt lookup.
- All-skipped or no-valid-row submissions are rejected as `400 no_included_rows`. A nonempty valid review that is entirely suppressed may commit a statement with zero **new** transactions and its real coverage/counts; that is not an empty/header-only file. A statement repeat still needs its explicit override. Do not transfer ownership of suppressed transactions to the new statement.
- Abandon and expiry purge review rows. Concurrent abandon/commit is serialized by the same account/review lock order; whichever wins is terminal. Cancel after successful commit returns its receipt, not a claim that the import was undone. Restart does not discard a prepared review or a committed receipt.

### 6.3 History, visibility, and deletion

History lists committed imports (including Hidden) with generated label, account, source format, confirmed dates, import timestamp, total/imported/suppressed/skipped counts, possible-duplicate and exact/statement-override counts, live effective-hidden count, and statement version. Do not show draft/failed/cancelled reviews as imports. Use `(created_at DESC, id DESC)` stable pagination; a separate owned open-review recovery list is not Import History.

Statement detail includes the summary and a paginated read-only list of the transactions **created by that statement**, with source position, tags, and effective visibility. This is the initial usable success link. Offer a link to `/transactions?statement_import_id=...` only once Thread #4 actually supports that filter; do not present the current placeholder as a working imported-transaction list.

Hide/restore/delete require a confirmation naming the generated statement label and exact affected transaction count. The request includes expected statement version, expected owned transaction count, and `confirm: true`. Count means all transactions owned by the statement, not total reviewed/suppressed rows. Under the account/statement lock recheck these values; stale scope returns conflict without effects. History/detail provides the fresh values for confirmation.

- Hide/restore changes only the parent flag/timestamp and increments statement version on a real transition. Increment child transaction versions on that transition so later transaction editing cannot overlook a visibility change. Repeating the same state with a fresh version is a successful no-op. Individual hide flags are untouched. All counts and read-only effective state use the common predicate.
- Delete removes that statement and exactly its owned transactions; tag links and source-ID discriminators cascade with them. Mark its commit receipt Deleted in the same transaction and preserve only the operation ID, owner/account, request digest, and safe terminal result. Delete releases fingerprint and row identity slots and increments account `import_version`. No raw/parsed review payload or orphan tag link survives. Suppressed rows owned by another import are never deleted.
- A repeated delete with the matching original confirmation may return its safe deleted result from the tombstone; unknown/foreign IDs still return identical 404. A concurrent old commit retry returns `410 import_deleted`, never recreates data. A new prepared review can import the deleted occurrences; occurrences still owned by other imports remain suppressed.

### 6.4 Migration `0006_statement_import`

Append a new migration; never change executed `0005`:

- Add `financial_accounts.import_version BIGINT NOT NULL DEFAULT 0`.
- Add `statement_imports.version`, `fingerprint_occurrence`, duplicate-policy version, possible-duplicate/override counts, and a compact override summary containing source row IDs/reasons and integer counts only. Enforce positive versions/slots, ordered dates, nonnegative counts, and the reconciliation equation. Add the fingerprint-slot uniqueness described above and `(customer_id, created_at, id)` history index.
- Add transaction identity-version and nullable source-ID namespace/value discriminator fields, with both-or-neither validation. Add occurrence-slot uniqueness, positive occurrence index, `(customer_id, statement_import_id, source_row_number)` uniqueness, and indexes for statement children and near-match lookups `(customer_id, financial_account_id, amount, transaction_date)`. Preserve current source fields and version/user-hide semantics.
- Strengthen relational account consistency: a transaction's `(customer_id, financial_account_id, statement_import_id)` references the corresponding unique triple on `statement_imports`. Existing same-customer foreign keys alone do not prevent linking two accounts belonging to the same customer.
- Add `customer_id` to `transaction_merchant_tags`, backfill it through its transaction, and add same-customer composite references to transactions and merchant tags. Preserve its pair primary key. Adapt every existing insert/delete/join/test helper mechanically in the same phase; names, counts, and deletion behaviour stay compatible. Reject mismatched legacy assignments before constraint installation rather than discarding them.
- Add `import_reviews` (owner/account/configuration/confirmed dates/currency/version/expiry/snapshot/summary and Prepared/Abandoned/Expired state), `import_review_rows` (owned review/row ID/position/immutable mapped fields and interpretation/validation/mutable choices), and `import_commit_receipts` (owner/account/operation ID/request digest/statement reference or Deleted state/result counts). Use customer/account composite references and cascade review-row cleanup. The receipt's operation ID survives review deletion; it has no cascading dependency on a review. Statement deletion must null its live reference and retain a terminal tombstone, not delete it. Retain the original statement UUID and deletion-confirmation digest/count in that tombstone to authenticate an identical delete retry without retaining any financial source fields.
- Add a customer-scoped quota lock path for review creation/cleanup; no global database lock. Payload limits include all persisted mapped-source text and context.
- Backfill any existing transaction identity from stored canonical fields and allocate deterministic occurrence indexes in each customer/account group. Preserve every existing transaction and statement; derive missing statement fingerprint slots deterministically from their owned rows/period. Do not merge, delete, round, or replace historical records. Preflight impossible ownership/count/precision states and fail migration with an operator-safe diagnostic. Tests cover upgrade from populated `0005`, not only a clean database.

The existing schema has no application-created imports, but its tests insert rows directly. Update those fixtures to satisfy the new constraints. Do not exempt test-like rows from production invariants.

## 7. Phases, dependencies, scope, and verification

Phases are sequential. They define implementation meaning, not assignments, session choices, or implementation review points. Each phase records PASS/FAIL/NOT RUN evidence for its criteria; an unavailable required environment does not count as a pass.

### Phase 1 — Bounded parsers and synthetic format evidence

**Dependency:** Published Plan 1 baseline. **Purpose:** establish deterministic parsing/validation before persistence consumes it.

**Exact scope:** `backend/Imports/` parser/domain/limits/child-runner files, explicitly including the approved strict `CsvRecordReader.cs`; `backend/Program.cs` early child-mode dispatch only; `backend/argus.Server.csproj` pinned PdfPig reference; backend test project resource/build-copy entries; `StatementParserTests.cs`, `StatementParserProcessTests.cs`, `Fixtures/Statements/` and `StatementFixtureBuilder.cs` under the existing backend test project. No migration or import route is made reachable in this phase.

**Work and acceptance:**

- Implement Section 4.2 and the format/amount/date/normalization value contracts. Disable parser logging; all exceptions map to enumerated safe reasons. ZIP validation completes before CSV parsing and never extracts to disk.
- Preserve the contract with the approved custom CSV tokenizer: verify physical start/end lines for quoted multiline records and the following record under LF/CRLF, including preceding blank/preamble lines; escaped quotes; malformed post-quote text such as `"Shop"garbage`; unmatched/illegal quotes; exact header width; strict UTF-8 rejection; and cap/cap-plus-one behaviour during reading. Exercise FNB, generic inspection, and confirmed generic parsing through the shared reader. Malformed quoting must remain invalid and record-limit exhaustion must not return a successful truncated statement. These checks instantiate the existing source-evidence, validation, and bounds obligations; they do not weaken or replace the phase's original criteria.
- Supply synthetic FNB card/cheque structures with six-line canary preambles, UTF-8 BOM, exact header spacing, padded/unpadded descriptions, zero/running balances, and **150/199** data rows. Generate matching ZIP and bare forms; assert identical parsed rows despite different filenames/balances.
- Supply actual PDF bytes, not just mocked text coordinates: four-page/49-row and eighteen-page/297-row structures, **66/356** continuation lines, reordered content streams, repeated headers/footers, cross-page continuation, cross-year dates, missing period, valid fee flow, all three flow directions, and invalid/missing/conflicting flow cases. Expected rows/cents/pages come from an independent fixture manifest, not the parser's own output. Include an independently hand-authored small PDF to avoid relying only on the extraction library's PDF writer. Fixture text is synthetic and contains no real account identifiers.
- Generic fixtures cover every confirmed delimiter/date/sign/decimal grouping choice, ambiguous numeric/date values, quoted commas/newlines, duplicated headers, malformed field counts, zero/missing descriptions, future/out-of-period dates, overflow, and precision beyond cents.
- Negative archive/PDF fixtures cover zero/multiple entries, extra non-CSV files, nested/encrypted/traversal/symlink archives, lying/oversized expansion, malformed ZIP, encrypted PDFs (including empty-password encryption), image-only tables, wrong/ambiguous layout, too many pages/rows/chars, and unreadable fonts. Whole-file rejections yield no parser success result; recoverable row defects produce invalid rows that cannot become valid by truncation or balance fallback.
- Exercise the actual parser child entry point from the built server assembly. Cancellation, timeout, excess output, abnormal exit, and parent disconnect terminate/reap the child and expose no partial prepared result. Verify child mode does not start a listener, load database settings, run migrations, or emit raw diagnostics.

**Engineer-owned review notes (single review; no gate blocked):**

- **P2-N3 (MEDIUM — worker lifetime and pipes):** Extend process tests to abrupt parent termination after input delivery, a child that stops reading, blocked input/output, and enough standard error to fill a redirected pipe. Discarding diagnostic content must not leave an undrained pipe. Enforce bounded child lifetime even if the request handler/parent is gone; verify cleanup where the parent remains alive and no partial success result or raw diagnostic leaks. Exercise both `dotnet run`/build-output launch and the existing apphost-free release form, `dotnet argus.Server.dll`. Record the actual guarantees, not a claim of kernel sandboxing. Phase 4 carries the deployment evidence.
- **P2-N6 (LOW — audit evidence):** Inspect and record the vulnerability-list result, not only command exit status. A reported vulnerability fails the stated dependency gate; an unavailable/incomplete vulnerability-data lookup is UNVERIFIED, not clean. Reuse the existing CI interpretation in `.github/workflows/ci.yml:76–83` where appropriate; no workflow change is required. Repeat the evidence check in Phase 4.

**Commands** (working directory `backend/`):

```bash
dotnet restore argus.Server.csproj
dotnet format argus.Server.csproj --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet test tests/argus.Server.Tests --filter 'FullyQualifiedName~StatementParser'
dotnet build -c Release
dotnet publish argus.Server.csproj -c Release --no-restore
dotnet list argus.Server.csproj package --vulnerable --include-transitive
```

Parser tests are database-independent; they must not instantiate `ApiTestBase`. PdfPig is Apache-2.0 and its published 0.1.16 assets support .NET 8/9 and .NET Standard. The actual `net10.0` restore/build/tests, not that compatibility claim alone, establish feasibility.

### Phase 2 — Durable review, duplicate authority, atomic API, and statement lifecycle

**Dependency:** Phase 1 parser contract. **Purpose:** expose the complete customer-isolated backend import contract without a client-authoritative data path.

**Exact scope:** migration in `backend/Data/Db.cs`; new `backend/Data/StatementImportStore.cs`; new `backend/Http/StatementImportEndpoints.cs`; shared guard extraction in `backend/Http/FinanceGuard.cs` with the existing `FinanceEndpoints.cs`/`HttpSupport.cs` integration; `backend/Http/MultipartImportReader.cs` for bounded request reading; `backend/Data/ImportCleanupService.cs` for the specified startup/15-minute cleanup; `Program.cs` registration/cleanup; `Security/RateLimiter.cs`; `backend/Imports/` review/identity/suggestion services, explicitly including the no-op/test-only fault seam in `ImportFaultInjector.cs`; mechanical tag-link SQL changes in `FinanceStore.cs`; backend import/migration/concurrency tests and existing fixture adaptations; import DTOs/client functions and request-contract tests in the frontend server library. Do not expose duplicate auth logic that can drift from finance guards.

**Work and acceptance:**

- Install Section 6.4 constraints without data loss. Update database resets for reviews/receipts. Add raw database tests rejecting cross-customer tags and same-customer wrong-account statement links.
- Implement Sections 4.3 and 6, including durable review quotas/expiry, receipts, snapshot refresh, archive recheck, exact tag validation, committed-only history, parent visibility, deletion, and tombstones. Only safe identifiers/counts/statuses may enter audit events. A parser rejection persists no statement, transaction, or prepared review.
- Add the Section 8 HTTP contract. Apply origin/application-secret/session/subscriber/rate guards to every method, including inspect, review reads, recovery, replay, and delete. Bound multipart/JSON before buffering. Foreign account inspect/prepare requests must not invoke a parser.
- Test the complete positive path through `WebApplicationFactory` + real PostgreSQL + actual CSV/ZIP/PDF bytes and parser child. Include synthetic FNB/PDF cross-format rows and generic reordering. This is integration evidence across the included boundaries, not a browser observation.
- Prove occurrence counts, gaps after deletion, exact/statement override separation, mixed source-ID matching, retained hidden protection, all-suppressed summaries, invalid-row accounting, stale reviews, multi-page choices, restart receipt recovery, and no resurrecting deleted commit.
- Force failures after the statement insert, a transaction insert, tag-link insert, and receipt write before transaction commit; assert zero partial durable finance records and a retryable review. Simulate a successful durable commit followed by a lost HTTP response and verify replay. Use explicit fault seams/test helpers, not production request flags.
- Race two different reviews for one account, repeated commits of one review, deletion versus commit, abandon versus commit, tag archive/delete versus accepted selection, and account archive versus commit. Outcomes are one serialized reviewed result or a clean conflict; never duplicate effects or dropped tag choices. Account-independent imports need not block each other.
- Add typed server-only frontend client functions and tests in this same phase. Preserve existing finance error union behaviour; use a separate import error/result union so `finance-actions.ts` error maps stay exhaustive. Run the frontend workspace checks against the shared response shapes now, not after UI work.

**Engineer-owned review notes (single review; no gate blocked):**

- **P2-N1 (MEDIUM — mixed-ID pairing):** Implement deterministic maximum-compatible pairing, or an equivalent method proved not to lose a compatible match through greedy traversal. For one fallback tuple, test existing `(namespace A, ID x)` plus anonymous against incoming `(namespace B, ID p)` and `(namespace A, ID y)`: pair B/p with A/x and A/y with anonymous, suppressing both. Include incoming permutations and occurrences spread across existing imports. Assert identical counts and one-use consumption of every existing occurrence. A deterministic first-fit ordering alone is not sufficient evidence.
- **P2-N2 (MEDIUM — effective database lock order):** Choose a customer-quota lock compatible with PostgreSQL foreign-key locks and document implicit as well as explicit acquisition. A `FOR UPDATE` customer quota lock held while waiting for an account can cycle with commit's account lock and its implicit customer `KEY SHARE` reference check. Race prepare/commit for the same account and different accounts of the same customer, plus cleanup/preparation. Prove no partial effect, no unexplained deadlock server error, and no accidental global serialization. The account-lock architecture remains unchanged; the quota mechanism is an implementation choice.
- **P2-N4 (MEDIUM — duplicate work bounds):** Bound matching/refresh query and computation time as well as parser output. Use indexed/batched access and cancellation; do not materialize unbounded candidate products or assume five displayed warnings bounds the search. Test dense same-amount/date inputs and refreshes against substantial existing history. Exhaustion returns a safe failure without committed effects; never silently truncate matching in a way that changes duplicate policy. Measure this in Phase 4.
- **P2-N5 (MEDIUM — preserve selected-tag intent):** Store accepted tag-ID intent independently of active tag records/suggestions, and preserve it through reads/refreshes. Neither cascading removal nor filtering through the current active set may silently erase a selection; a restrictive review reference must not block existing tag deletion. Test prepare/select, tag delete/archive, review read/refresh, and atomic commit rejection until the customer explicitly changes the selection. A rename changes labels, not accepted IDs. Phase 3 verifies the corresponding UI behaviour.

**Commands** (working directory `backend/`, then `frontend/` respectively):

```bash
dotnet format argus.Server.csproj --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet test tests/argus.Server.Tests
dotnet build -c Release
```

```bash
npm run lint
npm run test
npm run build
```

### Phase 3 — Import screens, bounded frontend transport, and history controls

**Dependency:** Phase 2 API/client contract. **Purpose:** make the workflow reachable without leaking credentials or inventing source fields.

**Exact scope:** `frontend/src/app/imports/` pages/components/upload route, explicitly including the pure request-option builder in `import-options.ts`; `frontend/src/lib/server/import-actions.ts`, import DTOs/client; minimal existing `finance-actions.ts` account-action reuse; `frontend/src/proxy.ts` only if tests prove a narrow method-specific response is needed for upload; `frontend/vitest.config.ts` and `frontend/vitest.setup.ts` only for the required real-library auth composition, route/component tests, and isolated mocks/reset; matching frontend tests, auth composition test, public smoke tests. No Plan 3 list or reporting screen is implemented.

**Work and acceptance:**

- Replace the no-upload placeholder/test intentionally. Provide account choice/inline creation, current-step indicator, generated selected-statement label, busy/cancel/error states, mapping preview, explicit ZAR/date/format confirmation, a paginated read-only review, row skip controls, separate duplicate decisions, unselected tag suggestions, and commit with reconciled totals. No source-field editing controls exist. Changing mapping/coverage abandons/prepares, rather than patches rows.
- `POST /imports/upload` is a fixed-purpose Node route, not a file-bearing Server Action; do not raise a global Server Action body limit. Authenticate and check exact Origin, stream-limit request bytes, create safe multipart names, forward fixed routes/trusted headers, propagate disconnect cancellation, and map safe backend errors. Small review/lifecycle mutations may use Server Actions with existing same-origin protection and backend reauthorization. Never send backend origin, application secret, or session token into client component props.
- Provide history and `/imports/[id]` detail with real summary/read-only owned-row links; open-review recovery is visibly separate. Confirm hide/restore/delete with fresh version/count, support 409 refresh, and preserve individual hide flags. Handle missing/deleted resources and expired reviews plainly. Keep an uncertain commit retry tied to the same review operation; do not automatically create a new review on network failure.
- Revalidate Imports, Settings counts, Transactions, and Overview paths after committed mutations, while leaving later screens honest about their currently unimplemented capabilities. URLs contain only IDs and nonsensitive filter values; no raw financial data is placed in query strings, browser storage, or analytics.
- Use existing design tokens, semantic controls, focus styles, themes, and labelled multi-tag controls. Paginated/bulk group actions state their scope. At 320/375 px and desktop, amounts and primary controls remain readable without horizontal page overflow. Keyboard users can select a file, map fields, skip rows, accept suggestions, commit, and confirm deletion. Use live status announcements and focus the first blocking reason on failure.
- Add an **unmocked pinned Auth.js composition test**: encode a real test JWT cookie, execute the installed server `auth()` path with Next request/header boundaries supplied, and verify `user.backendToken` reaches a server client call. Independently exercise the public session handler and assert no nested or top-level backend token/expiry leaks. Keep existing public-session/cookie tests. If this fails, stop for a boundary correction; do not fix it by exposing credentials publicly.
- Keep `import-options.ts` a pure request builder within the existing upload contract: initial generic inspection without a mapping omits `parser_config`; subsequent confirmed inspect/prepare sends the appropriate configuration. The helper is not a parser or a new confirmation authority. Test-runner configuration/setup may inline the pinned Auth.js packages, resolve the installed `next/server` implementation, and supply/reset Next navigation boundaries where needed. The composition test must still execute the real pinned Auth.js path; no runner change may mock away the behaviour being proved, weaken assertions, bypass route limits, or alter runtime authentication/deployment configuration. Re-run the existing frontend battery after these shared test-harness changes.
- Unit/component tests verify all decisions/errors and secret absence. Route tests exercise real byte limits and Origin rejection, not only a mocked action. Existing Playwright continues to check public/unauthenticated routes and can cover new review/detail redirect guards, without claiming it tested an authenticated import.

**Engineer-owned review-note continuation:**

- **P2-N5 (MEDIUM — UI side):** After an accepted tag is deleted or archived elsewhere, read/refresh must still show that the review has an unavailable selected ID. Commit must display the backend's atomic rejection rather than silently clear the choice. Only an explicit customer change removes that selection; tag rename refreshes its label while preserving its ID. Cover both component state and the action/error path.

**Commands** (working directory `frontend/`):

```bash
npm run lint
npm run test
npm run build
npm run test:e2e
```

### Phase 4 — Cross-boundary acceptance evidence and operating documentation

**Dependency:** Phases 1–3. **Purpose:** close the implemented import contract with honest evidence and recovery guidance.

**Exact scope:** test/fixture refinements within earlier phase paths; `docs/repository-layout.md`; new `docs/statement-import-runbook.md`; narrowly required import notes in `README.md` and `docs/tls-deployment-contract.md`. Application changes remain restricted to fixes within the earlier contracts; this phase cannot add features or change identity policy.

**Work and acceptance:**

- Run every Section 9 criterion and Section 11 adversarial scenario. Capture database counts/receipts, safe audit samples, parser exit/cleanup observations, and frontend test results without including private data or secrets.
- Conduct a local runtime walkthrough using synthetic files, two customers, the real backend, PostgreSQL, and Next.js: create account, inspect/map, prepare/skip, accept one suggestion, commit, recover a deliberately lost response, view history/detail, hide/restore, delete, then newly reimport. Observe public-session redaction and missing/foreign identifiers. Record the exact environment and paths exercised. This is separate from backend-independent Playwright; do not present component mocks as live evidence. Do not run the bootstrap or create local accounts/databases without the operator's required setup approval; if infrastructure is unavailable, mark this criterion NOT RUN and keep completion blocked.
- Manually check keyboard workflow and 320/375 px plus desktop in both themes on the running import UI. Automated component assertions alone do not prove layout or assistive usability.
- Document supported structures/unsupported variants, limits, raw retention, review expiry, zero-new-row summaries, statement/transaction ownership, both hide layers, stale reviews, uncertain commits, terminal deletion, parser child deployment/cleanup, and migration backup/recovery. Update the HTTP/tree reference. No real statement samples enter the repository or evidence record.
- Run the full project battery below. Record timings for a 10,000-row synthetic import and 18-page PDF with environment details; the brief's under-30-second import goal is a hypothesis, not a guaranteed service level. Exceeding the hard parser deadline must produce a safe failure, never a partial import.

**Engineer-owned review-note evidence:**

- **P2-N3 (MEDIUM):** Record release-output/apphost-free execution and the hostile worker/pipe cases from Phase 1 in the operating evidence, including parent-death bounds and actual cleanup guarantees. Do not substitute a cooperative-cancellation test for those cases.
- **P2-N4 (MEDIUM):** Record dense duplicate matching and refresh measurements against substantial existing history, including cancellation/timeout and safe no-effect failure. Parser timings alone do not cover duplicate computation or time spent holding an account lock.
- **P2-N6 (LOW):** Record the actual final vulnerability findings/data-availability status. A successful tool exit is not a clean audit; unresolved vulnerabilities fail the specified dependency gate, and incomplete data is UNVERIFIED.

**Commands:** the full Section 10 battery. No new implementation-review mandate or worker assignment is added by this phase.

### Phase 5 — Absolute expiry after commit waits (revision 6)

**Dependency:** existing CSV-only import implementation at `eda9511`. **Purpose:** enforce Section 6.2's explicit admission boundary without changing import identity or receipt semantics. **Exact scope:** the revision-6 L1 allowance in Section 8 only.

- [x] Hold a selected valid tag in an independent PostgreSQL transaction. Submit a coherent real commit request before a short future review deadline, observe its actual lock wait, wait for fresh database time to cross the stored expiry, then release the tag. Require `410 review_expired` and no surviving statement/transaction/tag-link/receipt/account-version/source/selection effect or success audit. Retain before-fix FAIL and after-fix PASS evidence. A fixed sleep or simultaneous launch alone does not prove that branch.
- [x] Pause an actual commit at the existing `before_commit` test seam after tentative writes, cross expiry, then release it. The final guard must refuse and roll back every tentative effect, preserving the original review payload until normal cleanup. No production request/environment flag enables the seam.
- [x] Preserve ordinary still-valid commit, no-tag and all-suppressed/zero-new-row behavior. Retain already-expired, foreign, abandon, version and selected-tag-deletion regressions. A new effect without tags is not exempt from final expiry.
- [x] Commit while valid, then retry the same operation after its original deadline and through a new host/service scope against the persisted test data. Return the same receipt without duplicate effects. Preserve terminal deletion and receipt-first resolution after payload purge.
- [x] Update the statement-import runbook with early/post-tag/final checks, final-admission meaning, rollback and receipt replay. Record exact results and limits on Thread #4; do not change Thread #3's status or infer programme progression.

**Acceptance:** an expired uncommitted review cannot produce a new durable import effect after either tested wait; committed operations remain replayable after the old deadline.

**Commands in `backend/`:** both format commands in Section 10; `dotnet test tests/argus.Server.Tests --filter 'FullyQualifiedName~StatementImportConcurrencyTests|FullyQualifiedName~StatementImportContractTests|FullyQualifiedName~StatementImportFaultTests'`; `dotnet test tests/argus.Server.Tests`; `dotnet build -c Release`. After the combined Thread #4 corrections, also run the existing frontend lint/test/build/Playwright battery and root `git diff --check`. Use only the dedicated `argus_test` database; no bootstrap, development-data reset/repointing or restart of existing development services is authorized.

## 8. Exact file scope and public/shared contracts

### File scope

| Paths | Allowed changes |
| --- | --- |
| `backend/Imports/*.cs` | New parser/domain/limits/identity/review/suggestion/child-runner code. Internal file splitting is allowed here without changing semantics. |
| `backend/Imports/CsvRecordReader.cs` | Explicitly approved shared strict CSV tokenizer within the existing wildcard: physical line ranges, malformed-quote state, and bounded reading. Preserves Section 4.2's contract; replaces the original `TextFieldParser` choice. |
| `backend/Imports/ImportFaultInjector.cs` | Explicitly approved within the existing wildcard: no-op production interface/implementation for named commit-boundary fault tests. Test dependency injection may substitute it; no request/environment-controlled failure switch or production bypass. |
| `backend/Data/Db.cs`, `StatementImportStore.cs` | New migration and all customer-scoped review/commit/history/lifecycle SQL. |
| `backend/Data/ImportCleanupService.cs` | Startup/15-minute expired-review cleanup already required by Section 2; retain terminal headers/receipts, cancellation and privacy rules. No new job system or retention policy. |
| `backend/Data/FinanceStore.cs` | Only tag-link ownership adaptation and shared owned-account/category/tag helpers required by this contract; preserve existing entity behaviour. |
| `backend/Http/StatementImportEndpoints.cs`, `FinanceEndpoints.cs`, `HttpSupport.cs` | New import routes/DTOs/safe bounded readers; extract/reuse the finance guard without altering auth semantics. |
| `backend/Http/FinanceGuard.cs` | Shared extraction used by entity and import routes. Preserve the existing origin, application-secret, opaque-session, verified-subscriber, rate-limit and safe-error chain; do not create a parallel authority. |
| `backend/Http/MultipartImportReader.cs` | Bounded multipart reader within the existing request contract, after authentication. Preserve exact file/options bytes, limits, malformed-input rejection and privacy; naming this helper does not accept the implementation or waive the truncation finding. |
| `backend/Program.cs`, `backend/Security/RateLimiter.cs`, `backend/argus.Server.csproj` | Early parser child mode; registrations/expiry cleanup; bounded parser/import buckets; pinned PDF dependency. No new environment surface. |
| `backend/tests/argus.Server.Tests/{StatementParserTests,StatementParserProcessTests,StatementImportContractTests,StatementImportConcurrencyTests,StatementImportMigrationTests,StatementFixtureBuilder}.cs`, `Fixtures/Statements/**` | Synthetic fixtures, independent manifests, parser/process/API/database/migration tests. |
| `backend/tests/argus.Server.Tests/{ApiTestBase,TestEnvironment,FinanceContractTests}.cs`, `argus.Server.Tests.csproj` | Multipart helpers, reset/owned-tag fixture adaptations, safe fault seams and child assembly resources. Existing assertions are preserved, not weakened. |
| `frontend/src/lib/server/{backend-client,import-actions,finance-actions}.ts` | Import types/client/actions; minimal account-action reuse. Existing error/response contracts stay compatible. |
| `frontend/src/app/imports/**` | Imports/history entry, `upload/route.ts`, `review/[id]/page.tsx`, `[id]/page.tsx`, local accessible components and loading/error states. IDs in these paths are the only source-specific path content. |
| `frontend/src/app/imports/import-options.ts` | Explicitly approved within the existing subtree: pure builder for initial generic inspection versus confirmed mapping/prepare options. No new HTTP fields or implicit confirmation. |
| `frontend/src/proxy.ts`, `frontend/src/types/next-auth.d.ts` | Only narrow upload unauthorized handling and accurate server-session typing if required; preserve proxy protection and public redaction. No auth configuration rewrite is planned. |
| `frontend/tests/app/imports/**`, `tests/lib/server/{backend-client,import-actions,finance-actions}.test.ts`, `tests/{auth-session.integration,proxy}.test.ts`, `tests/auth.test.ts`, `tests/e2e/smoke.spec.ts` | Contract, upload route, review/history interactions, real-library session composition, guards, and backend-independent smoke coverage. |
| `frontend/vitest.config.ts`, `frontend/vitest.setup.ts` | Bounded test-runner resolution/inlining and isolated Next navigation/request mocks/reset for the specified auth composition and route/component tests. Keep the composition's Auth.js and security assertions real; do not alter application/runtime/deployment configuration or weaken other tests. |
| `docs/repository-layout.md`, `docs/statement-import-runbook.md`, `docs/tls-deployment-contract.md`, `README.md` | Operational HTTP/lifecycle/limits/deployment/recovery documentation only. |

Mechanical adaptations to imports/types/helpers in these named files are allowed when needed to preserve a changed shared contract. The helper extractions named in revision 2 may remain at these paths; folding them into earlier named files is not required. No application/test/configuration edits are authorized by **authoring or correcting** this plan. If implementation needs a different runtime, parser choice beyond the approved custom CSV reader, parser package/version, public contract, unlisted application surface, or product behaviour, stop and seek an Architect correction rather than silently broadening the plan. No frontend dependency, runtime/deployment configuration, or CI workflow change is required; only the two named Vitest harness files are added to configuration scope.

### Revision 6 — exact L1 allowance

The correction is narrower than the original implementation table. For L1 only:

- `backend/Data/StatementImportStore.cs`: commit expiry guards, a small local database-clock helper and, if needed, a named call to the existing no-op/test fault seam. No parser, receipt, duplicate, source, visibility, deletion or tag-lock-order redesign.
- `backend/tests/argus.Server.Tests/StatementImportConcurrencyTests.cs`: real tag-wait expiry, no-tag/zero-new-row applicability and persisted no-effect assertions.
- `backend/tests/argus.Server.Tests/StatementImportContractTests.cs`: ordinary/expired commit, fixed expiry, replay and terminal compatibility assertions.
- `backend/tests/argus.Server.Tests/StatementImportFaultTests.cs`: final-precommit expiry rollback and receipt recovery through existing injection.
- `backend/tests/argus.Server.Tests/ApiTestBase.cs`: optional narrow database-observation/time-wait helpers; no reset/assertion weakening or global clock service.
- This Plan, `docs/statement-import-runbook.md`, and the narrow reference/evidence in `docs/plans/03-transactions-and-classification.md` and `docs/transaction-workspace-runbook.md`.

No `Db.cs`, migration, `ImportCleanupService`, parser, frontend application, configuration, dependency or PDF activation change is included. The existing `IImportFaultInjector` interface and registration need no change. Stop for an Architect decision if another surface is required. The operator explicitly approved the bounded correction and execution; this does not authorize further review, publication or deployment.

### Backend HTTP contract

All routes use `/v1/finance`, snake_case JSON, the existing trusted-caller and verified-subscriber guard, and no-store responses. Unknown JSON properties are rejected. Multipart uses exactly `file` plus `options` JSON, generated safe filename, explicit source kind, and fixed metadata limits. The backend validates content, not just extension/MIME. Route authentication happens before body parsing; use explicit bounded multipart readers, not implicit `IFormFile` binding that can buffer before the guard or spool to disk.

| Method/path under `/v1/finance` | Request | Success |
| --- | --- | --- |
| `POST /imports/inspect` | Multipart options: owned `financial_account_id`, `source_kind`, optional proposed `parser_config`/dates | `200` detected format, limits, sanitized preview, mapping choices/proposed period and required confirmations; no persistence. |
| `POST /imports/reviews` | Multipart options: account, source kind, confirmed parser config, `currency: "ZAR"`, `confirm_zar: true`, `start_date`, `end_date` | `201` review summary/version/expiry and first page of immutable rows. |
| `GET /imports/reviews` | Bounded pagination | `200` customer's nonexpired open review summaries, separate from history. |
| `GET /imports/reviews/{id}` | `page`, `page_size <= 100` | `200` review, counts, row decisions, reasons, suggestions, matching owned-import links. |
| `PATCH /imports/reviews/{id}` | `expected_review_version`, bounded row/group decisions, optional statement override confirmation | `200` new version, recomputed counts and summary hash. Source/configuration fields are forbidden. |
| `POST /imports/reviews/{id}/refresh` | Expected review version | `200` refreshed duplicate snapshot, new version/hash, renewed-confirmation flags. |
| `POST /imports/reviews/{id}/commit` | `expected_review_version`, `confirmed_summary_hash` | `201` statement/summary on first commit; `200` same receipt with `replayed: true` on identical retry. |
| `POST /imports/reviews/{id}/abandon` | Expected review version | `200` abandoned; or safe committed receipt if commit won. No financial undo. |
| `GET /imports` | Cursor, optional owned account and visible/hidden/all state | `200` bounded committed history, default all states with explicit Hidden badge. |
| `GET /imports/{id}` | Child page/page size | `200` statement summary/version, owned read-only created rows, live hidden count, confirmation scope. |
| `POST /imports/{id}/hide`, `/restore` | `expected_version`, `expected_transaction_count`, `confirm: true` | `200` updated summary/version/effective-hidden count. |
| `DELETE /imports/{id}` | JSON confirmation/version/count | `200` safe deleted result and actual deleted transaction count; matching replay may use tombstone. |

While Revision 5 is active, `source_kind = standard_bank_pdf` is rejected by
both upload routes with `400 pdf_import_deferred` before any decode, parser
child or durable effect. This is the only PDF-specific response the active
product exposes.

Review rows expose `row_id`, source line/record/page/context, `raw_values` restricted to mapped source fields, interpreted date/description/amount string or null, validation/warnings, duplicate reasons/matching owned IDs, skip/include/override state, suggestion IDs, and selected tag IDs. Summary counts and `confirmed_summary_hash` cover all pages. Statement DTOs expose only generated label/account/source/period/timestamps/counts/state/version and recorded override annotations. Neither DTO exposes fingerprint digests, source-ID values as URL fields, balance, metadata, filenames, backend tokens, or internal diagnostics. Committed rows need not retain raw mapped-field copies; immutable interpreted fields and source position suffice.

Errors use fixed codes and human-readable frontend messages: existing `401 unauthorized`, bodyless `403`, `404 not_found`, `429 rate_limited`; add `413 file_too_large`, `415 unsupported_format`, `400 invalid_request`, `unsupported_encoding`, `unsafe_archive`, `encrypted_pdf`, `text_pdf_required`, `unsupported_layout`, `processing_limit`, `format_confirmation_required`, `pdf_import_deferred`, `invalid_rows`, `duplicate_decision_required`, `no_included_rows`, `tag_unavailable`; `409 review_stale`, `version_conflict`, `statement_repeat`, `idempotency_conflict`, `account_archived`, `identity_conflict`; `410 review_expired` or `import_deleted`; `429 import_busy`; safe `500 internal_server_error`. A foreign review/import uses 404 rather than 410. Row error details may contain safe codes/positions and already authorized mapped values, never exception messages or omitted metadata. Existing import IDs returned by duplicate checks are customer-scoped.

## 9. Acceptance criteria and required evidence

Each criterion requires the described behaviour, including absence of effects, not just a green test count. All implementation verification is **NOT RUN at plan authoring**.

| Criterion | Observable outcome and evidence |
| --- | --- |
| **AC1 — Format fidelity** | Both synthetic FNB forms and the 4/18-page PDF structures produce every expected source row/date/description/cents/page exactly once. Generic choices cover the listed dialects/signs. Balance/preamble changes affect no values/identities. `StatementParserTests` plus real-byte API tests; unit and backend integration evidence. Brief acceptance criteria AC-IMP-1, 9, 11–14, 17–19, 26. |
| **AC2 — Safe rejection and bounds** | Unsafe ZIP/PDF/encoding/layout/type/size/page/row inputs reject safely with zero prepared reviews/statements/transactions; child timeout/cancel/exit is cleaned up. Recoverable bad rows stay invalid with source position and mapped raw values, never auto-fixed. Parser/process, API, and upload-route tests include boundary and limit+1 cases. AC-IMP-10, 15–16, 20, 26; SEC-3/8. |
| **AC3 — Explicit interpretation and immutable review** | ZAR, coverage and generic ambiguities require confirmation. Invalid rows block until skipped; within-file groups require a decision; no patch/commit body can set source fields. All-page counts reconcile. Missing description/zero/future rows behave as specified. Component tests and database/API tests. AC-IMP-2–3, 6, 13, 17, 27. |
| **AC4 — Occurrence-aware idempotency** | Existing one/incoming two creates one; repeated/reordered/container-changed/cross-format inputs do not create duplicates; different accounts do not collide. Hidden rows suppress. Deletion gaps use counts, not maximum slot. Stable-ID and anonymous mixtures consume slots once. Exact and statement overrides are independently explicit and retry safely. Database/concurrency tests with final counts and uniqueness assertions. AC-IMP-5, 21–25; DUP-1–15. |
| **AC5 — Atomic and terminal lifecycle** | Faults before database commit leave no partial statement/transactions/tags/receipt. Lost-response retry and restart return one result. Concurrent stale review changes conflict; abandoned/expired review cannot commit; deleted receipt never resurrects data. `StatementImportContractTests` and `StatementImportConcurrencyTests`, plus local lost-response runtime exercise. AC-IMP-4, 23; SEC-7. |
| **AC6 — Deliberate tags** | Suggestions are composed only from owned active tags reached through the customer's explicit aliases, the customer's own historical choices for the generated merchant key, or name-token matching (exact tokens before truncated prefixes). Suggestions start unselected. Accepting one writes that tag only; ignoring writes none; category stays Uncategorised. Foreign/archived/deleted selection rejects atomically. Component, real API/database and tag-race tests. AC-TAG-6; IMP-22; Plan 2 revision 7. |
| **AC7 — History and owned lifecycle** | Committed-only history/detail matches review counts/period/account; child list contains only rows created here. Hide/restore preserves individual flags; stale scope is rejected; delete removes exactly owned children/links/identity slots and permits clean new review while retaining other imports' suppression. Migration, API/database tests and local walkthrough. AC-IMP-7–8; AC-DEL-1; AC-HIDE-2–3. |
| **AC8 — Isolation, privacy, and wiring** | Every route rejects missing/untrusted/unentitled sessions; foreign UUIDs match missing UUIDs and parsing does not start for them. Canary financial fields/filenames never enter logs on success, parse errors, constraint failures, or child crashes. Public session/client props contain no credentials; pinned server auth reaches the trusted client. API, capturing-logger, actual-library composition and route tests; local two-customer observation. AC-SEC-1–4. |
| **AC9 — Usable real workflow** | Account setup through committed history/detail is reachable, with busy/error/uncertain retry/expired states and keyboard-accessible decisions at 320/375 px and desktop in both themes. Component/route tests plus separate real-stack runtime and manual accessibility/layout record. Playwright smoke proves redirects/public routes only. AC-UX-1–2; UX-4/6/8. |
| **AC10 — Upgrade compatibility** | Populated `0005` upgrades without dropping/merging/rounding records; constraints reject cross-owner/wrong-account links. Existing finance category/tag deletion and counts still pass. Full backend/frontend batteries pass; runbook documents retention/recovery. `StatementImportMigrationTests`, existing suites, and command evidence. |
| **AC11 — Absolute commit expiry (revision 6)** | Phase 5 real-handler/PostgreSQL barriers prove refusal after tag waits and complete rollback after a precommit pause crosses expiry. Preserve valid/no-tag/zero-new-row commits and original receipt replay after the deadline and restart. No new schema, source policy or Plan 3 lifecycle is introduced. |

## 10. Verification commands and stop conditions

Use the repository's .NET 10 SDK and Node 22/npm. Backend integration/migration tests require exactly `ARGUS_TEST_DATABASE_URL` against an isolated PostgreSQL database; do not target a development/production customer database. Do not print the connection string. Parser-only tests need no database. Existing setup scripts require separate operator approval; planning/execution approval alone does not authorize bootstrap.

Full final battery, working directory `backend/`:

```bash
dotnet format argus.Server.csproj --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet test tests/argus.Server.Tests
dotnet build -c Release
dotnet publish argus.Server.csproj -c Release --no-restore
dotnet list argus.Server.csproj package --vulnerable --include-transitive
```

Working directory `frontend/` (use `npm ci` if installing from the committed lockfile):

```bash
npx audit-ci@^7 --config ./audit-ci.jsonc
npm run lint
npm run test
npm run build
npm run test:e2e
```

Real-stack walkthrough, using already approved/configured local services: run `dotnet run` in `backend/`, and `npm run dev` in `frontend/`; use synthetic fixtures generated by the tests and the runbook steps. Record PASS/FAIL/NOT RUN per Section 9, without private credentials or files. Playwright needs Chromium and host libraries. An environmental launch failure is NOT RUN, not a product failure or a pass; fix the environment through its owner or report the blocker.

**Stop conditions:** failed required tests/build/format/audit; unknown ownership; source values missing where consumed; unsupported PDF recognition requiring guesses; unavailable required runtime/database evidence; partial/duplicate durable effects; unbounded upload/parser paths; public credential leakage; stale changes silently overwriting decisions; no way to recover an uncertain commit; or a populated migration that would discard data. Stop rather than weaken a criterion, skip an environment gate, substitute a balance, or silently change architecture. A vulnerable new parser dependency requires resolution before completion, not an ignored warning.

## 11. Adversarial scenarios

- Probe every account/review/import/tag operation with missing, foreign, same-customer wrong-account, malformed and stale identifiers. Assert identical missing/foreign responses, no parser invocation for denied account uploads, no cross-owner links, and zero denied side effects.
- Send oversized/chunked multipart without Content-Length, a false length, many parts/headers, boundary corruption, extra filenames, malformed JSON/options, encrypted/traversal/expanding archives, malformed PDFs, image-only transaction pages surrounded by text covers, and a header-like unrelated layout. Bound memory/time and reveal only safe reasons.
- Exercise generic comma/decimal ambiguity, conflicting signs, duplicate header names, CR/DR, grouping spaces, overflow, negative zero, precision beyond cents, missing amounts, FNB extra fields, and PDF multi-flow/zero-flow rows. No invalid input becomes a valid rounded or shifted transaction.
- Reorder repeated rows; compare one/two/three occurrences; use multiple existing imports with holes after deletion; mix bare/ZIP/PDF and optional stable-ID/anonymous forms; change tags/category/visibility. Only genuine additional or explicitly overridden occurrences create rows.
- Commit two overlapping reviews simultaneously, refresh/edit a review from two tabs, archive the account or selected tag during commit, delete a statement while another review relies on its suppression, and run independent accounts concurrently. Assert serialized account versions, conflicts when stale, and no accidental global serialization.
- Interrupt before each database write boundary, drop the HTTP response after durable commit, restart after commit but before response, retry after review cleanup/expiry, cancel against commit, and retry an old commit after statement deletion. Receipts determine truth; no duplicate or resurrected data is allowed.
- Hide a parent containing individually hidden rows, restore it, repeat state transitions, delete an all-suppressed statement, delete a statement whose skipped/suppressed rows belong elsewhere, and reimport afterward. Preserve individual flags and unrelated owners/counts.
- Use canaries in source description/amount, preamble/account metadata, file/ZIP entry names, category/tag names, malformed parser content, and forced database errors. Inspect captured web/parser/framework logs and UI error payloads for leaks. Never log exception objects, standard error, PostgreSQL Detail, or request/response bodies on this path.

## 12. Risks, compatibility, rollback, deferrals, and human decisions

- **Format evidence is structural, not real-bank certification.** Synthetic fixtures prove the documented contracts, not every FNB/Standard Bank variation. PdfPig coordinates/tolerances are implementation details within strict recognition; an unrecognized real sample must fail safely. Any private real-sample exercise requires separate permission and must leave no samples or financial evidence in Git/logs.
- **Dependency and deployment:** pin PdfPig 0.1.16; its pre-1.0 API can change between releases. Consult the pinned source for `IsEncrypted`, positioned letters, and no-op logging. Child process execution must work from published backend output in the existing .NET runtime image, not only `dotnet run`; verify the release-output launch in process tests. No new daemon, OCR binary, external network service, or secret is introduced. Deployment request/timeout limits must admit the documented bounded multipart envelope; document those requirements without changing global trust.
- **Migration compatibility:** adding tag-link owner columns and identity constraints affects raw SQL fixtures and category/tag join queries. Adapt these in Phase 2 and run the whole workspace immediately. Preserve names, category reassignment, version increments, and historical counts. Fresh databases and populated `0005` both require tests.
- **Operational recovery:** take a database backup before migration/deployment with customer data. Do not roll back by dropping finance tables. On a failed preflight, leave the prior schema/data intact, inspect safe diagnostics and reconcile through the operator. After accepting new imports, prefer a forward fix; reverting application code must not delete new schema/data/receipts. Disable import entry points at the deployment layer if needed while retaining read/recovery access. Any destructive restoration needs operator authorization.
- **Deletion ownership:** an overlapping import does not adopt suppressed transactions. Deleting their actual owner may leave another statement's historical suppressed count/coverage but no corresponding transactions; these are honest historical counts, not a claim that the later statement owns them. A new import creates only now-missing occurrences. No automatic reassignment/merge is added.
- **Retention:** raw bytes never persist. Open review payload is purged on commit/abandon/expiry; receipts retain only identifiers/digests/counts for retry safety. Database backup retention is an operational privacy concern, not a promise of immediate erasure from backups. Whole-customer deletion remains out of scope.
- **Deferred with owners:** individual transaction classification/hide and the main filtered workspace belong to Thread #4; all overview totals/coverage/charts belong to Thread #5. This unit establishes their exact source and visibility contracts, but cannot accept their outcomes. Broader bank formats/OCR and commercial service-level targets remain programme backlog under Thread #1, not implicit additions here.
- **Human decisions:** operator approval of this reviewed Plan is required before implementation; none is inferred from the authoring request, review verdict, or Thread state. No implementation approval is requested by this planning task. Commit/push is separately unauthorized. There are no unresolved architectural choices for an implementation agent to invent.

## Decisions log

| Date | Decision and rationale |
| --- | --- |
| 2026-11-07 | **Revision 6 approved — L1:** the operator instructed “approve, send to execution slot” for immutable contract `27141eaf-98da-4ba6-9378-3d84ce78c136`; Thread #4 approval `060f596f-31c3-4632-a1b3-a91e36879448`. Passing the early commit check is not a lifetime reservation. Require fresh post-tag and final-precommit expiry checks for new effects, full rollback on expiry and receipt-first replay. This resolves this Plan's own absolute-lifetime ambiguity without importing Plan 3 policy, changing CSV-only delivery, adding schema or reopening Thread #3. No additional review or publication is authorized. |
| 2026-11-09 | **Revision 7 approved — PR #8 authority:** operator instruction in the PR #8 artifact-revision request; Thread #3 Decision `a92fc81b-879b-4844-8543-1b25a51abda5`. Import suggestions may use explicit customer aliases, the customer's own historical choices keyed by a generic merchant key, and exact/truncated-prefix token matching; near duplicates require the exact transaction date, exact signed amount and token-set similarity at least 0.5, removing the ±3-day window. Suggestions stay advisory and unselected; import review commits Uncategorised. No global dictionary, cross-customer learning or automatic assignment is authorized. No new plan review, code acceptance or deployment is claimed. |
| 2026-11-05 | **PENDING revision 4 / C-PDF-2:** the operator confirms the two private PDFs are the intended production inputs. Their rejection is a container/template scope mismatch, not permission to alter transaction semantics. Propose one bounded real-template profile, an empty-user-only R6 guard, identity-and-placement checks for the known decorative asset, explicit metadata/postamble productions, real column gutters, and the residual findings in the current Thread #3 work. The copy-permission bit is disabled on these inputs; approval must explicitly allow import after empty-user authentication despite that advisory bit. Current revision 3 remains active until explicit approval of this proposal and the brief amendment. No new review is requested. |
| 2026-11-04 | Use the approved file brief and umbrella Requirement without manufacturing a child Requirement. They already specify this selected atomic unit; preserve file authority. |
| 2026-11-04 | Reupload request-local raw files for inspect/prepare, then persist sanitized reviews. This removes raw retention and sticky-session assumptions while making commits/restart recoverable. |
| 2026-11-04 | Serialize ledger changes with the account row and compare `import_version`. Counts shown to the user cannot silently change at commit, and multiple backend processes share one authority. |
| 2026-11-04 | Use occurrence slots plus separate explicit statement/row overrides. Simple uniqueness on a digest alone would discard legitimate repeated rows or prohibit intentional repeat statements. |
| 2026-11-04 | Retain terminal commit tombstones after deletion. Clean new reimport and safe retry of an old operation are different lifecycle events. |
| 2026-11-04 | Preserve the two hide flags and expose one effective predicate. Restoring a statement must not overwrite individual user intent. |
| 2026-11-04 | Preserve public-session redaction; test the pinned Auth.js server wrapper composition. Source inspection explains the token path and does not justify exposing it to browsers. |
| 2026-11-04 | Single Specialist review returned GO against blob `f03ffe737fc669f0b7d2737e04f2d13307a64306`. Accepted all six non-blocking notes as concrete Engineer-owned phase bullets: pairing, implicit locks, child/pipe lifecycle, matching work bounds, retained tag intent, and audit interpretation. This is operational disposition within revision 1, not an Architect rewrite or implementation approval. |
| 2026-11-04 | Revision 2 approves the strict custom `CsvRecordReader` in place of the originally selected `TextFieldParser`. The operator requested a bounded correction after the implementation review identified a MEDIUM plan deviation. Source inspection confirms shared decoding/call paths, physical start/end line tracking, malformed-record propagation and explicit read caps. Preserve all existing source-position, quote/width validation, UTF-8, row-limit, privacy and confirmation semantics; approval of the technique is not acceptance of the implementation or its tests. |
| 2026-11-04 | Revision 2 explicitly accepts `FinanceGuard.cs`, `MultipartImportReader.cs`, `ImportCleanupService.cs`, `ImportFaultInjector.cs`, `import-options.ts`, `vitest.config.ts` and `vitest.setup.ts` for the bounded responsibilities in Sections 7–8. The fault seam and option helper were already within scoped wildcards; they are named to remove ambiguity. No new product surface, public contract, phase purpose, invariant or acceptance criterion is authorized. No helper must be folded into another file. |
| 2026-11-04 | The operator explicitly declined another plan review for this correction. Retain the revision 1 GO as the single plan gate and distinguish it from implementation review `3d1f82bd-14d2-4037-81a0-e2513cbc7873` (NO-GO). Do not claim revision 2 or the implementation has received a new GO, clear implementation blockers, or publish from this documentation correction. |
| 2026-11-04 | **PENDING C-PDF-1 / proposed revision 3:** finished live inspection confirms that heuristic absence-of-transaction checks permit source omission. The first proposal, blob `716fb24ee405f9ad89815ec6f62d4f86e46866c6`, added unnecessary general content-accounting machinery. At the operator's request, simplify it to complete traversal of extracted text, row discovery before value validation, and a separate image rejection check. The operator explicitly requested one Specialist review of the simplified proposal. Revision 2 remains current until explicit approval; a review request is not Plan or implementation acceptance. |
| 2026-11-04 | Bounded correction review `3c40ca6b-3f95-4fc5-9599-54aba9f240db` returned **GO** against proposed revision 3 blob `053218c3a9640fa124126db8ad8e88a625576e7f`, with 0 blockers, CPDF-N1 MEDIUM and CPDF-N2 LOW. Accept CPDF-N1 as a concrete Phase 1 test bullet, with Phase 2 negative effects evidence. Dispose CPDF-N2 in Phase 4 operating guidance without adding frontend scope: record that the current no-text-layer message can misdescribe a raster-logo rejection. No new invariant, requirement, revision or review is introduced by these operational dispositions. Revision 3 still requires explicit approval; implementation F1 remains open. |
| 2026-11-04 | **Revision 3 approved:** after review and note disposition, the operator selected `Approve revision 3` in response to the explicit question approving C-PDF-1 as the governing correction for implementation, including rejection of raster-logo PDFs and the recorded existing error-wording limitation. The approved annotated proposal was blob `53a63747ba2bb7e79d4a1dc5b966fce6b11c7153`. Activate the same reviewed architecture with authority/status metadata only; no further semantic rewrite or review. This supersedes the pending state in the preceding historical proposal/review rows, not the implementation NO-GO. |
| 2026-11-05 | **C-PDF-1 root-cause disposition:** all six HIGH findings and both MEDIUM notes from implementation review `3e2dbd3f-232b-465f-a075-a11dd4d4e7fa` are CONFIRMED against existing obligations. Section I supplies one bounded corrective work order: establish page/header context before projection, account for every extracted word, make continuation permission consumable, and reconcile totals against every later recognized role. No acceptance criterion, invariant, phase purpose/scope, supported bank layout or public contract changes; revision 3 stays current. Local helpers/data structures remain implementation choices. No new review or implementation acceptance is inferred or requested. |
| 2026-11-06 | **Revision 5 approved (operator-directed):** Standard Bank PDF import is deferred to Thread #6 so the CSV/FNB and generic mapped CSV path can be completed and reviewed. PDF remains in the tree but is unreachable: the API returns `pdf_import_deferred` and the UI offers only CSV/ZIP. Proposed revision 4 / C-PDF-2 and the production-PDF brief amendment are deferred, not approved. The operator authorized this direct Plan amendment, the dormant-code treatment and one final CSV-only review. PDF-specific AC1/AC2/AC8 evidence is deferred; CPDF-N6's general prepared-source and seeded-data immutability gaps remain active and are covered by new regressions. |

## Planning validation and review record

- **Live-source inspection:** finished before drafting; bounded Explorer result and targeted reads cited in Section 1.
- **Self-review:** invariant-consistency, identity/authority/lifecycle/durability, phase inputs/file scope, reachable auth/upload path, exact amount transport, all-page count reconciliation, deletion/retry ownership, and test feasibility checked against the finished inspection. The architecture provides the inputs each phase consumes; constraints allow multiplicity/overrides without abandoning uniqueness; skipped/suppressed rows are never owned or deleted twice.
- **Implementation evidence:** NOT RUN. No application source, tests, runtime configuration, dependencies, or schema changed during plan authoring.
- **Single Specialist review:** **GO**, 0 blockers, request `9d4092ed-ce89-44e5-9e0b-fc63b572d865`, reviewed blob `f03ffe737fc669f0b7d2737e04f2d13307a64306`. Full saved review SHA-256 `3c3d227603443489af81f8ed6fc1634d0efae44735de63e294ee5d522ea1907b`, 18,756 bytes. All six notes accepted and disposed: P2-N1/N2/N4/N5 in Phase 2; P2-N3/N6 in Phase 1; P2-N5 UI continuation in Phase 3; P2-N3/N4/N6 evidence in Phase 4. No blocker rewrite or repeat review occurred. Thread #3 binds this verdict to the reviewed and final annotated blobs. Implementation approval and publication remain the operator's separate decisions.
- **Revision 2 inspection and consistency check:** targeted source reads, not a new plan/implementation review. `CsvStatementParsers.cs:10–26,95–138,323–407,601–607` supplies strict decoding, capped shared-reader calls and malformed/width validation. `CsvRecordReader.cs:5–9,32–131` supplies physical ranges, quote state and pre-append record caps. Named helper reads and their registration/call sites establish their bounded responsibilities. `auth-session.integration.test.ts` un-mocks `next/server` and imports actual Auth.js; the Vitest diff adds package inlining/resolution and navigation mock/reset support. Existing tests illustrate multiline, malformed-quote, width, preamble and initial-options obligations; no test was run or judged complete in this correction. Cross-checked Sections 2, 4.2, 7 and 8 so the approved technique, helper locations and test-harness permission agree without changing Sections 4.1 or 9 or the HTTP contract. No new Specialist review was requested or authorized. The final revision 2 blob and decision are recorded on Thread #3.

## Direct Execution Record

- **Current status:** The corrected 2026-11-05 C-PDF-1 baseline received a
  fresh independent review and returned **NO-GO**: 0 CRITICAL, 4 HIGH, 2 MEDIUM
  and 1 LOW finding, request `963d287e-c3e0-4214-ba9d-1aac0750ac9f`, immutable
  SHA-256 `b10e23a04e326881ef402532c7b6c5ee5090f03f0bd72876c8b4119e64ff3b7b`.
  The original I1–I6/N3/N4 probes are fixed, but conflicting-header bypasses,
  colon-attached metadata, a pre-header Summary omission, unbounded top/footer
  roles, closing-balance suffix placement and Phase 2 test-shape gaps remain.
  The corrected implementation is not accepted. Follow-up Thread #6 tracks the
  residual findings and real Standard Bank statement hardening. Publication and
  Threads #4/#5 remain blocked. The readiness probes below remain the historical
  reproduction of F1 on the pre-correction source. Earlier review and correction
  reports remain history, including claims later disproved.
- **Historical execution report:** The implementation/evidence entries below
  were reported against revision 1 before that implementation review. Preserve
  them as history, not proof that the reviewed blockers are corrected. The
  revision 1 Specialist plan GO remains the accepted planning gate.
- **Execution session:** `ses_e5abe3193ffe98v5npLPazUA06` (Generalist).
- **Date:** 2026-11-04.
- **Base:** `496f34ab39a792b090753ed4e2215562fe0ea15d`; Plan 2 revision 1.
- **Implemented:**
  - Phase 1: `backend/Imports/` bounded FNB CSV/ZIP, generic CSV, and positional
    Standard Bank PDF parsers; identity, fingerprint, amount/date, limits, PDF
    child protocol and parent runner; synthetic fixtures and process tests.
  - Phase 2: migration `0006_statement_import`; durable review, duplicate
    matching, receipts, cleanup, history/detail, hide/restore/delete authority;
    `/v1/finance/imports/*` endpoints; bounded multipart and JSON readers;
    quarantine of unsafe archives and encrypted/image-only PDFs.
  - Phase 3: `/imports` upload/confirm workspace, `/imports/upload` route,
    `/imports/review/[id]` decision UI, `/imports/[id]` statement detail and
    lifecycle controls; import client/actions and Auth.js composition coverage.
  - Phase 4: repository/HTTP/TLS/runbook documentation and real-stack evidence.
- **Verification evidence:**
  - Backend: `dotnet format --verify-no-changes` both projects PASS; `dotnet test
    tests/argus.Server.Tests` **99/99 PASS**; `dotnet build -c Release` PASS with
    0 warnings/0 errors; `dotnet publish` PASS; no vulnerable packages for the
    configured NuGet sources.
  - Frontend: `audit-ci` PASS; `npm run lint` PASS; `npm run test` **160/160
    PASS** (22 files); `npm run build` PASS; `npm run test:e2e` **17/17 PASS**.
  - Real-stack API walkthrough PASS on the isolated `argus_test` database with
    two seeded customers: synthetic FNB inspect/prepare/suggestion, commit,
    receipt replay after an ignored response, foreign-ID 404, history/detail,
    hide/restore, delete, reimport commit, and no canary in backend logs.
  - Real-stack UI walkthrough PASS with the real Next.js frontend and backend:
    login, Imports workspace, upload/inspect/ZAR/date confirmation, prepare,
    accept one suggestion, commit, statement detail, hide/restore.
    Screenshots `/tmp/argus-import-detail.png` and
    `/tmp/argus-ui-import-complete.png` are temporary evidence outside the
    repository.
- **Known limitations:** real-bank format accuracy remains unverified beyond
  the documented synthetic fixtures; no real statement samples or account data
  were used or committed. The runtime walkthroughs use the isolated test
  database and seeded synthetic accounts, not production data. No publication,
  commit, push, setup/bootstrap, or later-Thread work was performed. Publication
  and Plan 3 authoring remain operator decisions; Threads #4/#5 remain blocked.

### Post-review correction record (2026-11-04)

- **Original implementation review:** request `3d1f82bd-14d2-4037-81a0-e2513cbc7873`, verdict **FAIL / NO-GO**, 9 HIGH blocking findings and 8 MEDIUM notes, SHA-256 `17e862a596c65d76e177059796d7f9c18d5aef3782a1fcbb83fb191965982e00`.
- **Corrected blocking findings:** I1 skip-aware duplicate pairing; I2 PDF page/candidate/header handling; I3 exact CSV width, quoting, and sign grammar; I4 source-derived empty-description identity and canonical checks; I5 canonical identity/fingerprint backfill for populated `0005`; I6 reachable initial generic inspect; I7 FNB preamble suppression in generic mapping; I8 pre-allocation bounds, bucketed near matching, exact payload quota, and a successful 10,000-row import; I9 framing-correct multipart transport.
- **Corrected notes:** N1 tag-selection validation and over-limit rejection; N2 receipt-first abandon; N3 raw/source values, plain-language validation, global commit eligibility, history cursor, named hide/restore confirmation, account-selection refresh; N4 backend no-store, hidden native filename, safe multipart name, cancellation signal; N5 nonzero-exit rejection and bounded pipe cleanup. N8 is addressed by branch `plan/02-statement-import`. N7 is resolved by the accepted revision 2 bounded correction.
- **Fresh evidence:** backend 118/118 tests; frontend 166/166 Vitest and 17/17 Playwright; backend format/build/publish/vulnerability checks pass; frontend audit/lint/build pass; real-stack API lifecycle and narrow-width UI hide/restore pass with no 320/375 px overflow.
- **Remaining disposition:** N6 remains partially addressed: actual encrypted-PDF and a few adversarial evidence cases are still source-level/synthetic. No publication or merge has occurred; a fresh Specialist implementation review is requested next.

### Second-review correction record (2026-11-04)

- **Second implementation review:** request `d5f9c02c-17cc-40be-a460-c0b88405be5b`, verdict **FAIL / NO-GO**, 5 HIGH blockers and 7 MEDIUM notes, SHA-256 `3150919031b1c9f2f0e3073108fdc73502e4178e0710eb92359dcc16eb11ce71` (33,224 bytes). Reviewed baseline: branch `plan/02-statement-import`, aggregate `d6683aa2a3c90ee6fd99fe92b873aa8a5647b27f`, revision 2 blob `a8af2111fabe29028e6d274a2f795d6fcd990b8f`.
- **R1 FIXED:** headerless PDF pages now recognize signed/parenthesized money strings, not just unsigned amounts; a negative-flow headerless regression rejects the page.
- **R2 FIXED:** the canonical backfill stages every transaction and statement on a unique temporary key before writing canonical keys, avoiding live unique-constraint collisions. A mixed legacy/canonical two-row upgrade test verifies occurrence 1/2 and fingerprint 1/2.
- **R3 FIXED:** PATCH and refresh recalculate the persisted review payload under the quota lock (acquired before the account lock), enforce the customer limit, and update `payload_bytes`; a near-limit PATCH regression rejects without mutation.
- **R4 FIXED:** the generic parser detects the first plausible transaction header and rejects mappings that start inside a preamble, including malformed FNB headers ending in `Details`; a canary regression covers it.
- **R5 FIXED:** multipart closing markers are validated through their full suffix and line ending; a closing-like `--not-a-delimiter` substring regression preserves the file bytes.
- **C1 FIXED:** incoming and existing canonical components are compared before pairing.
- **C2 FIXED:** the read summary query now returns invalid-unskipped before pending-group counts; the review pagination caption describes the global included count.
- **C3 FIXED:** abandon and commit re-check the receipt when the review header has disappeared before returning 404.
- **C4 PARTIAL:** runner task observation, cancellation, and tree-kill attempts are improved; parent-death and published-output hostile cases remain unverified.
- **C5 PARTIAL:** actual encrypted-PDF composition and several controlled hostile races remain evidence limitations; source-level encryption rejection is retained.
- **C6 FIXED:** configured reinspection returns the confirmed mapping, malformed selected headers are rejected, index parsing requires decimal integers, and mapping edits disable prepare until reinspection.
- **C7 FIXED:** a wrong delimiter candidate that exhausts its record cap is skipped instead of rejecting a valid multiline CSV parsed by another candidate.
- **D1 FIXED:** the runbook distinguishes authorized finance response content from error bodies, logs, audit events, and telemetry.
- **Verification:** backend 122/122 tests; frontend 168/168 Vitest and 17/17 Playwright; backend format/build/publish/vulnerability checks pass; frontend audit/lint/build pass. No commit, push, or publication occurred.

### Third-review void and C4/C5 closure record (2026-11-04)

- **Third review void:** request `2b78b2b2-44ae-4ac0-8624-4043255e8abd` was admitted, but its session produced only a todo write and published no findings or reply. No review artifact exists for that request. The operator approved a fresh independent review after the evidence work below.
- **C4 implementation:** the parser child joins its own process group on Linux (`setpgid`) and the runner terminates that group on timeout, cancellation, output bounds, and after root exit when descendants still hold inherited pipes. The runner uses bounded cleanup waits and attaches a late-fault observation to every child task on every exit path, including exceptional ones; it no longer relies on `Process.Kill(entireProcessTree)` alone after the root has exited.
- **C4 evidence:** `StatementParserProcessTests` covers a descendant that survives its root and holds the pipes, abrupt parent death with the input pipe closing, production process-group membership, the apphost-free build output, and the published release output (`dotnet argus.Server.dll`). Timeout, stderr flood, abnormal exit, success-shaped nonzero exit, watchdog, and truncated input remain covered.
- **C5 evidence:** genuine RC4-128 (empty user password) and AES-128 (non-empty user password) PDF fixtures are rejected as `encrypted_pdf`; concurrent HTTP requests (not forced database interleavings) cover two different reviews, abandon/commit, tag delete/commit, account archive/commit, and delete/commit replay; review-creation count and payload quotas are enforced before insert; 200 identical rows match and refresh without combinatorial growth; a 10,000-row import commits and replays its receipt.
- **Measured 10k import:** commit 514 ms, receipt replay 11 ms in the isolated PostgreSQL test database.
- **Real-stack API walkthrough:** PASS against the published backend on an isolated database with two seeded synthetic customers: login, account and tag creation, FNB inspect/prepare, tag selection, commit, receipt replay, history/detail, hide/restore, delete, `410 import_deleted`, clean reimport, and foreign statement/review 404.
- **Real-stack UI walkthrough:** PASS with the real Next.js frontend and backend: login, upload, inspect, ZAR/date confirmation, prepare, tag suggestion, commit, statement detail, hide, and restore. 320 px and 375 px viewports showed no horizontal overflow in both themes; focus rings and keyboard login were verified. Screenshots are temporary evidence at `/home/ruann/.cache/playwright-mcp/argus-import-detail-375-light.png` and `argus-import-detail-320-dark.png`, outside the repository. A full keyboard-only data entry journey was not repeated after the pointer-driven journey.
- **Privacy check:** the real-stack backend log contains zero occurrences of the synthetic descriptions, amounts, filename, or account labels used in the walkthrough.
- **Final verification:** backend `dotnet format` both projects PASS; `dotnet test` **136/136 PASS**; `dotnet build -c Release` 0 warnings/0 errors; `dotnet publish` PASS; vulnerability scan reports no vulnerable packages. Frontend `audit-ci` PASS; lint PASS; Vitest **168/168 PASS**; production build PASS; Playwright **17/17 PASS**.
- **Criterion evidence (superseded by the fourth-review correction record):** AC1/AC2 were assessed PASS before the fourth review found the PDF omission class; AC8 was assessed PASS before the generic preamble leak. See the fourth-review correction record for the corrected assessment.
- **Publication:** no commit, push, or pull request has occurred. Independent acceptance and the protected-branch workflow remain the next gate.

### Fourth-review correction record (2026-11-04)

**Historical report:** its F1 closure and overall AC1/AC2 PASS claims were superseded by the fifth review and readiness correction. Preserve the observations, not a current claim of recognition safety.

- **Fourth implementation review:** request `925f643e-55d8-4c69-95dc-5b957ebb93ff`, verdict **NO-GO**, 0 CRITICAL, 2 HIGH (F1/F2), 5 MEDIUM (N1–N5), 0 LOW. SHA-256 `e83153a461278e2fbfc66dbe041d0442509c5f9aaa3fd4b167e6373b9bfb4094` (34,947 bytes). Baseline tree `cdc466ebf296cc3951c72b37ef5e74273e1737f9`; plan revision 2 blob `6da64cd97e857a4ed96be43bfdabece1190cadd8`; final baseline check 2026-11-04T13:08:04Z. No further review was requested by the operator.
- **F1 FIXED — PDF classification:** a page without the six-column header is no longer ignored on absence of a narrow transaction-line match. Image-only pages and pages with an image covering at least 25% of the page reject as `text_pdf_required`; headerless pages with transaction-shaped content (including ISO and `DD Mon YY` date tokens) reject as `unsupported_layout`; pages that are neither footer-only nor positively identified by cover/summary markers reject as `unsupported_layout`. Inside a recognized table, a description-bearing row with a populated flow and no date is retained as an invalid candidate (`invalid_date`) instead of being treated as a summary; explicit `total payments`/`balance` style rows remain non-candidates. Real-byte regressions cover all three reviewer probes and run through the actual published parser child.
- **F2 FIXED — FNB preamble exclusion:** a physical-line preamble detector runs before generic mapping, independent of the selected delimiter. It recognizes the six-line FNB preamble shape, rejects a selected header line inside that preamble, and still suppresses the preamble when the confirmed header is line 7. Tests cover `|` and `;` delimiters, a header line inside the preamble, a malformed line-7 header ending in `Desc`, and a configured line-7 parse whose serialized result contains no `PRIVATE` preamble values.
- **N1 FIXED — canonical group check:** existing and incoming rows are compared by identity key across the whole comparison group before stable-ID discriminator filtering. An unpaired inconsistent digest now fails closed as `identity_conflict`; a controlled test corrupts an existing digest and uses conflicting stable IDs so the matcher would not pair the rows.
- **N2 FIXED — process evidence and task observation:** the parent-death test now writes a valid incomplete frame (length 16, four header bytes), asserts the child is alive before the parent is killed, and treats a zombie as exited. `AwaitQuietly` attaches a fault observation when a bounded cleanup wait expires. The plan and runbook wording now says bounded waits plus late-fault observation, not unqualified task completion.
- **N3 ADDRESSED — evidence breadth:** the multipart closing-like regressions use CRLF; the `tag_links_inserted` fault now runs after a real selected tag and asserts zero links after rollback and one on retry; a PATCH payload-growth test proves byte growth and aggregate-boundary rejection without mutating the patched review; 200 mixed stable-ID/anonymous rows match and refresh; 60 near-match history rows disclose a bounded five candidates. The frontend workspace test covers changed mapping, blocked prepare, reinspection, and prepare with the confirmed `negative_style`. Remaining honest limits: the lifecycle races use concurrent HTTP requests without forced database interleavings, account-lock occupancy duration and cancellation/exhaustion are not separately measured, and the full authenticated keyboard-only journey was not repeated. AC9 remains partially UNVERIFIED for that reason.
- **N4 FIXED — cleanup lock order:** `CleanupExpiredAsync` now selects expired review headers `FOR UPDATE` before deleting child rows. A controlled expiry-boundary deadlock reproduction was not added; the reverse acquisition order was removed by source inspection.
- **N5 FIXED — negative style round-trip:** `ConfigToSuggestions` returns `negative_style`; detection suggestions include `any`; the frontend interaction test confirms that a `parentheses` configuration survives inspect → reinspect → prepare.
- **Corrected criterion assessment:** AC1/AC2 PASS with the new published-child regressions; AC3 PASS for immutable review and the changed-mapping/reinspect interaction, with the complete all-page decision matrix not re-exercised; AC4 PASS with mixed-ID dense matching; AC5 PASS at the exercised fault/race scope, with no forced interleavings; AC6 PASS; AC7 PASS; AC8 PASS with F2 closed; AC9 PARTIAL — responsive layout, themes, focus order, and activation are verified, but the full authenticated keyboard-only journey remains UNVERIFIED independently; AC10 PASS.
- **Final verification after corrections:** backend `dotnet format` both projects PASS; `dotnet test` **150/150 PASS**; `dotnet build -c Release` 0 warnings/0 errors; `dotnet publish` PASS; vulnerability scan reports no vulnerable packages. Frontend `audit-ci` PASS; lint PASS; Vitest **169/169 PASS**; production build PASS; Playwright **17/17 PASS**.
- **Publication:** still no commit, push, or pull request. The fourth review remains the last completed verdict; the operator decides whether the corrected tree is acceptable without another review.

### Fifth-review correction record (2026-11-04)

**Historical report:** its F1 recognition-class closure and overall AC1/AC2 PASS claims were disproved by the readiness probes below. Other findings were not independently reassessed in the bounded Architect correction.

- **Fifth implementation review:** request `e10b5101-c301-4bd0-98b5-a5bc7be6fca4`, verdict **NO-GO**, 0 CRITICAL, 2 HIGH (F1/F2), 2 MEDIUM (C1/C2), 0 LOW. SHA-256 `d1914fa676b2904719e44d88799d94dd8934ce53ee442967897bf15f5e8ae6d3` (31,388 bytes). Baseline tree `6d99af44cccae68eed555a5a1d8e5f506fe980b1`; plan blob `bef69207454fd913eae49181752c63f05c252a72`; final baseline check 2026-11-04T18:23:57Z. The operator explicitly declined another review after this correction.
- **F1 FIXED as a recognition class:** image coverage is now checked on every page, including a page with the six text header labels, so a text header above a large raster body rejects as `text_pdf_required`. Headerless pages no longer use substring-anywhere cover markers: they require a structural cover/summary lead-in or an exact cover phrase, and any unexplained monetary line rejects rather than being ignored. `ExplicitSummary` is now an exact label match, so `Summary Books purchase` with a payment stays visible as an invalid candidate. The transaction detector accepts integer and malformed-date candidates on headerless pages. Published-child regressions cover the header-plus-image page, `BAD-DATE` plus a payment, `02 Jan 25` plus an integer amount, and the `Summary Books purchase` missing-date row.
- **F2 FIXED with quoted-label handling:** the physical-line preamble detector strips leading quoting before matching labels, so `"Name"`, `"Account"`, and `"Balance"` cannot bypass it. A boundary now requires either a recognized transaction header after line 6 or an explicit FNB title; without one, description-first generic data is not rejected. `Desc` is recognized as a description label. Tests cover quoted labels with a selected line-1 header, the unquoted alternate-delimiter cases, the malformed `Desc` header, and a description-first generic statement.
- **C1 FIXED:** a description-first pipe-delimited generic statement with `Account`/`Name`/`Balance` transaction descriptions now parses all three rows instead of being rejected as an FNB preamble.
- **C2 ADDRESSED:** the quota test now sets the customer aggregate exactly at the limit, proves a same-state PATCH is accepted, proves a growing PATCH is rejected, and asserts selected IDs, version, and payload are unchanged after rejection. The dense test now uses one shared fallback identity with 20 stable-ID and 20 anonymous occurrences, reverses the incoming row order, and asserts all 40 suppress and survive refresh. The 60-row near-candidate cap remains valid; forced interleavings, cancellation/exhaustion, and lock occupancy remain explicitly unmeasured.
- **Verification after corrections:** backend `dotnet format` both projects PASS; `dotnet test` **156/156 PASS**; `dotnet build -c Release` 0 warnings/0 errors; `dotnet publish` PASS; vulnerability scan reports no vulnerable packages. Frontend `audit-ci` PASS; lint PASS; Vitest **169/169 PASS**; production build PASS; Playwright **17/17 PASS**.
- **Corrected criterion assessment:** AC1/AC2 PASS at the newly exercised recognition scope; AC3 PASS for immutable review and the changed-mapping interaction, with the all-page matrix not re-exercised; AC4 PASS for the shared-identity permutation; AC5 PASS at the exercised fault/race scope; AC6/AC7 PASS as previously evidenced; AC8 PASS with F2 closed; AC9 PARTIAL for the keyboard-only journey and full authenticated browser workflow; AC10 PASS.
- **Remaining limits:** no forced database interleavings, no account-lock occupancy or cancellation/exhaustion measurement, and no full authenticated keyboard-only journey. Real-bank variation and deployment operation remain unverified. No real customer statement was used.
- **Publication:** no commit, push, or pull request. The fifth review remains the last completed verdict; acceptance is the operator's decision.

### Readiness correction and Architect inspection (2026-11-04)

- **Authority:** Thread #3 Correction `d0ffb249-c673-4a34-ba27-8b68f2e548ca` corrects the fifth-review handover. **F1 is CONFIRMED and open; AC1 format fidelity and AC2 safe rejection FAIL for the reproduced inputs.** A later review/commit cannot recover source candidates omitted before review creation.
- **Inspected baseline:** branch `plan/02-statement-import`, HEAD `496f34ab39a792b090753ed4e2215562fe0ea15d`; supplied working snapshot `48f1bd5241e0ba00a63539701e67393826f98771`; preceding Plan blob `c16d083015f976d7cdb73825795b02a2e7c56587`. Direct hashing confirmed `StandardBankPdfParser.cs` blob `af33c4c716de9844bda6279b502b90cac7c8832a`, `CsvStatementParsers.cs` blob `203ff7dbf9a37d4307984debaeb89c507d065870`, and unchanged selected brief blob `32a45cf3f760112b2a520c4b327c45455427dd06`. Architect did not independently regenerate the aggregate tree.
- **Execution evidence supplied by execution, not rerun by Architect:** Release build PASS, 0 warnings/errors. In-memory PDF bytes passed through the real binary stdin frame, pinned PdfPig, and stdout result. A first table page had one valid transaction. A second page had `Statement period: 01 Jan 2025 to 31 Jan 2025` and, except in the safe-cover control, `BAD-DATE / Bank charge / payment`. Safe cover: accepted with one row. `-1,000.00`: rejected `unsupported_layout`. `-1000.00` and `20`: accepted with only the first row; payment omitted. All processes exited 0 without stderr. This is synthetic integration across the real child boundary, not authenticated HTTP or browser evidence.
- **Root cause, CONFIRMED by source inspection:** `StandardBankPdfParser.cs:181–196,368–415` uses failure to recognize a date/amount as evidence of nontransaction content, then one cover marker permits ignoring unrelated text. The same accounting gap exists at above-header skipping, low-Y/footer breaks (`201–219`), flow-only dropping and description continuation (`253–289`). The 25% image cutoff (`173–179,425–435`) does not prove smaller images are decoration. Fixing token expressions alone cannot close these paths.
- **Finished inspection:** Explorer `ses_e5811be2bffeFsJzJfD5WB0xd3` read the specified parser/process/fixture/HTTP test files and supported-format documentation before this proposal. Direct reads also covered `StatementSafetyTests.cs:164–299`, including the positive terminal `Total payments` control at `214–228`; the parser in full; `StatementText.cs:311–465`; `Program.cs:10–22`; `StatementParser.cs:23–29,51–57`; `PdfParserChild.cs:25–26,107–108`; `PdfParserRunner.cs:25–165`; and `StatementImportEndpoints.cs:125–152,187–197`. Both inspect and prepare invoke the same child; review persistence follows parser success. No new service or durable state is needed to reject uncertainty before persistence.
- **Input/test feasibility:** pinned PdfPig 0.1.16 exposes page letters, image bounds, drawn paths and annotations (`lib/net9.0/UglyToad.PdfPig.xml:2860–2974` in the installed package). Existing in-memory PDF builders can express positions, reordered streams and image pages; process tests launch actual published output. `ApiTestBase` and the PDF contract test provide authenticated HTTP plus isolated PostgreSQL checks. These are source/API-availability observations, not proof that the proposed implementation exists.
- **Other dispositions:** retaining the selected PDF preset and no OCR/services/dependencies is **INTENTIONAL**. Preserving the safe cover, terminal total and 4/18-page structures is **ALREADY SATISFIED** as a planning obligation, not verified implementation closure. F2, C1/C2 and other notes are **NOT APPLICABLE to this bounded reassessment**; their historical dispositions remain, without a new clearance claim. Later workspace/reporting features remain **DEFERRED** to Threads #4/#5. No part of F1 is deferred.
- **Operational caution:** Thread #1's cached next step still describes Plan 2 as verified and awaiting publication. That text conflicts with current Thread #3; it cannot authorize publication or child progression. This task changes neither programme order nor later-Thread status. Reconcile programme readiness before any cross-child progression.

### C-PDF-1 independent review result (2026-11-05)

**This result supersedes the C-PDF-1 execution record below wherever that record
claims implementation closure or acceptance.**

- **Independent implementation review:** request
  `3e2dbd3f-232b-465f-a075-a11dd4d4e7fa`, verdict **NO-GO**, 0 CRITICAL,
  6 HIGH, 2 MEDIUM, 0 LOW. Immutable SHA-256
  `b75db8a3d473bd0e30e5436532fe2c271165a48c4c52cbcf5657d3f112f6362d`
  (31,141 bytes). Reviewed baseline: branch `plan/02-statement-import`, HEAD
  `496f34ab39a792b090753ed4e2215562fe0ea15d`, parser blob
  `fdcd2e1dbdf48888ad89fe46b5ebe97821f270ad`, with the recorded test and
  runbook blobs. The working tree was unchanged at review time.
- **Blocking findings:** CPDF-I1 conflicting second-header bands can reverse a
  flow or expose Balance as a valid amount; CPDF-I2 unknown account metadata
  above a table is retained in a successful row; CPDF-I3 negative-X extracted
  words are dropped; CPDF-I4 stale cross-page continuation permission bypasses
  spacing and summary closure; CPDF-I5 headerless `Summary`/`Synthetic Bank`
  recognition ignores the top-of-page position; CPDF-I6 above-header candidates
  reuse source line 0. The review exercised actual PDF bytes through the
  published child and confirmed each condition.
- **Non-blocking notes:** CPDF-N3 `00 Feb 2025` with a four-digit year throws and
  rejects the whole PDF instead of remaining an invalid row; CPDF-N4
  terminal-total lookahead ignores later recognized content and can exclude a
  total that is not followed by the permitted suffix.
- **Assessment:** the findings are implementation defects against already
  approved C-PDF-1 obligations, not new scope. The green 197/197 backend and
  169/169 frontend results do not cover these paths and do not support F1
  closure or acceptance.
- **Next step:** root-cause correction is with Architect (request
  `405a635d-b00b-42a5-98f2-79b47198d7a1`), covering the whole class. Per the
  Shared Quality Contract, no third blind local patch was attempted. No re-review
  was requested. Publication and Threads #4/#5 remain blocked.

### C-PDF-1 correction execution (2026-11-05)

- **Execution session:** `ses_e53998c2bffeGzn7IZ4Tg4Q6SH` (Generalist).
- **Authority:** Thread #3 approved Plan revision 3 / C-PDF-1 (activated plan blob
  `a7080d0af841aa086a50838ff9ab8d75001d5bf3`). This is the bounded implementation
  of that approved correction, not a new plan or acceptance verdict.
- **Base and source blobs:** branch `plan/02-statement-import`, HEAD
  `496f34ab39a792b090753ed4e2215562fe0ea15d`, with the existing Plan 2 working
  tree preserved. `backend/Imports/StandardBankPdfParser.cs`
  `fdcd2e1dbdf48888ad89fe46b5ebe97821f270ad` replaces the pre-correction
  `af33c4c716de9844bda6279b502b90cac7c8832a`. Test sources:
  `StatementParserTests.cs` `2de428dc43e91816861de65684a45f1eef73b26b`,
  `StatementSafetyTests.cs` `bebbaaff23bab16ca9ae7c74eb30b9d810567ad4`,
  `StatementParserProcessTests.cs` `bba5f5e1172d4609708812df828080e16ccfa57b`,
  `StatementFixtureBuilder.cs` `515c7b3a4b5c30571f5f0dc4edbc2b50ed542121`,
  `StatementImportContractTests.cs` `cf5c622f6e1c9be63cfe5e3beab3ce9d4947e360`.
  `docs/statement-import-runbook.md` `3942bc467803c0252466c80881c1b73d438176de`.
- **Implemented:** complete positional traversal of every page and every
  non-whitespace extracted line; candidate discovery from any populated date or
  flow cell before validation; flow-only invalid-date candidates with the
  empty-description warning; date-only missing-amount candidates with no Balance
  fallback; strict description-band continuation attachment, cross-page
  continuation only on the immediately following compatible table, and
  rejection of orphans, closed sequences, and incompatible bands; complete
  cover/period/summary/footer structures consumed only on exact text; exact
  opening/closing balance placement and terminal-total lookahead; any raster
  image on any page rejected as `text_pdf_required`. Existing size, page,
  character, row, deadline, memory, and output bounds are retained.
- **Verification:** backend `dotnet format` both projects PASS; `dotnet test
  tests/argus.Server.Tests` **197/197 PASS**; `dotnet build -c Release` PASS with
  0 warnings/0 errors; `dotnet publish` PASS; vulnerability scan reports no
  vulnerable packages. Frontend `audit-ci` PASS; `npm run lint` PASS; `npm run
  test` **169/169 PASS**; `npm run build` PASS; `npm run test:e2e` **17/17
  PASS**. New evidence covers the approved matrix: three headerless amount
  probes, safe cover/period-only pages, terminal totals for all three flow
  bands, balance placement, footer controls, same-line header/period/footer/total
  residue, unknown text on first/middle/last pages, continuation closure and
  compatibility, small/multiple/cover images, PDF row limit plus one, the
  published child, authenticated inspect/prepare rejection with zero durable
  effects, and invalid-row commit refusal/reconciliation.
- **Evidence strength and limits:** parser and process evidence is synthetic PDF
  bytes through the real parser and published child; HTTP evidence is
  integration over the isolated `ARGUS_TEST_DATABASE_URL` database. No real
  customer statement, browser import journey, deployment observation, or new
  independent review is claimed. Real-bank layouts and logos outside the
  synthetic text-only subset may reject safely. F1 is corrected and evidenced
  in implementation; independent acceptance, publication, and Threads #4/#5
  remain operator decisions and are unchanged. No commit, stage, push, pull
  request, bootstrap, frontend change, schema change, or later-Thread work was
  performed.

### C-PDF-1 root-cause correction execution (2026-11-05)

- **Execution session:** `ses_e53998c2bffeGzn7IZ4Tg4Q6SH` (Generalist).
- **Authority:** Architect return for request
  `405a635d-b00b-42a5-98f2-79b47198d7a1`, disposition
  `87a19038-2647-4f26-9c57-bc2b3d3599ed`, Plan Section I at blob
  `6123ee61eb61a31758bfb92889c79368a8db3942`. The existing revision 3 approval
  `2056cbfa-7436-4dc7-ac47-79b2292e91b7` remains governing; the work order made
  no semantic change. This is an implementation record, not a new review or
  acceptance.
- **Base and source blobs:** branch `plan/02-statement-import`, HEAD
  `496f34ab39a792b090753ed4e2215562fe0ea15d`, existing working tree preserved.
  `backend/Imports/StandardBankPdfParser.cs`
  `5215c496da50e33da59778eb444c011e13fdc166` replaces the reviewed first-pass
  `fdcd2e1dbdf48888ad89fe46b5ebe97821f270ad`. Test sources:
  `StatementParserTests.cs` `94d57040b3defaf639a9e69e16346f439f58079e`,
  `StatementSafetyTests.cs` `63f460a5b36bfada6e1758ef4c25b2d34c1f7605`,
  `StatementParserProcessTests.cs` `3ead5976000acf6689bf5176939e807073b999b2`,
  `StatementFixtureBuilder.cs` `515c7b3a4b5c30571f5f0dc4edbc2b50ed542121`,
  `StatementImportContractTests.cs` `82a75afc85ed815d4da4683ae23107a369d328fb`.
  `docs/statement-import-runbook.md` `796a43454b2b69024881fdbbb0b102e06502d651`.
- **Implemented as one traversal model:** every grouped nonblank line receives a
  positive document source-line position before branching, including consumed
  headings/headers/footers, while output row ordinals remain separate; all
  complete page headers are found before projection, compatible repeats are
  consumed and conflict or semantic-column change rejects; every extracted word
  must map to a permitted cell/structure, so negative/non-finite coordinates
  reject; preamble covers/periods/headings use the top-of-page role, unknown
  Account/Customer/Branch metadata rejects before promotion, and only
  date-plus-flow pre-header rows remain candidates; cross-page continuation is a
  single consumed leading bridge within 80 points of the header followed by
  ordinary spacing, with summary/footer/close/restart boundaries enforced; the
  full ordered role list decides total exclusion, retains a disqualified total
  as an invalid-date candidate, and checks opening/closing placement against the
  final retained candidates; four-digit dates validate both day bounds.
- **Regression evidence:** before the parser fix, the new focused
  parser/safety regressions failed 19 of 136 and the authenticated contract
  regressions failed 6 of 29 on parser blob `fdcd2e1d`. The failing cases covered
  conflicting second headers (deposit/flow reversal and Balance-as-fees),
  preamble metadata, negative-X residue, stale cross-page continuation, mid-page
  headings, above-header source-line reuse, zero-day rejection and terminal
  suffix lookahead. After the correction: focused parser/safety **138/138
  PASS**, authenticated contract **29/29 PASS**, full backend **231/231 PASS**.
  The published child exercises the compatible/conflicting header controls,
  tolerated-column ambiguity, metadata, negative-X row/total, far and distant
  continuations, below-footer residue, preamble Summary closure, mid-page
  heading, source-line control, zero-day boundaries and the full permitted
  suffix; a direct published-child probe confirms no canary on standard error.
  Recorded tolerances: top-of-page upper 120 points; leading bridge within 80
  points below the header; header compatibility 6 points plus semantic-column
  check; ordinary continuation gap 12 points.
- **Full battery:** backend `dotnet format` both projects PASS; `dotnet test`
  **231/231 PASS**; Release build 0 warnings/0 errors; `dotnet publish` PASS;
  vulnerability scan reports no vulnerable packages. Frontend `audit-ci` PASS;
  lint PASS; Vitest **169/169 PASS**; production build PASS; Playwright
  **17/17 PASS**.
- **Evidence strength and limits:** parser/process evidence is synthetic
  actual-PDF unit/process integration through the real child; authenticated
  evidence is integration over the isolated `ARGUS_TEST_DATABASE_URL` database.
  No real customer statement, browser import journey, deployment observation or
  new independent review is claimed. The previous NO-GO remains the last
  independent verdict; acceptance, publication and Threads #4/#5 remain
  operator/programme decisions. No commit, stage, push, PR, merge, bootstrap,
  frontend, schema, dependency or later-Thread change was performed.

### C-PDF-1 second independent review result (2026-11-05)

**This result supersedes the corrected-baseline implementation claim in the
root-cause execution record below. It does not relabel any earlier verdict as
acceptance.**

- **Fresh independent review:** request
  `963d287e-c3e0-4214-ba9d-1aac0750ac9f`, verdict **NO-GO**, 0 CRITICAL,
  4 HIGH, 2 MEDIUM, 1 LOW. Immutable SHA-256
  `b10e23a04e326881ef402532c7b6c5ee5090f03f0bd72876c8b4119e64ff3b7b`
  (32,250 bytes). Baseline: branch `plan/02-statement-import`, HEAD
  `496f34ab39a792b090753ed4e2215562fe0ea15d`, parser blob
  `5215c496da50e33da59778eb444c011e13fdc166`, final test/runbook blobs as
  recorded in the root-cause execution record below. The reviewer independently
  exercised the published child (assembly SHA-256
  `27e7e81025985f2c45428948e15864b28662fdf6b6c56b24039d299759125336`) and
  reran the full backend/frontend battery.
- **HIGH findings:** CPDF-R1 conflicting-header bypasses through same-line
  residue and the 0.01 exact-tolerance shortcut; CPDF-R2 colon-attached
  Account/Customer metadata retained; CPDF-R3 an aligned pre-header `Summary`
  fragment consumed as a heading and erased; CPDF-R4 top/footer roles accepted
  above/below the page bounds.
- **MEDIUM/LOW:** CPDF-N5 closing-balance placement ignores later structural
  roles; CPDF-N6 Phase 2 tests do not prove prepared-row/version/summary
  immutability, seeded finance-data immutability, or ordinary Balance
  differential behavior; CPDF-N7 runbook wording incorrectly says a correction
  supersedes an independent verdict.
- **Assessment:** the green counts are real, but the fresh combinations leave
  the same class open. No blind local patch was attempted. Follow-up Thread #6
  (`60b8290f-9470-405b-bace-531094f65e47`) records the residual findings and the
  separate real-statement blockers. No further review was requested; acceptance,
  publication and Threads #4/#5 remain blocked.

### CSV-only delivery execution (2026-11-06)

**Authority:** Operator direction in the continuation session: amend Plan 2 to
CSV-only, keep the PDF parser dormant, reject PDF at the API, and commission one
final CSV-only review. This record implements Revision 5; it does not activate
proposed revision 4 or close the PDF findings.

- **Implemented:** `StatementParser` rejects `standard_bank_pdf` with
  `pdf_import_deferred` before decoding or starting a parser child. The Imports
  UI offers only FNB CSV/ZIP and generic mapped CSV, removes PDF from the file
  accept list, and rejects a selected `.pdf` with a plain-language message. The
  client recognizes the new error code. The dormant PDF parser, child runner,
  protocol and parser/process tests are retained.
- **Test changes:** PDF HTTP contract tests now prove deferral, zero durable
  effects and canary absence instead of exercising the dormant parser through
  the product API. Parser/process tests remain. Two Phase 2 regressions were
  added for CPDF-N6's general gaps: a malicious PATCH cannot rewrite prepared
  source fields/version, and preparing a second import does not mutate seeded
  categories, tags, transactions, statement counts or account import version.
  Balance-differential behavior is PDF-specific and deferred.
- **Documentation:** Revision 5 scope, active/deferred AC meanings, the brief
  amendment, runbook, repository layout, TLS deployment note and README now
  state that PDF is deferred and the API returns `pdf_import_deferred`.
- **Verification before the final review:** backend `dotnet format` both
  projects PASS; `dotnet test` **233/233 PASS**; Release build 0 warnings/0
  errors; `dotnet publish` PASS; no vulnerable NuGet packages. Frontend
  `audit-ci` PASS; lint PASS; Vitest **171/171 PASS** (24 files); production
  build PASS; Playwright **17/17 PASS**. The dormant PDF parser/process tests
  pass but are not acceptance evidence for the deferred PDF path. No real-stack
  browser walkthrough was repeated in this session; the prior CSV walkthrough
  record is unchanged. No commit, push, pull request, bootstrap or later-Thread
  work.
- **Final independent review:** request `dac8ef28-4b79-46e3-ac15-9cd84b269cbe`,
  immutable SHA-256
  `5e8f7b319ee0481a6c97c332966b5d9b526e0b5211e3f82aa76596c9b14f5ba4`
  (22,956 bytes), verdict **NO-GO**, 0 CRITICAL, 2 HIGH, 2 MEDIUM, 2 LOW.
  PDF parsing was confirmed safely unreachable from the product upload routes.
  The blockers were CSV-R1 (CSV display descriptions were rewritten into their
  whitespace-normalized matching form) and CSV-R2 (an unknown or foreign commit
  could seed categories before ownership was established).
- **Correction after review:** CSV-R1 now preserves CSV display/source
  whitespace, including internal repeated spaces, tabs and quoted newlines,
  while still trimming outer padding; only matching normalization collapses
  whitespace. Commit/readback and duplicate-matching regressions were added.
  PDF continuation display collapse is unchanged. CSV-R2 now resolves an owned
  review or receipt before category initialization and still rechecks under the
  normal locks; unseeded-subscriber regressions cover missing and foreign
  operation IDs with unchanged category rows and seeding marker. CSV-N1 was
  strengthened to snapshot source rows plus the review account, period, source
  kind, status, parser configuration, fingerprint and expiry; assert the allowed
  decision and exact count transitions; and compare the complete exposed
  category/tag lists, the existing statement summary and the transaction's
  category/tag/hide/version state before and after the second prepare. CSV-N2
  now gives CSV parsing the same shared two-slot admission and 30-second
  deadline as PDF, with admission/deadline and dispatch regressions. CSV-N3
  validates negative column indexes and returns a fixed mapping error. CSV-N4
  rejects a malformed FNB header and has a preset regression.
- **Corrected verification before re-review:** backend `dotnet format` both
  projects PASS; `dotnet test` **242/242 PASS**; Release build 0 warnings/0
  errors; `dotnet publish` PASS; no vulnerable NuGet packages. Frontend
  `audit-ci` PASS; lint PASS; Vitest **171/171 PASS** (24 files); production
  build PASS; Playwright **17/17 PASS**.
- **Independent re-review:** request `6a10e904-cc33-4e4b-a072-6f134c3e2c7d`,
  immutable SHA-256
  `e2fccce29301f7090bf2cbea668bcbfd6003c8ff889880d0ee6f9e874533c2ef`
  (22,853 bytes), verdict **NO-GO**, 0 CRITICAL, 1 HIGH, 1 MEDIUM, 0 LOW. Both
  original HIGH fixes were confirmed independently. The remaining HIGH
  (CSV-R3) was that the new admission helper released its slot when the
  caller's cancellable wait ended, not when the parser worker actually
  terminated; CSV-N1-R said the immutability evidence claim was broader than
  its assertions.
- **Second correction:** `ParserAdmission` now keeps the permit owned by the
  worker. A separate observer releases it only after the action task is
  terminal, observes late faults and disposes the linked deadline then. The
  caller's cancellable wait is registered before the observer can dispose the
  source. New deterministic tests hold a cancelled worker behind a cleanup gate,
  prove replacement work stays busy, then prove exactly one slot is admitted
  after the worker stops; success, synchronous exception, deadline, caller-abort
  and late-fault paths are covered. CSV-N1-R was closed by capturing parser
  configuration, fingerprint and expiry plus header account/period/source
  kind/status, comparing the complete exposed category and tag lists and the
  existing statement summary, and asserting the exact allowed decision and
  count transitions. The Plan wording was narrowed to those asserted fields.
- **Verification after the second correction:** backend `dotnet format` both
  projects PASS; `dotnet test` **246/246 PASS**; Release build 0 warnings/0
  errors; `dotnet publish` PASS; no vulnerable NuGet packages. Frontend
  `audit-ci` PASS; lint PASS; Vitest **171/171 PASS** (24 files); production
  build PASS; Playwright **17/17 PASS**.
- **Final acceptance review:** request `5524f1dd-f982-49af-a6fd-5a863fe95987`,
  immutable SHA-256
  `742f39ae9d9f938bcf4547e8f788f0eecb47f6091d45ee3becdb138565ee9702`
  (18,191 bytes), verdict **GO**, 0 CRITICAL, 0 HIGH, 0 MEDIUM, 0 LOW. Reviewed
  baseline: branch `plan/02-statement-import`, HEAD `496f34ab...`, reviewed Plan
  blob `901a93a59dd99e3a63316148a825dbb4632fa05c`, diff digest
  `de371ea2e6126b2cf2d8f8c4fb159b9e1ccb4579`, untracked digest
  `d23c207b740f806dd1bd6a8fd2268faf9a04bc74`. The reviewer independently reran
  the backend suite (**246/246**), all seven admission tests, both format
  checks, frontend Vitest (**171/171**) and lint; it confirmed CSV-R1, CSV-R2,
  CSV-N3 and CSV-N4 remain fixed, the CSV-R3 lifetime correction works against
  the rebuilt assembly (including delayed cleanup, caller cancellation and late
  fault), CSV-N1-R evidence and wording agree, and PDF remains unreachable from
  product routes. No blocking or non-blocking findings.
- **Status:** **review-accepted CSV-only implementation** on the exact baseline
  above. Earlier NO-GO reviews remain historical correction evidence and are not
  relabelled. Publication, commit/push, deployment and Threads #4/#5 progression
  remain separate operator decisions. The PDF verdict `963d...` remains open in
  Thread #6.

## Revision 3 — C-PDF-1: complete traversal of extracted text

**APPROVED by explicit operator instruction.** After the bounded Specialist GO and note dispositions, the operator selected `Approve revision 3` for annotated proposal blob `53a63747ba2bb7e79d4a1dc5b966fce6b11c7153`. This section now governs the Standard Bank rules in Section 4.2 and the bounded Phase 1/2/4 correction below wherever the earlier exclusion rules were underspecified. The existing safety invariants, acceptance outcomes, CSV contracts and public interfaces stay unchanged. The original plan GO and later implementation NO-GOs retain their exact baselines. The approval authorizes the correction as Plan meaning, not acceptance of the implementation or publication.

### A. The correction

The supported Standard Bank PDF is text-based. PdfPig already supplies the text and coordinates needed for the reproduced failures. **Examine all extracted text; return it in a transaction/continuation, recognize its complete nontransaction structure, or reject the document.** Failure to parse a date or amount never establishes that a line is safe to ignore.

Implement this as a complete positional traversal in the existing parser. No general recognition framework, source-object inventory, vector/annotation interpreter, OCR, service or dependency is required. Local helpers, flags and traversal technique are implementation choices. This is a substantive clarification of the recognition architecture, not a new product feature or a claim to understand arbitrary PDF representations.

### B. Traversal and row rules

1. **Visit every page and all its non-whitespace extracted text.** Positional line grouping must preserve every token. Do not skip everything above a table header, below Y=40, or after a footer. A recognized header/footer consumes only its own text; extra text still needs classification. Conflicting column bands, unreadable extraction or ambiguous associations reject `unsupported_layout` under the existing contract.
2. **For a headerless page, require every line to be known nontransaction content.** Apply the complete-line/structure rules in C. One title, period or summary marker cannot authorize ignoring the rest of the page. Any other line rejects `unsupported_layout`, whether it contains a valid date/amount, malformed fields, integers, or no digits at all. Do not infer columns from another page. Blank/footer-only pages are harmless only if no other content remains.
3. **For a recognized table, discover candidates before validating values.** Exclude only the complete structures in C. In the remaining table body, any nonblank date cell or flow cell establishes a candidate, even if every value is malformed or the description is absent. Retain raw mapped values and source position. Apply existing date/amount/sign/precision validators afterward; defective fields have null interpretations and existing validation codes. Valid fields remain intact. A flow-only line is an invalid-date candidate with the existing empty-description warning. A date-bearing row with no flow is a missing-amount candidate; Balance is never a fallback. Printed zero is populated, and multiple flows remain invalid.
4. **Attach description-only text only as a supported continuation.** It must occupy only the description band, follow the established continuation alignment/spacing, and have one unambiguous preceding candidate. Keep cover/summary words in descriptions as ordinary text. Orphans or competing associations reject. Cross-page attachment is limited to the immediately following recognized continuing table with compatible bands, preserving the existing synthetic examples. A cover, summary, blank page or incompatible table closes that continuation sequence. Hold the last row until the next page's role is known; finalize it at document end if no continuation follows. No unresolved association may survive success.
5. **Finish the whole document before returning success.** Any unexplained text on any page rejects all earlier candidates too. Inspect and prepare use this same parser; rejection must occur before the store creates a review. Preserve all existing size/page/character/row/memory/deadline/output limits during processing. Exhaustion must not return a successful prefix. Do not persist or log traversal bookkeeping, excluded metadata or balances.

These rules replace `LooksLikeTransactionContent`/money-token checks as permission to discard content. Such expressions may validate a value; adding more examples to them is not the recognition correction.

### C. What may be ignored

Recognition is local and complete: fixed text plus its expected position/fields/order. Whitespace/case normalization and fixture-backed coordinate tolerances are allowed. Prefix/substring matches and arbitrary trailing-text capture are not.

- **Covers and headings:** preserve the existing top-of-page `Synthetic Bank` / `Synthetic statement` cover, with or without its complete printed period, and the period-only safe control. Preserve the bank title/period above the hand-authored table. A complete `Statement summary`, `Account summary` or `Summary` heading in the top-of-page non-table region may consume that heading only. Unknown accompanying text rejects; it does not become safe because the heading exists. Period prefill keeps its existing date/conflict rules.
- **Account metadata:** exclude only complete, independently specified metadata fields in their preamble position. `Account`, `Customer` or `Branch` cannot be wildcards for arbitrary values/lines. The inspected positive fixtures establish no additional metadata block; unknown metadata rejects rather than leaking into review or prompting invention of a new bank layout.
- **Terminal totals:** preserve the existing `Total payments / 1,234.56` control after the last hand-authored transaction. It requires an empty date, the exact full description label, one well-formed amount in Payments, no other tokens/fields, and only recognized totals/balance/footer lines afterward. The corresponding `Total deposits` and `Total bank fees` labels require their own flow bands. Check the remainder before treating the row as terminal; never stop scanning at the label. Wrong/malformed fields or later transactions prevent summary exclusion: retain an unambiguous candidate or reject uncertainty.
- **Balance rows:** within a recognized table, `Opening balance` and exact labels ending `brought forward` are allowed only before transaction rows; `Closing balance` and exact labels ending `carried forward` only in the terminal suffix. The labels are the existing full forms: `Closing balance brought forward`, `Closing balance carried forward`, `Balance brought forward`, `Balance carried forward`, `Brought forward`, `Carried forward`. Require only that label and one Balance cell, with no date/flow/other text. An arbitrary description plus a balance is not a balance row. No balance or total enters output, identity or reconciliation.
- **Footers:** match complete `Page n`, `Page n of N`, or `Continued on page n` text in the footer position, with consistent page numbers. Consume that text only; inspect anything beside or below it. The same words in a transaction description are not footer authority.

`Bank charge`, `Account fee`, `Summary Books purchase` and `Total payments shop` are merchant text, not exclusion rules. A dated row whose description is exactly `Summary` or `Total payments` is still a transaction. Only the complete reserved terminal-total shape above is excluded; this is a syntactic format rule, not a claim about merchant meaning or bank authenticity. If assignments remain genuinely ambiguous, reject rather than choose the discard path. No headerless totals layout is established by the fixtures, so a headerless heading plus totals rejects instead of borrowing table columns.

### D. Separate image check

Keep image handling separate from text traversal. Check **every** page before any cover/table branch. For this text-only supported subset, reject any page containing a raster image as `text_pdf_required`: there is no evidenced safe-logo exception. A text header cannot bypass the check, and an image below 25% is not proven harmless. This is a simple rejection check, not image interpretation. Disclose that real-bank logos may therefore be rejected; do not claim broader real-bank certification. Existing encrypted/unreadable/unsupported-format rejection remains in force. No new general vector/annotation analysis or rendering capability is prescribed.

### E. Scope, phase order and verification

**Phase 1 — parser correction:** `backend/Imports/StandardBankPdfParser.cs`, optional local helpers within `backend/Imports/`, and `StatementParserTests.cs`, `StatementSafetyTests.cs`, `StatementParserProcessTests.cs`, `StatementFixtureBuilder.cs`, `Fixtures/Statements/**` under `backend/tests/argus.Server.Tests/`. `StatementSafetyTests.cs` is explicitly included to resolve Section 8's enumeration ambiguity. Test resource entries may be adapted mechanically. No CSV or child-protocol changes. Implement the traversal, exclusions and image check together; a regex-only patch is insufficient.

**Engineer-owned note CPDF-N1 (MEDIUM; no gate blocked):** instantiate the existing matrix with unknown tokens on the **same visual line** as a header, period, footer or terminal total, including adjacency near a column boundary. Do not reuse presence-only header matching or prefix footers to consume those tokens. Only the complete recognized structure may be excluded; residue must become an unambiguous candidate/continuation or reject. Ordinary transaction-row Balance cells remain recognized but non-retained content, never amount fallback, output or identity. Include applicable Phase 2 no-effect assertions. This is implementation/test guidance within the reviewed rules, not a source-object ledger or new architecture.

Commands in `backend/` (parser/process/safety tests need no database):

```bash
dotnet format argus.Server.csproj --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet test tests/argus.Server.Tests --filter 'FullyQualifiedName~StatementParser|FullyQualifiedName~StatementSafety'
dotnet build -c Release
dotnet publish argus.Server.csproj -c Release --no-restore
```

**Phase 2 — boundary evidence, after corrected Phase 1:** only the existing `StatementImportContractTests.cs` for authenticated inspect/prepare rejection and invalid-row skip tests. No persistence redesign. Use the approved isolated `ARGUS_TEST_DATABASE_URL`, never customer/development data. Run `dotnet test tests/argus.Server.Tests --filter 'FullyQualifiedName~StatementImportContractTests'` in `backend/`, then Section 10's full backend battery. If parser rejection still creates a review or partial success response, stop rather than hide it in the client.

**Phase 4 — evidence/runbook, after focused parser and API evidence:** update only the supported-format/evidence wording in `docs/statement-import-runbook.md`. Record exact source blobs, before/after outputs and evidence strength; run Section 10's full project battery for work acceptance. Existing dependency checks and outstanding keyboard/concurrency/work-bound evidence are not waived. No frontend, schema, service, runtime configuration, dependency or later-Thread change is added.

**CPDF-N2 (LOW; no gate blocked) — Architect scope disposition:** keep frontend changes outside this bounded correction. In the runbook, state both the text-only/no-raster restriction and the specific existing wording limitation: `frontend/src/app/imports/import-workspace.tsx:33` and `frontend/src/lib/server/import-actions.ts:45` currently say `That PDF has no text layer` for `text_pdf_required`, although the proposed policy also rejects text-bearing PDFs with raster logos. Do not describe that message as proof that text was absent. This records the limitation rather than claiming to fix the user-facing copy. Changing those messages would require an explicit bounded scope decision; this review grants none. No new error code or image interpretation is needed.

### F. Focused regression matrix

Use real PDF bytes with independently specified expected rows/positions, not mocked extracted lines. Preserve existing tests. **Reject** means no successful or partial parser result. **Invalid candidate** means exact row accounting, raw fields/position, correct validation, and commit blocked until explicit skip.

| Case | Required result |
| --- | --- |
| Existing 4-page/49-row/66-continuation and 18-page/297-row/356-continuation fixtures; hand-authored three rows; shuffled streams; cross-year/missing-period; three flows | Preserve every expected row/value/description/page exactly once and existing confirmations. Balance/preamble variations do not affect transactions or appear in serialized rows. |
| Synthetic cover with/without period; period-only page before/after a valid table; supported totals/balance/footer controls | Accept without extra transaction candidates. Preserve the existing terminal `Total payments / 1,234.56` control. |
| First table page has one valid January transaction; headerless second page has `Statement period: 01 Jan 2025 to 31 Jan 2025` plus `BAD-DATE / Bank charge` with `-1,000.00`, `-1000.00`, or `20` | Reject all three. Repeat with absent date, zero, garbage/absent amount and arbitrary nonnumeric text. The period-only control still returns exactly the first row. |
| Recognized table: valid/malformed/absent date crossed with grouped/ungrouped/integer/garbage/absent flow; flow-only/no-description; missing/multiple flows including zero | Every date- or flow-bearing row remains a candidate before validation. Valid grouped/ungrouped equivalents have identical cents under existing validators; defects stay visible. Both date and flow absent require a proven continuation or rejection. |
| Merchant descriptions contain cover/summary/footer words; missing-date `Summary Books purchase` plus payment | Retain ordinary descriptions; the missing-date payment is an invalid candidate. Extra tokens, wrong band, malformed total, a date, or a later transaction cannot trigger summary exclusion. |
| Unknown text on a cover, above a header, beside/below a footer, below the old Y=40 cutoff, or after otherwise valid content | Return it only as an unambiguous in-table candidate/continuation; otherwise reject the whole document. Test first/middle/last-page failure. No marker or early `break` hides the added text. |
| Same-page/immediate compatible next-page continuation; orphan, intervening cover/blank/summary, incompatible header or ambiguous columns | Preserve supported continuations and source positions exactly once; reject orphan/ambiguous attachment. Summary words in continuation text remain text. |
| Image-only page, text-header-plus-image, small image below 25%, multiple small images, image on a cover | Reject `text_pdf_required` regardless of other safe pages or markers. Text-only controls still pass. |
| Unknown nonnumeric words generated at fixed positions; shuffled content streams; existing limits and limit+1 | Shuffling positions unchanged preserves rows. Added unknown text is retained where structurally valid or rejects, never omitted. Bound exhaustion rejects rather than returning a prefix. |

Run the fresh three amount probes, safe-cover/terminal-total controls, invalid-candidate case, last-page residual and small-image case through the **actual published child** too. Through both authenticated HTTP inspect and prepare, exercise representative cover/last-page rejection and recoverable invalid candidate. Assert fixed safe errors, no partial success, no new review/statement/transaction/row/receipt records on rejection, and no excluded metadata or financial canaries in errors/logs. Assert commit refusal before skipping an invalid row and reconciled counts afterward. Child-only evidence is not HTTP/browser evidence.

### G. Stops, recovery and approval

Stop for accepted unexplained text, an omitted candidate, a rejected supported positive fixture, ambiguous association, partial success after later failure, lost position, privacy leakage, failed/unavailable required evidence or a required change outside the named scope. Do not expand bank layouts or weaken rejection to make a sample pass. No new critical implementation decision remains inside this proposed scope; geometry tolerances, helpers and test organization remain local choices.

This may reject documents previously accepted heuristically, including images/logos and unknown metadata. The text-only synthetic positives remain required. No schema rollback or receipt change is needed. Existing stored records cannot recover previously omitted content; do not automatically rewrite, delete or reimport them. Any real-data repair needs separate operator-authorized scope on Thread #3. F1 remains open until the correction and its evidence exist; publication and Threads #4/#5 remain blocked.

**Validation/provenance:** finished source inspection above remains applicable; parser/CSV/brief hashes were rechecked unchanged before simplification. The invariant-consistency pass checked complete traversal, terminal-total lookahead, candidate-before-validation, continuation boundaries, parse-before-persist, file scope and the matrix against the observed fixtures. No source-object ledger or general renderer is necessary. The prior broader proposal is preserved as blob `716fb24ee405f9ad89815ec6f62d4f86e46866c6`. The operator-authorized bounded review is complete, as recorded below. A review does not approve revision 3. Implementation/new regression evidence is **NOT RUN**; no application changes or publication have occurred in this Architect task.

### H. Bounded correction review and note disposition

- **Verdict: GO**, 0 CRITICAL, 0 HIGH, 1 MEDIUM and 1 LOW; no blockers. Request `3c40ca6b-3f95-4fc5-9599-54aba9f240db`; reviewed full-file Git blob `053218c3a9640fa124126db8ad8e88a625576e7f`, proposed revision 3 / C-PDF-1. Immutable complete review SHA-256 `a3d94295257b1066902c65d2f159999650135b1f8bffabc0d5d8f7f7e3a84d2e`, 19,280 bytes; final baseline check `2026-11-04T19:35:16Z`.
- **Assessment:** accept the GO. The complete traversal and default rejection close the proposed extracted-text omission model without a new recognition framework. This is architectural source-inspection evidence, not proof the current implementation is fixed. No blocking rewrite is needed.
- **Notes:** CPDF-N1 is accepted in Phase 1 above, with relevant Phase 2 checks. CPDF-N2 is disposed in Phase 4 above as an explicit operating/copy limitation with no frontend scope expansion. These annotations do not change the reviewed architecture, scope, safety invariants or acceptance outcomes; the final annotated blob is recorded on Thread #3.
- **Authority:** this completes the additional bounded review explicitly requested by the operator. No repeat review is requested or automatic. Original plan-review provenance and the fifth implementation NO-GO remain intact. The operator subsequently selected `Approve revision 3` for the annotated proposal named above; only authority/status metadata changed when activating it. F1, other findings, publication and later-Thread progression are not cleared by the architectural GO or Plan approval. All new implementation verification remains **NOT RUN**.

### I. Root-cause work order under revision 3 (2026-11-05)

#### I.1 Outcome, authority, and limits

**Disposition: the existing approved obligations cover the fix.** This is a
corrective work order within C-PDF-1 A–G, not revision 4, a new bank layout, or
acceptance of the implementation. There is **no change** to an acceptance
criterion, invariant, phase purpose or file scope. The processing order below
makes the existing obligations implementable together. Equivalent local
helpers, data structures, tolerances and test organization remain implementation
choices under A and G; they must establish the same outcomes.

The governing authority remains Thread #3 Decision
`2056cbfa-7436-4dc7-ac47-79b2292e91b7`, approving revision 3's annotated proposal
`53a63747ba2bb7e79d4a1dc5b966fce6b11c7153`, activated as
`a7080d0af841aa086a50838ff9ab8d75001d5bf3`. The architectural GO in H applies to
its recorded proposal, not to this implementation or to a new review of this
work order. The operator explicitly requested no further review.

Required outcome: every extracted non-whitespace word is accounted for by an
unambiguous mapped row/continuation or a complete permitted structure; otherwise
the whole document rejects before any review is created. Balance cells may be
recognized without being retained. Recognizing one structure never consumes
unrelated words. Every table page has one compatible set of bands; detecting
another header is not permission to reinterpret later rows under stale bands.

Current problem: the parser visits lines, but its first-header lookup, cell
projection, preamble branch, continuation flag and candidate-only event list do
not establish those obligations across all paths. More date/amount expressions
or eight independent exception patches would leave that class open.

Scope remains the Phase 1, Phase 2 and Phase 4 correction in E. No general
recognition framework, source-object inventory, rendering/annotation analysis,
OCR, dependency, service, configuration, child-protocol, frontend, schema,
CSV, existing-record repair or later-Thread work is added. Thread #1 retains
programme authority; Threads #4/#5 and publication remain blocked.

#### I.2 Finished inspection and finding disposition

**Inspected baseline:** branch `plan/02-statement-import`, HEAD
`496f34ab39a792b090753ed4e2215562fe0ea15d`, parser blob
`fdcd2e1dbdf48888ad89fe46b5ebe97821f270ad`. Immediately preceding Plan blob
`7654695ae5de7499b225ccc15444a04f046121dc` and runbook blob
`773fd3ad60acf001983a2f53a99f9184a93da7cd` are preserved as local Git objects.
Selected brief remains `32a45cf3f760112b2a520c4b327c45455427dd06`.

Finished Explorer result `ses_e53700fe4ffeKMLgpo8NCoQKbb` covered the named
parser/process/safety/fixture/API test files, parser dispatch/child/runner,
models, endpoints and the relevant review-store paths. Direct reads covered
the complete current `StandardBankPdfParser.cs`, the selected Plan/brief,
runbook, `ImportLimits.cs`, early child dispatch, and the specific fixture,
continuation, endpoint and privacy tests. Inspection finished before this work
order was drafted. Its evidence is **source inspection**, not a new execution
of the reviewer's probes.

The supplied actual-PDF counterexamples and verdict are recorded on Thread #3
Handover `18734253-5247-493e-8fba-e67ebff2ebe6`: review
`3e2dbd3f-232b-465f-a075-a11dd4d4e7fa`, NO-GO, immutable SHA-256
`b75db8a3d473bd0e30e5436532fe2c271165a48c4c52cbcf5657d3f112f6362d`, 31,141 bytes.
Architect verified their structural causes, not their runtime outputs.

| Item | Classification and existing authority | Current source evidence and owning phase |
| --- | --- | --- |
| CPDF-I1 — second complete header | **CONFIRMED**, B.1/B.3; source-truth invariant | Parser `181–182,225–230,679–733` finds/skips only the first header. Phase 1 must enumerate all complete headers before using page bands. |
| CPDF-I2 — unknown preamble metadata | **CONFIRMED**, C account-metadata rule and B.5; privacy invariant | Parser `238–277` promotes projected date/flow occupancy before rejecting unknown metadata. Phase 1 fixes precedence; Phase 2 proves no effect/leak. |
| CPDF-I3 — unmapped negative-X text | **CONFIRMED**, B.1/B.5 and CPDF-N1 | Parser `829–850` starts Date at X=0 and tests no coverage of the original words. Phase 1 must check complete assignment; Phase 2 proves no effect/leak. |
| CPDF-I4 — continuation permission reused | **CONFIRMED**, B.4/B.5 | Parser `221,241–244,269–277,334–340,640–646` leaves the cross-page flag active after attachment and consumes a preamble Summary without closing it. Phase 1 owns the lifecycle fix. |
| CPDF-I5 — headerless heading position | **CONFIRMED**, B.2 and C position rule | Parser `197–205` recognizes cover/summary/period text without checking its page role/position. Phase 1 must use the same positional recognizers in both page branches. |
| CPDF-I6 — lost source line | **CONFIRMED**, B.3/G and Section 4.2 | Parser `151,238–283` assigns pre-header candidates before incrementing `visualLine`; `405–410` separately assigns output ordinals. Phase 1 must preserve the distinction. |
| CPDF-N3 — zero-day date throws | **CONFIRMED**, B.3 and AC2 recoverable defects | Parser `941–954` checks only the upper day bound before construction. Phase 1 fixes validation; Phase 2 preserves explicit-skip behavior. |
| CPDF-N4 — incomplete terminal suffix | **CONFIRMED**, C terminal-total rule | Parser `354–391,861–869` checks later candidate events but records no later cover/summary/period/header roles. Phase 1 must reconcile the complete permitted remainder. |

None of these eight items is deferred. Preserving the named positive fixtures
is **ALREADY SATISFIED as an approved obligation**, not a new implementation
pass. The text-only restriction and no new metadata layout are **INTENTIONAL**.
Other CSV/auth/lifecycle findings are **NOT APPLICABLE** to this bounded
reassessment and are not independently cleared here. Transaction workspace and
reporting remain **DEFERRED** to their existing owners, Threads #4 and #5.

Inputs and reachability are established. `MinimalPdfBuilder` emits arbitrary
`PdfText(X,Y,Text)` on a 595×842 page without clamping coordinates
(`StatementFixtureBuilder.cs:489–602`); no dependency or production seam is
needed. Both inspect and prepare call the same runner/child/parser
(`StatementParser.cs:23–29,51–57`; `PdfParserChild.cs:25–26`). The early child
mode precedes web configuration (`Program.cs:10–22`). In the endpoints, parse
precedes preview response and `CreateReviewAsync`
(`StatementImportEndpoints.cs:125–152,187–202`). Rejection therefore precedes
review insertion and the store's category-seeding path; changing persistence is
unnecessary. Existing count helpers and `Factory.LogCapture` provide the
required isolated database and log evidence. The Explorer's unrelated deadline
field observation is not a new task or a changed deadline contract.

#### I.3 One local traversal model

Use bounded page-local preparation, the existing positional traversal, and
final document reconciliation. This is not a generic grammar/recognizer or a
ledger of PDF objects. Bookkeeping remains request-local and is never returned,
persisted or logged.

1. **Establish positions before branching.** Group every extracted word into
   visual lines without filtering by X, header location or footer location.
   Assign each nonblank grouped line its positive, monotonically increasing
   document source-line position before any headerless/preamble/header/footer
   branch. Include recognized lines in that numbering. Preserve the start
   page on each candidate. Assign `SourceRowNumber` only after final candidate
   reconciliation; it remains the output ordinal, not `SourceLine`. Stream
   reordering with unchanged geometry must not change either source position.
2. **Establish page bands before projecting cells.** Enumerate all complete
   six-label headers anywhere on the page, not just the first. Check label
   order, non-overlap and compatible boundaries/alignment using the existing
   fixture-backed tolerance. A repeated compatible header consumes only its
   own words and creates no row. Any conflicting complete header rejects the
   document as `unsupported_layout`, even if it occurs after valid rows. Do
   not turn that header into an invalid candidate or switch bands to rescue
   the page. Extra words beside a header must not disappear into the header
   match. No independent second table layout on the same page is introduced.
   If a tolerated header difference would place the same word in different
   semantic columns, reject that ambiguity; tolerance cannot authorize a
   different flow direction or turn Balance into a flow.
3. **Prove cell assignment before using projected strings.** For an ordinary
   table line, every original word must map to exactly one of the six ordered
   bands, or be consumed by its own complete permitted structure. Reject
   unmapped or ambiguous coordinates instead of clipping, dropping or moving
   the word into a neighbour. In particular, a negative X center must not
   vanish because the first band's lower bound is zero. Do not extend a flow
   band to swallow such residue. Keep the current positional convention for
   normal words and deterministic boundary ties; test it against the original
   word collection, not only the projected cell strings. Recognized ordinary
   Balance cells are non-retained content, never a flow or identity input.
   A classifier must not claim off-role or non-finite coordinates merely
   because the text resembles a known label; use the structure's complete
   positional rule before consuming its words.
4. **Apply preamble roles before candidate promotion.** Use complete text plus
   the expected top-of-page/preamble/footer position for nontransaction
   recognition in both headerless and table-page paths. The existing fixtures
   place masthead/period content around Y=780–820. Y=400 is not that role.
   Choose and record a bounded tolerance from the fixture geometry; test both
   sides of it. An account/customer/branch-labelled metadata line in the
   preamble has no approved accepted production: reject it before projecting
   it into a row, even when its words occupy Date or flow bands. Do not add an
   allowlist of account values or an accepted metadata block. Other
   unambiguous pre-header transaction rows remain candidates with their real
   source positions; this is not a blanket above-header skip/rejection. Bank
   title/period above a recognized table remains its masthead, not a separate
   cover page. Ordinary dated merchant descriptions and supported
   continuations containing `Summary`, `Account fee` or cover words remain
   source text, not structural markers.
5. **Make continuation authority local and consumable.** Hold at most the last
   unambiguous candidate for possible attachment. A page transition can grant
   one bridge to the immediately following compatible table's leading
   continuation; it does not waive spacing for the rest of that page. Consume
   that bridge on the first attachment and update the last-line position.
   Bound the first attachment to the fixture-backed leading table region too:
   the existing control has a header at Y=800 and continuation at Y=770. A
   first fragment hundreds of points below that header is not made safe by
   being the first description-only line. Exact tolerances remain local,
   but must preserve the supported leading slot and reject that distant one.
   Every later attachment, including another pre-header attachment, must meet
   ordinary alignment/positive vertical-gap rules. A new candidate replaces
   the preceding attachment target. A recognized summary heading in a table
   preamble closes the old sequence before later text is examined. A
   headerless cover/summary/blank page, incompatible next-page bands, total or
   closing-balance structure also closes it. A footer ends same-page
   continuation eligibility without skipping anything beside/below it; an
   otherwise eligible trailing candidate may still be held for the immediate
   next page. Table masthead/period and the expected compatible header do not
   by themselves invalidate the supported next-page fixture. Reject an
   orphan or distant description-only line instead of reviving a closed
   target. Preserve `CrossPageContinuationAttachesBeforeTheNextPagesFirstRow`
   and the generated multi-line cross-page blocks; do not solve the bug by
   disabling cross-page continuation.
6. **Retain enough ordered roles to decide exclusions.** Record the recognized
   roles relevant to final ordering, not just candidates. A provisional total
   may be excluded only if its entire later nonblank remainder consists of
   complete approved terminal totals, allowed closing/carried-forward balance
   lines and consistent footer lines. A later Summary, cover, period, header,
   table restart, candidate or ordinary continuation disqualifies that total's
   exclusion even if it produces no later candidate. Blank whitespace adds no
   text to this suffix. Use the same complete ordering when checking opening
   and closing balance placement. For a disqualified total whose original
   mapped cells still form an unambiguous candidate, retain that original
   invalid-date candidate with its flow/position; reject if association is
   ambiguous. Never delete it merely because it was provisionally labelled
   Total. A malformed/wrong-band total follows the existing candidate rules.
   The existing same-line Balance-residue test still yields its required
   invalid candidate; this work order does not erase that test's expectation.
   A bounded backward suffix check can resolve this without guessing: later
   disqualified totals count as candidates, not permitted terminal structures.
   Check opening/closing placement against the final retained candidates,
   including those totals, rather than the preliminary candidate-only list.
7. **Validate candidates, then publish once.** Date parsing must check both day
   bounds before constructing a four-digit date. `00 Feb 2025` becomes one
   `invalid_date` row with its other mapped values intact; it is not a
   document-layout failure. Preserve current year/century, period, sign,
   precision, zero/multiple-flow and description rules. Reconcile every
   pending total/continuation, enforce all existing bounds, then build one
   success result. No page, preview prefix or review can escape before the
   final page and reconciliation succeed. Raster and encryption checks remain
   separate and unchanged; any raster on any page still rejects.

The source of truth for columns is the complete page headers, never the last
row, labels found in a description, previous-page guesses or an amount regex.
The source of row fields/positions is the original positional text. The Plan
remains policy authority. `ImportLimits` remains configuration authority.
Customer/account/session authority and durable review/receipt state remain in
the existing backend/store; none is introduced into the child.

#### I.4 Phases and exact edit scope

**Phase 1 — correct the traversal and establish its regressions.** Dependency:
the existing reviewed working tree. Implement I.3 as one coherent parser
correction, not a sequence of independently declared finding closures.

- Production file: `backend/Imports/StandardBankPdfParser.cs`. Optional private
  helper splitting remains inside `backend/Imports/` as already allowed in E;
  no other parser, runner, protocol, model or endpoint change is required.
- Tests: `backend/tests/argus.Server.Tests/StatementParserTests.cs`,
  `StatementSafetyTests.cs`, `StatementParserProcessTests.cs`,
  `StatementFixtureBuilder.cs`, and `Fixtures/Statements/**` under that same
  test directory. Only existing test-resource entries may change mechanically
  in `backend/tests/argus.Server.Tests/argus.Server.Tests.csproj` if needed.
- Engineer-owned bullets: cover all eight findings with I.5, plus the original
  CPDF-N1 same-line/column-boundary and non-retained Balance cases. Preserve
  every existing positive and required invalid-candidate expectation. Extend
  the existing published-output test rather than creating another child path.

Commands, working directory `backend/`:

```bash
dotnet format argus.Server.csproj --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet test tests/argus.Server.Tests --filter 'FullyQualifiedName~StatementParser|FullyQualifiedName~StatementSafety'
dotnet build -c Release
dotnet publish argus.Server.csproj -c Release --no-restore
```

**Phase 2 — prove the existing boundary rejects before durable effects.**
Dependency: corrected Phase 1. Edit only
`backend/tests/argus.Server.Tests/StatementImportContractTests.cs` for the
authenticated cases in I.6. Reuse `MultipartPost`, database helpers and
`Factory.LogCapture`; test-local helpers can live in this file. No production
endpoint/store/schema/auth changes are added. Run in `backend/`:

```bash
dotnet test tests/argus.Server.Tests --filter 'FullyQualifiedName~StatementImportContractTests'
dotnet test tests/argus.Server.Tests
```

**Phase 4 — correct operating claims and record evidence.** Dependency:
Phases 1/2 evidence. Update only the format/evidence sections of
`docs/statement-import-runbook.md`; record execution in the Plan's existing
Direct Execution Record and Thread #3. Keep previous claims labelled historical
and superseded, not deleted. Keep CPDF-N2's explicit copy limitation. Run the
full Section 10 backend/frontend battery, including dependency-result
inspection, and report the exact new baseline and results. The earlier
keyboard/runtime/concurrency evidence obligations are not waived. Phase 3 is
not reopened; there is no frontend or shared/public contract change requiring
a downstream adaptation. No new assignment surface or review point is added.

#### I.5 Actual-PDF regression matrix

Use the reviewer's bytes if available, or independently specified equivalent
synthetic PDFs below. There is no need to obtain private customer statements
or recover the reviewer's temporary files. Use actual PDF bytes through
PdfPig, not mocked extracted lines. Expected geometry/rows/cents/positions must
be literal test inputs/expectations, not calculated with production column or
classification helpers. Existing generated manifests remain scale evidence;
the small cases below provide independent oracles. A4 fixture coordinates use
origin at the bottom left, 595×842 points and the existing 8pt text builder.

For the new small table controls, use the existing header positions Date 40,
Description 100, Payments 300, Deposits 400, Bank 470, fees 500, Balance 530;
specify Y separately. Supply confirmed February 2025 dates unless the case
tests date resolution. Shuffle content-stream order while preserving positions
for representative positive and negative variants.

| Actual-PDF case | Required result and evidence |
| --- | --- |
| Existing 4-page/49-row/66-continuation and 18-page/297-row/356-continuation fixtures, shuffled streams, cross-year/missing-period cases, three flows | Preserve each expected value, description, start page and validation exactly once; no reduced row/note counts. These are the existing AC1 format-fidelity outcomes. |
| Existing hand-authored three-row PDF, safe cover/period-only pages, exact terminal totals, opening/closing balances and footers | Preserve success without extra candidates, including the terminal `Total payments / 1,234.56` control. Preserve period-only pages before/after a table when no total's exclusion relies on an invalid suffix. |
| CPDF-I1 compatible repeat: header at Y=800, valid row Y=760, identical complete header Y=700, valid row Y=680 | Exactly two intended rows; no header candidate. Repeat with same-line residue: residue cannot be consumed as part of either header. Probe tolerated shifts with a word near a boundary; a changed semantic column is ambiguity, not permission to change flow/Balance meaning. |
| CPDF-I1 conflicting repeat: use the first header above, then at Y=700 use Date 40, Description 100, Payments 220, Deposits 300, Bank 400, fees 430, Balance 500; row at Y=680 contains a deposit at X=300 | Reject `unsupported_layout`; never a negative Payments transaction under the first header. Repeat with only a Balance `9,999.99` at X=500 on that row: reject, never Bank fees `-999999` cents with `balance_ignored`. The second header itself must not become a defective review row. |
| CPDF-I2: `Account: PRIVATE_ACCOUNT_7731` at X=40,Y=820 above a normal Y=800 header and a valid row | Reject `unsupported_layout`, not a missing-date row containing the canary. Include Customer/Branch-labelled preamble counterparts and a value extending into other bands. No accepted metadata layout is created. Body merchant text such as dated `Account fee` remains ordinary source. |
| CPDF-I3: add `UNMAPPED_CANARY_4421` at X=-95,Y=760 to an otherwise valid row | Reject; never success with a shortened row. Repeat on a terminal-total line, beside a header/period/footer and on a headerless page. Include in-bounds boundary ties and residue near boundaries so all words are either unambiguously accounted for or the document rejects. |
| CPDF-I4: preserve the existing immediate cross-page fixture, then add a second continuation 8 points below the first | Both adjacent continuation fragments attach exactly once. In a separate PDF, put the second fragment 470 points below the first: reject. Also reject a page with header Y=800 and its first purported continuation at Y=300, and description residue below a recognized footer. Neither a new nor reused bridge may waive the leading position or subsequent spacing. |
| CPDF-I4: next page has a top preamble `Summary`, then a compatible header and description-only text in the first table slot | Reject the orphan; the Summary closes the preceding page's target. Preserve the existing cover/blank/summary-page and incompatible-band rejection controls and ordinary Summary words in a valid continuation. |
| CPDF-I5: after a valid table, add a headerless page whose only text is `Summary` or `Synthetic Bank` at Y=400 | Reject both. Top-of-page counterparts at fixture-backed positions still pass when no unexplained text remains. Test just inside/outside the chosen tolerance, plus mid-page periods and same-line residue. |
| CPDF-I6: one page has candidates at Y=820 and Y=810, then header Y=780, then candidate Y=760; no other lines | Retain the three unambiguous rows with source lines 1,2,4 and source page 1; output ordinals are 1,2,3. Use short descriptions so the 10-point gap does not merge lines. Repeat on a later page: source lines advance across every prior grouped line, including consumed headings/headers/footer, and never reuse an earlier value. |
| CPDF-N3: well-mapped rows with `00 Feb 2025`, `01 Feb 2025`, `28 Feb 2025`, `29 Feb 2025`, `31 Apr 2025`, and `29 Feb 2024` with suitable confirmed periods | Day zero and impossible dates yield retained `invalid_date` rows, with mapped flow/description/source intact; valid boundary/leap dates remain valid. Exercise two-digit equivalents too, without changing century/year policy. No constructor exception rejects the whole otherwise recognizable document. |
| CPDF-N4: valid row, exact terminal total, then a headerless top-of-page Summary page | Do not exclude the total. With unambiguous original mapping, retain it as an extra invalid-date candidate; rejection is required instead if association is ambiguous. Repeat with later cover, period-only page, compatible header/table restart, later candidate and nonterminal balance role. No later-candidate-only test may authorize exclusion. |
| Complete permitted suffix: totals for each correct flow band followed only by allowed closing/carried-forward balances and consistent footers, through document end | Exclude those structures; no output balance/total/canary. Wrong label/flow, malformed amount, extra same-line fields, earlier/later position violations retain the required unambiguous invalid candidate or reject uncertainty under C. |
| Existing malformed-date/flow-only/missing/multiple/zero-flow rows; same-line total Balance residue; below-old-cutoff dated/flow rows | Preserve invalid-row accounting and existing explicit expectations; do not turn source defects into layout rejection solely to simplify traversal. Never use Balance as a missing-flow fallback. |
| First/middle/last-page unexplained content, original grouped/ungrouped/integer probes, encrypted fixtures, images of every existing size/placement, limits and limit+1 | Preserve whole-document safe rejection, no successful prefix, encrypted/image reasons and existing resource bounds. Keep the text-only positives; no safe-logo exception. |

Run the new compatible/conflicting-header controls, metadata case, both negative-X
row/total cases, far/below-footer continuation, preamble Summary closure,
mid-page heading, source-line control, zero-day row and full-suffix case through
the **actual published child** in `StatementParserProcessTests.cs`. Assert exact
safe rejection or expected row/validation/position, not just exit code. Preserve
the existing hand-authored, cross-page, safe-cover/total and image/encryption
controls through their existing parser/process coverage. Check that standard
error exposes no canary. These are synthetic integration results across the
real process boundary, not HTTP or browser evidence.

#### I.6 Authenticated boundary and no-effect evidence

Through **both** authenticated `POST /v1/finance/imports/inspect` and
`POST /v1/finance/imports/reviews`, exercise the metadata preamble,
conflicting-header Balance-as-fees case, negative-X row and negative-X total.
Use the real PDF child and existing isolated PostgreSQL database. Give each
rejection case its own customer/reset so import-rate limits cannot masquerade
as parser rejection. All must return HTTP 400 with the fixed
`unsupported_layout` error, no preview/review/partial-success payload, and no
metadata, omission or balance canary in response bodies or captured server
logs. Include first/middle/last-page placement representatives.

After **each** rejected request, assert no new `import_reviews`,
`import_review_rows`, `statement_imports`, `transactions`, or
`import_commit_receipts`; retain the account's ledger/import version and existing
finance data unchanged. Authentication's ordinary session/rate bookkeeping is
not a financial effect and must not be misreported as zero database activity.
Use current count/scalar helpers; do not add a production bypass or change the
store to hide failure. An unexpected review creation is a stop outside this
test-only Phase 2 scope.

Retain the valid ordinary Balance-cell and opening/closing-balance privacy
controls in inspect and prepare. Assert balance changes do not alter row
date/description/amount/identity inputs and do not appear in raw values,
normalized descriptions, output or logs. A valid prepare may create its expected
sanitized review; that is distinct from the zero-effect rejection cases.

For a well-mapped zero-day candidate plus one valid row, and for the unambiguous
total followed by a Summary page, prove inspect exposes the extra invalid row
and prepare preserves it. Commit before explicit skip must return `invalid_rows`
with no statement/transaction/receipt effect and the prepared source rows
unchanged. After explicit skip, commit only the valid rows and assert
`total_rows = imported_count + suppressed_count + skipped_count`, including
the retained invalid row. No source editing or new validation code is needed.

#### I.7 Evidence, adversarial checks, and recovery

Before implementation, add the independent regressions and capture their
failure against parser blob `fdcd2e1dbdf48888ad89fe46b5ebe97821f270ad`; then
capture their success against the corrected blob. Record literal expected
versus observed rows/positions/reasons, the published assembly identity, test
source blobs, commands and PASS/FAIL/NOT RUN results. If a supplied probe cannot
be reproduced, report the mismatch rather than asserting a before-failure.
Do not weaken existing tests to obtain green counts.

Same-page duplicate/conflicting headers, stream reordering, first/middle/last
page rejection, immediate versus stale continuation permission, marker/merchant
collisions, whole-document suffix ordering and exact day boundaries are the
adversarial cases of this correction. Existing timeout/cancel/child-exit,
concurrency, restart/receipt and isolation behavior remains unchanged and must
keep passing; this work does not invent new durable parser state.

Stop under G and Section 10 for an omitted word/candidate, lost position,
retained metadata/balance, reused closed continuation, invalid-row erasure,
conflicting bands accepted, positive-fixture regression, partial success or
durable effect on rejection, exhausted bound, failed check or unavailable
required evidence. Parser tests need no database. Authenticated tests require
the already configured isolated `ARGUS_TEST_DATABASE_URL`; do not bootstrap,
create databases, use development/customer data, or print credentials.

This correction may reject previously accepted unsupported PDFs, as already
approved. There is no schema rollback or historical-row rewrite. Preserve the
existing uncommitted tree and earlier evidence. Any required wider scope or
repair of already imported data returns to the operator on Thread #3; no such
repair is deferred into this work order. Publication and later-Thread readiness
remain the operator's decisions after real evidence, not this architectural
disposition.

#### I.8 Architect validation and handoff status

Finished inspection supplied the execution path, available positional inputs,
reachable inspect/prepare branches, parse-before-store effects and feasible
real-byte/process/database/log tests. Self-review and the invariant-consistency
pass checked that compatible repeated headers do not create rows, conflicting
headers reject before stale mapping, unknown metadata takes precedence over
preamble promotion, and unmapped words cannot vanish from exclusions. The pass
also checked that one consumed cross-page bridge still permits adjacent
continuations, source lines do not depend on candidate filtering, all later
roles participate in suffix exclusion, and malformed dates remain skippable.
The existing supported positives and Phase 2 test-only boundary are preserved.
No phase lacks an input or requires a public/schema/frontend change.

**Planning/source-inspection checks: complete. Implementation and new
regressions: NOT RUN by Architect.** This work order was not submitted for
another review and carries no new GO. Only the governing Plan and operating
documentation were edited. Exact final blobs and this authority disposition are
recorded on Thread #3 for the requested peer return. Acceptance remains blocked
by the six HIGH findings until corrected implementation/evidence exists; the
two MEDIUM notes are included in the owning phases, not dropped or used to
authorize another review.

## Proposed revision 4 — C-PDF-2: production Standard Bank template

**PENDING OPERATOR APPROVAL. Not implementation authority.** This proposal
belongs to Thread #3. Thread #6 remains the related finding tracker, not a new
programme child or an independently executable plan. It adds no programme work
unit, changes no child order, and clears no implementation finding or gate.

**Activation/precedence after approval:** record the operator's exact decision
against the full-file Plan and brief blobs; set the current revision to 4 and
mark the brief amendment accepted. The accepted brief appendix immediately
takes precedence for its named clauses; later consolidation into the body is
editorial, not a delay in authority. This proposal then governs the PDF-specific
rules where they conflict with the original Section 4.2, C-PDF-1 C/D, and I's
synthetic-only metadata/geometry/file restrictions. It extends only the named
real productions; A–I's complete traversal, absence-of-side-effects and other
unchanged rules remain binding. Section 9 below refines original AC1/AC2/AC8;
other acceptance outcomes and CSV contracts do not change. Preserve prior
versions and verdicts as history. No approval or activation occurs in this task.

### 1. Desired outcome and current mismatch

Support the two privately supplied production templates, and files that satisfy
their bounded format contract, through the existing Standard Bank preset. They
must produce their 49/297 transaction rows and 66/356 continuation lines without
losing source text, taking a Balance as a flow, leaking metadata or accepting
unexplained content. Counts identify the two acceptance samples; they are not
hardcoded limits for every future statement.

The current parser rejects these inputs before extraction because they are
encrypted. Its image and preamble policies would reject them next. Its
header-midpoint bands also begin Payments too early for the real description
width. Synthetic fixtures reproduce transaction semantics, but their separate
cover, unencrypted container and A4 layout do not reproduce this production
template. Both the legacy synthetic controls and new production-profile
fixtures remain required; real files never become repository fixtures.

### 2. Confirmed decisions, inspection and assumptions

**Operator decision:** these two private PDFs are intended production inputs.
Drafting their support is authorized; implementation and Plan approval are not.
**Existing decisions retained:** ZAR/account/period confirmation, immutable
source fields, invalid-row skip, exact money/sign/direction, ignored balances,
customer isolation, bounded child processing and atomic/idempotent persistence.

**Inspected baseline:** branch `plan/02-statement-import`, HEAD
`496f34ab39a792b090753ed4e2215562fe0ea15d`; parser
`5215c496da50e33da59778eb444c011e13fdc166`; preceding Plan
`316244d09c384714727202ee455a41433b8720ca`, runbook
`2e9c589a7114a95a3bab9d57bc9b882f039b8646`, brief
`32a45cf3f760112b2a520c4b327c45455427dd06`. Those documentation versions are
preserved as local Git objects. No application changes were made for inspection.

Explorer `ses_e5319e71efferV3tB4g3JENixR` failed on a network lookup and supplies
no findings. Finished direct reads covered the full current PDF parser,
dispatch/child/runner, authenticated parse-before-store endpoints, current
fixtures/parser/safety/process/contract tests, readback/count/log helpers, and
the installed PdfPig 0.1.16 API documentation and version-tagged upstream source.
This completes source inspection without relying on the failed scan.

Private read-only extraction used already installed PDF.js 4.10.38 outside the
repository, without rendering, external uploads or output of source values or
filenames. This is **structural extraction**, not product-parser acceptance:

| Structural property | Observed production template |
| --- | --- |
| Pages and table reachability | Headers on **all** 4/18 pages, including the first page. Date-anchor counts 49/297; per-page counts 14/17/17/1 and 14, sixteen pages of 17, then 11. Preamble and first table coexist. |
| Page frame | 612×792 points, origin at bottom left. First table header baseline about Y=531.7; later headers about Y=601.8. |
| Raster | One identical 656×225 asset on every page, placed about X=18.4–175.8, Y=714.3–768.3. No other raster was observed. Asset sameness/placement is established; its decorative content still needs the private confirmation in Section 12. |
| Columns | Date/Description start near X=38.3/95. Header right edges near X=358.6/429.4/500.2/579.6 for Payments/Deposits/Bank fees/Balance. Populated amount runs end near X=358.5/429.4. |
| Description separation | Inspected main-line extents reach X=287.1 and X=251, past the old midpoint boundary but before the real amount area. Smallest inspected gap to a populated amount run was about 45.9 points. No overlapping-glyph amount guess is justified. |
| Protection | Empty-password AES-256, Standard handler V=5/R=6, reported by the prior private inspection. New structural extraction opens with the empty password. It reports **text-copy permission disabled** on both inputs. |
| Page roles | First-page Account number/Account holder/Product name and five-line Address block; abbreviated metadata later; Available balance above each table; a terminal statement notice, Statement summary with Payments/Deposits totals, closing message and legal/page footers. |

The request supplies the 66/356 continuation expectations. Product extraction
of those counts, every date/description/flow and every source position remains
**NOT RUN**. A prior diagnostic CSV is not an independent value oracle.

### 3. Explicit scope and non-goals

In scope: the existing six-column preset's production container/profile,
residual C-PDF-1 findings, synthetic representations of this profile, private
acceptance evidence and necessary brief/runbook corrections. Supporting real
first-page metadata and terminal summary shapes is an explicit scope extension.

No OCR, renderer, image interpretation, generic PDF mapping, source-object
inventory, new service/dependency, replacement of PdfPig, frontend/schema,
HTTP/child-protocol/password field, CSV policy, ledger/duplicate policy,
historical-data repair, publication or later-Thread work. Do not learn a new
layout from whichever document happens to pass. The profile is finite code/data
for this preset, not a registration framework or a customer-editable grammar.

### 4. Target invariants and architecture

The original Plan Section 4.1 invariants remain intact. The following are
**explicit semantic changes to the accepted input contract**, requiring approval:

- Replace “reject all encryption, including empty password” with support for
  unencrypted inputs and **Standard AES-256 V=5/R=6 with verified empty USER
  password**. Other protection still rejects; no password input is added.
- Replace blanket raster rejection with a **known-asset-and-placement** rule
  for this template. “Small image” alone is never sufficient.
- Add the complete real first/later-page metadata forms, Available balance,
  legal/footer and terminal statement-summary forms below. They are excluded
  before any source output, but only after full structural recognition.
- Support the real 612×792 geometry and its real description/flow gutters.
  Legacy synthetic geometry remains a separate positive profile; it cannot
  lend its exclusions or bands to an unrecognized real page.

No source-loss, privacy, authority, exact-money, row-validation, resource or
durability invariant is weakened. Every extracted nonblank word must still be
accounted for exactly once, or the entire document rejects. A recognized slot
consumes its own spans only. Metadata is not transaction context. Balances and
summary totals never become amount fallback, output or identity.

#### 4.1 Protection gate inside the existing child

Remove raw `/Encrypt` substring rejection as the security authority. Use pinned
PdfPig's structured encryption dictionary and an explicit empty-password open;
no guessed, supplied, remembered or owner password is tried by application code.
Keep strict parsing, missing-font failure and silent diagnostics.

**Successful opening is not sufficient.** The pinned `EncryptionHandler`
authenticates owner before user, and its user verifier is internal. After open,
before requesting any page, resolve the one encryption dictionary through
`document.Structure.Trailer.EncryptionToken` and its public token scanner/object
access. This single dictionary lookup is not a source-object inventory.
Require Standard filter, V=5, R=6, 256-bit AESV3 stream/string protection and
well-formed bounded U/O/UE/OE/Perms fields. Reject unsupported/malformed security
without exposing tokens or exception text.
For R6 those fields have exact byte lengths U=48, O=48, UE=32, OE=32 and
Perms=16. Resolve only this security dictionary and its named crypt-filter
entries; do not build a new byte lexer or scan all source objects.

Add a narrow **empty-user R6 validation guard**, not a decryptor: apply PDF 2.0
Algorithm 2.B to the empty password and the eight-byte user-validation salt in
U; compare the result to U's first 32 bytes with fixed-time equality. Do not use
owner validation or the U vector for this user check. The pinned source's
`IsUserPasswordRevision5And6`/`ComputeStupidIsoHash` is an implementation reference,
not a callable public API. Use .NET's existing SHA-256/384/512 and AES-CBC
primitives; no reflection into internals, new dependency or hand-written block
cipher. The standard R6 loop has at most 287 rounds for this fixed input;
bound all field lengths and allocations and retain the child deadline/memory
limits. Only then may PdfPig's existing decryptor supply pages. Do not retain
keys/password material in results, logs or files.

Pinned source references: [PdfPig v0.1.16 EncryptionHandler](https://github.com/UglyToad/PdfPig/blob/v0.1.16/src/UglyToad.PdfPig/Encryption/EncryptionHandler.cs),
[Structure](https://github.com/UglyToad/PdfPig/blob/v0.1.16/src/UglyToad.PdfPig/Structure.cs)
and [TrailerDictionary](https://github.com/UglyToad/PdfPig/blob/v0.1.16/src/UglyToad.PdfPig/CrossReference/TrailerDictionary.cs).
The installed XML confirms public image/letter/structure APIs. Framework
fixed-time comparison is documented by [Microsoft](https://learn.microsoft.com/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals?view=net-10.0).

Independent known-answer vectors and independently generated AES-256 fixtures
must prove empty-user success, nonempty-user failure, **empty-owner with
nonempty-user failure**, malformed fields and wrong revision/filter failure.
Fixture generation must not call the production guard. Existing RC4/AES-128
fixtures stay protection rejections; this is not support for every encryption
suite. A `/Encrypt` word in ordinary unencrypted text is not itself protection.
Malformed ordinary structure may return `unsupported_layout`; actual required
password/unsupported protection returns the existing `encrypted_pdf` code.

**Approval decision about permission bits:** the target files disable text-copy
permission. This proposal permits their account import after empty-user
authentication despite that owner-set advisory flag. It does not change any PDF,
recover an owner password or grant owner operations. Backend customer/account
authorization still decides destination ownership; PDF metadata/permission bits
never grant application authority. If the operator does not approve this
specific decision, these exact files cannot be called supported under this plan.

#### 4.2 Known decorative asset, not a size heuristic

Enumerate images through the existing page API. For the real profile, permit
only the privately confirmed masthead asset: one instance per page, the exact
approved encoded-image digest and interpretation parameters, 656×225 samples,
and the observed upright placement. Record the approved nonfinancial asset's
digest/parameters as fixed profile constants after private confirmation; never
derive an allowlist from the current untrusted upload. No real PDF, customer
field or extracted customer image is committed as a fixture.

Inspection established identical decoded pixels, not necessarily identical
encoded streams. Freeze the finite one-or-two witnessed encodings from the two
approved source templates if their payloads differ; each must represent that
same confirmed nonfinancial asset. Do not silently admit other encodings or
images by dimensions, approximate similarity or a digest learned from an upload.

Use `IPdfImage.RawBytes`/`RawMemory` and `ImageDictionary` to compare the
decrypted encoded payload, filter/decode/color parameters and any mask identity
against that confirmed asset. This needs no runtime image decoder. Bounds must
be finite, within the page, axis-aligned and within one point of the measured
masthead rectangle; they must not intersect text/data/summary zones. No
additional image, unapproved mask, inline variant, relocation, crop trick or
same-size replacement is exempt. A synthetic decorative asset may be a second
explicit fixed profile constant for the synthetic production-profile fixtures;
it has no upload-controlled registration or test-only production bypass.

Every real-profile page must also contain its complete recognized text form and
table header. Image-only/scanned bodies, a header over an unapproved raster,
missing/unreadable text, and any image not satisfying this asset rule reject.
Use existing `text_pdf_required` for raster/text-layer failure and
`unsupported_layout` for unexplained text/geometry; no error code is added.

This establishes the relevant image-omission claim: every permitted image is
the **same privately confirmed nontransaction asset**, outside the table, so a
new transaction cannot exist only in an accepted image. It is not proof of bank
authenticity, the bank's complete ledger, or arbitrary PDF visual equivalence.
Selectable text alone and an image-area percentage do not prove completeness.
The private comparison in Section 9 establishes fidelity for the actual two
files; unknown future variants fail closed rather than being certified from a
logo or a title. No general vector/annotation rendering analysis is introduced.

#### 4.3 Real page form and complete metadata productions

Select the real profile from the **whole** measured page form: 612×792 frame,
upright uncropped coordinates, complete issuer/metadata form, Transaction details
heading, Available balance shape and complete six-label header. Use both lower
and upper bounds on every role. Normalize case/whitespace and allow a small
recorded extraction tolerance around measured anchors, not open-ended margins.
The following values are structural measurements, never customer values:

Y measurements in this proposal are **text baselines**, not bounding-box
centers. Use the pinned `Letter.StartBaseLine`/`EndBaseLine` data, or an
equivalent explicitly calibrated conversion, so the glyph ascender does not
shift a field into a neighbouring role. Check full finite glyph/page bounds
separately. Initial text-anchor tolerances are at most two points; calibration
inside that bound is local. Wider movement needs new evidence and approval,
not a broad top-of-page test. Whitespace normalization does not authorize
coordinate clipping or a changed semantic column.

| Production | Bounded shape |
| --- | --- |
| Shared masthead | Confirmed asset at X=18.4–175.8,Y=714.3–768.3; Customer Care and Website lines in the right masthead around Y=752.5/741; numbered statement title and complete `From`/`To` date lines around Y=722.5/709.7/694.1, right edge 585.4. Public fixed wording is exact; numeric/date slots are explicitly typed, not arbitrary suffixes. Only From/To supply period prefill under the existing conflict/confirmation rules. |
| First-page issuer tuple | `Standard Bank` centered around X=279.1–366.8,Y=724.8; one bounded issuer/subtitle text line around Y=702.7, one printed issue-date line Y=679.9 and one integer sequence slot Y=658.7. These are non-retained metadata fields, not financial-account or period authority. |
| First-page customer fields | Exact `Account number`, `Account holder`, `Product name` labels at X=32.6, baselines about Y=646.3/630.6/615; one same-line value at X=104.6 inside its named slot. Account number is bounded digits/grouping/mask syntax; holder/product are bounded single-line text. No other field/line is accepted by the labels' mere presence. |
| First-page Address | Exact `Address` heading at right edge 585.4,Y=661.9, then exactly five right-aligned value lines at about Y=646.3/630.6/615/598.5/582.8. Explicit address-line slots, a four-digit postal slot and a short uppercase country-code slot; no continuation outside those slots. This is the particular inspected address form, not an arbitrary preamble rectangle. |
| Later-page metadata | Measured issuer tuple shifted to the later-page anchors near Y=725.6/703.5/680.7/659.5; one exact Account number field at Y=661.9. No holder/product/address block is borrowed from page 1 or consumed as a wildcard. |
| Table lead-in | Exact `Transaction details` at X=32.6,Y=553.7 on page 1 / 623.7 later. Exact `Available balance` plus one currency/money value above the table at Y=555.7 / 625.8, with right edge 585.4. Consume that complete field without returning its value or using it as an amount or identity source. |
| Bottom footer | Two exact fixed legal/service lines near Y=34/27.1 and the complete right-aligned `Pg n of N` shape near Y=32.2, with consistent page numbers. They consume only their own words; nearby table continuation text still belongs to its row. |

Customer Care's phone and Website text are exact confirmed issuer constants,
not arbitrary numeric/value suffixes. The explicitly variable masthead slots
are the statement ordinal/interval, From/To dates, issue date and sequence.

Public boilerplate literals and the bounded issuer/subtitle field are captured
from these named productions, not inferred by “common text on this upload”.
Private confirmation must distinguish constant bank wording from customer
fields. Encode constants exactly; use synthetic values for all variable slots.
Bound each value to its measured line/box and at most 128 Unicode scalars;
address line slots may use at most 80. No trailing capture, extra line, new
label, changed order, field outside its box or coincident transaction fields
is accepted. Unexplained residue rejects the document. In particular,
`Account:123` elsewhere cannot be promoted to a candidate or accepted by this
form. Do not suppress arbitrary text above the real header at Y=531.7/601.8.

Available balance is one right-aligned labelled field inside X=400–585.4;
its label moves left with value width, so do not fix the label's left edge
to one sample. Its baseline and final right edge remain anchored. Likewise,
right-aligned address slots are bounded fields, not fixed customer strings.

First-page form and table must both complete; the first page is **not** ignored.
All variable metadata, printed Available balance and summary values remain
request-local and leave no raw context, normalized description, diagnostic,
identity, review or database copy. Destination account/currency remains the
customer's explicit authenticated selection.

#### 4.4 Real column and lifecycle rules

Use complete header glyph positions together with the fixed real-profile
gutters, rather than midpoints between short header words. In this profile,
Date ends before X=88.5, Description starts at X=95 and may extend to X=294;
flow cells occupy approximately Payments X=296–364, Deposits X=367–435,
Bank fees X=438–506, with Balance X=509–585.4. The intervening gaps are not
discard zones: any unexplained text there is an ambiguity/rejection. Header
right edges near 358.6/429.4/500.2/579.6 establish column identity; populated
amount runs are right-aligned to those edges within a small documented
tolerance. Re-derive/check every page header against the same profile; never
borrow a previous page's bands or change flow meaning to make a row parse.

Group glyphs/words using actual positions so a separate minus sign and amount
form one raw flow cell. Numeric identifiers within the description stay there.
Cell occupancy, not successful money recognition, establishes a candidate;
garbage/missing/zero/multiple flow cells retain existing validation behavior.
The existing magnitude validator already trims whitespace after a leading
sign (`StatementText.cs:327–354`), so separated minus/number runs need no shared
CSV/money contract change; preserve their raw mapped text.
An amount that cannot be separated unambiguously from description text rejects
the layout; do not select the first/last number, subtract balances, split a
word to manufacture an amount, or widen a lane beyond the profile. Exact
overflow/precision/sign/date validators are unchanged. Letter APIs are
available if default word grouping crosses a field; no data is dropped.

Keep one complete-header interpretation per page even with extra same-line
words. A conflicting header core rejects before its residue is classified.
All numeric tolerances, including “exact”, must still perform the semantic
column check. Apply finite media/crop bounds before any header/footer/preamble
recognizer. Assign source positions before classification as in revision 3.

Description-only fragments follow the established row alignment and local
spacing, including smaller-font note lines at the real Description anchor.
Hold the last row only for the immediate compatible next table; consume the
single bounded cross-page bridge as in Section I. A word such as Summary that
also fits the current row's continuation position must not be erased by a
preamble label rule: retain it as the unambiguous continuation, or reject a
genuine conflict. It is not a structural heading merely because it matches.

The real **terminal postamble** is a distinct complete production after all
candidate/continuation rows: the exact fixed statement-care notice; exact
`Statement summary`; Payments then Deposits labels at X≈41 with separate
currency/money values ending near X=194.2; the exact fixed closing message;
then only recognized legal/page footers through document end. In the observed
files the notice is about 25 points below the final note, summary heading
49.3 below the notice, Payments 24 below the heading, Deposits 27.7 below
Payments, and closing message 23.8 below Deposits. Absolute Y varies with the
last page's row count. Match the complete ordered text/field/spacing shape,
not a standalone mid-page Summary label. No late transaction, continuation,
header restart or unrelated text may disappear into this suffix.

That complete production is a new approved exclusion if revision 4 is accepted;
it does not make all headerless or mid-page summary text safe. Its totals are
not retained or used to correct transactions. Legacy terminal-total and
opening/closing-balance productions still obey their complete suffix rules;
a later unrelated Summary/period/header disqualifies exclusion. Do not use
only the last candidate index for closing-balance placement.

### 5. Authoritative sources and configuration

The approved Plan and brief define the supported format. Fixed
`StandardBankPdfProfile` data owns geometry, complete productions and approved
nonfinancial asset identity. An upload, customer field, filename, logo size or
diagnostic CSV cannot extend that data. PdfPig remains the extractor/decryptor;
the narrow R6 guard verifies empty-user access. No password, profile or image
allowlist field is added to HTTP, child options or environment configuration.

`ImportLimits` retains every existing byte/page/row/character/deadline/heap/
resident/output/concurrency bound. Images are compared as bounded encoded
payloads rather than decoded at arbitrary dimensions. Customer/session/account
authority, sanitized reviews, duplicate state and receipts remain in their
existing backend/store paths. No new durable parser state is introduced.

Profile selection is deterministic. A 612×792 page that fails the complete
real form cannot fall back to legacy table-only or headerless recognition.
Legacy A4 controls retain A–I with the residual corrections; they do not grant
their synthetic preamble interpretation to real pages. Multiple plausible
interpretations reject, rather than choose the one with fewer rows.

### 6. Data/control flow and transitions

Authenticated inspect/prepare → bounded multipart → existing child → strict
open and empty-user gate → page/asset/frame validation → complete page/header
role checks → original-word accounting → candidates and bounded continuations
→ final suffix reconciliation → existing value validation → one sanitized
result. Inspect returns a bounded preview only after full success. Prepare
creates a review only after that same full parse. Any later failure discards
all earlier drafts before a response/store call.

Protection/asset/preamble bookkeeping never crosses the child result boundary.
Commit still reads immutable stored source rows and revalidates; no real-PDF
special case, client row setter, bypass of explicit skip or relaxed receipt
policy is allowed. Re-entry/cancellation/retry semantics stay unchanged.

### 7. Phases, dependencies and exact scope

Implementation starts **only after explicit approval** of revision 4 and its
brief amendment. Correction order within this existing work unit is **Phase 1a
→ Phase 1b → Phase 2 → Phase 4**. Phase 3 frontend work is not reopened. These
are semantic dependencies, not worker assignments or new review points.

**Phase 1a — close residual traversal defects.** Dependency: approval and the
reviewed baseline. Production scope is `backend/Imports/StandardBankPdfParser.cs`.
Test scope is `backend/tests/argus.Server.Tests/StatementParserTests.cs`,
`StatementSafetyTests.cs`, `StatementParserProcessTests.cs` and
`StatementFixtureBuilder.cs`. Establish the residual matrix in Section 9 before
any widened container policy is considered complete. Do not mask these failures
by rejecting all real-profile documents or disabling continuations.

**Phase 1b — implement the production profile as one coherent parser change.**
Dependency: the Phase 1a behaviors. Scope is the same parser, with explicitly
allowed private helpers `backend/Imports/StandardBankPdfProfile.cs` and
`backend/Imports/PdfEmptyUserAccess.cs` (equivalent internal organization inside
these scoped files is local). No runner/protocol/endpoint/model change is
required. Same test files, `Fixtures/Statements/**` and only mechanical fixture
resource entries in `argus.Server.Tests.csproj` are allowed. Extend the existing
builder for 612×792 pages, real row pitch/notes, forms, separate sign runs,
known synthetic masthead asset and PDF 2.0. Add independently generated
synthetic AES-256 R6 bytes and fixed known-answer vectors with provenance;
runtime/CI must not install or invoke another PDF tool/dependency. Test asset
identities are fixed constants, never a production test switch.

Phase 1a/1b commands, working directory `backend/`:

```bash
dotnet format argus.Server.csproj --verify-no-changes
dotnet format tests/argus.Server.Tests/argus.Server.Tests.csproj --verify-no-changes
dotnet test tests/argus.Server.Tests --filter 'FullyQualifiedName~StatementParser|FullyQualifiedName~StatementSafety'
dotnet build -c Release
dotnet publish argus.Server.csproj -c Release --no-restore
```

**Phase 2 — authenticated synthetic boundary evidence.** Dependency: completed
parser profile and residual regressions. Edit only
`backend/tests/argus.Server.Tests/StatementImportContractTests.cs`; use existing
count/readback/log helpers and test-local snapshot utilities. No production
persistence/schema/auth change. Use only the configured isolated test database.

```bash
dotnet test tests/argus.Server.Tests --filter 'FullyQualifiedName~StatementImportContractTests'
dotnet test tests/argus.Server.Tests
```

**Phase 4 — private sample acceptance and operating evidence.** Dependency:
Phase 1/2 results and the operator's approved private access/evidence procedure.
Update `docs/statement-import-runbook.md` and operational Plan/Thread records;
consolidate the already accepted brief wording in `docs/product-brief.md`.
Private harnesses and manifests stay outside the repository; no application
utility, general script framework or new dependency is added. Run the original
Plan Section 10's full backend/frontend battery. Record each actual command,
result and evidence level, not only test counts. No publication, setup or
later-Thread permission is implied.

### 8. Public contracts and briefing amendments

No route, request/response field, DTO, schema, configuration or error code
changes. The same `standard_bank_pdf` preset selects the supported profile.
Existing `encrypted_pdf`, `text_pdf_required`, `unsupported_layout`,
`processing_limit` and row validation codes retain their enumerated contract;
their supported-input policy changes are explicit in Section 4.

Approval must also approve the **Pending production-PDF amendment** appended
to `docs/product-brief.md`. It replaces the blanket wording in IMP-17 and
clarifies references in Sections 1.1, 4.10, 5.2, SEC-3, financial rule 20,
MVP-21, AC-IMP-15/16 and the error table. Password-protected means requiring
a nonempty USER password for this preset, not merely carrying an owner
permission/encryption dictionary. The approved umbrella Requirement already
names text-based PDF import and rejection of password-required documents;
this proposal does not silently revise a native Requirement or infer approval.
If the operator intends that Requirement to prohibit even empty-user access,
stop and obtain its explicit native correction before activating this Plan.

The existing no-text-layer frontend copy can still misdescribe an unsupported
raster rejection. Keep CPDF-N2 recorded as a limitation; no frontend scope is
added. A correction changes an implementation baseline, not the historical
independent verdict (CPDF-N7).

### 9. Acceptance criteria and required evidence

These are **revision-4 acceptance refinements**, mapped to existing outcomes.
AC1 format fidelity now includes the actual two private samples; AC2 rejection
now distinguishes permitted empty-user encryption/asset/template from unsafe
inputs. AC8 privacy now covers the full real metadata forms. Other acceptance
criteria are unchanged. No criterion is marked PASS at proposal time.

| Criterion / matrix | Required outcome and concrete evidence |
| --- | --- |
| P4-A — production fidelity (AC1) | Both actual private PDFs pass the **published product child**, yielding 49/297 rows and 66/356 notes with correct pages, every original date/description/flow and no preamble/Balance. Independent private value/position oracle, not the earlier heuristic CSV. New synthetic real-profile 4/18-page fixtures reproduce all-page tables and the measured 14/17/17/1 and 14/17…/11 row distribution. |
| P4-B — protection (AC2) | Unencrypted and proper empty-USER AES-256 R6 controls pass; nonempty-user, empty-owner-only, malformed U/UE/O/OE/Perms, wrong filter/revision and unsupported older protection reject safely. Known-answer vectors and independently generated PDFs; real published child too. Do not test production guard against fixtures generated by that same guard. |
| P4-C — asset/text coverage (AC2) | Exact approved decorative asset outside table plus complete text passes. Same-size different asset, correct asset relocated/duplicated/rotated into body, changed interpretation/mask, image-only page, text header over scanned body, missing text/font failure and last-page image residue reject the whole document. Unit real-byte and published-child evidence. No area threshold or “logo-looking” guess. |
| P4-D — real form/privacy (AC1/8) | Complete first/later-page forms and terminal postamble consume only their named fields; varying every synthetic account/address/balance value changes no source row/identity. Missing labels, extra tokens/lines, wrong order/position, unknown Account/Customer/Branch fields or a transaction-shaped intruder reject or remain an unambiguous source row, never disappear. Authenticated inspect/prepare responses, stored review reads and captured server/child logs contain no canaries from excluded slots. |
| P4-E — geometry/validation (AC1/2/3) | Long descriptions past the old midpoint remain intact; numbers inside them are not flows. Separate minus/amount runs, all three directions, zero, multiple/missing/malformed flow, overflow/precision, missing/invalid dates and cross-year periods retain exact existing outcomes. Deliberate overlap/ambiguous gutters reject, never borrow Balance. Shuffle streams without changing geometry. Unit and actual-child row/value/source-position assertions. |
| Compatibility positives | Keep legacy A4 4/49/66 and 18/297/356, hand-authored three-row/cover/terminal-total, safe period pages, ordinary Summary merchant/continuation and immediate cross-page continuation. Unapproved legacy raster/encryption controls still reject. Change obsolete assertions only against the explicit policy change, not to hide a failure. |
| CPDF-R1 — **CONFIRMED**, Phase 1a, B.1/I.3 | Scan complete ordered header cores even with same-line residue. Conflicting core plus residue rejects before using stale bands. Test deposit reversal and Balance-as-fees plus shifts below/at/above 0.01 and normal tolerance; every tolerated case still checks semantic assignment. Compatible header plus unexplained Balance-only residue rejects, rather than emitting header labels as a defective row. Correct that existing process-test expectation explicitly. |
| CPDF-R2 — **CONFIRMED**, Phase 1a/2, C/I.3 | Colon-attached/split labels (`Account:123`, `Customer:VALUE`, `Branch:VALUE`) outside the complete approved form reject before candidate promotion. Add a flow token to prove it cannot force promotion. Body merchant text remains text. Both authenticated endpoints: exact safe failure and no metadata in output/logs/reviews. |
| CPDF-R3 — **CONFIRMED**, Phase 1a, B.4/I.3 | Above-header candidate followed eight points below by aligned Summary is preserved as continuation (or a true ambiguity rejects), never successful with the fragment missing. True Summary before an incoming table still closes an old cross-page sequence. Exact-row-description and whole-document rejection controls through published child. |
| CPDF-R4 — **CONFIRMED**, Phase 1a, B.1/C | Summary at Y=900, footer at Y=-100, X beyond page width, nonfinite/cropped/out-of-frame words and image placements reject before role consumption. Keep valid near-edge controls on both page sizes. Both page bounds are checked; a tolerance never admits off-page source content. |
| CPDF-N5 — **CONFIRMED**, Phase 1a, C/I.3 | Legacy closing balance followed by Summary/period/header-only restart/nonterminal role rejects as nonterminal. Only the complete permitted suffix excludes it. Real production postamble is tested as its own complete newly authorized production, not a blanket exception to the legacy rule. |
| CPDF-N6 — **CONFIRMED**, Phase 2, I.6 | Seed existing statements/transactions/receipt/identity/tag links and an unrelated live review, then rejected inspect/prepare must leave **all** seeded fields, counts and account versions unchanged, with no new import payload. Before refused invalid-row commit capture source rows/IDs, review version and summary/hash; reread afterwards and compare, then skip/commit and reconcile. Two otherwise equal PDFs with different ordinary Balance values must have equal mapped/interpreted fields and identity/fingerprint inputs, excluding allocated row/review IDs. Absence-only output tests are insufficient. |
| CPDF-N7 — **ALREADY SATISFIED in current runbook wording**, Phase 4 preservation | Preceding runbook blob records the fresh NO-GO and corrects “correction supersedes verdict”. Retain that accurate chronology; do not claim a new GO or repeat a review. |

No residual finding is deferred to another work unit. Thread #6 tracks the same
unresolved findings until real evidence exists; accepting this proposal does
not close that tracker or Thread #3. Source inspection confirms R1 at parser
`775–833,846–853`, R2 at `704–713`, R3 at `295–307`, R4 at
`198–205,660–665,701–702`, and N5 at `468–483`. Current Phase 2 tests at
`1083–1269` show N6's missing comparisons. None was runtime re-reviewed here.

**Private sample procedure (P4-A):**

1. Use only the operator-designated two source files, selected locally and
   referred to as SB4/SB18. Do not print filenames, PDF metadata, values or
   exceptions. Keep a private alias-to-file/SHA-256 binding with restricted
   permissions outside Git and HiveMind; no raw-file digest in public evidence.
   Do not move/alter the originals or use production/customer databases.
2. Confirm locally that the one repeated asset is nonfinancial decoration and
   distinguish fixed public bank wording from variable customer fields. Keep
   the actual asset and all customer strings private. Harvest only approved
   nonfinancial profile constants; never account/address/value fingerprints.
3. Establish an independent private row/value/continuation/page oracle from the
   originals in the operator's existing PDF viewer and a separate extraction
   path. Compare every row's date, complete description and signed flow; a
   count-only match or old diagnostic CSV is insufficient. Keep expected and
   actual values in memory or an explicitly protected private manifest, never
   test assertion dumps, tool transcripts, Git or server logs.
4. Build the product's local Release publish output, without deployment or
   repository publication. Feed the original encrypted bytes
   through its unchanged framed stdin protocol to
   `dotnet <published>/argus.Server.dll --parse-statement-pdf`. Confirmed dates
   go in the private stdin options, not command arguments. Capture stdout in
   memory, compare without printing it, inspect stderr without echoing content,
   and emit only alias, page/row/note counts and boolean comparison results.
   Record product assembly/source hashes, duration and bound results.
5. Exercise authenticated HTTP/real-store behavior with **synthetic replicas**
   carrying canaries and the same protection/layout/asset policy. Do not put
   actual customer transactions into the shared isolation database or CI.
   Private-child fidelity and synthetic authenticated integration are distinct
   evidence claims; neither is called a real authenticated browser journey.
6. Release buffers and dispose processes/files after the private run. Do not
   delete prior diagnostic CSVs or the operator's files without permission.
   Existing private artifacts are not trusted product output and are not added
   to the repository. Durable evidence contains structural counts, booleans,
   source/assembly/test hashes, commands with placeholders and evidence level
   only. If comparison/capture cannot avoid disclosure, stop rather than print.

### 10. Verification commands and stop conditions

Run the phase commands above and the **original Plan's full Section 10 battery**:
backend format (both projects), full tests, Release build/publish and actual
vulnerability findings; frontend audit-ci, lint, tests, build and Playwright.
No tests are run against development/production data. Use only the existing
isolated `ARGUS_TEST_DATABASE_URL`; no bootstrap, database creation or secret
output is authorized. Existing runtime/keyboard/concurrency obligations remain.

Stop for failed/unavailable required evidence, mismatched private fields/counts,
unexplained text/image/metadata, ambiguous columns or marker association,
unsupported encryption or failed empty-user check, source-position loss,
positive-fixture regression, canary leakage, changed seeded state, partial
success after a later failure, exhausted limits or need for wider scope. A
missing private oracle/asset confirmation is **UNVERIFIED**, not a pass. Do not
install a new dependency, change error fields, loosen profile rules, increase
limits or use a general renderer to work around a stop.

### 11. Adversarial scenarios

Combine defects, not only isolated probes: conflicting header plus residue
plus Balance sentinel; sub-0.01 header movement with a near-boundary word;
colon-attached metadata plus a valid-looking flow; aligned Summary after a
candidate; off-page role text; legitimate late note beside a footer; real
metadata form with one extra transaction-shaped token; late malformed image
after valid earlier pages; known logo with altered mask or body placement;
empty-owner/nonempty-user encryption; partial password/dictionary data;
wrong-profile geometry; summary-looking merchant text and transaction after
the terminal postamble. Always reject the whole file or preserve the complete
unambiguous source row, with no suppressed residue or durable rejection effect.
Keep existing timeout/cancel/restart/idempotency and concurrency tests intact.

### 12. Risks, recovery and decisions requiring confirmation

- **Explicit approval is required.** It must bind the exact proposed Plan and
  brief amendment, permit empty-user R6 import despite the copy-permission
  flag, and confirm the nonfinancial asset/fixed-wording classification in the
  private template. The user may decline or change those choices. Current
  revision 3 remains active until that decision is recorded; do not infer it
  from this request, a peer delivery or a complete draft.
- This is a finite template contract, not certification of arbitrary Standard
  Bank variants or PDF authenticity. Different logos/templates/addresses,
  rotation, damaged fonts or true text/amount overlap may reject. Unknown
  variants need new evidence/approval, not a wildcard recognizer.
- The extra R6 guard is security-sensitive but bounded to empty-user
  validation, with independent vectors and owner-only negative coverage. If it
  cannot be implemented safely on the pinned APIs, stop for a new decision;
  do not reflect into internals, replace the parser or accept owner access.
- No schema rollback or existing-record rewrite is needed. This proposal does
  not repair earlier omitted/leaked imports. Any real-data repair needs
  separate operator authority. Preserve receipts, source history and the
  current uncommitted tree. A future application rollback must not delete data.
- Latest independent verdict remains request `963d287e-c3e0-4214-ba9d-1aac0750ac9f`,
  NO-GO, immutable SHA-256
  `b10e23a04e326881ef402532c7b6c5ee5090f03f0bd72876c8b4119e64ff3b7b`.
  No new review was requested or commissioned. Proposal approval changes scope,
  not that verdict or the implementation acceptance gate. Publication and
  Threads #4/#5 remain blocked. Programme order remains authoritative on
  Thread #1; Thread #6 is not silently inserted into it.

**Architect self-check:** complete after the finished direct inspection. The
invariant-consistency pass checked that empty-user authentication is distinct
from owner opening; the permission-bit decision does not contradict supporting
the actual files; a fixed outside-table asset replaces a size heuristic; real
first-page metadata does not suppress its table; real gutters preserve existing
flow/Balance meaning; full postambles and continuations have nonconflicting
roles; every phase has named inputs/files/commands; and no new durable/public
contract is needed. Scope/acceptance changes are listed explicitly above.
Implementation, new tests and private **product** acceptance remain **NOT RUN**.
Only planning/operating documents and a temporary redacted inspection utility
outside the repository were authored. No application implementation, source
test edits, new review, bootstrap, commit/push or publication occurred.

## Revision 5 — CSV-only delivery scope (approved 2026-11-06)

**Authority:** The operator explicitly directed this revision in the execution
session that continued Thread #3: complete the statement-import functionality
with `.csv`/ZIP inputs for now, keep the PDF parser in the tree but reject PDF
at the API, and fix PDF later. The operator also directed that Plan 2 be
amended directly, that the PDF code remain dormant, and that the CSV-only
baseline receive one final independent review. This is a semantic scope change
to the earlier approved revisions; it is recorded here and on Thread #3.

### 1. Active scope

Active import formats are unchanged in behaviour, not reduced in quality:

- FNB CSV and FNB ZIP;
- generic mapped CSV with confirmed header, dialect, columns, dates, signs and
  periods;
- durable review, invalid-row skip, duplicate classes and overrides;
- atomic commit, retry receipts, history, statement detail, hide/restore/delete;
- migration, privacy, isolation, bounded transport and all existing CSV
  acceptance criteria.

### 2. Deferred scope

The following are deferred to Thread #6 and are **not** acceptance targets for
this revision:

- Standard Bank PDF import, including the supported text-layer subset;
- proposed revision 4 / C-PDF-2 and the production-template support;
- CPDF-R1–R4, CPDF-N5 and the PDF-specific part of CPDF-N6;
- PDF-specific evidence in original AC1, AC2 and AC8.

The last independent PDF verdict `963d287e-c3e0-4214-ba9d-1aac0750ac9f`
(NO-GO, 4 HIGH, 2 MEDIUM, 1 LOW) remains the PDF baseline and is not
relabelled. It must be resolved in Thread #6 before PDF import is re-enabled.
The production-PDF amendment in `docs/product-brief.md` is deferred, not
approved.

### 3. Product behaviour while deferred

- `POST /v1/finance/imports/inspect` and
  `POST /v1/finance/imports/reviews` reject
  `source_kind = "standard_bank_pdf"` with HTTP 400 and the fixed
  `pdf_import_deferred` error before decoding the file or starting a parser
  child. They create no review, statement, transaction or receipt.
- The Imports UI offers only FNB CSV/ZIP and generic mapped CSV. A selected
  `.pdf` file is rejected with a plain-language message. Existing review or
  history labels for old PDF records may still render.
- The PDF parser, child runner, protocol and parser/process tests stay in the
  repository as dormant implementation for Thread #6. They are unreachable
  from the product endpoints while this revision is active.
- No new PDF dependency, password field, OCR, renderer or external service is
  added.

### 4. Acceptance-criteria adjustment

| Criterion | Active meaning in Revision 5 |
| --- | --- |
| **AC1 — Format fidelity** | FNB synthetic forms and generic dialect/sign choices produce every expected row exactly once. The 4/18-page PDF structure is deferred with PDF. |
| **AC2 — Safe rejection and bounds** | CSV/ZIP/encoding/type/size/row bounds and parser-process cleanup remain active. PDF layout/image/encryption rejection is deferred; a PDF request must reject with `pdf_import_deferred` and zero durable effects. |
| **AC3–AC7, AC9–AC10** | Unchanged, except evidence that named PDF source content is not part of the CSV-only acceptance claim. |
| **AC8 — Isolation and privacy** | Active for every route and for the PDF deferral response. Dormant PDF parser canary evidence is deferred with PDF. |
| **CPDF-N6** | The general Phase 2 gaps are addressed under this revision: prepared source rows/version/summary identity cannot be rewritten by a patch, and preparing a new import does not mutate seeded finance data. Balance-differential behavior is PDF-specific and deferred. |

### 5. Verification

Run the full Section 10 backend and frontend battery, including the dormant
parser/process tests, plus the new CSV-only contract regressions. Three reviews
have now occurred: `dac8ef28-4b79-46e3-ac15-9cd84b269cbe` returned NO-GO with
two active CSV findings and confirmed PDF was unreachable; re-review
`6a10e904-cc33-4e4b-a072-6f134c3e2c7d` confirmed both original HIGH fixes and
found the admission cancellation/lifetime defect (CSV-R3) plus the evidence
wording gap (CSV-N1-R); both were corrected. Final review
`5524f1dd-f982-49af-a6fd-5a863fe95987` returned **GO with zero findings** on the
exact baseline recorded at the top of this Plan. The CSV-only implementation is
review-accepted. No further review is requested. Publication has since occurred
through PR #1 at 78a43e4; Thread #4 is operator-accepted and Thread #5 is
unblocked for Plan 4 planning. Deployment remains a separate operator decision.

## Revision 6 — L1 inspection, authority and recovery

- **Approval:** operator instruction “approve, send to execution slot” explicitly approved immutable contract `27141eaf-98da-4ba6-9378-3d84ce78c136`; Thread #4 Decision `060f596f-31c3-4632-a1b3-a91e36879448` records approval. This is a bounded correction, not a new programme unit or reopening of Thread #3. No additional review is authorized or claimed.
- **Inspection:** finished Explorer result `ses_e49674be1ffen0rc5yJ0dK6Uxh` and direct reads of `StatementImportStore.cs:2139–2523,3318–3341`, `StatementImportEndpoints.cs:291–315,513–528`, `ImportFaultInjector.cs`, tests and the runbook establish the reachable commit/tag wait, available persisted expiry, tentative writes, receipt-first paths and existing test injection. The prior explicit expiry test rejects an already-expired review; it does not cover the later wait. Evidence is source inspection only.
- **Self-review/invariant consistency:** complete after inspection. New-effect expiry refusal and receipt-first replay are separate branches, so expiry cannot invalidate historical success. Both guards use the stored absolute deadline; no admission extension, second policy authority, lock-order change or new state is invented. The final check follows all tentative writes and the existing test seam; rollback preserves atomicity. Named files and existing test seams supply the required inputs and test feasibility.
- **Compatibility/recovery:** no migration or data backfill is needed. An expired attempt rolls back; an uncertain commit is still resolved through its original receipt identity. Do not delete receipts or extend expired deadlines to recover an operation. A fresh upload/review is required for new work after expiry. Removing the guards reopens L1; do not call that a safe policy-preserving rollback. Valuable-data recovery and existing service changes still require separate operator authorization.

## Revision 7 — Suggestion sources and exact-date near duplicates

- **Approval/provenance:** Operator instruction in the PR #8 artifact-revision request, 2026-11-09; Thread #3 Decision `a92fc81b-879b-4844-8543-1b25a51abda5`. This is a semantic correction of the frozen suggestion and near-duplicate clauses, requested after Specialist review `0b3f927b` returned NO-GO against the shipped behavior. No new Specialist plan review is commissioned or claimed. The PR #8 implementation remains subject to its own fresh acceptance review.
- **Finished inspection:** direct reads of `backend/Finance/TagSuggestions.cs`, `backend/Finance/MerchantSuggestions.cs`, `backend/Finance/MerchantKey.cs`, `backend/Data/SuggestionLoader.cs`, `backend/Data/StatementImportStore.cs` (near-duplicate scan and suggestion composition), `backend/Data/Db.cs` (migration `0008`), `frontend/src/app/imports/review/[id]/review-workspace.tsx` (unselected suggestion chips), and `docs/statement-import-runbook.md`. Evidence is source inspection, not execution; this revision does not accept the PR #8 code.
- **Suggestion composition:** Replace Section 6.1 step 7 and AC6's source clause. Import-review tag suggestions may be drawn from:
  - explicit per-customer aliases: normalized alias text matched as a token sequence against the normalized description, resolving to one active owned tag and an optional active owned non-system category; alias ownership and lifecycle are defined by Plan 1 revision 2;
  - the customer's own history: committed transactions grouped by a generic merchant key derived from the normalized description. The key is the first two significant lowercased tokens; one-character tokens, all-numeric tokens and English three-letter month names are excluded, and values are joined with one space. Only active owned tags and the most frequent active non-system category for that key are eligible; no other customer's history is read;
  - token matching: active owned tag names as contiguous token sequences; a statement token may match a longer tag token only when it is a truncated prefix of at least four characters and the tag token is at least five characters; exact tokens score before truncated-prefix matches; a longer statement token is never shortened to a tag token.
  Compose aliases first, then history by descending count, then token matches; deduplicate; exclude already assigned tags; return at most 20 deterministic suggestions with `has_more_suggestions`. Suggestions start unselected. Import review accepts tag IDs only; the category stays Uncategorised at commit. `suggested_category` is a transaction-detail field owned by Plan 3 revision 4 and is never applied by import review.
- **Near duplicate:** Replace Section 4.3's near-duplicate clause. Same account, exact transaction date and exact signed amount, but not an exact identity match. Flag when normalized descriptions have token-set Jaccard similarity at least 0.5; also flag a conflicting stable-ID match. Empty descriptions match only each other for this warning. Apply the same rule within the incoming file and against existing rows, including hidden rows. Show up to five strongest candidates per row, sorted deterministically, and disclose additional candidates. A warning defaults to included and never independently suppresses or changes a row.
- **Exact scope:** `backend/Data/Db.cs` (migration `0008_suggestion_learning`, shared with Plan 1 revision 2); `backend/Finance/MerchantKey.cs`, `backend/Finance/MerchantSuggestions.cs`, `backend/Finance/TagSuggestions.cs`; `backend/Data/SuggestionLoader.cs`; `backend/Data/StatementImportStore.cs`; `backend/Http/StatementImportEndpoints.cs` only where the review projection changes; backend import contract/query tests; `frontend/src/app/imports/review/[id]/review-workspace.tsx` and its tests; `docs/statement-import-runbook.md`. No parser, commit, receipt, identity, occurrence, statement-lifecycle, PDF or privacy contract changes.
- **Compatibility/limits:** Exact identity, statement fingerprint, occurrence matching, commit atomicity, receipt replay, hidden-record duplicate protection, CSV-only delivery and PDF deferral are unchanged. Aliases and history are suggestion inputs only; they never write assignments. No global merchant dictionary, external service, cross-customer learning or automatic category assignment is authorized. The finance IP bound is governed by Plan 1 revision 2.
- **Verification for PR #8 acceptance:** backend format/test/build and frontend format/lint/test/build/e2e on the final PR #8 head, plus focused tests proving alias/history/prefix suggestion composition, active/archived lifecycle behavior, and the exact-date near-duplicate boundary. This revision itself changes no application code and is not evidence that the PR passes.

## Direct execution record — revision 6, Phase 5

- **2026-11-08**, session `ses_e49b69c71ffeKYFvPtoENBQBHU`: added fresh PostgreSQL-clock expiry checks after tag validation and immediately before commit, plus a database-clock early check. Existing receipt-first replay is unchanged. The new named test seam uses the existing no-op production injector; no request or environment value enables it.
- **Evidence:** a real tag-lock wait crossed the deadline and returned `201` before the fix instead of the required `410`; it passes after correction. Four seam cases also failed before correction. The post-fix suite covers tagged, untagged and fully suppressed attempts at both applicable boundaries, unchanged review/source/selection data after rollback, and receipt replay after the original deadline through a new test host. The focused import classes passed **69/69**; both backend format checks and Release build passed.
- **Combined battery:** two full backend runs each passed **307/308** but failed different pre-existing transaction tests: a 50,000-row fixture timed out, then an existing expiry-wait test did not observe its deadline within four seconds. The first test passed in isolation; all three expiry-wait variants initially passed in isolation. A third monitored full run and a fourth repeat full run passed **308/308** (2026-11-08); the repeat frontend battery passed lint/build, **249/249** Vitest and **17/17** Playwright smoke tests. A later isolated expiry-wait run passed two of three variants and timed out on the third. A bounded probe observed the host and database wall clocks moving backward by about 1.37 seconds while the host monotonic clock advanced 1.0–1.2 seconds. This could explain the expiry test's short wait but does not prove its cause or explain the 50,000-row fixture timeout. No new review, Thread #3 status change, publication, deployment or programme progression occurred.
- **Verification at authoring:** implementation/build/tests NOT RUN; only this planning file and Plan 3 were amended by Architect. Historical revision-5 GO remains bound to its baseline. The whole-tree NO-GO against `c76c78f` is not acceptance of this correction. Exact amended blobs and operational execution routing are recorded on Thread #4, not in Plan meaning.
