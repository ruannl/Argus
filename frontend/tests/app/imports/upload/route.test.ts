import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  inspectImportFile: vi.fn(),
  prepareImportReview: vi.fn(),
}));

import { POST } from "@/app/imports/upload/route";
import { auth } from "@/auth";
import {
  inspectImportFile,
  prepareImportReview,
} from "@/lib/server/backend-client";

const origin = "http://frontend.test";
const accountId = "00000000-0000-0000-0000-000000000001";

function multipartBody(
  options: unknown,
  file = new Uint8Array([1, 2, 3]),
  extra?: { name: string; value: string },
): { body: Uint8Array; contentType: string } {
  const boundary = "----argus-test-boundary";
  const chunks: Uint8Array[] = [
    Buffer.from(
      `--${boundary}\r\nContent-Disposition: form-data; name="options"\r\nContent-Type: application/json\r\n\r\n${JSON.stringify(options)}\r\n`,
    ),
    Buffer.from(
      `--${boundary}\r\nContent-Disposition: form-data; name="file"; filename="statement.csv"\r\nContent-Type: application/octet-stream\r\n\r\n`,
    ),
    file,
    Buffer.from("\r\n"),
  ];
  if (extra) {
    chunks.push(
      Buffer.from(
        `--${boundary}\r\nContent-Disposition: form-data; name="${extra.name}"\r\n\r\n${extra.value}\r\n`,
      ),
    );
  }
  chunks.push(Buffer.from(`--${boundary}--\r\n`));
  const total = chunks.reduce((sum, chunk) => sum + chunk.byteLength, 0);
  const body = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return { body, contentType: `multipart/form-data; boundary=${boundary}` };
}

function asBody(body: Uint8Array): ArrayBuffer {
  return body.buffer.slice(body.byteOffset, body.byteOffset + body.byteLength) as ArrayBuffer;
}

function uploadRequest(options: unknown): Request {
  const { body, contentType } = multipartBody(options);
  return new Request("http://frontend.test/imports/upload?mode=inspect", {
    method: "POST",
    body: asBody(body),
    headers: { origin, "content-type": contentType },
  });
}

function inspectResult() {
  return {
    detected_source_kind: "fnb_csv",
    limits: { max_file_bytes: 1, max_data_rows: 1, max_pdf_pages: 1, max_description_scalars: 1 },
    proposed_start_date: null,
    proposed_end_date: null,
    mapping: {},
    required_confirmations: [],
    warnings: [],
    preview: [],
    preview_truncated: false,
    can_prepare: true,
  };
}

