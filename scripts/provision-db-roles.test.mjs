import assert from "node:assert/strict";
import { test } from "node:test";
import { provisionRoles, sanitizeToolError } from "./provision-db-roles.mjs";

function ok(stdout = "") {
  return { status: 0, stdout, stderr: "" };
}

function fakeSpawn({ attributes = "false", exists = false, roleExists = () => exists, attributesFor } = {}) {
  const calls = [];
  const spawn = (command, args, options = {}) => {
    const sql = options.input ?? "";
    calls.push({ command, args, options, sql });
    const role = sql.match(/rolname = '([^']+)'/)?.[1];
    if (sql.includes("SELECT 1 FROM pg_roles WHERE rolname = ")) {
      return ok(roleExists(role) ? "1" : "0");
    }
    if (sql.includes("SELECT rolcanlogin")) {
      return ok(attributesFor?.(role) ?? attributes);
    }
    if (sql.includes("BEGIN;")) {
      return ok();
    }
    return ok();
  };
  return { spawn, calls };
}

const root = new URL("..", import.meta.url).pathname;

test("provisionRoles creates missing roles once without printing passwords", () => {
  const { spawn, calls } = fakeSpawn({ exists: false });
  const lines = [];
  const result = provisionRoles({
    spawnSync: spawn,
    root,
    adminUrl: "postgres://postgres:secret@127.0.0.1:5432/argus_test",
    dockerContainer: "argus_pg16_t10",
    migratorPassword: "migrator-password-value",
    runtimePassword: "runtime-password-value",
    log: (line) => lines.push(line),
  });

  assert.equal(result.created.owner, true);
  assert.equal(result.created.migrator, true);
  assert.equal(result.created.runtime, true);
  const sql = calls.map((call) => call.sql).join("\n");
  assert.match(sql, /CREATE ROLE argus_owner NOLOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT/);
  assert.match(sql, /CREATE ROLE argus_migrator LOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT PASSWORD 'migrator-password-value'/);
  assert.match(sql, /CREATE ROLE argus_runtime LOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT NOCREATEDB NOCREATEROLE PASSWORD 'runtime-password-value'/);
  assert.match(sql, /GRANT argus_owner TO argus_migrator/);
  const output = lines.join("\n");
  assert.doesNotMatch(output, /migrator-password-value|runtime-password-value/);
  for (const call of calls) {
    assert.doesNotMatch(call.args.join(" "), /migrator-password-value|runtime-password-value|admin-secret/);
    if (call.command === "docker") {
      assert.equal(call.options.env.PGPASSWORD, "secret");
      assert.match(call.args.join(" "), /-e PGPASSWORD/);
    }
  }
  assert.match(calls.map((call) => call.sql).join("\n"), /CREATE ROLE argus_owner/);
});

test("provisionRoles keeps existing matching roles and never resets their password", () => {
  const spawn = (command, args, options = {}) => {
    const sql = options.input ?? "";
    if (sql.includes("SELECT 1 FROM pg_roles WHERE rolname = ")) {
      return ok("1");
    }
    if (sql.includes("SELECT rolcanlogin")) {
      const isOwner = sql.includes("'argus_owner'");
      return ok(isOwner ? "false|false|false|false|false|false" : "true|false|false|false|false|false");
    }
    if (sql.includes("BEGIN;")) {
      return ok();
    }
    return ok();
  };
  const lines = [];
  const result = provisionRoles({
    spawnSync: spawn,
    root,
    adminUrl: "postgres://postgres:secret@127.0.0.1:5432/argus_test",
    dockerContainer: "argus_pg16_t10",
    migratorPassword: "ignored-migrator",
    runtimePassword: "ignored-runtime",
    log: (line) => lines.push(line),
  });

  assert.equal(result.created.owner, false);
  assert.equal(result.created.migrator, false);
  assert.equal(result.created.runtime, false);
  assert.match(lines.join("\n"), /kept argus_owner/);
});

test("provisionRoles refuses to repair an existing role with unexpected attributes", () => {
  const { spawn } = fakeSpawn({ exists: true, attributes: "true|false|false|false|false|true" });
  assert.throws(
    () =>
      provisionRoles({
        spawnSync: spawn,
        root,
        adminUrl: "postgres://postgres:secret@127.0.0.1:5432/argus_test",
        dockerContainer: "argus_pg16_t10",
        log: () => {},
      }),
    /unexpected attributes/,
  );
});

