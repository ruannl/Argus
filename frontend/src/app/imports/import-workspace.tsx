"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useRef, useState, useTransition } from "react";

import SectionTabs from "@/components/section-tabs";
import {
  EMPTY_STATE_CLASS,
  STATUS_PILL_CLASS,
  TABLE_CLASS,
  TABLE_HEAD_CLASS,
  TABLE_ROW_CLASS,
  TABLE_WRAPPER_CLASS,
  TD_CLASS,
  TH_CLASS,
} from "@/components/table-styles";
import { createAccountAction } from "@/lib/server/finance-actions";
import type {
  FinancialAccount,
  ImportInspectResult,
  ImportOpenReview,
  ImportStatementSummary,
} from "@/lib/server/backend-client";
import { accountBank, statementBank } from "./import-institution";
import { buildUploadOptions, type SourceKind } from "./import-options";

type ImportWorkspaceProps = {
  accounts: FinancialAccount[];
  reviews: ImportOpenReview[];
  history: ImportStatementSummary[];
  loadError: string | null;
  nextCursor?: string | null;
  initialTab: string;
};

type Message = { tone: "status" | "error"; text: string };

const ERROR_MESSAGES: Record<string, string> = {
  invalid_request: "That request could not be accepted.",
  file_too_large: "That statement is larger than the 10 MB limit.",
  unsupported_format:
    "That file type is not supported. Use an FNB CSV/ZIP, a Standard Bank PDF, or a mapped CSV.",
  unsupported_encoding: "That CSV is not valid UTF-8. Re-export it as UTF-8.",
  unsafe_archive: "That archive is unsafe or does not contain exactly one root-level CSV.",
  encrypted_pdf: "That PDF requires a password or uses unsupported protection.",
  text_pdf_required:
    "That PDF is not a supported text-based statement. Scanned or image-only PDFs, and unrecognised images, are outside the MVP.",
  unsupported_layout: "That statement layout is not recognised.",
  processing_limit: "That statement exceeds a processing limit.",
  format_confirmation_required: "Confirm the mapping and dates before continuing.",
  pdf_import_deferred:
    "PDF import is not available in this build. Use an FNB CSV/ZIP or a mapped CSV.",
  import_busy: "Another statement is being processed. Try again shortly.",
  not_found: "That item no longer exists.",
  review_expired: "That review has expired.",
  review_stale: "The account changed while you reviewed. Refresh the review.",
  version_conflict: "The review changed in another tab.",
  statement_repeat: "This statement was already imported.",
  account_archived: "That financial account is archived. Restore it in Settings.",
  invalid_rows: "Invalid rows must be skipped before committing.",
  duplicate_decision_required: "Resolve every repeated-row group before committing.",
  no_included_rows: "Include at least one valid row before committing.",
  tag_unavailable: "A selected tag is unavailable.",
  rate_limited: "Too many requests. Try again in a few minutes.",
  forbidden: "The upload origin was rejected. Refresh the page and try again.",
  unauthorized: "Your session has expired. Please sign in again.",
  unavailable: "Something went wrong. Please try again later.",
};

