#!/usr/bin/env node
/**
 * Run the backend isolation tests with the test-maintenance URL delivered
 * explicitly to the test process. An already scoped environment value wins
 * over the ignored backend/.env.test file. No other Argus secrets are
 * inherited from the parent environment.
 *
 *   node scripts/run-backend-tests.mjs
 *   node scripts/run-backend-tests.mjs --filter "FullyQualifiedName~CustomerDbScopeTests"
 */
import { spawnSync as defaultSpawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

export function readTestDatabaseUrl(root, env = process.env) {
  const fromEnvironment = env.ARGUS_TEST_DATABASE_URL;
  if (fromEnvironment) {
    return fromEnvironment;
  }

  const file = path.join(root, "backend", ".env.test");
  if (!fs.existsSync(file)) {
    return null;
  }

  for (const line of fs.readFileSync(file, "utf8").split(/\r?\n/)) {
    const trimmed = line.trim();
    if (trimmed.startsWith("ARGUS_TEST_DATABASE_URL=")) {
      return trimmed.slice("ARGUS_TEST_DATABASE_URL=".length);
    }
  }

  return null;
}

export function runBackendTests(options = {}) {
  const spawn = options.spawnSync ?? defaultSpawnSync;
  const root = options.root ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  const env = options.env ?? process.env;
  const args = options.args ?? process.argv.slice(2);
  const testUrl = readTestDatabaseUrl(root, env);
  if (!testUrl) {
    return {
      ok: false,
      message:
        "error: ARGUS_TEST_DATABASE_URL is required; set it in the test process or backend/.env.test",
    };
  }

  const allowed = [
    "PATH", "HOME", "USER", "TMPDIR", "TEMP", "TMP", "LANG", "LC_ALL",
    "DOTNET_ROOT", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "DOTNET_NOLOGO",
    "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_USE_POLLING_FILE_WATCHER",
  ];
  const childEnvironment = {};
  for (const key of allowed) {
    if (env[key] !== undefined) {
      childEnvironment[key] = env[key];
    }
  }
  childEnvironment.ARGUS_TEST_DATABASE_URL = testUrl;

  const result = spawn(
    "dotnet",
    ["test", "tests/argus.Server.Tests", "-warnaserror", ...args],
    {
      cwd: path.join(root, "backend"),
      env: childEnvironment,
      stdio: "inherit",
    },
  );

  return result.error || result.status !== 0
    ? { ok: false, message: "error: dotnet test failed" }
    : { ok: true };
}

const invoked = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (invoked) {
  const result = runBackendTests();
  if (!result.ok) {
    process.stderr.write(`${result.message}\n`);
    process.exit(1);
  }
}
