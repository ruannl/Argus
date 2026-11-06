import { describe, expect, it } from "vitest";

import { buildUploadOptions } from "@/app/imports/import-options";

describe("import upload options", () => {
  it("omits parser_config for the initial generic inspect", () => {
    const options = buildUploadOptions(
      "generic_csv", "account-id", "", "", false, {}, "inspect",
    );

    expect(options.source_kind).toBe("generic_csv");
    expect(options.parser_config).toBeUndefined();
  });

  it("sends the confirmed mapping for generic inspect and prepare", () => {
    const mapping = {
      header_line: "1",
      delimiter: ",",
      date_format: "yyyy-MM-dd",
      date_column: "0",
      description_column: "1",
      amount_column: "2",
    };
    const inspect = buildUploadOptions(
      "generic_csv", "account-id", "", "", false, mapping, "inspect",
    );
    const prepare = buildUploadOptions(
      "generic_csv", "account-id", "2025-01-01", "2025-01-31", true, mapping, "prepare",
    );

    expect(inspect.parser_config).toEqual(expect.objectContaining({ header_line: 1 }));
    expect(prepare.parser_config).toEqual(expect.objectContaining({ header_line: 1 }));
    expect(prepare.confirm_zar).toBe(true);
  });

  it("rejects non-integer index text instead of parsing a prefix", () => {
    const options = buildUploadOptions(
      "generic_csv",
      "account-id",
      "2025-01-01",
      "2025-01-31",
      true,
      {
        header_line: "1abc",
        date_column: "0x10",
        description_column: "1",
        amount_column: "2",
      },
      "prepare",
    );

    expect(options.parser_config?.header_line).toBeNull();
    expect(options.parser_config?.date_column).toBeNull();
  });

  it("uses the preset config for FNB and Standard Bank", () => {
    expect(buildUploadOptions("fnb", "account", "", "", false, {}, "inspect").parser_config)
      .toEqual({ format: "fnb" });
    expect(
      buildUploadOptions("standard_bank_pdf", "account", "", "", false, {}, "inspect")
        .parser_config,
    ).toEqual({ format: "standard_bank" });
  });
});
