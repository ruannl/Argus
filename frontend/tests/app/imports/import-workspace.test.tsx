import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

const push = vi.fn();
const refresh = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push, refresh }),
}));

vi.mock("@/lib/server/finance-actions", () => ({
  createAccountAction: vi.fn(),
}));

import ImportWorkspace from "@/app/imports/import-workspace";

const account = {
  id: "financial-account-id",
  display_name: "Cheque",
  institution: null,
  currency: "ZAR",
  is_archived: false,
  transaction_count: 0,
  created_at: "2026-01-01T00:00:00Z",
  updated_at: "2026-01-01T00:00:00Z",
};

function inspectResult(headerLine: string) {
  return {
    detected_source_kind: "generic_csv",
    limits: {
      max_file_bytes: 10_000_000,
      max_data_rows: 20_000,
      max_pdf_pages: 200,
      max_description_scalars: 4096,
    },
    proposed_start_date: "2025-01-01",
    proposed_end_date: "2025-01-31",
    mapping: {
      header_line: headerLine,
      delimiter: ",",
      date_format: "yyyy-MM-dd",
      decimal_separator: ".",
      thousands_separator: "none",
      amount_mode: "signed",
      date_column: "0",
      description_column: "1",
      amount_column: "2",
      direction: "normal",
      negative_style: "parentheses",
    },
    required_confirmations: [],
    warnings: [],
    preview: [],
    preview_truncated: false,
    can_prepare: true,
  };
}

function jsonResponse(value: unknown) {
  return Promise.resolve(
    new Response(JSON.stringify(value), {
      status: 200,
      headers: { "content-type": "application/json" },
    }),
  );
}

describe("import workspace", () => {
  beforeEach(() => {
    push.mockReset();
    refresh.mockReset();
    vi.restoreAllMocks();
  });

  it("requires reinspection after a mapping change and preserves the confirmed negative style", async () => {
    const inspectBodies: Record<string, unknown>[] = [];
    const prepareBodies: Record<string, unknown>[] = [];
    let inspectCount = 0;
    vi.stubGlobal("fetch", vi.fn(async (url: string, init?: RequestInit) => {
      const options = JSON.parse(
        String((init?.body as FormData).get("options")),
      ) as Record<string, unknown>;
      const mode = new URL(url, "http://localhost").searchParams.get("mode");
      if (mode === "prepare") {
        prepareBodies.push(options);
        return jsonResponse({ summary: { id: "review-id" }, page: { rows: [] } });
      }
      inspectBodies.push(options);
      inspectCount += 1;
      return jsonResponse(inspectResult(inspectCount === 1 ? "1" : "2"));
    }));

    const user = userEvent.setup();
    const { container } = render(
      <ImportWorkspace
        accounts={[account]}
        reviews={[]}
        history={[]}
        loadError={null}
        nextCursor={null}
      />,
    );

    const fileInput = container.querySelector('input[type="file"]');
    expect(fileInput).not.toBeNull();
    fireEvent.change(fileInput as HTMLInputElement, {
      target: {
        files: [
          new File(["Date,Description,Amount\n2025-01-02,Shop,-10.00\n"], "statement.csv", {
            type: "text/csv",
          }),
        ],
      },
    });

    await user.selectOptions(screen.getByLabelText("Statement format"), "generic_csv");
    await user.click(screen.getByRole("button", { name: "Inspect statement" }));
    await waitFor(() => {
      expect(screen.getByText("Inspection complete. Confirm the mapping, ZAR, and statement dates."))
        .toBeInTheDocument();
    });

    const headerLine = screen.getByLabelText("Header line");
    await user.clear(headerLine);
    await user.type(headerLine, "2");

    expect(
      screen.getByText("Mapping changed. Inspect again to refresh the preview before preparing."),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Prepare review" })).toBeDisabled();
    expect(prepareBodies).toHaveLength(0);

    await user.click(screen.getByRole("button", { name: "Inspect statement" }));
    await waitFor(() => {
      expect(screen.getByLabelText("Header line")).toHaveValue("2");
    });
    await user.click(screen.getByLabelText(/South African rand/));
    await user.click(screen.getByRole("button", { name: "Prepare review" }));

    await waitFor(() => expect(push).toHaveBeenCalledWith("/imports/review/review-id"));
    expect(prepareBodies).toHaveLength(1);
    const config = prepareBodies[0].parser_config as Record<string, unknown>;
    expect(config.header_line).toBe(2);
    expect(config.negative_style).toBe("parentheses");
    expect(inspectBodies).toHaveLength(2);
  });

  it("does not offer PDF import and rejects a selected PDF", () => {
    const { container } = render(
      <ImportWorkspace
        accounts={[account]}
        reviews={[]}
        history={[]}
        loadError={null}
        nextCursor={null}
      />,
    );

    const format = screen.getByLabelText("Statement format") as HTMLSelectElement;
    expect(Array.from(format.options).map((option) => option.textContent)).toEqual([
      "FNB CSV or ZIP",
      "Generic CSV with mapping",
    ]);

    const fileInput = container.querySelector('input[type="file"]') as HTMLInputElement;
    expect(fileInput.accept).not.toContain("pdf");
    fireEvent.change(fileInput, {
      target: {
        files: [new File(["%PDF-1.7"], "statement.pdf", { type: "application/pdf" })],
      },
    });

    expect(screen.getByRole("alert").textContent).toBe(
      "PDF import is coming later. Use an FNB CSV/ZIP or a mapped CSV for now.",
    );
    expect(screen.getByText("No statement selected.")).toBeInTheDocument();
  });
});
