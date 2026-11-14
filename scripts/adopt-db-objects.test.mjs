import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { adoptObjects } from "./adopt-db-objects.mjs";

function ok(stdout = "") {
  return { status: 0, stdout, stderr: "" };
}

function manifestFile(value) {
  const file = path.join(fs.mkdtempSync(path.join(os.tmpdir(), "argus-adopt-")), "manifest.json");
  fs.writeFileSync(file, JSON.stringify(value));
  return file;
}

test("adoptObjects transfers only manifest-listed objects transactionally", () => {
  const manifest = manifestFile({
    schema: "public",
    tables: ["customers", "financial_accounts"],
    sequences: [],
    views: [],
    functions: ["argus_merchant_key(text)"],
  });
  const calls = [];
  const spawn = (command, args, options = {}) => {
    const sql = options.input ?? "";
    calls.push(sql);
    if (sql.includes("relkind = 'r'")) {
      return ok("table|customers\ntable|financial_accounts\nfunction|argus_merchant_key(text)");
    }
    return ok();
  };

  const result = adoptObjects({
    spawnSync: spawn,
    root: new URL("..", import.meta.url).pathname,
    adminUrl: "postgres://previous@127.0.0.1:5432/argus",
    manifestPath: manifest,
    dockerContainer: "argus_pg16_t10",
  });

  assert.equal(result.objects, 3);
  const transfer = calls.find((sql) => sql.startsWith("BEGIN;"));
  assert.match(transfer, /ALTER TABLE "public"\."customers" OWNER TO "argus_owner"/);
  assert.match(transfer, /ALTER FUNCTION "public"\.argus_merchant_key\(text\) OWNER TO "argus_owner"/);
  assert.doesNotMatch(transfer, /REASSIGN OWNED/);
});

test("adoptObjects refuses an inventory mismatch before any ownership change", () => {
  const manifest = manifestFile({
    schema: "public",
    tables: ["customers"],
    sequences: [],
    views: [],
    functions: [],
  });
  const spawn = (command, args, options = {}) => {
    const sql = options.input ?? "";
    if (sql.includes("relkind = 'r'")) {
      return ok("table|customers\ntable|unexpected_table");
    }
    return ok();
  };

  assert.throws(
    () =>
      adoptObjects({
        spawnSync: spawn,
        root: new URL("..", import.meta.url).pathname,
        adminUrl: "postgres://previous@127.0.0.1:5432/argus",
        manifestPath: manifest,
        dockerContainer: "argus_pg16_t10",
      }),
    /unexpected_table/,
  );
});

test("adoptObjects transfers an opted-in schema and materialized views", () => {
  const manifest = manifestFile({
    schema: "public",
    transfer_schema_owner: true,
    tables: ["customers"],
    sequences: [],
    views: ["customer_view"],
    matviews: ["customer_rollup"],
    functions: [],
  });
  const calls = [];
  const spawn = (command, args, options = {}) => {
    const sql = options.input ?? "";
    calls.push(sql);
    if (sql.includes("relkind = 'r'")) {
      return ok("table|customers\nview|customer_view\nmatview|customer_rollup");
    }
    return ok();
  };

  const result = adoptObjects({
    spawnSync: spawn,
    root: new URL("..", import.meta.url).pathname,
    adminUrl: "postgres://previous@127.0.0.1:5432/argus",
    manifestPath: manifest,
    dockerContainer: "argus_pg16_t10",
  });

  assert.equal(result.objects, 3);
  const transfer = calls.find((sql) => sql.startsWith("BEGIN;"));
  assert.match(transfer, /ALTER SCHEMA "public" OWNER TO "argus_owner"/);
  assert.match(transfer, /ALTER VIEW "public"\."customer_view" OWNER TO "argus_owner"/);
  assert.match(transfer, /ALTER MATERIALIZED VIEW "public"\."customer_rollup" OWNER TO "argus_owner"/);
});

test("adoptObjects rejects an already-qualified function manifest entry", () => {
  const manifest = manifestFile({
    schema: "custom",
    tables: [],
    sequences: [],
    views: [],
    matviews: [],
    functions: ["custom.argus_merchant_key(text)"],
  });
  const spawn = (command, args, options = {}) => {
    const sql = options.input ?? "";
    if (sql.includes("relkind = 'r'")) {
      return ok("function|argus_merchant_key(text)");
    }
    return ok();
  };
  assert.throws(
    () =>
      adoptObjects({
        spawnSync: spawn,
        root: new URL("..", import.meta.url).pathname,
        adminUrl: "postgres://previous@127.0.0.1:5432/argus",
        manifestPath: manifest,
        dockerContainer: "argus_pg16_t10",
      }),
    /object inventory mismatch/,
  );
});

test("adoptObjects rejects a non-boolean schema-owner option", () => {
  const manifest = manifestFile({
    schema: "public",
    transfer_schema_owner: "yes",
    tables: [],
    sequences: [],
    views: [],
    functions: [],
  });
  assert.throws(
    () =>
      adoptObjects({
        spawnSync: () => ok(),
        root: new URL("..", import.meta.url).pathname,
        adminUrl: "postgres://previous@127.0.0.1:5432/argus",
        manifestPath: manifest,
        dockerContainer: "argus_pg16_t10",
      }),
    /transfer_schema_owner must be a boolean/,
  );
});
