import { describe, expect, it } from "vitest";

import { accountBank, statementBank } from "@/app/imports/import-institution";

describe("import institution matching", () => {
  it("maps the known statement formats to their bank", () => {
    expect(statementBank("fnb_csv")).toBe("fnb");
    expect(statementBank("fnb_zip")).toBe("fnb");
    expect(statementBank("standard_bank_pdf")).toBe("standardbank");
    expect(statementBank("generic_csv")).toBeNull();
    expect(statementBank("unknown")).toBeNull();
  });

  it("normalises common institution spellings", () => {
    expect(accountBank("FNB")).toBe("fnb");
    expect(accountBank("First National Bank")).toBe("fnb");
    expect(accountBank("Standardbank")).toBe("standardbank");
    expect(accountBank("Standard Bank")).toBe("standardbank");
    expect(accountBank("Capitec")).toBe("other");
    expect(accountBank(null)).toBeNull();
    expect(accountBank("  ")).toBeNull();
  });
});
