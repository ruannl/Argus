import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { runBootstrap } from "./bootstrap-dev.mjs";

const ADMIN_URL = "postgres://postgres:admin-secret@127.0.0.1:55432/argus_bootstrap_t10";
const TEST_URL = "postgres://postgres:admin-secret@127.0.0.1:55432/argus_test";

function ok(stdout = "") {
  return { status: 0, stdout, stderr: "" };
}

function makeRoot(brand = 'public static class Brand { public const string ProductSlug = "argus"; }') {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "argus-bootstrap-"));
  fs.mkdirSync(path.join(root, "backend"), { recursive: true });
  fs.mkdirSync(path.join(root, "frontend"), { recursive: true });
  fs.writeFileSync(path.join(root, "backend", "Brand.cs"), brand);
  return root;
}

function makeSpawn({ roleExists = false } = {}) {
  let hexCounter = 0;
  const calls = [];
  const spawn = (command, args, options = {}) => {
    calls.push({ command, args, options });
    if (command === "openssl") {
      if (args[0] === "version") {
        return ok("OpenSSL 3.0.0");
      }
      hexCounter += 1;
      return ok(hexCounter.toString(16).padStart(64, "0"));
    }
    if (command === "dotnet" && args[0] === "--version") {
      return ok("10.0.0");
    }
    if (command === "docker" && args[0] === "ps") {
      return ok("argus_pg16_t10\npostgres_db\n");
    }
    if (command === "docker" && args[0] === "exec") {
      const sql = options.input ?? "";
      if (sql.includes("SELECT 1 FROM pg_roles WHERE rolname = ")) {
        return ok(roleExists ? "1" : "0");
      }
      if (sql.includes("SELECT rolcanlogin")) {
        const isOwner = sql.includes("'argus_owner'");
        return ok(isOwner ? "false|false|false|false|false|false" : "true|false|false|false|false|false");
      }
      if (sql.includes("pg_database WHERE datname")) {
        return ok("0");
      }
      return ok();
    }
    if (command === "dotnet" && args[0] === "run") {
      return ok("");
    }
    return ok();
  };
  return { spawn, calls };
}

function dotnetRuns(calls) {
  return calls.filter((call) => call.command === "dotnet" && call.args[0] === "run");
}

test("bootstrap writes runtime-only backend/.env and delivers secrets per child", () => {
  const root = makeRoot();
  const { spawn, calls } = makeSpawn({ roleExists: false });
  const lines = [];
  const result = runBootstrap({
    spawnSync: spawn,
    root,
    env: {
      ARGUS_DATABASE_URL: ADMIN_URL,
      ARGUS_TEST_DATABASE_URL: TEST_URL,
      ARGUS_DOCKER_CONTAINER: "argus_pg16_t10",
      ARGUS_SEED_DEV_PASSWORD: "dev-seed-password",
      ARGUS_SEED_USER_PASSWORD: "user-seed-password",
    },
    log: (line) => lines.push(line),
    output: (line) => lines.push(line),
    error: (line) => lines.push(line),
  });

  assert.equal(result.ok, true, lines.join("\n"));
  const backendEnv = fs.readFileSync(path.join(root, "backend", ".env"), "utf8");
  const testEnv = fs.readFileSync(path.join(root, "backend", ".env.test"), "utf8");
  const maintenanceEnv = fs.readFileSync(path.join(root, "backend", ".env.maintenance"), "utf8");
  assert.match(backendEnv, /ARGUS_DATABASE_URL=postgres:\/\/argus_runtime:/);
  assert.doesNotMatch(backendEnv, /ARGUS_TEST_DATABASE_URL|ARGUS_MAINTENANCE_DATABASE_URL/);
  assert.match(testEnv, /ARGUS_TEST_DATABASE_URL=postgres:\/\/postgres:/);
  assert.match(maintenanceEnv, /ARGUS_DATABASE_URL=postgres:\/\/argus_migrator:/);
  assert.equal(fs.statSync(path.join(root, "backend", ".env.test")).mode & 0o777, 0o600);
  assert.equal(fs.statSync(path.join(root, "backend", ".env.maintenance")).mode & 0o777, 0o600);

  const runs = dotnetRuns(calls);
  assert.equal(runs.length, 2);
  const migrate = runs[0];
  const seed = runs[1];
  assert.equal(migrate.options.env.ARGUS_DATABASE_URL, maintenanceEnv.trim().slice("ARGUS_DATABASE_URL=".length));
  assert.equal(migrate.options.env.ARGUS_TEST_DATABASE_URL, undefined);
  assert.equal(migrate.options.env.ARGUS_API_SECRET, undefined);
  assert.equal(seed.options.env.ARGUS_SEED_DEV_PASSWORD, "dev-seed-password");
  assert.equal(seed.options.env.ARGUS_SEED_USER_PASSWORD, "user-seed-password");
  assert.doesNotMatch(lines.join("\n"), /postgres:\/\/postgres:admin-secret@/);
});

