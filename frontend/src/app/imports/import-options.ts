import type {
  ImportParseOptions,
  ImportParserConfig,
} from "@/lib/server/backend-client";

export type SourceKind = "fnb" | "standard_bank_pdf" | "generic_csv";

export function buildParserConfig(mapping: Record<string, string>): ImportParserConfig {
  const numberOrNull = (value: string | undefined): number | null => {
    if (!value || value.trim() === "") return null;
    const trimmed = value.trim();
    if (!/^\d+$/.test(trimmed)) return null;
    const parsed = Number(trimmed);
    return Number.isSafeInteger(parsed) ? parsed : null;
  };
  const descriptionColumn = mapping.description_column ?? "";
  return {
    format: "generic",
    header_line: numberOrNull(mapping.header_line),
    delimiter: mapping.delimiter ?? ",",
    date_format: mapping.date_format ?? "yyyy-MM-dd",
    decimal_separator: mapping.decimal_separator ?? ".",
    thousands_separator: mapping.thousands_separator ?? "none",
    amount_mode: (mapping.amount_mode as "signed" | "debit_credit") ?? "signed",
    date_column: numberOrNull(mapping.date_column),
    description_column: descriptionColumn === "" ? null : numberOrNull(descriptionColumn),
    confirm_empty_description: descriptionColumn === "",
    amount_column: numberOrNull(mapping.amount_column),
    debit_column: numberOrNull(mapping.debit_column),
    credit_column: numberOrNull(mapping.credit_column),
    direction: (mapping.direction as "normal" | "inverted") ?? "normal",
    negative_style: mapping.negative_style ?? "any",
    stable_id_column: numberOrNull(mapping.stable_id_column),
    source_namespace:
      mapping.source_namespace && mapping.source_namespace.trim() !== ""
        ? mapping.source_namespace
        : null,
    century: numberOrNull(mapping.century),
  };
}

/**
 * Initial generic inspection must omit parser_config so the backend can detect
 * and propose a mapping. Once a mapping exists, or when preparing, the
 * confirmed configuration is sent.
 */
export function buildUploadOptions(
  sourceKind: SourceKind,
  accountId: string,
  startDate: string,
  endDate: string,
  confirmZar: boolean,
  mapping: Record<string, string>,
  mode: "inspect" | "prepare",
): ImportParseOptions {
  const options: ImportParseOptions = {
    financial_account_id: accountId,
    source_kind: sourceKind,
    start_date: startDate || null,
    end_date: endDate || null,
    currency: "ZAR",
    confirm_zar: confirmZar,
  };

  if (sourceKind === "generic_csv") {
    if (mode === "prepare" || Object.keys(mapping).length > 0) {
      options.parser_config = buildParserConfig(mapping);
    }
  } else {
    options.parser_config = {
      format: sourceKind === "standard_bank_pdf" ? "standard_bank" : "fnb",
    };
  }
  return options;
}
