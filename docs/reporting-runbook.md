# Reporting runbook

The Overview answers what happened to imported money over one selected period.
It is a live view over committed transactions, not a stored monthly report. This
runbook covers the period rules, salary-cycle reporting dates, reconciliation
and coverage definitions, the HTTP surface, migration/role prerequisites, and
recovery boundaries.

## Default and alternatives

Salary-cycle reporting is the default. With at least one confirmed salary-cycle
start, the default period runs from the latest confirmed date through today
(`Salary cycle to date`, an open cycle). With no confirmed start, Overview shows
setup with direct **Calendar month to date** and **Custom dates** alternatives;
it never invents a boundary or silently opens a calendar report.

The period modes are:

| Mode                     | Request                                                      | Comparison                                                                                                |
| ------------------------ | ------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------- |
| Salary cycle             | latest or an explicit `cycle_start_id`                       | previous actual full cycle, or equal elapsed days capped at the prior cycle end for the latest open cycle |
| Calendar month to date   | current month start through today                            | same day-of-month range in the prior month, capped at its month end                                       |
| Calendar previous month  | previous full month                                          | the month before it                                                                                       |
| Calendar last 3/6 months | first day two/five months before today's month through today | immediately preceding inclusive range of equal day length                                                 |
| Calendar month           | an explicit `YYYY-MM`                                        | previous full calendar month                                                                              |
| Custom                   | explicit inclusive `start_date`/`end_date` only              | immediately preceding inclusive range of equal day length                                                 |

There is no rolling N-day, start-plus-duration, fixed-payday, weekend or
projected-next-salary control. Future custom/full-month ranges are accepted
with a visible partial/future note.

### Salary-cycle dates

- A start is selected explicitly from an owned, visible, positive imported
  transaction dated today or earlier. Its category (including an unconfirmed
  category) and tags do not matter and are never changed.
- One workspace-wide sequence applies independently of account, category and
  tag filters. A later confirmed start closes the previous cycle on the day
  before it.
- Multiple eligible receipts on the same calendar date share one boundary. A
  second receipt on that date reports `already_confirmed` and changes no
  source, boundary, revision or cycle.
- The confirmed boundary stores only the calendar date, owner and timestamp.
  It retains no amount, description, account label, transaction payload,
  source locator or transaction foreign key.
- A confirmed date survives source hiding or deletion. Removing the date and
  selecting another receipt is an explicit correction; the source is never
  edited. Removing the earliest date can make earlier transactions unavailable
  in salary mode; Calendar and Custom remain available. With every date
  removed, setup returns.
- Confirmation and removal use expected source/sequence revisions. A removal
  confirmation is bound to the boundary ID and sequence revision captured by
  its preview; it is never re-resolved by date or rebound to a refreshed list
  revision. A stale preview returns `cycle_sequence_changed`; it never
  overwrites a newer sequence. A failed or uncertain response retires the
  preview, reloads the sequence and requires a fresh impact preview. An
  uncertain response is resolved by reading the sequence, not by automatically
  repeating the removal. Revisions travel as decimal strings end to end.

### Salary-cycle trends

When salary-cycle mode is active, the 2/3/6 selector on the Category spending
trend and the main Trend card means the last 2/3/6 confirmed cycles ending at the
selected cycle. Each cycle runs from its start to the day before the next start,
or through today for the open cycle. The money cards, coverage and
current-versus-prior comparison remain tied to the one selected cycle.

### Change in category spending

Open **Category spending as a table** to see **Change vs previous period**.
Every displayed period has its own change against the period immediately
before it. An older displayed period still compares with its full
predecessor even when that predecessor is outside the columns on screen, and
one period's missing coverage does not hide another period's change.
In Calendar and Custom modes, the chart ends at the current calendar month
using reporting today in Africa/Johannesburg, even if the Overview is set to
a different date range. Only the account filter applies to the chart.
Choosing 2, 3, or 6 periods changes the visible history, not the comparison
for a period that remains visible.

- Salary cycles use the confirmed start and end dates of each cycle. A closed
  cycle compares the whole previous cycle. The open cycle compares its
  confirmed start through today with the whole previous cycle, and its
  heading says so. Calendar months still use whole months for completed
  months.
- A current calendar month compares the same number of elapsed days in the
  previous month, counting the first and last dates. If the previous month is
  shorter, its slice ends on its last day. There is no estimate or adjustment
  for different period lengths.
