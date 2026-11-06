import { NextResponse } from "next/server";

import { auth } from "@/auth";
import {
  inspectImportFile,
  prepareImportReview,
} from "@/lib/server/backend-client";
import type { ImportParseOptions } from "@/lib/server/backend-client";

export const runtime = "nodejs";

const MAX_MULTIPART_BYTES = 10_100_000;
const MAX_FILE_BYTES = 10_000_000;

function response(body: unknown, status = 200): NextResponse {
  return NextResponse.json(body, {
    status,
    headers: { "Cache-Control": "no-store" },
  });
}

function statusFor(kind: string): number {
  switch (kind) {
    case "file_too_large":
      return 413;
    case "unsupported_format":
      return 415;
    case "not_found":
      return 404;
    case "review_expired":
    case "import_deleted":
      return 410;
    case "review_stale":
    case "version_conflict":
    case "statement_repeat":
    case "idempotency_conflict":
    case "account_archived":
    case "identity_conflict":
    case "tag_unavailable":
      return 409;
    case "import_busy":
    case "rate_limited":
      return 429;
    default:
      return 400;
  }
}

async function readBounded(
  request: Request,
  maximum: number,
): Promise<Uint8Array | null> {
  if (!request.body) {
    return null;
  }
  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let total = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) {
      break;
    }
    total += value.byteLength;
    if (total > maximum) {
      await reader.cancel();
      return null;
    }
    chunks.push(value);
  }
  const output = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    output.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return output;
}

function validClosingMarker(body: string, index: number, marker: string): boolean {
  const after = index + marker.length;
  if (!body.startsWith("--", after)) {
    return false;
  }
  let cursor = after + 2;
  while (cursor < body.length && (body[cursor] === " " || body[cursor] === "\t")) {
    cursor++;
  }
  if (cursor === body.length) {
    return true;
  }
  if (body.slice(cursor, cursor + 2) !== "\r\n") {
    return false;
  }
  for (let rest = cursor + 2; rest < body.length; rest++) {
    if (!/\s/.test(body[rest])) {
      return false;
    }
  }
  return true;
}

function findDelimiter(body: string, marker: string, from: number): number {
  let index = body.indexOf(marker, from);
  while (index >= 0) {
    const beforeIsLineStart = index >= 2 && body.slice(index - 2, index) === "\r\n";
    const suffix = body.slice(index + marker.length, index + marker.length + 2);
    if (beforeIsLineStart &&
        (suffix === "\r\n" || (suffix === "--" && validClosingMarker(body, index, marker)))) {
      return index;
    }
    index = body.indexOf(marker, index + 1);
  }
  return -1;
}

function parseMultipart(
  raw: Uint8Array,
  contentType: string,
): { options: string; file: Uint8Array } {
  const boundaryMatch = /boundary=(?:"([^"]+)"|([^;]+))/i.exec(contentType);
  const boundary = (boundaryMatch?.[1] ?? boundaryMatch?.[2] ?? "").trim();
  if (!boundary) {
    throw new Error("missing multipart boundary");
  }

  const body = Buffer.from(raw).toString("latin1");
  const marker = `--${boundary}`;
  if (!body.startsWith(marker)) {
    throw new Error("invalid multipart body");
  }

  const parts: { headers: string; content: string }[] = [];
  let cursor = 0;
  for (;;) {
    if (!body.startsWith(marker, cursor)) {
      throw new Error("invalid multipart delimiter");
    }
    let after = cursor + marker.length;
    if (body.startsWith("--", after)) {
      if (!validClosingMarker(body, cursor, marker)) {
        throw new Error("invalid multipart closing delimiter");
      }
      break;
    }
    if (!body.startsWith("\r\n", after)) {
      throw new Error("invalid multipart delimiter suffix");
    }
    after += 2;
    const next = findDelimiter(body, marker, after);
    if (next < 0) {
      throw new Error("missing multipart closing delimiter");
    }
    let part = body.slice(after, next);
    if (part.endsWith("\r\n")) {
      part = part.slice(0, -2);
    }
    const headerEnd = part.indexOf("\r\n\r\n");
    if (headerEnd < 0) {
      throw new Error("multipart part without headers");
    }
    parts.push({
      headers: part.slice(0, headerEnd),
      content: part.slice(headerEnd + 4),
    });
    cursor = next;
  }

  if (parts.length !== 2) {
    throw new Error("unexpected multipart part count");
  }

  let options: string | null = null;
  let file: Uint8Array | null = null;
  for (const part of parts) {
    const nameMatch = /name="([^"]+)"/i.exec(part.headers);
    if (!nameMatch) {
      throw new Error("multipart part without a name");
    }
    const name = nameMatch[1];
    if (name === "options" && options === null) {
      options = Buffer.from(part.content, "latin1").toString("utf8");
    } else if (name === "file" && file === null) {
      file = new Uint8Array(Buffer.from(part.content, "latin1"));
    } else {
      throw new Error("unexpected multipart part");
    }
  }

  if (options === null || file === null) {
    throw new Error("missing multipart part");
  }
  return { options, file };
}

export async function POST(request: Request): Promise<NextResponse> {
  const expectedOrigin = process.env.ARGUS_FRONTEND_URL;
  const origin = request.headers.get("origin");
  if (!expectedOrigin || origin !== expectedOrigin) {
    return response({ error: "forbidden" }, 403);
  }

  const token = (await auth())?.user?.backendToken;
  if (!token) {
    return response({ error: "unauthorized" }, 401);
  }

  const mode = new URL(request.url).searchParams.get("mode");
  if (mode !== "inspect" && mode !== "prepare") {
    return response({ error: "invalid_request" }, 400);
  }

  const declared = Number(request.headers.get("content-length") ?? "");
  if (Number.isFinite(declared) && declared > MAX_MULTIPART_BYTES) {
    return response({ error: "file_too_large" }, 413);
  }

  const raw = await readBounded(request, MAX_MULTIPART_BYTES);
  if (raw === null) {
    return response({ error: "file_too_large" }, 413);
  }

  let parsed: { options: string; file: Uint8Array };
  try {
    parsed = parseMultipart(raw, request.headers.get("content-type") ?? "");
  } catch {
    return response({ error: "invalid_request" }, 400);
  }
  if (parsed.file.byteLength > MAX_FILE_BYTES) {
    return response({ error: "file_too_large" }, 413);
  }

  let options: ImportParseOptions;
  try {
    options = JSON.parse(parsed.options) as ImportParseOptions;
  } catch {
    return response({ error: "invalid_request" }, 400);
  }

  const bytes = parsed.file;
  const result =
    mode === "inspect"
      ? await inspectImportFile(token, bytes, options, "statement.bin", request.signal)
      : await prepareImportReview(token, bytes, options, "statement.bin", request.signal);

  if (!result.ok) {
    return response(
      {
        error: result.kind,
        existing_import_ids: result.existingImportIds,
      },
      statusFor(result.kind),
    );
  }

  return response(result.value, mode === "prepare" ? 201 : 200);
}
