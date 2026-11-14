#!/usr/bin/env node
/**
 * First-run after placement: databases, roles, aligned env files, migrations
 * and seed. Same command on Ubuntu 24.04 and native Windows 11 PowerShell:
 *   node scripts/bootstrap-dev.mjs
 *
 * The parent process never spreads its environment into children. Each child
 * receives only the values it needs: migrate/seed receive the migrator URL,
 * the test runner receives the test-maintenance URL, and the normal runtime
 * receives only the restricted runtime URL.
 */
import { spawnSync as defaultSpawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { provisionRoles, runAdminSql, sanitizeToolError } from "./provision-db-roles.mjs";

const DB_URL_DEFAULT = "postgres://postgres@127.0.0.1:5432/argus";
const TEST_DB_URL_DEFAULT = "postgres://postgres@127.0.0.1:5432/argus_test";
const FRONTEND_URL_DEFAULT = "http://127.0.0.1:3000";
const BACKEND_URL_DEFAULT = "http://127.0.0.1:3001";
const DOCKER_CONTAINER_DEFAULT = "postgres_db";

export function dbNameOf(url) {
  const withoutQuery = url.split("?")[0];
  return withoutQuery.replace(/^[^/]*\/\/[^/]*\//, "");
}

export function setEnvKey(file, key, value) {
  if (!fs.existsSync(file)) {
    fs.writeFileSync(file, "", { encoding: "utf8", mode: 0o600 });
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

export function readEnvFile(file) {
  if (!fs.existsSync(file)) {
    return {};
  }
  const values = {};
  for (const line of fs.readFileSync(file, "utf8").split(/\r?\n/)) {
    const trimmed = line.trim();
    if (trimmed.length === 0 || trimmed.startsWith("#") || !trimmed.includes("=")) {
      continue;
    }
    const separator = trimmed.indexOf("=");
    values[trimmed.slice(0, separator).trim()] = trimmed.slice(separator + 1).trim();
  }
  return values;
}

function missingBin(result) {
  return Boolean(result.error && result.error.code === "ENOENT");
}

function hasCommand(spawn, cmd, args) {
  const result = spawn(cmd, args, { encoding: "utf8", env: childEnvironment(process.env, {}) });
  return !missingBin(result) && result.status === 0;
}

function dockerHasContainer(spawn, container) {
  const result = spawn("docker", ["ps", "--format", "{{.Names}}"], {
    encoding: "utf8",
    env: childEnvironment(process.env, {}),
  });
  if (missingBin(result) || result.status !== 0) {
    return false;
  }
  return (result.stdout ?? "").split(/\r?\n/).map((line) => line.trim()).includes(container);
}

export function checkTools(spawn, container = DOCKER_CONTAINER_DEFAULT) {
  if (!hasCommand(spawn, "openssl", ["version"])) {
    return { ok: false, tool: "openssl" };
  }
  if (!hasCommand(spawn, "dotnet", ["--version"])) {
    return { ok: false, tool: "dotnet" };
  }
  const docker = container !== null && dockerHasContainer(spawn, container);
  const psql = hasCommand(spawn, "psql", ["--version"]);
  if (!docker && !psql) {
    return { ok: false, tool: "postgres" };
  }
  return { ok: true, docker };
}

function opensslHex(spawn) {
  const result = spawn("openssl", ["rand", "-hex", "32"], {
    encoding: "utf8",
    env: childEnvironment(process.env, {}),
  });
  if (result.status !== 0 || !result.stdout) {
    throw new Error("openssl rand failed");
  }
  return result.stdout.trim();
}

function replaceUserInfo(url, userName, password) {
  const parsed = new URL(url);
  parsed.username = userName;
  parsed.password = password;
  return parsed.toString();
}

function addSearchPath(url, schema = "public") {
  const parsed = new URL(url);
  parsed.searchParams.set("search_path", schema);
  return parsed.toString();
}

function roleOf(url) {
  try {
    return decodeURIComponent(new URL(url).username);
  } catch {
    return "";
  }
}

function adminMaintenanceUrl(url) {
  const parsed = new URL(url);
  parsed.pathname = "/postgres";
  return parsed.toString();
}

function adminSql(spawn, url, useDocker, container, sql) {
  return runAdminSql(spawn, sql, {
    adminUrl: url,
    dockerContainer: useDocker ? container : undefined,
  });
}

function ensureDb(spawn, url, useDocker, container, log) {
  const name = dbNameOf(url);
  const maintenanceUrl = adminMaintenanceUrl(url);
  const exists = adminSql(
    spawn,
    maintenanceUrl,
    useDocker,
    container,
    `SELECT 1 FROM pg_database WHERE datname = '${name}'`,
  );
  if ((exists.stdout ?? "").trim() === "1") {
    log(`==> database ${name} already exists`);
    return;
  }
  const result = adminSql(spawn, maintenanceUrl, useDocker, container, `CREATE DATABASE "${name}";`);
  if (result.error || result.status !== 0) {
    throw new Error(sanitizeToolError(result.stderr, `failed to create database ${name}`));
  }
  log(`==> created database ${name}`);
}

function grantSchemaPrivileges(spawn, url, useDocker, container, roles) {
  const result = adminSql(
    spawn,
    url,
    useDocker,
    container,
    `GRANT USAGE, CREATE ON SCHEMA public TO ${roles.owner};
     GRANT USAGE ON SCHEMA public TO ${roles.runtime};
     REVOKE CREATE ON SCHEMA public FROM PUBLIC;`,
  );
  if (result.error || result.status !== 0) {
    throw new Error(sanitizeToolError(result.stderr, "failed to grant schema privileges"));
  }
}

function childEnvironment(baseEnv, extra) {
  const allowed = [
    "PATH", "HOME", "USER", "TMPDIR", "TEMP", "TMP", "LANG", "LC_ALL",
    "DOCKER_HOST", "DOCKER_CONFIG",
    "DOTNET_ROOT", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "DOTNET_NOLOGO",
    "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_USE_POLLING_FILE_WATCHER",
  ];
  const env = {};
  for (const key of allowed) {
    if (baseEnv[key] !== undefined) {
      env[key] = baseEnv[key];
    }
  }
  return { ...env, ...extra };
}

function runDotnet(spawn, backend, args, extraEnv, baseEnv, output, error) {
  const result = spawn("dotnet", ["run", "--", ...args], {
    encoding: "utf8",
    cwd: backend,
    env: childEnvironment(baseEnv, extraEnv),
  });
  if (result.stdout) {
    output(result.stdout);
  }
  if (result.error || result.status !== 0) {
    if (result.stderr) {
      error(sanitizeToolError(result.stderr, "dotnet maintenance command failed") + "\n");
    }
    return false;
  }
  return true;
}

export function runBootstrap(options = {}) {
  const spawn = options.spawnSync ?? defaultSpawnSync;
  const log = options.log ?? ((message) => process.stdout.write(`${message}\n`));
  const output = options.output ?? ((message) => process.stdout.write(message));
  const error = options.error ?? ((message) => process.stderr.write(message));
  const fail = (message) => {
    error(`${message}\n`);
    return { ok: false, message };
  };
  const root = options.root ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  const env = options.env ?? process.env;
  const skipSeed = options.skipSeed === true;
  const backend = path.join(root, "backend");
  const frontend = path.join(root, "frontend");
  const container = env.ARGUS_DOCKER_CONTAINER === "none"
    ? null
    : env.ARGUS_DOCKER_CONTAINER ?? DOCKER_CONTAINER_DEFAULT;

  const tools = checkTools(spawn, container);
  if (!tools.ok) {
    if (tools.tool === "openssl") {
      return fail("error: missing openssl");
    }
    if (tools.tool === "dotnet") {
      return fail("error: missing dotnet");
    }
    return fail(
      "error: no PostgreSQL container and no psql; start PostgreSQL or set ARGUS_DOCKER_CONTAINER",
    );
  }

  const backendEnvFile = path.join(backend, ".env");
  const backendTestEnvFile = path.join(backend, ".env.test");
  const backendMaintenanceFile = path.join(backend, ".env.maintenance");
  const frontendEnvFile = path.join(frontend, ".env");
  const existingRuntime = readEnvFile(backendEnvFile);
  const existingMaintenance = readEnvFile(backendMaintenanceFile);

  for (const key of [
    "ARGUS_TEST_DATABASE_URL",
    "ARGUS_MAINTENANCE_DATABASE_URL",
    "ARGUS_MIGRATOR_DATABASE_URL",
    "ARGUS_ADMIN_DATABASE_URL",
  ]) {
    if (existingRuntime[key] !== undefined) {
      return fail(
        `error: ${backendEnvFile} contains ${key}; an authorized operator must relocate it to backend/.env.test or backend/.env.maintenance before startup`,
      );
    }
  }

  const adminUrl = env.ARGUS_DATABASE_URL ?? DB_URL_DEFAULT;
  const testDbUrl = env.ARGUS_TEST_DATABASE_URL ?? TEST_DB_URL_DEFAULT;
  const frontendUrl = env.ARGUS_FRONTEND_URL ?? FRONTEND_URL_DEFAULT;
  const backendUrl = env.ARGUS_BACKEND_URL ?? BACKEND_URL_DEFAULT;

  log("==> [1/6] Databases");
  ensureDb(spawn, adminUrl, tools.docker, container, log);
  ensureDb(spawn, testDbUrl, tools.docker, container, log);

  log("==> [2/6] Roles");
  const generatedMigrator = opensslHex(spawn);
  const generatedRuntime = opensslHex(spawn);
  const provisioned = provisionRoles({
    spawnSync: spawn,
    root,
    adminUrl,
    dockerContainer: tools.docker ? container : undefined,
    migratorPassword: generatedMigrator,
    runtimePassword: generatedRuntime,
    log,
  });
  const roles = provisioned.roles;

  if (existingRuntime.ARGUS_DATABASE_URL && roleOf(existingRuntime.ARGUS_DATABASE_URL) !== roles.runtime) {
    return fail(
      `error: ${backendEnvFile} ARGUS_DATABASE_URL is not the ${roles.runtime} runtime login; an authorized operator must perform the cutover`,
    );
  }

  if (existingMaintenance.ARGUS_DATABASE_URL && roleOf(existingMaintenance.ARGUS_DATABASE_URL) !== roles.migrator) {
    return fail(
      `error: ${backendMaintenanceFile} ARGUS_DATABASE_URL is not the ${roles.migrator} maintenance login; an authorized operator must perform the cutover`,
    );
  }

  const runtimeUrl = provisioned.created.runtime
    ? addSearchPath(replaceUserInfo(adminUrl, roles.runtime, generatedRuntime))
    : existingRuntime.ARGUS_DATABASE_URL ??
      (env.ARGUS_RUNTIME_PASSWORD
        ? addSearchPath(replaceUserInfo(adminUrl, roles.runtime, env.ARGUS_RUNTIME_PASSWORD))
        : null);
  if (!runtimeUrl) {
    return fail(`error: ${roles.runtime} exists but ${backendEnvFile} has no runtime URL and ARGUS_RUNTIME_PASSWORD is not supplied; refusing to reset its password`);
  }

  const maintenanceUrl = provisioned.created.migrator
    ? addSearchPath(replaceUserInfo(adminUrl, roles.migrator, generatedMigrator))
    : existingMaintenance.ARGUS_DATABASE_URL ??
      (env.ARGUS_MIGRATOR_PASSWORD
        ? addSearchPath(replaceUserInfo(adminUrl, roles.migrator, env.ARGUS_MIGRATOR_PASSWORD))
        : null);
  if (!maintenanceUrl) {
    return fail(`error: ${roles.migrator} exists but ${backendMaintenanceFile} has no migrator URL and ARGUS_MIGRATOR_PASSWORD is not supplied; refusing to reset its password`);
  }

  log("==> [3/6] Schema privileges");
  grantSchemaPrivileges(spawn, addSearchPath(adminUrl), tools.docker, container, roles);

  log("==> [4/6] Environment files");
  const apiSecret = existingRuntime.ARGUS_API_SECRET ?? opensslHex(spawn);
  const backendKeys = [
    ["ARGUS_DATABASE_URL", runtimeUrl],
    ["ARGUS_HOST", "127.0.0.1"],
    ["ARGUS_PORT", "3001"],
    ["ARGUS_API_SECRET", apiSecret],
    ["ARGUS_FRONTEND_URL", frontendUrl],
    ["ARGUS_RESEND_API_KEY", "dev-placeholder-key-not-for-production"],
    ["ARGUS_RESEND_FROM", "local@example.com"],
    ["ARGUS_BILLING_WEBHOOK_SECRET", existingRuntime.ARGUS_BILLING_WEBHOOK_SECRET ?? opensslHex(spawn)],
  ];
  const frontendKeys = [
    ["ARGUS_BACKEND_URL", backendUrl],
    ["ARGUS_API_SECRET", apiSecret],
    ["ARGUS_FRONTEND_URL", frontendUrl],
    ["ARGUS_AUTH_SECRET", opensslHex(spawn)],
    ["ARGUS_AUTH_TRUST_HOST", "true"],
  ];
  for (const [key, value] of backendKeys) {
    const action = setEnvKey(backendEnvFile, key, value);
    log(`     ${action === "wrote" ? "wrote" : "kept "} ${key} ${action === "wrote" ? `to ${backendEnvFile}` : "(already set)"}`);
  }
  for (const [key, value] of frontendKeys) {
    const action = setEnvKey(frontendEnvFile, key, value);
    log(`     ${action === "wrote" ? "wrote" : "kept "} ${key} ${action === "wrote" ? `to ${frontendEnvFile}` : "(already set)"}`);
  }
  const testAction = setEnvKey(backendTestEnvFile, "ARGUS_TEST_DATABASE_URL", testDbUrl);
  fs.chmodSync(backendTestEnvFile, 0o600);
  log(`     ${testAction === "wrote" ? "wrote" : "kept "} ARGUS_TEST_DATABASE_URL in ${backendTestEnvFile}`);
  const maintenanceAction = setEnvKey(backendMaintenanceFile, "ARGUS_DATABASE_URL", maintenanceUrl);
  fs.chmodSync(backendMaintenanceFile, 0o600);
  log(`     ${maintenanceAction === "wrote" ? "wrote" : "kept "} ARGUS_DATABASE_URL in ${backendMaintenanceFile}`);

  if (!skipSeed) {
    log("==> [5/6] Migrations");
    if (!runDotnet(spawn, backend, ["migrate"], { ARGUS_DATABASE_URL: maintenanceUrl }, env, output, error)) {
      return fail("error: dotnet run -- migrate failed");
    }

    log("==> [6/6] Seed accounts");
    const seedEnvironment = { ARGUS_DATABASE_URL: maintenanceUrl };
    if (env.ARGUS_SEED_DEV_PASSWORD) {
      seedEnvironment.ARGUS_SEED_DEV_PASSWORD = env.ARGUS_SEED_DEV_PASSWORD;
    }
    if (env.ARGUS_SEED_USER_PASSWORD) {
      seedEnvironment.ARGUS_SEED_USER_PASSWORD = env.ARGUS_SEED_USER_PASSWORD;
    }
    if (!runDotnet(spawn, backend, ["seed"], seedEnvironment, env, output, error)) {
      return fail("error: dotnet run -- seed failed");
    }
  }

  log("==> Ready");
  log("");
  log("  Backend:   cd backend");
  log("             dotnet run");
  log("  Frontend:  cd frontend");
  log("             npm ci");
  log(`             npm run dev   (open ${frontendUrl})`);
  log("  Backend tests:  node scripts/run-backend-tests.mjs");
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
