import { expect, test, type Page } from "@playwright/test";
import { ids } from "./fixture-backend";

async function scenario(page: Page, name: string) {
  await page.request.post("http://127.0.0.1:3211/__scenario", { data: { scenario: name } });
}

async function login(page: Page, theme: string) {
  await scenario(page, "ready");
  await page.addInitScript((value) => localStorage.setItem("argus-theme", value), theme);
  await page.goto("/login");
  await page.getByLabel("Email", { exact: true }).fill("synthetic@example.test");
  await page.getByLabel("Password", { exact: true }).fill("Synthetic-only-password1!");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page).toHaveURL(/\/home/);
}

async function scrollTop(page: Page): Promise<number> {
  return page.locator(".finance-scroll").evaluate((node) => node.scrollTop);
}

async function setScrollTop(page: Page, value: number): Promise<number> {
  return page.locator(".finance-scroll").evaluate(async (node, next) => {
    node.scrollTop = next;
    await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
    return node.scrollTop;
  }, value);
}

async function fits(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
  const clipped = await page
    .locator(".finance-record-table")
    .evaluateAll(
      (tables) =>
        tables.filter(
          (table) =>
            table.getBoundingClientRect().width > 0 && table.scrollWidth > table.clientWidth + 1,
        ).length,
    );
  if ((page.viewportSize()?.width ?? 0) <= 700) expect(clipped).toBe(0);
}

async function showBulkEdit(page: Page) {
  const show = page.getByRole("button", { name: "Show bulk edit" });
  if (await show.isVisible()) {
    await show.click();
    await page.locator(".finance-scroll").evaluate((node) => {
      node.scrollTop = 0;
    });
  }
}

test("finance workspace loads the bundled Inter variable font", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await login(page, "dark");

  const workspace = page.locator(".finance-workspace").first();
  await expect(workspace).toHaveCSS("font-family", /financeSans/);

  const loaded = await page.evaluate(async () => {
    await document.fonts.ready;
    return document.fonts.check("14px financeSans");
  });
  expect(loaded).toBe(true);
});

