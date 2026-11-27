/** Maps known statement formats and account institutions to one bank name. */

export type KnownBank = "fnb" | "standardbank";

export function statementBank(kind: string): KnownBank | null {
  if (kind === "fnb_csv" || kind === "fnb_zip") {
    return "fnb";
  }
  if (kind === "standard_bank_pdf") {
    return "standardbank";
  }
  return null;
}

/**
 * Returns the account's bank when its free-text institution clearly names one.
 * "other" means a non-empty institution that does not match either known bank,
 * so an import from FNB or Standard Bank is still worth a confirmation.
 */
export function accountBank(institution: string | null | undefined): KnownBank | "other" | null {
  if (!institution?.trim()) {
    return null;
  }
  const normalized = institution.toLowerCase().replace(/[^a-z0-9]/g, "");
  if (normalized.includes("fnb") || normalized.includes("firstnationalbank")) {
    return "fnb";
  }
  if (normalized.includes("standardbank")) {
    return "standardbank";
  }
  return "other";
}
