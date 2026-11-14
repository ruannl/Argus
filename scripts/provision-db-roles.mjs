#!/usr/bin/env node
/**
 * Provision the three database roles for one Argus database. It creates a
 * missing role once; an existing role must already match the contract, or the
 * script reports drift and stops without changing its password. Passwords are
 * read from the process environment and are never written to stdout.
 *
 * CLI:
 *   ARGUS_ADMIN_DATABASE_URL=postgres://... \
 *   ARGUS_MIGRATOR_PASSWORD=... ARGUS_RUNTIME_PASSWORD=... \
 *   node scripts/provision-db-roles.mjs
 *
 * Set ARGUS_DOCKER_CONTAINER=argus_pg16_t10 to use the container's psql when
 * no local psql client is installed.
 */
import { spawnSync as defaultSpawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

export function productSlug(root) {
  const brand = fs.readFileSync(path.join(root, "backend", "Brand.cs"), "utf8");
  const match = brand.match(/ProductSlug\s*=\s*"([^"]+)"/);
  if (!match) {
    throw new Error("could not read ProductSlug from backend/Brand.cs");
  }
  return match[1];
}

export function databaseName(url) {
  const withoutQuery = url.split("?")[0];
  return withoutQuery.replace(/^[^/]*\/\/[^/]*\//, "");
}

const _toolEnvironmentKeys = [
  "PATH", "HOME", "USER", "TMPDIR", "TEMP", "TMP", "LANG", "LC_ALL",
  "DOCKER_HOST", "DOCKER_CONFIG",
];

function toolEnvironment(extra = {}) {
  const env = {};
  for (const key of _toolEnvironmentKeys) {
    if (process.env[key] !== undefined) {
      env[key] = process.env[key];
    }
  }
  return { ...env, ...extra };
}

function parseDatabaseUrl(url) {
  const parsed = new URL(url);
  return {
    host: parsed.hostname || undefined,
    port: parsed.port || undefined,
    user: parsed.username ? decodeURIComponent(parsed.username) : undefined,
    password: parsed.password ? decodeURIComponent(parsed.password) : undefined,
    database: parsed.pathname.replace(/^\//, ""),
    sslmode: parsed.searchParams.get("sslmode") ?? undefined,
  };
}

/**
 * Runs admin SQL without putting SQL or credentials in process arguments.
 * SQL travels on stdin; the password travels in the child environment only.
 */
export function sanitizeToolError(stderr, fallback) {
  const first = String(stderr ?? "")
    .split(/\r?\n/)
    .map((line) => line.trim())
    .find((line) => line.length > 0) ?? "";
  const safe = first
    .replace(/postgres(?:ql)?:\/\/\S+/gi, "[redacted-url]")
    .replace(/PGPASSWORD=.*$/gi, "PGPASSWORD=[redacted]")
    .replace(/PASSWORD\s+'(?:[^']|'')*'/gi, "PASSWORD '[redacted]'")
    .slice(0, 160);
  return safe.length > 0 ? `${fallback}: ${safe}` : fallback;
}

export function runAdminSql(spawn, input, options) {
  const connection = parseDatabaseUrl(options.adminUrl);
  const dockerContainer = options.dockerContainer === "none" ? undefined : options.dockerContainer;
  if (dockerContainer) {
    const env = toolEnvironment();
    if (connection.password !== undefined) {
      env.PGPASSWORD = connection.password;
    }
    const args = ["exec", "-i"];
    if (connection.password !== undefined) {
      args.push("-e", "PGPASSWORD");
    }
    args.push(
      dockerContainer,
      "psql",
      "-U",
      connection.user ?? "postgres",
      "-d",
      connection.database,
      "-X",
      "-q",
      "-t",
      "-A",
      "-v",
      "ON_ERROR_STOP=1",
      "-f",
      "-",
    );
    return spawn("docker", args, { encoding: "utf8", env, input });
  }

  const env = toolEnvironment();
  if (connection.host !== undefined) env.PGHOST = connection.host;
  if (connection.port !== undefined) env.PGPORT = connection.port;
  if (connection.user !== undefined) env.PGUSER = connection.user;
  if (connection.password !== undefined) env.PGPASSWORD = connection.password;
  if (connection.database !== "") env.PGDATABASE = connection.database;
  if (connection.sslmode !== undefined) env.PGSSLMODE = connection.sslmode;

  return spawn("psql", ["-X", "-q", "-t", "-A", "-v", "ON_ERROR_STOP=1", "-f", "-"], {
    encoding: "utf8",
    env,
    input,
  });
}

function queryValue(spawn, sql, options) {
  const result = runAdminSql(spawn, sql, options);
  if (result.error || result.status !== 0) {
    throw new Error(sanitizeToolError(result.stderr, "role query failed"));
  }
  return (result.stdout ?? "").trim();
}

function roleExists(spawn, role, options) {
  return queryValue(
    spawn,
    `SELECT 1 FROM pg_roles WHERE rolname = '${role}'`,
    options,
  ) === "1";
}

function requireAttributes(spawn, role, expectations, options) {
  const row = queryValue(
    spawn,
    `SELECT rolcanlogin || '|' || rolsuper || '|' || rolbypassrls || '|' || rolinherit || '|' || rolcreatedb || '|' || rolcreaterole
     FROM pg_roles WHERE rolname = '${role}'`,
    options,
  );
  if (row !== expectations) {
    throw new Error(`role ${role} exists with unexpected attributes (${row}); refusing to repair silently`);
  }
}

function quoteLiteral(value) {
  return `'${String(value).replaceAll("'", "''")}'`;
}

function quoteIdentifier(value) {
  return `"${String(value).replaceAll('"', '""')}"`;
}

export function provisionRoles(options = {}) {
  const spawn = options.spawnSync ?? defaultSpawnSync;
  const log = options.log ?? ((message) => process.stdout.write(`${message}\n`));
  const root = options.root ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  const adminUrl = options.adminUrl ?? process.env.ARGUS_ADMIN_DATABASE_URL;
  if (!adminUrl) {
    throw new Error("ARGUS_ADMIN_DATABASE_URL is required");
  }

  const slug = options.slug ?? productSlug(root);
  const roles = {
    owner: `${slug}_owner`,
    migrator: `${slug}_migrator`,
    runtime: `${slug}_runtime`,
  };
  const configuredContainer = options.dockerContainer ?? process.env.ARGUS_DOCKER_CONTAINER;
  const sqlOptions = {
    adminUrl,
    dockerContainer: configuredContainer === "none" ? undefined : configuredContainer,
  };

  const ownerExists = roleExists(spawn, roles.owner, sqlOptions);
  const migratorExists = roleExists(spawn, roles.migrator, sqlOptions);
  const runtimeExists = roleExists(spawn, roles.runtime, sqlOptions);

  if (ownerExists) {
    requireAttributes(spawn, roles.owner, "false|false|false|false|false|false", sqlOptions);
  }
  if (migratorExists) {
    requireAttributes(spawn, roles.migrator, "true|false|false|false|false|false", sqlOptions);
  }
  if (runtimeExists) {
    requireAttributes(spawn, roles.runtime, "true|false|false|false|false|false", sqlOptions);
  }

  if (!migratorExists && !options.migratorPassword) {
    throw new Error("a missing migrator login role requires a nonempty migrator password");
  }
  if (!runtimeExists && !options.runtimePassword) {
    throw new Error("a missing runtime login role requires a nonempty runtime password");
  }

  const statements = ["BEGIN;"];
  if (!ownerExists) {
    statements.push(`CREATE ROLE ${roles.owner} NOLOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT;`);
  }
  if (!migratorExists) {
    statements.push(
      `CREATE ROLE ${roles.migrator} LOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT PASSWORD ${quoteLiteral(options.migratorPassword)};`,
    );
  }
  if (!runtimeExists) {
    statements.push(
      `CREATE ROLE ${roles.runtime} LOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT NOCREATEDB NOCREATEROLE PASSWORD ${quoteLiteral(options.runtimePassword)};`,
    );
  }
  statements.push(`GRANT ${roles.owner} TO ${roles.migrator};`);
  statements.push(
    `GRANT CONNECT ON DATABASE ${quoteIdentifier(databaseName(adminUrl))} TO ${roles.owner}, ${roles.migrator}, ${roles.runtime};`,
  );
  statements.push("COMMIT;");

  const result = runAdminSql(spawn, statements.join("\n"), sqlOptions);
  if (result.error || result.status !== 0) {
    throw new Error(sanitizeToolError(result.stderr, "role provisioning failed"));
  }

  log(`==> roles ${roles.owner}/${roles.migrator}/${roles.runtime}`);
  log(`     ${ownerExists ? "kept" : "created"} ${roles.owner}`);
  log(`     ${migratorExists ? "kept" : "created"} ${roles.migrator}`);
  log(`     ${runtimeExists ? "kept" : "created"} ${roles.runtime}`);

  return {
    roles,
    created: {
      owner: !ownerExists,
      migrator: !migratorExists,
      runtime: !runtimeExists,
    },
  };
}

const invoked = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (invoked) {
  try {
    provisionRoles({
      adminUrl: process.env.ARGUS_ADMIN_DATABASE_URL,
      migratorPassword: process.env.ARGUS_MIGRATOR_PASSWORD,
      runtimePassword: process.env.ARGUS_RUNTIME_PASSWORD,
    });
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : "role provisioning failed"}\n`);
    process.exit(1);
  }
}