test("bootstrap preserves existing runtime and maintenance values on a re-run", () => {
  const root = makeRoot();
  fs.writeFileSync(
    path.join(root, "backend", ".env"),
    "ARGUS_DATABASE_URL=postgres://argus_runtime:existing@127.0.0.1:55432/argus_bootstrap_t10?search_path=public\n",
  );
  fs.writeFileSync(
    path.join(root, "backend", ".env.maintenance"),
    "ARGUS_DATABASE_URL=postgres://argus_migrator:existing@127.0.0.1:55432/argus_bootstrap_t10?search_path=public\n",
  );
  const { spawn } = makeSpawn({ roleExists: true });
  const result = runBootstrap({
    spawnSync: spawn,
    root,
    env: {
      ARGUS_DATABASE_URL: ADMIN_URL,
      ARGUS_TEST_DATABASE_URL: TEST_URL,
      ARGUS_DOCKER_CONTAINER: "argus_pg16_t10",
    },
    log: () => {},
    output: () => {},
    error: () => {},
  });

  assert.equal(result.ok, true);
  assert.match(
    fs.readFileSync(path.join(root, "backend", ".env"), "utf8"),
    /ARGUS_DATABASE_URL=postgres:\/\/argus_runtime:existing@/,
  );
  assert.match(
    fs.readFileSync(path.join(root, "backend", ".env.maintenance"), "utf8"),
    /ARGUS_DATABASE_URL=postgres:\/\/argus_migrator:existing@/,
  );
});

test("bootstrap blocks a legacy runtime .env that still carries the test URL", () => {
  const root = makeRoot();
  fs.writeFileSync(
    path.join(root, "backend", ".env"),
    "ARGUS_TEST_DATABASE_URL=postgres://postgres@127.0.0.1:5432/argus_test\n",
  );
  const { spawn, calls } = makeSpawn({ roleExists: true });
  const result = runBootstrap({
    spawnSync: spawn,
    root,
    env: { ARGUS_DOCKER_CONTAINER: "argus_pg16_t10" },
    log: () => {},
    output: () => {},
    error: () => {},
  });

  assert.equal(result.ok, false);
  assert.match(result.message, /relocate it/);
  assert.equal(dotnetRuns(calls).length, 0);
});

test("bootstrap blocks a privileged runtime URL instead of silently keeping it", () => {
  const root = makeRoot();
  fs.writeFileSync(
    path.join(root, "backend", ".env"),
    "ARGUS_DATABASE_URL=postgres://postgres:admin-secret@127.0.0.1:55432/argus_bootstrap_t10\n",
  );
  const { spawn, calls } = makeSpawn({ roleExists: true });
  const result = runBootstrap({
    spawnSync: spawn,
    root,
    env: {
      ARGUS_DATABASE_URL: ADMIN_URL,
      ARGUS_DOCKER_CONTAINER: "argus_pg16_t10",
    },
    log: () => {},
    output: () => {},
    error: () => {},
  });

  assert.equal(result.ok, false);
  assert.match(result.message, /not the argus_runtime runtime login/);
  assert.equal(dotnetRuns(calls).length, 0);
});