function messageFor(error: string): string {
  return ERROR_MESSAGES[error] ?? ERROR_MESSAGES.unavailable;
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KiB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MiB`;
}

function fileTypeLabel(file: File): string {
  const extension = file.name.split(".").pop()?.toLowerCase();
  if (extension === "zip") return "ZIP";
  if (extension === "pdf") return "PDF";
  return "CSV";
}

function isPdfFile(file: File): boolean {
  const extension = file.name.split(".").pop()?.toLowerCase();
  return extension === "pdf" || file.type === "application/pdf";
}

function sourceKindLabel(kind: string): string {
  if (kind === "standard_bank_pdf") return "Standard Bank PDF";
  if (kind === "generic_csv") return "Generic CSV";
  return "FNB CSV or ZIP";
}

export default function ImportWorkspace({
  accounts,
  reviews,
  history,
  loadError,
  nextCursor,
  initialTab,
}: ImportWorkspaceProps) {
  const router = useRouter();
  const fileInput = useRef<HTMLInputElement>(null);
  const abortRef = useRef<AbortController | null>(null);
  const activeAccounts = accounts.filter((account) => !account.is_archived);
  const archivedAccounts = accounts.filter((account) => account.is_archived);

  const [accountId, setAccountId] = useState(activeAccounts[0]?.id ?? "");
  const [sourceKind, setSourceKind] = useState<SourceKind>("fnb");
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [inspect, setInspect] = useState<ImportInspectResult | null>(null);
  const [mapping, setMapping] = useState<Record<string, string>>({});
  const [mappingDirty, setMappingDirty] = useState(false);
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [century, setCentury] = useState("2000");
  const [busy, setBusy] = useState<"idle" | "inspecting" | "preparing">("idle");
  const [message, setMessage] = useState<Message | null>(null);
  const [mismatchAcknowledged, setMismatchAcknowledged] = useState(false);
  const [pending, startTransition] = useTransition();

  function resetInspection() {
    setInspect(null);
    setMapping({});
    setStartDate("");
    setEndDate("");
    setMismatchAcknowledged(false);
  }

  function chooseFile(file: File | null) {
    if (file && isPdfFile(file) && sourceKind !== "standard_bank_pdf") {
      setSourceKind("standard_bank_pdf");
      setCentury("2000");
    }
    setSelectedFile(file);
    resetInspection();
    setMessage(null);
  }

  async function sendUpload(mode: "inspect" | "prepare") {
    if (!selectedFile || !accountId) return null;
    const options = buildUploadOptions(
      sourceKind,
      accountId,
      startDate,
      endDate,
      true,
      mapping,
      mode,
      century,
    );

    const form = new FormData();
    form.append("file", selectedFile, "statement.bin");
    form.append("options", JSON.stringify(options));
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    const response = await fetch(`/imports/upload?mode=${mode}`, {
      method: "POST",
      body: form,
      signal: controller.signal,
    });
    const body: unknown = await response.json().catch(() => null);
    if (!response.ok) {
      const error =
        typeof body === "object" && body !== null && "error" in body
          ? String((body as { error: unknown }).error)
          : "unavailable";
      setMessage({ tone: "error", text: messageFor(error) });
      return null;
    }
    return body;
  }

  async function handleInspect() {
    if (!selectedFile || !accountId) {
      setMessage({ tone: "error", text: "Choose an account and a statement file first." });
      return;
    }
    setMismatchAcknowledged(false);
    setBusy("inspecting");
    setMessage(null);
    try {
      const body = (await sendUpload("inspect")) as ImportInspectResult | null;
      if (!body) return;
      setInspect(body);
      setMapping(body.mapping);
      setMappingDirty(false);
      setStartDate(body.proposed_start_date ?? "");
      setEndDate(body.proposed_end_date ?? "");
      setMessage({
        tone: "status",
        text: "Inspection complete. Confirm the mapping and statement dates.",
      });
    } catch (error) {
      if ((error as Error).name !== "AbortError") {
        setMessage({ tone: "error", text: messageFor("unavailable") });
      }
    } finally {
      setBusy("idle");
    }
  }

  async function handlePrepare() {
    if (!inspect || !selectedFile) return;
    if (!startDate || !endDate) {
      setMessage({ tone: "error", text: "Enter the statement start and end dates." });
      return;
    }
    if (startDate > endDate) {
      setMessage({ tone: "error", text: "The start date must not be after the end date." });
      return;
    }
    if (mappingDirty) {
      setMessage({
        tone: "error",
        text: "The mapping changed. Inspect the statement again before preparing the review.",
      });
      return;
    }
    setBusy("preparing");
    setMessage(null);
    try {
      const body = (await sendUpload("prepare")) as { summary?: { id?: string } } | null;
      const reviewId = body?.summary?.id;
      if (!reviewId) {
        setMessage({ tone: "error", text: messageFor("unavailable") });
        return;
      }
      router.push(`/imports/review/${reviewId}`);
    } catch (error) {
      if ((error as Error).name !== "AbortError") {
        setMessage({ tone: "error", text: messageFor("unavailable") });
      }
    } finally {
      setBusy("idle");
    }
  }

  function cancelUpload() {
    abortRef.current?.abort();
    setBusy("idle");
    setMessage({ tone: "status", text: "Upload cancelled. Nothing was imported." });
  }

  function createAccount(formData: FormData) {
    startTransition(async () => {
      const result = await createAccountAction(formData);
      setMessage({ tone: result.ok ? "status" : "error", text: result.message });
      if (result.ok) {
        router.refresh();
      }
    });
  }

  function updateMapping(name: string, value: string) {
    setMapping((current) => ({ ...current, [name]: value }));
    setMappingDirty(true);
  }

  const ready = Boolean(accountId && selectedFile && inspect?.can_prepare);
  const previousImports = inspect?.previous_imports;
  const unresolvedDates = Boolean(inspect?.preview.some((row) => !row.transaction_date));
  const amountMode = mapping.amount_mode ?? "signed";
  const working = busy !== "idle" || pending;
  const selectedAccount = activeAccounts.find((account) => account.id === accountId) ?? null;
  const detectedBank = inspect ? statementBank(inspect.detected_source_kind) : null;
  const selectedAccountBank = accountBank(selectedAccount?.institution);
  const accountInstitutionMismatch =
    inspect !== null &&
    detectedBank !== null &&
    selectedAccountBank !== null &&
    selectedAccountBank !== detectedBank;
  const canPrepare = ready && (!accountInstitutionMismatch || mismatchAcknowledged);

  return (
    <div className="mt-8">
      {loadError ? (
        <p className="rounded-2xl border border-warning-line bg-warning-soft p-4 text-sm font-semibold text-warning-ink">
          {loadError}
        </p>
      ) : null}

      <SectionTabs
        clearParams={["cursor"]}
        defaultKey="new"
        initialTab={initialTab}
        key={initialTab}
        label="Import sections"
        tabs={[
          {
            key: "new",
            label: "New import",
            panel: (
              <>
                {activeAccounts.length === 0 ? (
                  <section className="finance-unboxed">
                    <h2 className="text-2xl font-semibold tracking-[-0.03em] text-ink">
                      Add a financial account first
                    </h2>
                    <p className="mt-3 max-w-2xl text-base leading-7 text-muted">
                      Imports need an active ZAR account. Create one here, or restore an archived
                      account in Settings.
                    </p>
                    <form action={createAccount} className="mt-5 grid gap-3 sm:max-w-xl">
                      <label className="grid gap-2 text-sm font-semibold text-ink">
                        Account name
                        <input
                          className="rounded-xl border border-line bg-page px-3 py-2 text-base font-normal text-ink"
                          name="display_name"
                          required
                        />
                      </label>
                      <label className="grid gap-2 text-sm font-semibold text-ink">
                        Institution (optional)
                        <input
                          className="rounded-xl border border-line bg-page px-3 py-2 text-base font-normal text-ink"
                          name="institution"
                        />
                      </label>
                      <button
                        className="primary-link inline-flex w-fit items-center justify-center rounded-full px-5 py-3 text-sm font-bold"
                        disabled={pending}
                        type="submit"
                      >
                        {pending ? "Creating…" : "Create account"}
                      </button>
                    </form>
                    {archivedAccounts.length > 0 ? (
                      <p className="mt-4 text-sm leading-6 text-muted">
                        Archived accounts keep their history. Restore one in{" "}
                        <Link
                          className="font-semibold text-accent-text underline underline-offset-4"
                          href="/settings"
                        >
                          Settings
                        </Link>
                        .
                      </p>
                    ) : null}
                  </section>
                ) : (
                  <section className="finance-unboxed">
                    <div className="flex flex-wrap items-start justify-between gap-4">
                      <div>
                        <p className="text-xs font-semibold tracking-[0.14em] text-accent-text uppercase">
                          Step 1
                        </p>
                        <h2 className="mt-2 text-2xl font-semibold tracking-[-0.03em] text-ink">
                          Choose a statement
                        </h2>
                        <p className="mt-2 max-w-2xl text-sm leading-6 text-muted">
                          Files are processed in memory. Only sanitized, reviewed rows are stored.
                          The original filename is not displayed or retained.
                        </p>
                      </div>
                      <span className="rounded-full border border-line px-3 py-1 text-xs font-semibold text-muted">
                        ZAR · 10 MB max
                      </span>
                    </div>

                    <div className="mt-6 grid gap-4 sm:grid-cols-2">
                      <label className="grid gap-2 text-sm font-semibold text-ink">
                        Financial account
                        <select
                          className="rounded-xl border border-line bg-page px-3 py-2 text-base font-normal text-ink"
                          onChange={(event) => {
                            setAccountId(event.target.value);
                            resetInspection();
                          }}
                          value={accountId}
                        >
                          {activeAccounts.map((account) => (
                            <option key={account.id} value={account.id}>
                              {account.display_name}
                              {account.institution ? ` — ${account.institution}` : ""}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label className="grid gap-2 text-sm font-semibold text-ink">
                        Statement format
                        <select
                          className="rounded-xl border border-line bg-page px-3 py-2 text-base font-normal text-ink"
                          onChange={(event) => {
                            const next = event.target.value as SourceKind;
                            setSourceKind(next);
                            setCentury(next === "standard_bank_pdf" ? "2000" : "");
                            resetInspection();
                          }}
                          value={sourceKind}
                        >
                          <option value="fnb">FNB CSV or ZIP</option>
                          <option value="standard_bank_pdf">Standard Bank PDF</option>
                          <option value="generic_csv">Generic CSV with mapping</option>
                        </select>
                      </label>
                    </div>

                    <div className="mt-5 rounded-2xl border border-dashed border-line bg-page p-4">
                      <div className="grid gap-2">
                        <span className="text-sm font-semibold text-ink" id="statement-file-label">
                          Statement file
                        </span>
                        <input
                          accept=".csv,.zip,.pdf,text/csv,application/zip,application/pdf"
                          aria-labelledby="statement-file-label"
                          className="sr-only"
                          onChange={(event) => chooseFile(event.target.files?.[0] ?? null)}
                          ref={fileInput}
                          type="file"
                        />
                        <button
                          className="w-fit rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
                          onClick={() => fileInput.current?.click()}
                          type="button"
                        >
                          Choose statement file
                        </button>
                      </div>
                      <p className="mt-3 text-sm text-muted" role="status">
                        {selectedFile
                          ? `Selected ${fileTypeLabel(selectedFile)} statement · ${formatBytes(selectedFile.size)}`
                          : "No statement selected."}
                      </p>
                      <div className="mt-4 flex flex-wrap gap-2">
                        <button
                          className="primary-link inline-flex items-center justify-center rounded-full px-5 py-3 text-sm font-bold disabled:opacity-60"
                          disabled={!selectedFile || !accountId || working}
                          onClick={handleInspect}
                          type="button"
                        >
                          {busy === "inspecting" ? "Inspecting…" : "Inspect statement"}
                        </button>
                        {working ? (
                          <button
                            className="inline-flex items-center justify-center rounded-full border border-line px-5 py-3 text-sm font-bold text-ink"
                            onClick={cancelUpload}
                            type="button"
                          >
                            Cancel
                          </button>
                        ) : null}
                      </div>
                    </div>

                    {sourceKind === "standard_bank_pdf" ? (
                      <div className="mt-4 grid max-w-xs gap-2 text-sm font-semibold text-ink">
                        <label htmlFor="pdf-century">Two-digit-year century</label>
                        <input
                          className="rounded-xl border border-line bg-page px-3 py-2 text-base font-normal text-ink"
                          id="pdf-century"
                          inputMode="numeric"
                          onChange={(event) => {
                            setCentury(event.target.value);
                            setMessage(null);
                            resetInspection();
                          }}
                          placeholder="e.g. 2000"
                          value={century}
                        />
                        <span className="text-xs font-normal text-muted">
                          Defaults to 2000. Use 1900 for older statements. Changing it clears the
                          preview, so inspect again.
                        </span>
                      </div>
                    ) : null}

                    {message ? (
                      <p
                        className={
                          message.tone === "error"
                            ? "mt-4 rounded-xl border border-warning-line bg-warning-soft p-3 text-sm font-semibold text-warning-ink"
                            : "mt-4 rounded-xl border border-line bg-elevated p-3 text-sm font-semibold text-ink"
                        }
                        role={message.tone === "error" ? "alert" : "status"}
                      >
                        {message.text}
                      </p>
                    ) : null}

                    {inspect ? (
                      <div className="mt-6 space-y-5">
                        <div className="rounded-2xl border border-line bg-page p-4">
                          <p className="text-sm font-semibold text-ink">
                            Detected {sourceKindLabel(inspect.detected_source_kind)}
                          </p>
                          <p className="mt-1 text-sm text-muted">
                            This workspace accepts ZAR statements only. Confirm that the coverage
                            dates are inclusive. Rows outside the coverage period stay invalid until
                            you adjust the dates or skip them.
                          </p>
                        </div>

                        {accountInstitutionMismatch && selectedAccount ? (
                          <div
                            className="rounded-2xl border border-warning-line bg-warning-soft p-4"
                            role="alert"
                          >
                            <h3 className="text-sm font-bold text-warning-ink">
                              Check the destination account
                            </h3>
                            <p className="mt-1 text-sm leading-6 text-warning-ink">
                              The detected format is {sourceKindLabel(inspect.detected_source_kind)}
                              , but the selected account “{selectedAccount.display_name}” is set to{" "}
                              {selectedAccount.institution}. Choose the matching account, or confirm
                              below to import it here anyway.
                            </p>
                            <label className="mt-3 flex items-start gap-2 text-sm font-semibold text-warning-ink">
                              <input
                                checked={mismatchAcknowledged}
                                disabled={working}
                                onChange={(event) => setMismatchAcknowledged(event.target.checked)}
                                type="checkbox"
                              />
                              Import this statement into this account anyway.
                            </label>
                          </div>
                        ) : null}

                        {previousImports === undefined ? null : (
                          <div className="rounded-2xl border border-line bg-page p-4">
                            <h3 className="text-sm font-semibold text-ink">
                              Previous imports for this account and format
                            </h3>
                            {previousImports.length === 0 ? (
                              <p className="mt-1 text-sm text-muted">
                                No previous imports for this account and format.
                              </p>
                            ) : (
                              <>
                                <p className="mt-1 text-sm text-muted">
                                  Most recent first. Check the coverage before choosing the new
                                  statement&apos;s start date.
                                </p>
                                <ul className="mt-3 space-y-2">
                                  {previousImports.map((item) => (
                                    <li
                                      className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1 rounded-xl border border-line bg-surface px-3 py-2 text-sm"
                                      key={item.id}
                                    >
                                      <span className="font-mono text-ink">
                                        {item.start_date} to {item.end_date}
                                      </span>
                                      <span className="text-muted">
                                        Imported {item.created_at.slice(0, 10)} ·{" "}
                                        {sourceKindLabel(item.source_type)}
                                      </span>
                                    </li>
                                  ))}
                                </ul>
                                <p className="mt-3 text-xs leading-5 text-muted">
                                  Rows that match an earlier import are flagged during review,
                                  before anything is committed.
                                </p>
                              </>
                            )}
                          </div>
                        )}

                        <div className="grid gap-4 sm:grid-cols-2">
                          <label className="grid gap-2 text-sm font-semibold text-ink">
                            Statement start date
                            <input
                              className="rounded-xl border border-line bg-page px-3 py-2 text-base font-normal text-ink"
                              onChange={(event) => setStartDate(event.target.value)}
                              type="date"
                              value={startDate}
                            />
                          </label>
                          <label className="grid gap-2 text-sm font-semibold text-ink">
                            Statement end date
                            <input
                              className="rounded-xl border border-line bg-page px-3 py-2 text-base font-normal text-ink"
                              onChange={(event) => setEndDate(event.target.value)}
                              type="date"
                              value={endDate}
                            />
                          </label>
                        </div>

                        {sourceKind === "generic_csv" ? (
                          <fieldset className="rounded-2xl border border-line bg-page p-4">
                            <legend className="px-1 text-sm font-bold text-ink">
                              Column mapping
                            </legend>
                            <div className="mt-3 grid gap-3 sm:grid-cols-2">
                              {[
                                ["header_line", "Header line"],
                                ["delimiter", "Delimiter"],
                                ["date_format", "Date format"],
                                ["decimal_separator", "Decimal separator"],
                                ["thousands_separator", "Thousands separator"],
                                ["date_column", "Date column index"],
                                ["description_column", "Description column index (blank to omit)"],
                                ["amount_mode", "Amount mode"],
                                ["amount_column", "Signed amount column index"],
                                ["debit_column", "Debit column index"],
                                ["credit_column", "Credit column index"],
                                ["direction", "Direction"],
                                ["stable_id_column", "Stable ID column index (optional)"],
                                ["source_namespace", "Source namespace (optional)"],
                                ["century", "Two-digit-year century (optional)"],
                              ].map(([name, label]) => (
                                <label
                                  className="grid gap-1 text-xs font-semibold text-ink"
                                  key={name}
                                >
                                  {label}
                                  <input
                                    className="rounded-lg border border-line bg-surface px-2 py-1.5 text-sm font-normal text-ink"
                                    onChange={(event) => updateMapping(name, event.target.value)}
                                    value={mapping[name] ?? ""}
                                  />
                                </label>
                              ))}
                            </div>
                            {mappingDirty ? (
                              <p className="mt-2 text-xs font-semibold text-warning-ink">
                                Mapping changed. Inspect again to refresh the preview before
                                preparing.
                              </p>
                            ) : null}
                            <p className="mt-3 text-xs leading-5 text-muted">
                              Column indexes are zero-based. Blank optional indexes are omitted.
                              {amountMode === "debit_credit"
                                ? " Debit maps to outflow and credit to inflow."
                                : " Signed mode uses the amount column and chosen direction."}
                            </p>
                          </fieldset>
                        ) : null}

                        <div>
                          <h3 className="text-sm font-bold text-ink">Preview</h3>
                          {unresolvedDates ? (
                            <p
                              className="mt-2 rounded-xl border border-warning-line bg-warning-soft p-3 text-sm text-warning-ink"
                              role="status"
                            >
                              Some dates use two-digit years and could not be resolved. Check the
                              century above, then inspect again.
                            </p>
                          ) : null}
                          <div className="mt-2 space-y-2">
                            {inspect.preview.map((row) => (
                              <div
                                className="grid gap-1 rounded-xl border border-line bg-page p-3 text-sm sm:grid-cols-[7rem_1fr_8rem]"
                                key={row.row_id}
                              >
                                <span
                                  className={
                                    row.transaction_date
                                      ? "font-mono text-xs text-muted"
                                      : "font-mono text-xs font-semibold text-warning-ink"
                                  }
                                >
                                  {row.transaction_date ?? "Date unresolved"}
                                </span>
                                <span className="break-words text-ink">{row.description}</span>
                                <span className="font-mono tabular-nums text-ink sm:text-right">
                                  {row.amount ?? "amount?"}
                                </span>
                                {row.validation_codes.length > 0 ? (
                                  <span className="text-xs font-semibold text-warning-ink sm:col-span-3">
                                    {row.validation_codes.join(", ")}
                                  </span>
                                ) : null}
                              </div>
                            ))}
                          </div>
                          {inspect.preview_truncated ? (
                            <p className="mt-2 text-xs text-muted">
                              Preview is limited to the first 20 rows; the review contains all rows.
                            </p>
                          ) : null}
                        </div>

                        <div className="flex flex-wrap items-center gap-3">
                          <button
                            className="primary-link inline-flex items-center justify-center rounded-full px-5 py-3 text-sm font-bold disabled:opacity-60"
                            disabled={!canPrepare || working || mappingDirty}
                            onClick={handlePrepare}
                            type="button"
                          >
                            {busy === "preparing" ? "Preparing review…" : "Prepare review"}
                          </button>
                          <button
                            className="inline-flex items-center justify-center rounded-full border border-line px-5 py-3 text-sm font-bold text-ink"
                            onClick={() => {
                              chooseFile(null);
                              if (fileInput.current) fileInput.current.value = "";
                            }}
                            type="button"
                          >
                            Start over
                          </button>
                        </div>
                      </div>
                    ) : null}
                  </section>
                )}
              </>
            ),
          },
          {
            key: "reviews",
            label: "Open reviews",
            attention: reviews.length > 0,
            panel: (
              <section aria-labelledby="imports-reviews-title">
                <h2
                  className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
                  id="imports-reviews-title"
                >
                  Open reviews
                </h2>
                <p className="mt-3 max-w-2xl text-base leading-7 text-muted">
                  Continue a prepared review. Reviews expire 24 hours after preparation.
                </p>

                {reviews.length === 0 ? (
                  <p className={EMPTY_STATE_CLASS}>No open reviews.</p>
                ) : (
                  <div className={TABLE_WRAPPER_CLASS}>
                    <table
                      role="table"
                      aria-label="Open import reviews"
                      className={`${TABLE_CLASS} min-w-[44rem]`}
                    >
                      <thead className={TABLE_HEAD_CLASS}>
                        <tr>
                          <th className={TH_CLASS} scope="col">
                            Statement
                          </th>
                          <th className={TH_CLASS} scope="col">
                            Coverage
                          </th>
                          <th className={TH_CLASS} scope="col">
                            Rows
                          </th>
                          <th className={`${TH_CLASS} text-right`} scope="col">
                            Action
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {reviews.map((review) => (
                          <tr className={TABLE_ROW_CLASS} key={review.id}>
                            <td data-label="Statement" className={TD_CLASS}>
                              <p className="font-semibold text-ink">
                                {sourceKindLabel(review.source_kind)}
                              </p>
                              <p className="mt-0.5 text-xs text-muted">Version {review.version}</p>
                            </td>
                            <td data-label="Coverage" className={`${TD_CLASS} text-muted`}>
                              {review.start_date ?? "Unknown start"} to{" "}
                              {review.end_date ?? "unknown end"}
                            </td>
                            <td data-label="Rows" className={`${TD_CLASS} text-muted tabular-nums`}>
                              {review.imported_count} included · {review.suppressed_count}{" "}
                              suppressed · {review.skipped_count} skipped
                            </td>
                            <td data-label="Action" className={`${TD_CLASS} text-right`}>
                              <Link
                                className="inline-flex rounded-full border border-line px-4 py-2 text-sm font-bold text-ink transition hover:border-ink"
                                href={`/imports/review/${review.id}`}
                              >
                                Continue review
                              </Link>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>
            ),
          },
          {
            key: "history",
            label: "History",
            panel: (
              <section aria-labelledby="imports-history-title">
                <h2
                  className="text-2xl leading-tight font-semibold tracking-[-0.03em] text-ink sm:text-3xl"
                  id="imports-history-title"
                >
                  Import history
                </h2>
                <p className="mt-3 max-w-2xl text-base leading-7 text-muted">
                  Committed statements. Only committed imports appear here.
                </p>

                {history.length === 0 ? (
                  <p className={EMPTY_STATE_CLASS}>No statements imported yet.</p>
                ) : (
                  <div className={TABLE_WRAPPER_CLASS}>
                    <table
                      role="table"
                      aria-label="Committed import history"
                      className={`${TABLE_CLASS} min-w-[52rem]`}
                    >
                      <thead className={TABLE_HEAD_CLASS}>
                        <tr>
                          <th className={TH_CLASS} scope="col">
                            Statement
                          </th>
                          <th className={TH_CLASS} scope="col">
                            Account
                          </th>
                          <th className={TH_CLASS} scope="col">
                            Coverage
                          </th>
                          <th className={TH_CLASS} scope="col">
                            Source
                          </th>
                          <th className={TH_CLASS} scope="col">
                            Rows
                          </th>
                          <th className={TH_CLASS} scope="col">
                            Status
                          </th>
                          <th className={`${TH_CLASS} text-right`} scope="col">
                            Action
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {history.map((item) => {
                          const itemAccount = accounts.find(
                            (candidate) => candidate.id === item.financial_account_id,
                          );
                          return (
                            <tr className={TABLE_ROW_CLASS} key={item.id}>
                              <td
                                data-label="Statement"
                                className={`${TD_CLASS} font-semibold text-ink`}
                              >
                                {item.statement_label}
                              </td>
                              <td data-label="Account" className={`${TD_CLASS} text-muted`}>
                                {itemAccount ? (
                                  <>
                                    {itemAccount.display_name}
                                    {itemAccount.is_archived ? " (archived)" : ""}
                                  </>
                                ) : (
                                  "Unknown account"
                                )}
                              </td>
                              <td data-label="Coverage" className={`${TD_CLASS} text-muted`}>
                                {item.start_date} to {item.end_date}
                              </td>
                              <td data-label="Source" className={`${TD_CLASS} text-muted`}>
                                {sourceKindLabel(item.source_type)}
                              </td>
                              <td
                                data-label="Rows"
                                className={`${TD_CLASS} text-muted tabular-nums`}
                              >
                                {item.imported_count} imported · {item.suppressed_count} suppressed
                                · {item.skipped_count} skipped
                              </td>
                              <td data-label="Status" className={TD_CLASS}>
                                {item.is_hidden ? (
                                  <span
                                    className={`${STATUS_PILL_CLASS} bg-warning-soft text-warning-text`}
                                  >
                                    Hidden
                                  </span>
                                ) : item.live_hidden_count > 0 ? (
                                  <span className={`${STATUS_PILL_CLASS} bg-elevated text-muted`}>
                                    {item.live_hidden_count} hidden
                                  </span>
                                ) : (
                                  <span
                                    className={`${STATUS_PILL_CLASS} bg-success-soft text-success-ink`}
                                  >
                                    Visible
                                  </span>
                                )}
                              </td>
                              <td data-label="Action" className={`${TD_CLASS} text-right`}>
                                <Link
                                  className="inline-flex rounded-full border border-line px-4 py-2 text-sm font-bold text-ink transition hover:border-ink"
                                  href={`/imports/${item.id}`}
                                >
                                  View statement
                                </Link>
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                )}

                {nextCursor ? (
                  <Link
                    className="mt-4 inline-flex rounded-full border border-line px-4 py-2 text-sm font-bold text-ink"
                    href={`/imports?tab=history&cursor=${encodeURIComponent(nextCursor)}`}
                  >
                    Load older imports
                  </Link>
                ) : null}
              </section>
            ),
          },
        ]}
      />
    </div>
  );
}
