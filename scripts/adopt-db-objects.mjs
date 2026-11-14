#!/usr/bin/env node
/**
 * Bounded previous-owner application-object adoption for an existing Argus
 * installation. It inventories only one application schema and transfers only
 * the exact objects listed in a reviewed manifest to the designated SQL owner.
 * It never changes row data, never runs cluster-wide REASSIGN OWNED and refuses
 * unexpected objects.
 *
 *   ARGUS_ADMIN_DATABASE_URL=postgres://previous-owner:... \
 *   ARGUS_ADOPT_MANIFEST=./adopt-manifest.json \
 *   ARGUS_ADOPT_OWNER=argus_owner \
 *   ARGUS_ADOPT_SCHEMA=public \
 *   node scripts/adopt-db-objects.mjs
 *
 * Manifest shape:
 *   {
 *     "schema": "public",
 *     "transfer_schema_owner": true,
 *     "tables": ["customers", "financial_accounts"],
 *     "sequences": [],
 *     "views": [],
 *     "matviews": [],
 *     "functions": ["argus_merchant_key(text)"]
 *   }
 *
 * `transfer_schema_owner` is opt-in because `public` may be shared. Object
 * inventory must match exactly; unexpected objects stop the transfer.
 */
import { spawnSync as defaultSpawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { productSlug, runAdminSql, sanitizeToolError } from "./provision-db-roles.mjs";

function quoteIdentifier(value) {
  return `"${String(value).replaceAll('"', '""')}"`;
}

function normalizeManifest(value, defaultSchema) {
  const schema = value.schema ?? defaultSchema;
  if (!/^[a-z][a-z0-9_]*$/.test(schema)) {
    throw new Error("manifest schema must be a lowercase identifier");
  }
  const arrays = {};
  for (const key of ["tables", "sequences", "views", "matviews", "functions"]) {
    const items = value[key] ?? [];
    if (!Array.isArray(items) || items.some((item) => typeof item !== "string" || item.length === 0)) {
      throw new Error(`manifest ${key} must be an array of non-empty strings`);
    }
    if (new Set(items).size !== items.length) {
      throw new Error(`manifest ${key} contains duplicates`);
    }
    arrays[key] = items;
  }
  if (value.transfer_schema_owner !== undefined && typeof value.transfer_schema_owner !== "boolean") {
    throw new Error("manifest transfer_schema_owner must be a boolean");
  }
  return { schema, transferSchemaOwner: value.transfer_schema_owner === true, ...arrays };
}

function queryInventory(spawn, adminUrl, dockerContainer, schema) {
  const sql = `
    SELECT 'table|' || c.relname
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = '${schema}' AND c.relkind = 'r'
    UNION ALL
    SELECT 'sequence|' || c.relname
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = '${schema}' AND c.relkind = 'S'
    UNION ALL
    SELECT CASE WHEN c.relkind = 'm' THEN 'matview|' ELSE 'view|' END || c.relname
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = '${schema}' AND c.relkind IN ('v', 'm')
    UNION ALL
    SELECT 'function|' || p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')'
    FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
    WHERE n.nspname = '${schema}'
    ORDER BY 1`;
  const result = runAdminSql(spawn, sql, { adminUrl, dockerContainer });
  if (result.error || result.status !== 0) {
    throw new Error(sanitizeToolError(result.stderr, "object inventory failed"));
  }
  const actual = new Set();
  for (const line of (result.stdout ?? "").split(/\r?\n/)) {
    if (line.trim().length > 0) {
      actual.add(line.trim());
    }
  }
  return actual;
}

function expectedInventory(manifest) {
  const expected = new Set();
  for (const name of manifest.tables) expected.add(`table|${name}`);
  for (const name of manifest.sequences) expected.add(`sequence|${name}`);
  for (const name of manifest.views) expected.add(`view|${name}`);
  for (const name of manifest.matviews) expected.add(`matview|${name}`);
  for (const name of manifest.functions) expected.add(`function|${name}`);
  return expected;
}

function describeDiff(expected, actual) {
  const missing = [...expected].filter((item) => !actual.has(item));
  const unexpected = [...actual].filter((item) => !expected.has(item));
  return { missing, unexpected };
}

export function adoptObjects(options = {}) {
  const spawn = options.spawnSync ?? defaultSpawnSync;
  const root = options.root ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  const adminUrl = options.adminUrl ?? process.env.ARGUS_ADMIN_DATABASE_URL;
  if (!adminUrl) {
    throw new Error("ARGUS_ADMIN_DATABASE_URL is required");
  }
  const manifestPath = options.manifestPath ?? process.env.ARGUS_ADOPT_MANIFEST;
  if (!manifestPath) {
    throw new Error("ARGUS_ADOPT_MANIFEST is required");
  }
  const manifest = normalizeManifest(JSON.parse(fs.readFileSync(manifestPath, "utf8")), options.schema ?? process.env.ARGUS_ADOPT_SCHEMA ?? "public");
  const owner = options.owner ?? process.env.ARGUS_ADOPT_OWNER ?? `${productSlug(root)}_owner`;
  if (!/^[a-z][a-z0-9_]*$/.test(owner)) {
    throw new Error("adoption owner must be a lowercase identifier");
  }
  const dockerContainer = options.dockerContainer ?? process.env.ARGUS_DOCKER_CONTAINER;
  const actual = queryInventory(spawn, adminUrl, dockerContainer, manifest.schema);
  const expected = expectedInventory(manifest);
  const diff = describeDiff(expected, actual);
  if (diff.missing.length > 0 || diff.unexpected.length > 0) {
    throw new Error(
      `object inventory mismatch: missing=${diff.missing.join(",")} unexpected=${diff.unexpected.join(",")}; refusing transfer`,
    );
  }

  const statements = ["BEGIN;"];
  if (manifest.transferSchemaOwner) {
    statements.push(`ALTER SCHEMA ${quoteIdentifier(manifest.schema)} OWNER TO ${quoteIdentifier(owner)};`);
  }
  for (const name of manifest.tables) {
    statements.push(`ALTER TABLE ${quoteIdentifier(manifest.schema)}.${quoteIdentifier(name)} OWNER TO ${quoteIdentifier(owner)};`);
  }
  for (const name of manifest.sequences) {
    statements.push(`ALTER SEQUENCE ${quoteIdentifier(manifest.schema)}.${quoteIdentifier(name)} OWNER TO ${quoteIdentifier(owner)};`);
  }
  for (const name of manifest.views) {
    statements.push(`ALTER VIEW ${quoteIdentifier(manifest.schema)}.${quoteIdentifier(name)} OWNER TO ${quoteIdentifier(owner)};`);
  }
  for (const name of manifest.matviews) {
    statements.push(`ALTER MATERIALIZED VIEW ${quoteIdentifier(manifest.schema)}.${quoteIdentifier(name)} OWNER TO ${quoteIdentifier(owner)};`);
  }
  for (const name of manifest.functions) {
    statements.push(`ALTER FUNCTION ${quoteIdentifier(manifest.schema)}.${name} OWNER TO ${quoteIdentifier(owner)};`);
  }
  statements.push("COMMIT;");

  const result = runAdminSql(spawn, statements.join("\n"), { adminUrl, dockerContainer });
  if (result.error || result.status !== 0) {
    throw new Error(sanitizeToolError(result.stderr, "object ownership transfer failed"));
  }

  return {
    schema: manifest.schema,
    owner,
    objects: expected.size,
    missing: diff.missing,
    unexpected: diff.unexpected,
  };
}

const invoked = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (invoked) {
  try {
    const result = adoptObjects();
    process.stdout.write(
      `==> adopted ${result.objects} objects in ${result.schema} to ${result.owner}\n`,
    );
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : "object adoption failed"}\n`);
    process.exit(1);
  }
}
