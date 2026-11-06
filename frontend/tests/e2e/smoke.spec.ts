import { expect, test } from "@playwright/test";

import { PRODUCT_NAME } from "@/lib/brand";
import {
  AUDIENCE_OUTCOME_DETAIL,
  AUDIENCE_OUTCOME_LEAD,
  FEATURE_HIGHLIGHTS,
  FORBIDDEN_UI_PHRASES,
  HOW_IT_WORKS_STEPS,
  PRIMARY_CTA_LABEL,
  PRODUCT_TAGLINE,
  STATUS_EXAMPLE,
} from "@/lib/landing-content";

test("security headers are present on every page response", async ({ page }) => {
  const response = await page.goto("/");
  expect(response).not.toBeNull();

  await expect
    .poll(() => response?.headers()["content-security-policy"] ?? "")
    .toContain("default-src 'self'");
  await expect
    .poll(() => response?.headers()["x-content-type-options"] ?? "")
    .toBe("nosniff");
  await expect.poll(() => response?.headers()["x-frame-options"] ?? "").toBe("DENY");
  await expect
    .poll(() => response?.headers()["referrer-policy"] ?? "")
    .toBe("strict-origin-when-cross-origin");
  await expect
    .poll(() => response?.headers()["strict-transport-security"] ?? "")
    .toContain("max-age=63072000");
});

test("landing page presents the Argus finance story and tagline", async ({ page }) => {
  await page.goto("/");

  await expect(
    page.getByRole("heading", { level: 1, name: PRODUCT_NAME }),
  ).toBeVisible();
  await expect(page.getByText(PRODUCT_TAGLINE, { exact: true })).toBeVisible();
  await expect(page.getByText(AUDIENCE_OUTCOME_LEAD)).toBeVisible();
  await expect(page.getByText(AUDIENCE_OUTCOME_DETAIL)).toBeVisible();

  for (const content of FEATURE_HIGHLIGHTS) {
    await expect(
      page.getByRole("heading", { level: 3, name: content.heading }),
    ).toBeVisible();
  }

  for (const step of HOW_IT_WORKS_STEPS) {
    await expect(page.getByText(step, { exact: true })).toBeVisible();
  }

  await expect(page.getByText(STATUS_EXAMPLE)).toBeVisible();
  const cta = page.getByRole("link", { name: PRIMARY_CTA_LABEL });
  await expect(cta).toHaveAttribute("href", "/register");
  await expect(cta).toBeVisible();

  await expect(page.locator('meta[name="description"]')).toHaveAttribute(
    "content",
    /See where your money goes/,
  );
});

test("the landing header links to login and register", async ({ page }) => {
  await page.goto("/");

  const accountNav = page.getByRole("navigation", { name: "Account" });
  await expect(
    accountNav.getByRole("link", { name: "Log in" }),
  ).toHaveAttribute("href", "/login");
  await expect(
    accountNav.getByRole("link", { name: "Create an account" }),
  ).toHaveAttribute("href", "/register");

  await accountNav.getByRole("link", { name: "Log in" }).click();
  await expect(page).toHaveURL(/\/login$/);
});

test("the primary action opens the registration page", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("link", { name: PRIMARY_CTA_LABEL }).click();

  await expect(page).toHaveURL(/\/register$/);
  await expect(
    page.getByRole("heading", { level: 1, name: "Create an account" }),
  ).toBeVisible();
});

test("the retired sample page returns 404", async ({ page }) => {
  const response = await page.goto("/sample-page");

  expect(response?.status()).toBe(404);
  await expect(page.locator("body")).not.toContainText("A sample content page");
});

test("the login page shows email, password, and submit controls", async ({ page }) => {
  await page.goto("/login");

  await expect(page.getByRole("heading", { level: 1, name: "Sign in" })).toBeVisible();
  await expect(page.getByLabel("Email")).toBeVisible();
  await expect(page.getByLabel("Password")).toBeVisible();
  await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Forgot password?" })).toHaveAttribute(
    "href",
    "/forgot-password",
  );
});

