#!/usr/bin/env node
/**
 * First-run after placement: databases, aligned env files, migrations, seed.
 * Same command on Ubuntu 24.04 and native Windows 11 PowerShell:
 *   node scripts/bootstrap-dev.mjs
 * Does not require Git, Bash, or WSL.
 */
import { spawnSync as defaultSpawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const DB_URL_DEFAULT = "postgres://postgres@127.0.0.1:5432/argus";
const TEST_DB_URL_DEFAULT = "postgres://postgres@127.0.0.1:5432/argus_test";
const FRONTEND_URL_DEFAULT = "http://127.0.0.1:3000";
const BACKEND_URL_DEFAULT = "http://127.0.0.1:3001";
const DOCKER_CONTAINER = "postgres_db";

export function dbNameOf(url) {
  const withoutQuery = url.split("?")[0];
  const afterHost = withoutQuery.replace(/^[^/]*\/\/[^/]*\//, "");
  return afterHost;
}

export function setEnvKey(file, key, value) {
  if (!fs.existsSync(file)) {
    fs.writeFileSync(file, "", "utf8");
  }
  const text = fs.readFileSync(file, "utf8");
  const lines = text.length === 0 ? [] : text.split(/\r?\n/);
  if (lines.some((line) => line.startsWith(`${key}=`))) {
    return "kept";
  }
  const prefix = text.length === 0 || text.endsWith("\n") ? "" : "\n";
  fs.appendFileSync(file, `${prefix}${key}=${value}\n`, "utf8");
  return "wrote";
}

function missingBin(result) {
  return Boolean(result.error && result.error.code === "ENOENT");
}

function hasCommand(spawn, cmd, args) {
  const result = spawn(cmd, args, { encoding: "utf8" });
  if (missingBin(result)) {
    return false;
  }
  return result.status === 0;
}

function dockerHasContainer(spawn) {
  const result = spawn("docker", ["ps", "--format", "{{.Names}}"], {
    encoding: "utf8",
  });
  if (missingBin(result) || result.status !== 0) {
    return false;
  }
  const names = (result.stdout ?? "").split(/\r?\n/).map((line) => line.trim());
  return names.includes(DOCKER_CONTAINER);
}

export function checkTools(spawn) {
  if (!hasCommand(spawn, "openssl", ["version"])) {
    return { ok: false, tool: "openssl" };
  }
  if (!hasCommand(spawn, "dotnet", ["--version"])) {
    return { ok: false, tool: "dotnet" };
  }
  const docker = dockerHasContainer(spawn);
  const psql = hasCommand(spawn, "psql", ["--version"]);
  const createdb = hasCommand(spawn, "createdb", ["--version"]);
  if (!docker && !(psql && createdb)) {
    return { ok: false, tool: "postgres" };
  }
  return { ok: true, docker };
}

function opensslHex(spawn) {
  const result = spawn("openssl", ["rand", "-hex", "32"], { encoding: "utf8" });
  if (result.status !== 0 || !result.stdout) {
    throw new Error("openssl rand failed");
  }
  return result.stdout.trim();
}

function dbExists(spawn, url, useDocker) {
  const name = dbNameOf(url);
  if (useDocker) {
    const result = spawn(
      "docker",
      [
        "exec",
        DOCKER_CONTAINER,
        "psql",
        "-U",
        "postgres",
        "-tAc",
        `SELECT 1 FROM pg_database WHERE datname = '${name}'`,
      ],
      { encoding: "utf8" },
    );
    return (result.stdout ?? "").trim().includes("1");
  }
  const result = spawn("psql", [url, "-tAc", "SELECT 1"], { encoding: "utf8" });
  return result.status === 0;
}

function ensureDb(spawn, url, useDocker, log) {
  const name = dbNameOf(url);
  if (dbExists(spawn, url, useDocker)) {
    log(`==> database ${name} already exists`);
    return;
  }
  if (useDocker) {
    const result = spawn(
      "docker",
      ["exec", DOCKER_CONTAINER, "psql", "-U", "postgres", "-c", `CREATE DATABASE "${name}";`],
      { encoding: "utf8" },
    );
    if (result.status !== 0) {
      throw new Error(result.stderr || `failed to create database ${name}`);
    }
  } else {
    const result = spawn("createdb", [name], { encoding: "utf8" });
    if (result.status !== 0) {
      throw new Error(result.stderr || `failed to create database ${name}`);
    }
  }
  log(`==> created database ${name}`);
}

export function runBootstrap(options = {}) {
  const spawn = options.spawnSync ?? defaultSpawnSync;
  const log = options.log ?? ((msg) => process.stdout.write(`${msg}\n`));
  const error = options.error ?? ((msg) => process.stderr.write(`${msg}\n`));
  const fail = (message) => {
    error(message);
    return { ok: false, message };
  };
  const root = options.root ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  const env = options.env ?? process.env;
  const skipSeed = options.skipSeed === true;
  const backend = path.join(root, "backend");
  const frontend = path.join(root, "frontend");

  const tools = checkTools(spawn);
  if (!tools.ok) {
    if (tools.tool === "openssl") {
      return fail("error: missing openssl");
    }
    if (tools.tool === "dotnet") {
      return fail("error: missing dotnet");
    }
    return fail(
      "error: no postgres_db container and no psql/createdb; start PostgreSQL (Docker container named postgres_db) or install PostgreSQL client tools",
    );
  }

  const dbUrl = env.ARGUS_DATABASE_URL ?? DB_URL_DEFAULT;
  const testDbUrl = env.ARGUS_TEST_DATABASE_URL ?? TEST_DB_URL_DEFAULT;
  const frontendUrl = env.ARGUS_FRONTEND_URL ?? FRONTEND_URL_DEFAULT;
  const backendUrl = env.ARGUS_BACKEND_URL ?? BACKEND_URL_DEFAULT;

  log("==> [1/5] Dev database");
  ensureDb(spawn, dbUrl, tools.docker, log);
  log("==> [2/5] Isolation-test database");
  ensureDb(spawn, testDbUrl, tools.docker, log);

  log("==> [3/5] Environment files");
  const apiSecret = opensslHex(spawn);
  const backendEnv = path.join(backend, ".env");
  const frontendEnv = path.join(frontend, ".env");
  const backendKeys = [
    ["ARGUS_DATABASE_URL", dbUrl],
    ["ARGUS_TEST_DATABASE_URL", testDbUrl],
    ["ARGUS_HOST", "127.0.0.1"],
    ["ARGUS_PORT", "3001"],
    ["ARGUS_API_SECRET", apiSecret],
    ["ARGUS_FRONTEND_URL", frontendUrl],
    ["ARGUS_RESEND_API_KEY", "dev-placeholder-key-not-for-production"],
    ["ARGUS_RESEND_FROM", "local@example.com"],
    ["ARGUS_BILLING_WEBHOOK_SECRET", opensslHex(spawn)],
  ];
  const frontendKeys = [
    ["ARGUS_BACKEND_URL", backendUrl],
    ["ARGUS_API_SECRET", apiSecret],
    ["ARGUS_FRONTEND_URL", frontendUrl],
    ["ARGUS_AUTH_SECRET", opensslHex(spawn)],
    ["ARGUS_AUTH_TRUST_HOST", "true"],
  ];
  for (const [key, value] of backendKeys) {
    const action = setEnvKey(backendEnv, key, value);
    log(`     ${action === "wrote" ? "wrote" : "kept "} ${key} ${action === "wrote" ? `to ${backendEnv}` : "(already set)"}`);
  }
  for (const [key, value] of frontendKeys) {
    const action = setEnvKey(frontendEnv, key, value);
    log(`     ${action === "wrote" ? "wrote" : "kept "} ${key} ${action === "wrote" ? `to ${frontendEnv}` : "(already set)"}`);
  }

  if (!skipSeed) {
    log("==> [4/5] Migrations + seed accounts");
    const seed = spawn("dotnet", ["run", "--", "seed"], {
      encoding: "utf8",
      cwd: backend,
    });
    if (seed.stdout) {
      process.stdout.write(seed.stdout);
    }
    if (seed.status !== 0) {
      if (seed.stderr) {
        process.stderr.write(seed.stderr);
      }
      return fail("error: dotnet run -- seed failed");
    }
  }

  log("==> [5/5] Ready");
  log("");
  log("  Backend:   cd backend");
  log("             dotnet run");
  log("  Frontend:  cd frontend");
  log("             npm ci");
  log(`             npm run dev   (open ${frontendUrl})`);
  log("  Backend tests:  cd backend && dotnet test tests/argus.Server.Tests");
  log(`                  (uses ${testDbUrl}, already created above)`);
  log("  Seed logins are printed above; passwords change each run unless pinned");
  log("  via ARGUS_SEED_DEV_PASSWORD / ARGUS_SEED_USER_PASSWORD.");
  return { ok: true };
}

const invoked = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (invoked) {
  try {
    const result = runBootstrap();
    if (!result.ok) {
      process.exit(1);
    }
  } catch (err) {
    process.stderr.write(`${err instanceof Error ? err.message : "bootstrap failed"}\n`);
    process.exit(1);
  }
}