test("category spending trend switches ranges and keeps exact monthly drill-downs", async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 900 });
  await login(page, "light");
  await page.goto("/home?mode=calendar&preset=month_to_date");

  const breakdown = page.getByRole("region", { name: "Category breakdown" });
  await expect(breakdown.locator(".category-share-track")).toHaveCount(2);
  await expect(breakdown.locator(".category-share-fill")).toHaveCount(2);

  const trend = page.getByRole("region", { name: "Category spending trend" });
  await expect(trend).toBeVisible();
  await expect(trend.getByRole("button", { name: "3 months" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  await expect(trend.getByRole("button", { name: "6 months" })).toHaveAttribute(
    "aria-pressed",
    "false",
  );
  await trend.getByText("Category spending as a table").click();

  const table = trend.getByRole("table", {
    name: /Included Money out by category and month/,
  });
  await expect(table.getByRole("columnheader", { name: /Sep 2026/ })).toBeVisible();
  await expect(table.getByRole("columnheader", { name: /Aug 2026/ })).toHaveCount(0);

  await trend.getByRole("button", { name: "2 months" }).click();
  await expect(trend.getByRole("button", { name: "2 months" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  await expect(table.getByRole("columnheader", { name: /Sep 2026/ })).toHaveCount(0);
  await expect(table.getByRole("columnheader", { name: /Oct 2026/ })).toBeVisible();

  const groceries = table.getByRole("row", { name: /Household essentials and groceries/ });
  const october = groceries.getByRole("link", { name: "R 1 000.00" });
  await expect(october).toHaveAttribute("href", /start_date=2026-10-01/);
  await expect(october).toHaveAttribute("href", /end_date=2026-10-31/);
  await expect(october).toHaveAttribute("href", /category_id=22222222-2222-4222-8222-222222222222/);
  await expect(october).toHaveAttribute(
    "href",
    /financial_account_id=11111111-1111-4111-8111-111111111111/,
  );
  await expect(october).toHaveAttribute("href", /direction=out/);
  await expect(october).toHaveAttribute("href", /calculation_scope=included/);

  await trend.getByRole("button", { name: "6 months" }).click();
  await expect(table.getByRole("columnheader", { name: /Jun 2026/ })).toBeVisible();
  await fits(page);

  await page.setViewportSize({ width: 320, height: 900 });
  await fits(page);
});

for (const theme of ["light", "dark"]) {
  for (const width of [320, 375, 1440]) {
    test(`${theme} ${width}px: authenticated screens retain labels, amounts and controls`, async ({
      page,
    }, info) => {
      await page.setViewportSize({ width, height: 900 });
      await login(page, theme);
      for (const [route, title] of [
        ["/home?mode=calendar&preset=month_to_date&view=charts", "Overview"],
        ["/transactions", "Transactions"],
        [`/transactions/${ids.transaction}`, "Transaction detail"],
        ["/imports", "Imports"],
        ["/imports?tab=reviews", "Imports"],
        ["/imports?tab=history", "Imports"],
        [`/imports/review/${ids.review}`, "Import review"],
        [`/imports/${ids.statement}`, "Statement detail"],
        ["/settings", "Settings"],
        ["/settings?tab=categories", "Settings"],
        ["/settings?tab=merchant-tags", "Settings"],
        ["/settings?tab=aliases", "Settings"],
        ["/settings?tab=profile", "Settings"],
      ]) {
        await page.goto(route);
        if (route === "/transactions") {
          await expect(page.getByRole("table", { name: "Matching transactions" })).toBeVisible();
          await expect(page.getByRole("checkbox", { name: "Select this page" })).toBeVisible();
          await expect(page.getByRole("button", { name: "Show bulk edit" })).toHaveAttribute(
            "aria-expanded",
            "false",
          );
          await expect(
            page.getByRole("button", { name: "Select all matching filters" }),
          ).not.toBeVisible();
        }
        await expect(
          page.getByRole("heading", { level: 1, name: title, exact: true }),
        ).toBeVisible();
        const nav = page.getByRole("navigation", { name: "Workspace", exact: true });
        for (const name of ["Overview", "Transactions", "Imports", "Settings"]) {
          await expect(nav.getByRole("link", { name, exact: true })).toBeVisible();
        }
        await fits(page);
        const frame = await page.evaluate(() => {
          const header = document.querySelector(".finance-shell")?.getBoundingClientRect();
          const content = document.querySelector(".finance-scroll")?.getBoundingClientRect();
          return {
            documentFixed: document.documentElement.scrollHeight <= window.innerHeight + 1,
            headerTop: header?.top ?? -1,
            headerBottom: header?.bottom ?? -1,
            contentTop: content?.top ?? -1,
            contentHeight: content?.height ?? 0,
          };
        });
        expect(frame.documentFixed).toBe(true);
        expect(frame.headerTop).toBe(0);
        expect(frame.contentTop).toBeGreaterThanOrEqual(frame.headerBottom - 1);
        expect(frame.contentHeight).toBeGreaterThan(0);
        expect(await page.locator("h1").evaluate((el) => getComputedStyle(el).fontSize)).toBe(
          width < 700 ? "24px" : "26px",
        );
        if (route === "/imports?tab=history") {
          const history = page.getByRole("table", { name: "Committed import history" });
          await expect(history.getByRole("columnheader", { name: "Account" })).toBeAttached();
          await expect(
            history.getByText("Synthetic everyday account", { exact: true }),
          ).toBeVisible();
        }
        if (route.startsWith("/home?")) {
          const moneyTrend = page.getByRole("region", { name: "Trend", exact: true });
          await expect(moneyTrend.locator(".bg-chart-in").last()).toHaveCSS(
            "background-color",
            theme === "dark" ? "rgb(74, 222, 128)" : "rgb(22, 101, 52)",
          );
          await expect(moneyTrend.locator(".bg-chart-out").last()).toHaveCSS(
            "background-color",
            theme === "dark" ? "rgb(248, 113, 113)" : "rgb(153, 27, 27)",
          );
        }
        if (route === "/settings?tab=categories") {
          const activeTab = page.getByRole("tab", { name: "Categories", exact: true });
          await activeTab.hover();
          const tabContrast = await activeTab.evaluate((element) => {
            const style = getComputedStyle(element);
            const parse = (value: string) => (value.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number);
            const luminance = (values: number[]) =>
              values
                .map((value) => {
                  const channel = value / 255;
                  return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
                })
                .reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index], 0);
            const foreground = luminance(parse(style.color));
            const background = luminance(parse(style.backgroundColor));
            const lighter = Math.max(foreground, background);
            const darker = Math.min(foreground, background);
            return (lighter + 0.05) / (darker + 0.05);
          });
          expect(tabContrast).toBeGreaterThanOrEqual(4.5);
        }
        if (route === "/transactions") {
          await expect(page.getByText("2 transactions filtered")).toBeVisible();
          const transactionsTable = page.getByRole("table", { name: "Matching transactions" });
          await expect(
            transactionsTable.getByRole("columnheader", { name: "Tags" }),
          ).toBeAttached();
          await expect(
            transactionsTable.getByText("Synthetic corner shop", { exact: true }),
          ).toBeVisible();
          const rowSuggestion = page.getByRole("button", {
            name: /Add suggested tag Synthetic row suggestion/,
          });
          await expect(rowSuggestion).toBeVisible();
          await page.mouse.move(1, 1);
          const rowSuggestionBefore = await rowSuggestion.evaluate((element) => {
            const style = getComputedStyle(element);
            return { border: style.borderColor, cursor: style.cursor };
          });
          expect(rowSuggestionBefore.cursor).toBe("pointer");
          await rowSuggestion.hover();
          expect(
            await rowSuggestion.evaluate((element) => getComputedStyle(element).borderColor),
          ).not.toBe(rowSuggestionBefore.border);
          await expect(
            page.getByRole("button", {
              name: /^Confirm category Household essentials and groceries for/,
            }),
          ).toBeVisible();
          await page
            .getByRole("button", {
              name: /^Edit inline$|^Close inline editor$/,
            })
            .last()
            .click();
          await expect(
            page.getByRole("button", { name: "Add suggested tag Synthetic suggested tag" }),
          ).toBeVisible();
          const neutralButton = page
            .getByRole("button", { name: /^Edit inline$|^Close inline editor$/ })
            .last();
          await page.mouse.move(1, 1);
          const neutralBorderBefore = await neutralButton.evaluate(
            (element) => getComputedStyle(element).borderColor,
          );
          await neutralButton.hover();
          expect(
            await neutralButton.evaluate((element) => getComputedStyle(element).borderColor),
          ).not.toBe(neutralBorderBefore);
          const visibleFilter = page.getByRole("button", { name: "Visible", exact: true });
          await visibleFilter.hover();
          const filterContrast = await visibleFilter.evaluate((element) => {
            const style = getComputedStyle(element);
            const parse = (value: string) => (value.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number);
            const luminance = (values: number[]) =>
              values
                .map((value) => {
                  const channel = value / 255;
                  return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
                })
                .reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index], 0);
            const foreground = luminance(parse(style.color));
            const background = luminance(parse(style.backgroundColor));
            const lighter = Math.max(foreground, background);
            const darker = Math.min(foreground, background);
            return (lighter + 0.05) / (darker + 0.05);
          });
          expect(filterContrast).toBeGreaterThanOrEqual(4.5);
          const samples = await page.evaluate(() => {
            const rgb = (value: string) => (value.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number);
            const luminance = (values: number[]) =>
              values
                .map((n) => {
                  const s = n / 255;
                  return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
                })
                .reduce((sum, n, i) => sum + n * [0.2126, 0.7152, 0.0722][i], 0);
            const ratio = (a: number[], b: number[]) => {
              const l = [luminance(a), luminance(b)].sort((x, y) => y - x);
              return (l[0] + 0.05) / (l[1] + 0.05);
            };
            const field = getComputedStyle(document.querySelector('input[type="search"]')!);
            const nav = getComputedStyle(
              document.querySelector('.finance-nav-link[aria-current="page"]')!,
            );
            const primary = getComputedStyle(
              Array.from(document.querySelectorAll("button")).find(
                (el) => el.textContent === "Review change",
              )!,
            );
            const secondary = getComputedStyle(document.querySelector(".finance-page-heading p")!);
            const dark = document.documentElement.classList.contains("dark");
            return {
              fieldText: ratio(rgb(field.color), rgb(field.backgroundColor)),
              fieldBoundary: ratio(rgb(field.borderColor), rgb(field.backgroundColor)),
              navText: ratio(rgb(nav.color), rgb(nav.backgroundColor)),
              primaryText: ratio(rgb(primary.color), rgb(primary.backgroundColor)),
              secondaryGlass: ratio(rgb(secondary.color), dark ? [42, 62, 91] : [245, 247, 251]),
            };
          });
          for (const [role, value] of Object.entries(samples))
            expect(value, role).toBeGreaterThanOrEqual(role === "fieldBoundary" ? 3 : 4.5);
          if (theme === "dark") {
            const material = await page.evaluate(() => {
              const app = document.querySelector(".finance-app");
              const shell = document.querySelector(".finance-shell");
              const card = document.querySelector(
                ".finance-workspace .card-shadow:not(.finance-summary)",
              );
              const tableHead = document.querySelector(".finance-record-table thead");
              return {
                appBackground: app ? getComputedStyle(app).backgroundImage : "none",
                shellBlur: shell ? getComputedStyle(shell).backdropFilter : "none",
                cardBackground: card ? getComputedStyle(card).backgroundColor : "transparent",
                tableHeadBackground: tableHead
                  ? getComputedStyle(tableHead).backgroundColor
                  : "transparent",
              };
            });
            expect(material.appBackground).toContain("radial-gradient");
            expect(material.shellBlur).not.toBe("none");
            expect(material.cardBackground).toMatch(/^rgba\(/);
            expect(material.tableHeadBackground).toMatch(/^rgba\(/);
          }
          if (width === 1440)
            expect(
              await page
                .getByRole("button", { name: "Search", exact: true })
                .evaluate((el) => el.getBoundingClientRect().height),
            ).toBe(36);
          await expect(page.getByText("Confirmed", { exact: true })).toBeVisible();
          await expect(
            page.locator('td[data-label="Classification"]').filter({ hasText: "Unconfirmed" }),
          ).toBeVisible();
          await expect(page.locator('td[data-label="Amount"]').first()).toContainText(
            "-1234.56 ZAR",
          );
          const tree = await page
            .getByRole("table", { name: "Matching transactions" })
            .ariaSnapshot();
          expect(tree).toContain("columnheader");
          expect(tree).toContain("cell");
          expect(tree).toContain("-1234.56 ZAR");
          if (theme === "dark" && width === 1440) {
            const frames = await page.evaluate(async () => {
              const intervals: number[] = [];
              let previous = performance.now();
              for (let frame = 0; frame < 60; frame++) {
                await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
                const now = performance.now();
                intervals.push(now - previous);
                previous = now;
                window.scrollTo(0, frame * 25);
              }
              window.scrollTo(0, 0);
              intervals.sort((a, b) => a - b);
              return { median: intervals[30], p95: intervals[57], maximum: intervals[59] };
            });
            console.log("dark-shell-scroll-frame-sample", JSON.stringify(frames));
          }
        }
        if (route.includes("review/")) {
          await expect(page.getByText("Blocks import", { exact: true })).toBeVisible();
          await expect(page.getByRole("button", { name: "Commit import" })).toBeDisabled();
          await expect(page.getByText("Suggested from your history or alias")).toBeVisible();
          const pages = page.getByRole("navigation", { name: "Review pages" });
          const rowsTable = page.getByRole("table", { name: "Import review rows" });
          expect(await pages.evaluate((node) => node.getBoundingClientRect().top)).toBeLessThan(
            await rowsTable.evaluate((node) => node.getBoundingClientRect().top),
          );
          await expect(pages.getByLabel("Sort by date")).toBeVisible();
          await expect(pages.getByLabel("Rows per page")).toBeVisible();
          if (width > 700) {
            const lastCellFitsFrame = await rowsTable.evaluate((table) => {
              const cell = table.querySelector('td[data-label="Actions"]');
              const frame = table.closest(".finance-table-frame");
              if (!cell || !frame) return false;
              return cell.getBoundingClientRect().right <= frame.getBoundingClientRect().right;
            });
            expect(lastCellFitsFrame).toBe(true);
          }
        }
        if (
          [
            "/transactions",
            "/home?mode=calendar&preset=month_to_date&view=charts",
            "/imports?tab=history",
            "/settings?tab=aliases",
          ].includes(route)
        ) {
          await page.screenshot({
            path: info.outputPath(`${title.toLowerCase()}-${theme}-${width}.png`),
            fullPage: true,
          });
        }
      }
    });
  }

  test(`${theme}: keyboard drawer, tabs, destructive confirmation and material fallbacks`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: 375, height: 900 });
    await login(page, theme);
    await page.goto("/transactions");
    const opener = page.getByRole("button", {
      name: "Synthetic groceries — monthly household purchase",
      exact: true,
    });
    await opener.focus();
    await page.keyboard.press("Enter");
    const drawer = page.getByRole("dialog", { name: "Transaction detail drawer" });
    await expect(drawer).toBeVisible();
    await expect(drawer.getByRole("button", { name: "Close detail" })).toBeFocused();
    await page.keyboard.press("Shift+Tab");
    expect(await drawer.evaluate((el) => el.contains(document.activeElement))).toBe(true);
    await page.keyboard.press("Escape");
    await expect(opener).toBeFocused();
    expect(await opener.evaluate((el) => getComputedStyle(el).outlineWidth)).toBe("2px");
    await page.locator(".finance-scroll").evaluate((node) => {
      node.scrollTop = 0;
    });
    const search = page.getByLabel("Search description or tag");
    await search.focus();
    await page.keyboard.press("PageDown");
    await expect.poll(() => scrollTop(page)).toBeGreaterThan(0);
    await page.locator(".finance-scroll").evaluate((node) => {
      node.scrollTop = 0;
    });
    for (let index = 0; index < 32; index++) {
      await page.keyboard.press("Tab");
      const focused = await page.evaluate(() => {
        const scroll = document.querySelector(".finance-scroll");
        const active = document.activeElement;
        if (!(scroll instanceof HTMLElement) || !(active instanceof HTMLElement)) {
          return { inside: false, visible: true };
        }
        if (!scroll.contains(active)) {
          return { inside: false, visible: true };
        }
        const pane = scroll.getBoundingClientRect();
        const box = active.getBoundingClientRect();
        return { inside: true, visible: box.top >= pane.top - 1 && box.bottom <= pane.bottom + 1 };
      });
      if (focused.inside) {
        expect(focused.visible, "Keyboard focus must remain visible in the content region").toBe(
          true,
        );
      }
      const overlap = await page.evaluate(() => {
        const active = document.activeElement?.getBoundingClientRect();
        const toggle = document
          .querySelector('button[aria-label="Switch between light and dark theme"]')
          ?.getBoundingClientRect();
        return (
          active &&
          toggle &&
          document.activeElement?.getAttribute("aria-label") !==
            "Switch between light and dark theme" &&
          active.left < toggle.right &&
          active.right > toggle.left &&
          active.top < toggle.bottom &&
          active.bottom > toggle.top
        );
      });
      expect(overlap, "Keyboard focus must not be covered by the theme toggle").toBe(false);
    }
    await page.goto("/settings");
    const accounts = page.getByRole("tab", { name: "Accounts", exact: true });
    await accounts.focus();
    await page.keyboard.press("End");
    await expect(page.getByRole("tab", { name: "Profile", exact: true })).toBeFocused();
    await expect(page).toHaveURL(/tab=profile/);
    await page.keyboard.press("Home");
    await expect(accounts).toBeFocused();
    await page.goto(`/imports/${ids.statement}`);
    await page.getByRole("button", { name: "Delete statement…" }).click();
    await expect(page.getByText(/exactly its 2 owned transactions/)).toBeVisible();
    await expect(page.getByRole("button", { name: "Delete permanently" })).not.toBeFocused();
    await page.getByRole("button", { name: "Keep statement" }).click();
    await expect(page.getByRole("button", { name: "Delete permanently" })).toHaveCount(0);
    await fits(page);
    await page.emulateMedia({ reducedMotion: "reduce" });
    const cdp = await page.context().newCDPSession(page);
    await cdp.send("Emulation.setEmulatedMedia", {
      features: [{ name: "prefers-reduced-transparency", value: "reduce" }],
    });
    expect(
      await page.locator(".finance-shell").evaluate((el) => getComputedStyle(el).backdropFilter),
    ).toBe("none");
    const opaqueFallback = await page.evaluate(() => {
      const app = document.querySelector(".finance-app");
      const card = document.querySelector(".finance-workspace .card-shadow:not(.finance-summary)");
      return {
        background: app ? getComputedStyle(app).backgroundImage : "none",
        card: card ? getComputedStyle(card).backgroundColor : "transparent",
      };
    });
    expect(opaqueFallback.background).toBe("none");
    expect(opaqueFallback.card).not.toMatch(/^rgba\(/);
    await page.emulateMedia({ forcedColors: "active" });
    const activeDestination = page
      .getByRole("navigation", { name: "Workspace", exact: true })
      .locator('[aria-current="page"]');
    await expect(activeDestination).toBeVisible();
    expect(
      await activeDestination.evaluate((element) => getComputedStyle(element).borderBottomWidth),
    ).toBe("3px");
    expect(await page.locator(".finance-nav-icon").first().isVisible()).toBe(false);
  });

  test(`${theme}: empty, partial read, error and captured bulk confirmation stay distinct`, async ({
    page,
  }) => {
    await login(page, theme);
    await page.setViewportSize({ width: 320, height: 900 });
    await scenario(page, "empty");
    await page.goto("/transactions");
    await expect(page.getByRole("heading", { name: "No transactions yet" })).toBeVisible();
    await fits(page);
    await scenario(page, "error");
    await page.goto("/transactions");
    await expect(page.getByRole("heading", { name: "Transactions could not load" })).toBeVisible();
    await fits(page);
    await scenario(page, "account-error");
    await page.goto("/transactions");
    await expect(page.getByRole("heading", { name: "Transactions unavailable" })).toBeVisible();
    await expect(page.getByText(/We did not sign you out/)).toBeVisible();
    await expect(page.getByRole("button", { name: "Retry account check" })).toBeVisible();
    await fits(page);
    await scenario(page, "partial");
    await page.goto("/transactions");
    await expect(page.getByRole("main").getByRole("alert")).toContainText(
      "Some accounts, categories, or tags could not be loaded",
    );
    await expect(page.getByText("Confirmed", { exact: true })).toBeVisible();
    await fits(page);
    await scenario(page, "ready");
    await page.goto("/transactions");
    await showBulkEdit(page);
    await page.getByRole("checkbox", { name: /^Select transaction Synthetic groceries/ }).check();
    await page.getByRole("button", { name: "Hide", exact: true }).click();
    await page.getByRole("button", { name: "Review change", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Confirm this change" })).toBeVisible();
    await expect(page.getByText(/1 will change and 0 already match/)).toBeVisible();
    await expect(page.getByRole("button", { name: "Apply change", exact: true })).toBeEnabled();
    await fits(page);
    await page.reload();
    await expect(page.getByRole("heading", { name: "Recovered confirmation" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Apply change", exact: true })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Abandon and prepare again" })).toBeVisible();
    await fits(page);
  });

  test(`${theme}: local pending, success and changed-version feedback remain readable`, async ({
    page,
  }) => {
    await login(page, theme);
    await page.setViewportSize({ width: 375, height: 900 });
    await page.goto("/transactions");
    await scenario(page, "pending");
    await page.getByLabel("Search description or tag").fill("synthetic");
    await page.getByRole("button", { name: "Search", exact: true }).click();
    await expect(page.getByText("Refreshing…", { exact: true })).toBeVisible();
    await expect(page.getByRole("heading", { level: 1, name: "Transactions" })).toBeVisible();
    await fits(page);
    await expect(page.getByText("Refreshing…", { exact: true })).toHaveCount(0);
    await scenario(page, "conflict");
    await page.goto(`/transactions/${ids.transaction}`);
    await page
      .getByRole("button", { name: "Remove tag Synthetic corner shop", exact: true })
      .click();
    await page.getByRole("button", { name: "Save classification", exact: true }).click();
    await expect(
      page.getByRole("alert").filter({ hasText: /changed|version|another/i }),
    ).toBeVisible();
    await fits(page);
    await scenario(page, "ready");
    await page.goto("/settings");
    await page.getByLabel("Account name", { exact: true }).fill("Synthetic new account");
    await page.getByRole("button", { name: "Add ZAR account", exact: true }).click();
    await expect(page.getByRole("status")).toContainText(/created|added/i);
    await fits(page);
  });

  test(`${theme}: steady navigation starts new destinations at the top and restores Back/Forward`, async ({
    page,
  }) => {
    await login(page, theme);
    await page.setViewportSize({ width: 375, height: 900 });
    await page.goto("/transactions");
    await expect(page.getByRole("heading", { level: 1, name: "Transactions" })).toBeVisible();
    const headerBefore = await page.locator(".finance-shell").evaluate((node) => {
      const box = node.getBoundingClientRect();
      return { top: box.top, height: box.height };
    });
    await page.evaluate(() => {
      (window as Window & { __argusNoReload?: string }).__argusNoReload = "same-document";
    });
    await expect.poll(() => scrollTop(page)).toBe(0);
    const transactionsPosition = await setScrollTop(page, 600);
    expect(transactionsPosition).toBeGreaterThan(0);
    const headerScrolled = await page.locator(".finance-shell").evaluate((node) => {
      const box = node.getBoundingClientRect();
      return { top: box.top, height: box.height };
    });
    expect(headerScrolled.top).toBe(headerBefore.top);
    expect(headerScrolled.height).toBe(headerBefore.height);
    expect(await page.evaluate(() => window.scrollY)).toBe(0);

    await page
      .getByRole("navigation", { name: "Workspace", exact: true })
      .getByRole("link", { name: "Imports", exact: true })
      .click();
    await expect(page).toHaveURL(/\/imports$/);
    await expect(page.getByRole("heading", { level: 1, name: "Imports" })).toBeVisible();
    await expect.poll(() => scrollTop(page)).toBe(0);
    expect(
      await page.evaluate(() => (window as Window & { __argusNoReload?: string }).__argusNoReload),
    ).toBe("same-document");
    expect(
      await page.locator(".finance-shell").evaluate((node) => node.getBoundingClientRect().top),
    ).toBe(0);

    const importsPosition = await setScrollTop(page, 400);
    expect(importsPosition).toBeGreaterThan(0);
    await page.goBack();
    await expect(page).toHaveURL(/\/transactions$/);
    await expect(page.getByRole("heading", { level: 1, name: "Transactions" })).toBeVisible();
    await expect.poll(() => scrollTop(page)).toBe(transactionsPosition);

    await page.goForward();
    await expect(page).toHaveURL(/\/imports$/);
    await expect(page.getByRole("heading", { level: 1, name: "Imports" })).toBeVisible();
    await expect.poll(() => scrollTop(page)).toBe(importsPosition);

    for (const [label, path, heading] of [
      ["Settings", /\/settings$/, "Settings"],
      ["Overview", /\/home$/, "Overview"],
      ["Transactions", /\/transactions$/, "Transactions"],
    ] as const) {
      await page
        .getByRole("navigation", { name: "Workspace", exact: true })
        .getByRole("link", { name: label, exact: true })
        .click();
      await expect(page).toHaveURL(path);
      await expect(page.getByRole("heading", { level: 1, name: heading })).toBeVisible();
      await expect.poll(() => scrollTop(page)).toBe(0);
      expect(await page.evaluate(() => window.scrollY)).toBe(0);
    }
    expect(
      await page.evaluate(() => (window as Window & { __argusNoReload?: string }).__argusNoReload),
    ).toBe("same-document");

    await page.goto("/home?mode=calendar&preset=month_to_date");
    await expect(page.getByRole("heading", { level: 1, name: "Overview" })).toBeVisible();
    await expect.poll(() => scrollTop(page)).toBe(0);
    const overviewPosition = await setScrollTop(page, 500);
    expect(overviewPosition).toBeGreaterThan(0);
    await page
      .getByRole("navigation", { name: "Overview views" })
      .getByRole("link", { name: "Charts", exact: true })
      .evaluate((node) => (node as HTMLAnchorElement).click());
    await expect(page).toHaveURL(/view=charts/);
    await expect.poll(() => scrollTop(page)).toBe(0);
    expect(await setScrollTop(page, 220)).toBeGreaterThan(0);
    await page.goBack();
    await expect(page).not.toHaveURL(/view=charts/);
    await expect.poll(() => scrollTop(page)).toBe(overviewPosition);
    await page.goForward();
    await expect(page).toHaveURL(/view=charts/);
    await expect.poll(() => scrollTop(page)).toBe(220);

    await page.goto("/settings");
    await expect(page.getByRole("heading", { level: 1, name: "Settings" })).toBeVisible();
    expect(await setScrollTop(page, 120)).toBeGreaterThan(0);
    await page
      .getByRole("tab", { name: "Categories", exact: true })
      .evaluate((node) => (node as HTMLButtonElement).click());
    await expect(page).toHaveURL(/tab=categories/);
    await expect.poll(() => scrollTop(page)).toBeGreaterThan(0);

    await page.goto("/transactions");
    const saved = await page.locator(".finance-scroll").evaluate((node) => {
      node.scrollTop = node.scrollHeight;
      return node.scrollTop;
    });
    expect(saved).toBeGreaterThan(0);
    await page
      .getByRole("navigation", { name: "Workspace", exact: true })
      .getByRole("link", { name: "Imports", exact: true })
      .click();
    await expect(page.getByRole("heading", { level: 1, name: "Imports" })).toBeVisible();
    await page.setViewportSize({ width: 375, height: 1800 });
    await page.goBack();
    await expect(page).toHaveURL(/\/transactions$/);
    await expect(page.getByRole("heading", { level: 1, name: "Transactions" })).toBeVisible();
    const maximum = await page
      .locator(".finance-scroll")
      .evaluate((node) => node.scrollHeight - node.clientHeight);
    await expect.poll(() => scrollTop(page)).toBe(maximum);
    expect(maximum).toBeLessThan(saved);
    expect(await page.evaluate(() => window.scrollY)).toBe(0);

    await page.getByRole("button", { name: "Log out" }).click();
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.locator(".finance-app")).toHaveCount(0);
    const released = await page.evaluate(() => ({
      htmlOverflow: getComputedStyle(document.documentElement).overflow,
      bodyOverflow: getComputedStyle(document.body).overflow,
    }));
    expect(released.htmlOverflow).not.toBe("hidden");
    expect(released.bodyOverflow).not.toBe("hidden");
  });

  test(`${theme}: touch targets, long exact values and enlarged text reflow`, async ({
    browser,
  }) => {
    const context = await browser.newContext({
      viewport: { width: 375, height: 900 },
      hasTouch: true,
      isMobile: true,
    });
    const page = await context.newPage();
    await login(page, theme);
    await page.goto("/transactions");
    expect(
      await page
        .getByRole("button", { name: "Search", exact: true })
        .evaluate((el) => el.getBoundingClientRect().height),
    ).toBeGreaterThanOrEqual(44);
    expect(
      await page
        .getByRole("navigation", { name: "Workspace", exact: true })
        .getByRole("link", { name: "Transactions", exact: true })
        .evaluate((el) => el.getBoundingClientRect().height),
    ).toBeGreaterThanOrEqual(44);
    await page
      .locator('td[data-label="Amount"]')
      .first()
      .evaluate((el) => {
        el.textContent = "-12345678901234567890.01 ZAR";
      });
    await page
      .locator('td[data-label="Classification"]')
      .first()
      .evaluate((el) => {
        el.textContent = "Unconfirmed — ExtremelyLongSyntheticCategoryWithoutSpaces".repeat(3);
      });
    await page.evaluate(() => {
      document.documentElement.style.fontSize = "200%";
      const style = document.createElement("style");
      style.textContent =
        ".finance-workspace * { line-height: 1.5 !important; letter-spacing: .12em !important; word-spacing: .16em !important; } .finance-workspace p { margin-bottom: 2em !important; }";
      document.head.append(style);
    });
    await page.setViewportSize({ width: 320, height: 900 });
    await fits(page);
    const amount = page.locator('td[data-label="Amount"]').first();
    expect(await amount.textContent()).toBe("-12345678901234567890.01 ZAR");
    expect(await amount.evaluate((el) => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
    await page.setViewportSize({ width: 320, height: 480 });
    const shortViewport = await page.evaluate(async () => {
      await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
      const header = document.querySelector(".finance-shell")?.getBoundingClientRect();
      const content = document.querySelector(".finance-scroll")?.getBoundingClientRect();
      return {
        bodyFixed: document.body.getBoundingClientRect().height <= window.innerHeight + 1,
        windowScroll: window.scrollY,
        headerBottom: header?.bottom ?? -1,
        contentTop: content?.top ?? -1,
        contentHeight: content?.height ?? 0,
      };
    });
    expect(shortViewport.bodyFixed).toBe(true);
    expect(shortViewport.windowScroll).toBe(0);
    expect(shortViewport.contentTop).toBeGreaterThanOrEqual(shortViewport.headerBottom - 1);
    expect(shortViewport.contentHeight).toBeGreaterThan(0);
    await page.mouse.move(10, 10);
    await page.mouse.wheel(0, 5000);
    await expect.poll(() => scrollTop(page)).toBe(0);
    expect(await page.evaluate(() => window.scrollY)).toBe(0);
    await page
      .getByRole("navigation", { name: "Workspace", exact: true })
      .getByRole("link", { name: "Settings", exact: true })
      .focus();
    const focusedNavigation = await page.evaluate(() => {
      const header = document.querySelector(".finance-shell")?.getBoundingClientRect();
      const active = document.activeElement?.getBoundingClientRect();
      return {
        visible:
          header !== undefined &&
          active !== undefined &&
          active.top >= header.top - 1 &&
          active.bottom <= header.bottom + 1,
        windowScroll: window.scrollY,
      };
    });
    expect(focusedNavigation.visible).toBe(true);
    expect(focusedNavigation.windowScroll).toBe(0);
    await fits(page);
    await context.close();
  });
}