describe("import upload route", () => {
  beforeEach(() => {
    vi.stubEnv("ARGUS_FRONTEND_URL", origin);
    vi.mocked(auth).mockReset();
    vi.mocked(inspectImportFile).mockReset();
    vi.mocked(prepareImportReview).mockReset();
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("rejects a missing or foreign origin before reading auth", async () => {
    const form = new FormData();
    form.append("options", "{}");
    const response = await POST(new Request("http://frontend.test/imports/upload?mode=inspect", {
      method: "POST",
      body: form,
      headers: { origin: "http://evil.test" },
    }));

    expect(response.status).toBe(403);
    expect(auth).not.toHaveBeenCalled();
  });

  it("rejects an unauthenticated request", async () => {
    vi.mocked(auth).mockResolvedValue(null as never);
    const response = await POST(uploadRequest({ financial_account_id: accountId, source_kind: "fnb" }));

    expect(response.status).toBe(401);
    expect(inspectImportFile).not.toHaveBeenCalled();
  });

  it("rejects an unknown mode", async () => {
    const form = new FormData();
    form.append("options", "{}");
    const response = await POST(new Request("http://frontend.test/imports/upload?mode=other", {
      method: "POST",
      body: form,
      headers: { origin },
    }));

    expect(response.status).toBe(400);
  });

  it("inspects through the server client and returns the backend payload", async () => {
    vi.mocked(inspectImportFile).mockResolvedValue({ ok: true, value: inspectResult() });

    const response = await POST(uploadRequest({
      financial_account_id: accountId,
      source_kind: "fnb",
    }));

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual(inspectResult());
    expect(inspectImportFile).toHaveBeenCalledWith(
      "backend-token",
      new Uint8Array([1, 2, 3]),
      { financial_account_id: accountId, source_kind: "fnb" },
      "statement.bin",
      expect.anything(),
    );
  });

  it("forwards boundary-like file bytes without truncation", async () => {
    const marker = "------argus-test-boundary";
    const file = new TextEncoder().encode(
      `Date,Description\r\n${marker}-not-a-delimiter\r\nLast row\r\n`,
    );
    const { body, contentType } = multipartBody(
      { financial_account_id: accountId, source_kind: "fnb" },
      file,
    );
    const request = new Request("http://frontend.test/imports/upload?mode=inspect", {
      method: "POST",
      body: asBody(body),
      headers: { origin, "content-type": contentType },
    });
    vi.mocked(inspectImportFile).mockResolvedValue({ ok: true, value: inspectResult() });

    const response = await POST(request);

    expect(response.status).toBe(200);
    const call = vi.mocked(inspectImportFile).mock.calls[0];
    expect(Array.from(call[1])).toEqual(Array.from(file));
    expect(call[2]).toEqual({ financial_account_id: accountId, source_kind: "fnb" });
  });

  it("does not treat a closing-like substring as the final delimiter", async () => {
    const boundaryMarker = "------argus-test-boundary";
    const file = new TextEncoder().encode(
      `Date,Description\r\n${boundaryMarker}--not-a-delimiter\r\nLast row\r\n`,
    );
    const { body, contentType } = multipartBody(
      { financial_account_id: accountId, source_kind: "fnb" },
      file,
    );
    const request = new Request("http://frontend.test/imports/upload?mode=inspect", {
      method: "POST",
      body: asBody(body),
      headers: { origin, "content-type": contentType },
    });
    vi.mocked(inspectImportFile).mockResolvedValue({ ok: true, value: inspectResult() });

    const response = await POST(request);

    expect(response.status).toBe(200);
    const call = vi.mocked(inspectImportFile).mock.calls[0];
    expect(Array.from(call[1])).toEqual(Array.from(file));
  });

  it("prepares through the server client with a 201", async () => {
    vi.mocked(prepareImportReview).mockResolvedValue({
      ok: true,
      value: { summary: { id: "review-id" }, page: { rows: [] } } as never,
    });
    const { body, contentType } = multipartBody(
      { financial_account_id: accountId, source_kind: "fnb" },
      new Uint8Array([1]),
    );
    const request = new Request("http://frontend.test/imports/upload?mode=prepare", {
      method: "POST",
      body: asBody(body),
      headers: { origin, "content-type": contentType },
    });

    const response = await POST(request);

    expect(response.status).toBe(201);
    expect(prepareImportReview).toHaveBeenCalledOnce();
  });

  it("maps a backend file-too-large failure to 413", async () => {
    vi.mocked(inspectImportFile).mockResolvedValue({ ok: false, kind: "file_too_large" });

    const response = await POST(uploadRequest({
      financial_account_id: accountId,
      source_kind: "fnb",
    }));

    expect(response.status).toBe(413);
    expect(await response.json()).toEqual({ error: "file_too_large" });
  });

  it("rejects extra multipart fields", async () => {
    const { body, contentType } = multipartBody("{}", new Uint8Array([1]), {
      name: "extra",
      value: "value",
    });
    const request = new Request("http://frontend.test/imports/upload?mode=inspect", {
      method: "POST",
      body: asBody(body),
      headers: { origin, "content-type": contentType },
    });

    const response = await POST(request);

    expect(response.status).toBe(400);
    expect(inspectImportFile).not.toHaveBeenCalled();
  });
});