test("the forgot-password page shows the email request form", async ({ page }) => {
  await page.goto("/forgot-password");

  await expect(
    page.getByRole("heading", { level: 1, name: "Forgot your password?" }),
  ).toBeVisible();
  await expect(page.getByLabel("Email")).toBeVisible();
  await expect(page.getByRole("button", { name: "Request password reset" })).toBeVisible();
});

test("reset without a token shows the invalid-link copy", async ({ page }) => {
  await page.goto("/reset");

  await expect(page.getByText("This reset link is invalid or has expired.")).toBeVisible();
  await expect(page.getByLabel("New password")).not.toBeVisible();
});

test("reset with a well-formed token renders the password form without a backend", async ({
  page,
}) => {
  const token = "a".repeat(64);
  await page.goto(`/reset?token=${token}`);

  await expect(page.getByLabel("New password")).toBeVisible();
  await expect(page.getByRole("button", { name: "Reset password" })).toBeVisible();
  await expect(page.locator("body")).not.toContainText(token);
});

test("unauthenticated workspace routes end on the login page", async ({ page }) => {
  for (const route of [
    "/home",
    "/transactions",
    "/imports",
    "/imports/review/missing",
    "/imports/missing",
    "/settings",
    "/admin",
  ]) {
    await page.goto(route);
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole("heading", { level: 1, name: "Sign in" })).toBeVisible();
  }
});

test("the register page shows the customer form", async ({ page }) => {
  await page.goto("/register");

  await expect(page.getByRole("heading", { level: 1, name: "Create an account" })).toBeVisible();
  await expect(page.getByLabel("Email")).toBeVisible();
  await expect(page.getByLabel("Password")).toBeVisible();
  await expect(page.getByRole("button", { name: "Create account" })).toBeVisible();
});

test("verify without a token shows the invalid-link copy", async ({ page }) => {
  await page.goto("/verify");

  await expect(
    page.getByText("This verification link is invalid or has expired."),
  ).toBeVisible();
  await expect(page.getByRole("button", { name: "Verify email" })).not.toBeVisible();
});

test("verify with a well-formed token shows confirmation without a backend", async ({ page }) => {
  await page.goto(`/verify?token=${"a".repeat(64)}`);

  await expect(page.getByRole("button", { name: "Verify email" })).toBeVisible();
});

test("forbidden phrases are absent from the landing page", async ({ page }) => {
  await page.goto("/");
  const renderedText = await page.locator("body").innerText();

  for (const phrase of FORBIDDEN_UI_PHRASES) {
    expect(renderedText).not.toContain(phrase);
  }
});

test("the CTA remains usable at narrow and wide viewports", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 800 });
  await page.goto("/");
  const cta = page.getByRole("link", { name: PRIMARY_CTA_LABEL });
  await expect(cta).toBeVisible();
  await expect(cta).toBeEnabled();

  await page.setViewportSize({ width: 1280, height: 800 });
  await expect(cta).toBeVisible();
  await expect(cta).toBeEnabled();
});

test("keyboard focus and Enter activate the CTA at 1280px", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto("/");

  const cta = page.getByRole("link", { name: PRIMARY_CTA_LABEL });
  for (let index = 0; index < 10; index += 1) {
    await page.keyboard.press("Tab");
    if (await cta.evaluate((element) => element === document.activeElement)) {
      break;
    }
  }
  await expect(cta).toBeFocused();
  await page.keyboard.press("Enter");

  await expect(page).toHaveURL(/\/register$/);
});

test("the theme defaults to dark and the toggle persists light", async ({ page }) => {
  await page.goto("/");

  const html = page.locator("html");
  await expect(html).toHaveClass(/dark/);

  await page.getByRole("button", { name: "Switch between light and dark theme" }).click();
  await expect(html).not.toHaveClass(/dark/);
  await expect
    .poll(() => page.evaluate(() => document.documentElement.style.colorScheme))
    .toBe("light");

  await page.reload();
  await expect(html).not.toHaveClass(/dark/);
});