- Each column heading shows the exact previous dates, and a period-to-date
  column also shows its current dates. Open **Compared amounts** under a first
  visible or period-to-date amount to see the actual current and previous ZAR
  amounts used for that change. The existing period amount, total and
  transaction link still use their original displayed ranges. For example, a
  prior month can show R1,000.00 even when only R100.00 falls in its matched
  slice.
- A period without a defined predecessor shows **No previous confirmed
  cycle** or **No prior period**, and a dash under each amount.
  There is no invented range.
- **Incomplete coverage** overrides the percentage for only that period if
  either compared slice has a statement-coverage gap. Its heading names
  whether the current or previous period is missing coverage. A gap outside
  that period's matched slice does not invalidate it, and other periods keep
  their changes. Amounts are recorded values, not proof that missing days had
  no spending.
- **No included prior transactions** means both slices are
  covered but the previous slice has no visible included rows. A positive or
  zero-valued included transaction is enough to establish prior data; it does
  not count towards a category's Money out.
- When the comparison is available, zero previous spending and positive
  current spending shows **New spending**; two recorded zeros show **No
  spending**. Otherwise the backend rounds the change to one decimal, with
  halfway values rounded away from zero. Green ↑ means spending increased
  and red ↓ means it decreased; neither colour is financial advice. Equal
  positive amounts show `0.0%` with no arrow. A drop from positive spending
  to zero shows `−100.0%`. A small change that rounds to `0.0%` keeps its
  true direction. There is no arrow or percentage for any of the text-only
  states above.

## Money, categories and tags

- **Money in** is the gross positive visible included amount. **Money out** is
  the gross magnitude of negative visible included amounts. **Net** is Money in
  minus Money out. A negative Net is neutral information, not a failure.
- Included means effective-visible and not `Transfer` or `Excluded`.
  Income-category outflows and Expense-category refunds keep their sign. Zero
  rows count but do not change totals.
- Hidden transactions are excluded from financial totals, category and tag
  ranks, and included-transaction comparisons. Statement coverage follows
  the separate rule below. The hidden count and gross directional values
  are labelled diagnostics with a restore link. Parent hidden rows are
  restored in Imports; individually hidden rows can be restored from the
  transaction list.
- Category ranks are additive and reconcile to the direction headline,
  including Uncategorised. Every category with activity is listed individually,
  ordered from largest to smallest directional amount. The API retains the
  top-five/Other rank projection for compatibility, but the Overview uses the
  complete ordered category list rather than hiding a remainder in an Other
  group. Each row has its own amount and transaction link; this is not a
  combined-tag filter.
- Merchant-tag ranks overlap because one transaction may carry several tags.
  They are never summed into a headline. Every tag is listed individually in
  descending directional amount order; there is no grouped Other-tags amount or
  combined-tag filter. Untagged remains an explicit separate row.

Amounts travel as invariant two-decimal strings. The backend uses PostgreSQL
`numeric` and C# `decimal`; the frontend uses string/BigInt cents for display
and never routes money through `Number`, `parseFloat` or locale parsing.

## Statement coverage

Coverage is the union of committed, non-hidden `statement_imports.start_date`
to `end_date` intervals for the selected account or all accounts. It is
independent of category/tag filters and individual child hide flags. The report
names uncovered inclusive intervals and pages them; a covered empty bin may
truthfully show zero recorded transactions, while an uncovered bin shows any
imported value with an `uncovered` label and does not imply zero spending.

## HTTP surface and errors

| Method | Path                                | Purpose                                                           |
| ------ | ----------------------------------- | ----------------------------------------------------------------- |
| POST   | `/v1/finance/reports/query`         | One repeatable-read report snapshot                               |
| GET    | `/v1/finance/salary-cycles`         | Sequence revision and confirmed starts, newest first, 25 per page |
| POST   | `/v1/finance/salary-cycles/preview` | Confirm/remove impact preview; never writes                       |
| POST   | `/v1/finance/salary-cycles`         | Confirm an eligible receipt date                                  |
| DELETE | `/v1/finance/salary-cycles/{id}`    | Remove one confirmed boundary                                     |

Report bodies are capped at 16 KiB and strictly allowlisted. Report reads have a
120-request/customer/15-minute in-process bucket; salary previews/mutations have 30. These are per-process abuse controls, not distributed correctness locks.
All report and salary responses are `no-store`, including guard and parser
failures.

The error union is `invalid_request` (400), `not_found` (404),
`request_too_large` (413), `unauthorized` (401), origin rejection (403),
`rate_limited` (429), `receipt_ineligible` (422),
`version_conflict`/`cycle_sequence_changed`/`report_busy` (409), and sanitized
`internal_server_error` (500) or unavailable behaviour. No partial response is
rendered as a successful zero report.

