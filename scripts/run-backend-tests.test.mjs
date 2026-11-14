import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { readTestDatabaseUrl, runBackendTests } from "./run-backend-tests.mjs";

function makeRoot() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "argus-tests-"));
  fs.mkdirSync(path.join(root, "backend"), { recursive: true });
  return root;
}

test("an already scoped test URL wins over backend/.env.test", () => {
  const root = makeRoot();
  fs.writeFileSync(
    path.join(root, "backend", ".env.test"),
    "ARGUS_TEST_DATABASE_URL=postgres://file-url@127.0.0.1:5432/argus_test\n",
  );
  assert.equal(
    readTestDatabaseUrl(root, { ARGUS_TEST_DATABASE_URL: "postgres://env-url@127.0.0.1:5432/argus_test" }),
    "postgres://env-url@127.0.0.1:5432/argus_test",
  );
  assert.equal(
    readTestDatabaseUrl(root, {}),
    "postgres://file-url@127.0.0.1:5432/argus_test",
  );
});

test("the launcher forwards only the test URL and the allowed base environment", () => {
  const root = makeRoot();
  let captured;
  const spawn = (command, args, options) => {
    captured = { command, args, options };
    return { status: 0 };
  };
  const result = runBackendTests({
    spawnSync: spawn,
    root,
    env: {
      PATH: "/usr/bin",
      HOME: "/home/test",
      ARGUS_TEST_DATABASE_URL: "postgres://test@127.0.0.1:5432/argus_test",
      ARGUS_API_SECRET: "must-not-leak",
    },
    args: ["--filter", "FullyQualifiedName~CustomerDbScopeTests"],
  });

  assert.equal(result.ok, true);
  assert.equal(captured.command, "dotnet");
  assert.deepEqual(captured.args, [
    "test",
    "tests/argus.Server.Tests",
    "-warnaserror",
    "--filter",
    "FullyQualifiedName~CustomerDbScopeTests",
  ]);
  assert.equal(captured.options.env.ARGUS_TEST_DATABASE_URL, "postgres://test@127.0.0.1:5432/argus_test");
  assert.equal(captured.options.env.ARGUS_API_SECRET, undefined);
  assert.equal(captured.options.env.PATH, "/usr/bin");
});

test("a missing test URL fails closed", () => {
  const root = makeRoot();
  const result = runBackendTests({
    spawnSync: () => {
      throw new Error("must not run");
    },
    root,
    env: {},
  });

  assert.equal(result.ok, false);
  assert.match(result.message, /ARGUS_TEST_DATABASE_URL is required/);
});
