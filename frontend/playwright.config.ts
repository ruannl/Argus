import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/e2e",
  timeout: 30_000,
  expect: {
    timeout: 10_000,
  },
  fullyParallel: false,
  workers: 1,
  reporter: "list",
  use: {
    baseURL: process.env.ARGUS_FRONTEND_URL ?? "http://127.0.0.1:3100",
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
      },
    },
  ],
  webServer: [
    {
      command: "npm run build && npm run start -- --hostname 127.0.0.1 --port 3100",
      port: 3100,
      reuseExistingServer: !process.env.CI,
      env: {
        ARGUS_AUTH_SECRET:
          process.env.ARGUS_AUTH_SECRET ??
          "test-only-auth-secret-placeholder-32-bytes",
        // Auth.js pins its public origin from ARGUS_FRONTEND_URL. The test
        // server's exact origin is 127.0.0.1:3100, not the local development
        // default in .env, so the redirects stay on the server under test.
        ARGUS_FRONTEND_URL: "http://127.0.0.1:3100",
      },
    },
  ],
});