test("provisionRoles rejects a new role set without generated passwords", () => {
  const { spawn } = fakeSpawn({ exists: false });
  assert.throws(
    () =>
      provisionRoles({
        spawnSync: spawn,
        root,
        adminUrl: "postgres://postgres:secret@127.0.0.1:5432/argus_test",
        dockerContainer: "argus_pg16_t10",
        log: () => {},
      }),
    /missing migrator login role requires a nonempty migrator password/,
  );
});

test("provisionRoles requires a password for each missing login role independently", () => {
  const { spawn, calls } = fakeSpawn({ exists: false });
  assert.throws(
    () =>
      provisionRoles({
        spawnSync: spawn,
        root,
        adminUrl: "postgres://postgres:secret@127.0.0.1:5432/argus_test",
        dockerContainer: "argus_pg16_t10",
        runtimePassword: "runtime-only",
        log: () => {},
      }),
    /missing migrator login role/,
  );
  assert.equal(calls.some((call) => call.sql.includes("CREATE ROLE")), false);
});

test("provisionRoles refuses an existing owner with a missing runtime login password", () => {
  const { spawn, calls } = fakeSpawn({
    roleExists: (role) => role === "argus_owner",
    attributes: "false|false|false|false|false|false",
  });
  assert.throws(
    () =>
      provisionRoles({
        spawnSync: spawn,
        root,
        adminUrl: "postgres://postgres:secret@127.0.0.1:5432/argus_test",
        dockerContainer: "argus_pg16_t10",
        migratorPassword: "migrator-only",
        log: () => {},
      }),
    /missing runtime login role/,
  );
  assert.equal(calls.some((call) => call.sql.includes("CREATE ROLE")), false);
});

test("sanitizeToolError redacts connection URLs and password literals", () => {
  const message = sanitizeToolError(
    "psql: error: postgres://postgres:admin-secret@127.0.0.1/argus PASSWORD 'runtime-secret'\nsecond line",
    "provisioning failed",
  );
  assert.match(message, /^provisioning failed: /);
  assert.doesNotMatch(message, /admin-secret|runtime-secret|postgres:\/\/postgres/);
  assert.match(message, /\[redacted-url\]/);
  assert.doesNotMatch(message, /second line/);
});

test("sanitizeToolError redacts doubled-quote and whitespace-containing secrets", () => {
  const message = sanitizeToolError(
    "psql: error: PASSWORD 'prefix''VISIBLE_SUFFIX' PGPASSWORD=first VISIBLE_SUFFIX",
    "provisioning failed",
  );
  assert.doesNotMatch(message, /VISIBLE_SUFFIX|first VISIBLE_SUFFIX|prefix/);
  assert.match(message, /PASSWORD '\[redacted\]'/);
  assert.match(message, /PGPASSWORD=\[redacted\]/);
});

test("provisionRoles sends a native psql password only through its environment", () => {
  const calls = [];
  const spawn = (command, args, options = {}) => {
    calls.push({ command, args, options });
    return { status: 1, stdout: "", stderr: "psql: could not connect" };
  };
  assert.throws(
    () =>
      provisionRoles({
        spawnSync: spawn,
        root,
        adminUrl: "postgres://postgres:admin-secret@127.0.0.1:5432/argus_test",
        migratorPassword: "migrator-password-value",
        runtimePassword: "runtime-password-value",
        log: () => {},
      }),
    /role query failed/,
  );
  assert.equal(calls[0].command, "psql");
  assert.equal(calls[0].options.env.PGPASSWORD, "admin-secret");
  assert.doesNotMatch(calls[0].args.join(" "), /admin-secret|migrator-password-value|runtime-password-value/);
  assert.doesNotMatch(calls[0].options.input, /admin-secret/);
});

test("provisionRoles treats an explicit none container as native psql", () => {
  const calls = [];
  const spawn = (command, args, options = {}) => {
    calls.push({ command, args, options });
    const sql = options.input ?? "";
    if (sql.includes("SELECT 1 FROM pg_roles WHERE rolname = ")) {
      return ok("0");
    }
    return ok();
  };
  provisionRoles({
    spawnSync: spawn,
    root,
    adminUrl: "postgres://postgres:secret@127.0.0.1:5432/argus_test",
    dockerContainer: "none",
    migratorPassword: "migrator-password-value",
    runtimePassword: "runtime-password-value",
    log: () => {},
  });
  assert.equal(calls.every((call) => call.command === "psql"), true);
  assert.equal(calls.some((call) => call.args.includes("none")), false);
});
