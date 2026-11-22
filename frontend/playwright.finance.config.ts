import { defineConfig, devices } from "@playwright/test";
import { PRODUCT_TOKEN } from "./src/lib/brand";

/** Isolated synthetic frontend journeys. The real backend is never contacted. */
export default defineConfig({
  testDir: "./tests/e2e/finance",
  testMatch: "*.spec.ts",
  timeout: 60_000,
  workers: 1,
  reporter: "list",
  use: {
    baseURL: "http://127.0.0.1:3210",
    trace: "retain-on-failure",
    ...devices["Desktop Chrome"],
    launchOptions: { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH },
  },
  webServer: [
    {
      command: "node tests/e2e/finance/fixture-backend.ts",
      url: "http://127.0.0.1:3211/health",
      reuseExistingServer: false,
      env: { ARGUS_FIXTURE_HEADER_TOKEN: PRODUCT_TOKEN },
    },
    {
      command: "npm run build && npm run start -- --hostname 127.0.0.1 --port 3210",
      url: "http://127.0.0.1:3210/login",
      reuseExistingServer: false,
      env: {
        ARGUS_AUTH_SECRET: "synthetic-only-auth-placeholder-at-least-32-bytes",
        ARGUS_API_SECRET: "synthetic-only-application-placeholder",
        ARGUS_FRONTEND_URL: "http://127.0.0.1:3210",
        ARGUS_BACKEND_URL: "http://127.0.0.1:3211",
      },
    },
  ],
});