## Prerequisites and migration 0012

Migration `0012_salary_cycles` adds `salary_cycle_state` and
`salary_cycle_starts`, their uniqueness/check constraints and ownership FKs,
forced row-level security, and least-privilege runtime grants. The latest model
therefore has 15 protected tables, 30 RLS policies, 19 non-ledger tables and 22
base tables. Prefix 11 keeps its frozen 13-table isolation contract; migration
0011 is never rewritten to reference the salary tables.

Normal startup does **not** migrate. Apply migrations through the existing
maintenance path with the migrator login acting as owner:

```bash
cd backend
dotnet run -- migrate
```

The runtime login receives `SELECT, INSERT` on both salary tables,
`UPDATE (revision, updated_at)` on the state row, and `DELETE` only on starts.
It cannot delete the sequence-state row or update a confirmed date. The normal
host refuses unknown history, schema drift, missing forced RLS or a privileged
runtime login.

Role/schema prerequisites are those in
[`customer-isolation-runbook.md`](customer-isolation-runbook.md). A missing
`Africa/Johannesburg` timezone makes reporting fail explicitly; it never
silently substitutes UTC or browser-local dates. Transaction dates are imported
calendar dates and are never timezone-shifted.

## Live-view limits

One report response is one repeatable-read financial snapshot: current/prior
totals, category and tag ranks, trend buckets, latest category changes,
coverage and detail rows come from the same admitted context, with a final
authority check before commit.
Separate navigation requests are new live views. A concurrent import,
classification, category/tag deletion, statement hide/delete or salary change
can therefore change a later drill-down total; the UI offers refresh rather than
claiming permanent reconciliation. Client cancellation ends the request through
the normal host cancellation path; there is no background reporting job or
materialized report cache.

## Recovery

- Before a salary write commits, rollback has no lasting effect. After a
  successful write, refresh the owned sequence and report; after an uncertain
  write, read the sequence before retrying.
- After migration 0012, the pre-Plan prefix-11 binary/catalog is **not** a safe
  rollback target. Its history/schema validation rejects the retained salary
  tables and prefix-12 history. Recovery must be a forward fix, or a named
  artifact that retains prefix-12 migration/catalog/RLS support while reversing
  only feature behaviour. Do not drop the salary tables, run a destructive
  `Down`, grant the runtime privileged access, or weaken validation to make an
  old binary start.
- A corrected/removed salary date never deletes or edits its source
  transaction. Restoring or hiding the source does not remove the date.
- A report timeout/overflow/provider failure rolls back and returns a retryable
  busy or sanitized failure. It never returns a truncated or partial total.
- The category-spending change column adds no stored report data or migration.
  Older frontends ignore the new fields. A new frontend receiving an older
  response displays **No comparison available** instead of computing from
  the displayed totals. A fresh request to a matching backend recomputes the
  comparison; no repair or backfill is needed.

## Verification

The reporting tests are selected with:

```bash
# Repository root; run only against the dedicated local argus_test database
node scripts/run-backend-tests.mjs --filter '(FullyQualifiedName~Reporting|FullyQualifiedName~SalaryCycleContractTests|FullyQualifiedName~TransactionQueryTests|FullyQualifiedName~PersistenceBoundaryTests)&Category!=Performance'
node scripts/run-backend-tests.mjs --filter 'FullyQualifiedName~ReportingPerformanceTests&Category=Performance'

# Backend; database-free period tests
(cd backend && dotnet test tests/argus.Server.Tests -warnaserror --filter 'FullyQualifiedName~ReportingPeriodTests')

# Frontend; focused Overview and transaction transport/state tests
(cd frontend && npm run test -- tests/app/home tests/lib/reporting-state.test.ts tests/lib/money-format.test.ts tests/lib/server/reporting-actions.test.ts tests/app/transactions)
(cd frontend && npm run test:e2e:finance -- tests/e2e/finance/workspace.spec.ts)
```

`ReportingPerformanceTests` (Category=Performance) seeds 20,000 synthetic rows,
mixed tag fan-out and confirmed salary dates, then times 30 ready reports across
salary-cycle, calendar and custom periods. The measured target is p95 under two
seconds; record the printed hardware, PostgreSQL and runtime versions with the
result. Guarded backend tests against synthetic PostgreSQL data verify the
date, Money out, coverage and snapshot rules. The finance browser journey
starts a synthetic fixture backend and verifies the presentation, including
exact date and amount disclosures, links, keyboard and touch, and contrast
in both themes. It does not verify backend reconciliation, real customer
data, or assistive-technology use.
