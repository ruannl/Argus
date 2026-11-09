import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { UserEvent } from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("@/lib/server/account-actions", () => ({
  endSessionAction: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  getCurrentAccount: vi.fn(),
  listFinancialAccounts: vi.fn(),
  listCategories: vi.fn(),
  listMerchantAliases: vi.fn(),
  listMerchantTags: vi.fn(),
}));

vi.mock("@/lib/server/alias-actions", () => ({
  createAliasAction: vi.fn(),
  deleteAliasAction: vi.fn(),
  updateAliasAction: vi.fn(),
}));

vi.mock("@/lib/server/finance-actions", () => ({
  archiveAccountAction: vi.fn(),
  archiveCategoryAction: vi.fn(),
  archiveMerchantTagAction: vi.fn(),
  createAccountAction: vi.fn(),
  createCategoryAction: vi.fn(),
  createMerchantTagAction: vi.fn(),
  deleteCategoryAction: vi.fn(),
  deleteMerchantTagAction: vi.fn(),
  renameAccountAction: vi.fn(),
  restoreAccountAction: vi.fn(),
  restoreCategoryAction: vi.fn(),
  restoreMerchantTagAction: vi.fn(),
  updateAccountInstitutionAction: vi.fn(),
  updateCategoryAction: vi.fn(),
  updateMerchantTagAction: vi.fn(),
}));

import { redirect } from "next/navigation";

import SettingsPage from "@/app/settings/page";
import { auth } from "@/auth";
import {
  getCurrentAccount,
  listCategories,
  listFinancialAccounts,
  listMerchantAliases,
  listMerchantTags,
} from "@/lib/server/backend-client";
import { createAccountAction, createMerchantTagAction } from "@/lib/server/finance-actions";

const account = {
  id: "account-id",
  email: "customer@example.test",
  email_verified: true,
  subscriber_active: true,
  subscription_started_at: "2030-01-01T00:00:00Z",
  next_billing_at: null,
  lifetime: false,
  admin: true,
};

const activeAccount = {
  id: "account-active",
  display_name: "Cheque",
  institution: "FNB",
  currency: "ZAR",
  is_archived: false,
  transaction_count: 4,
  created_at: "2026-01-01T00:00:00.0000000Z",
  updated_at: "2026-01-01T00:00:00.0000000Z",
};

const archivedAccount = {
  ...activeAccount,
  id: "account-archived",
  display_name: "Old card",
  institution: null,
  is_archived: true,
  transaction_count: 0,
};

const systemCategory = {
  id: "category-system",
  display_name: "Uncategorised",
  kind: "Uncategorised" as const,
  is_system: true,
  is_default: true,
  is_archived: false,
  transaction_count: 0,
  created_at: "2026-01-01T00:00:00.0000000Z",
  updated_at: "2026-01-01T00:00:00.0000000Z",
};

const groceries = {
  id: "category-groceries",
  display_name: "Groceries",
  kind: "Expense" as const,
  is_system: false,
  is_default: true,
  is_archived: false,
  transaction_count: 2,
  created_at: "2026-01-01T00:00:00.0000000Z",
  updated_at: "2026-01-01T00:00:00.0000000Z",
};

const travel = {
  ...groceries,
  id: "category-travel",
  display_name: "Travel",
  is_archived: true,
  transaction_count: 0,
};

const woolworths = {
  id: "tag-woolworths",
  display_name: "Woolworths",
  is_archived: false,
  transaction_count: 2,
  created_at: "2026-01-01T00:00:00.0000000Z",
  updated_at: "2026-01-01T00:00:00.0000000Z",
};

function signedIn() {
  vi.mocked(auth).mockResolvedValue({
    expires: "2030-01-01T00:00:00.000Z",
    authenticated: true,
    user: { backendToken: "backend-token" },
  } as never);
}

function loadedWorkspace() {
  vi.mocked(getCurrentAccount).mockResolvedValue(account);
  vi.mocked(listFinancialAccounts).mockResolvedValue({
    ok: true,
    value: { accounts: [activeAccount, archivedAccount] },
  });
  vi.mocked(listCategories).mockResolvedValue({
    ok: true,
    value: { categories: [systemCategory, groceries, travel] },
  });
  vi.mocked(listMerchantTags).mockResolvedValue({
    ok: true,
    value: { merchant_tags: [woolworths] },
  });
}

async function openTab(user: UserEvent, name: string) {
  await user.click(screen.getByRole("tab", { name }));
}

function activePanel(): HTMLElement {
  return screen.getByRole("tabpanel");
}

describe("settings page", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(listFinancialAccounts).mockReset();
    vi.mocked(listCategories).mockReset();
    vi.mocked(listMerchantAliases).mockReset();
    vi.mocked(listMerchantTags).mockReset();
    vi.mocked(createAccountAction).mockReset();
    vi.mocked(createMerchantTagAction).mockReset();
    vi.mocked(redirect).mockReset();
    vi.mocked(listMerchantAliases).mockResolvedValue({
      ok: true,
      value: { merchant_aliases: [] },
    });
    window.history.replaceState(null, "", "/settings");
  });

  it("renders one tab per section and opens Accounts first", async () => {
    signedIn();
    loadedWorkspace();

    render(await SettingsPage());

    expect(screen.getByRole("heading", { level: 1, name: "Settings" })).toBeInTheDocument();
    const tablist = screen.getByRole("tablist", { name: "Settings sections" });
    expect(
      within(tablist)
        .getAllByRole("tab")
        .map((tab) => tab.textContent),
    ).toEqual(["Accounts", "Categories", "Tags", "Aliases", "Profile"]);
    expect(screen.getByRole("tab", { name: "Accounts" })).toHaveAttribute("aria-selected", "true");

    const accountsPanel = activePanel();
    expect(
      within(accountsPanel).getByRole("heading", { level: 2, name: "Accounts" }),
    ).toBeInTheDocument();
    expect(within(accountsPanel).getByText("Cheque")).toBeInTheDocument();
    expect(within(accountsPanel).getByText("Old card")).toBeInTheDocument();
    expect(within(accountsPanel).getAllByText("ZAR").length).toBeGreaterThanOrEqual(2);
    expect(within(accountsPanel).getByRole("table")).toBeInTheDocument();

    const nav = screen.getByRole("navigation", { name: "Workspace" });
    expect(within(nav).getByRole("link", { name: "Settings" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(redirect).not.toHaveBeenCalled();
  });

  it("switches panels from the tab list", async () => {
    signedIn();
    loadedWorkspace();
    const user = userEvent.setup();

    render(await SettingsPage());

    await openTab(user, "Categories");
    expect(screen.getByRole("tab", { name: "Categories" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    const categoriesPanel = activePanel();
    expect(
      within(categoriesPanel).getByRole("heading", { level: 2, name: "Categories" }),
    ).toBeInTheDocument();
    expect(within(categoriesPanel).getByText("Groceries")).toBeInTheDocument();
    expect(within(categoriesPanel).getByText("Travel")).toBeInTheDocument();
    expect(
      within(categoriesPanel).getByText(/System bucket — cannot be deleted/),
    ).toBeInTheDocument();

    await openTab(user, "Tags");
    const tagsPanel = activePanel();
    expect(within(tagsPanel).getByRole("heading", { level: 2, name: "Tags" })).toBeInTheDocument();
    expect(within(tagsPanel).getByText("Woolworths")).toBeInTheDocument();

    await openTab(user, "Aliases");
    expect(within(activePanel()).getByText("No aliases yet. Add one above.")).toBeInTheDocument();

    await openTab(user, "Profile");
    const profilePanel = activePanel();
    expect(
      within(profilePanel).getByRole("heading", { level: 2, name: "Profile" }),
    ).toBeInTheDocument();
    expect(within(profilePanel).getByText("customer@example.test")).toBeInTheDocument();
    expect(within(profilePanel).getByRole("link", { name: "Admin" })).toBeInTheDocument();
    expect(within(profilePanel).getByRole("button", { name: "Log out" })).toBeInTheDocument();
    expect(within(profilePanel).getByText(/Theme preference/)).toBeInTheDocument();
  });

  it("renders merchant aliases with their tag and category", async () => {
    signedIn();
    loadedWorkspace();
    vi.mocked(listMerchantAliases).mockResolvedValue({
      ok: true,
      value: {
        merchant_aliases: [
          {
            id: "alias-id",
            display_alias: "apteek",
            merchant_tag_id: "tag-id",
            tag_display_name: "Serene Pharmacy",
            category_id: "category-id",
            category_display_name: "Health",
            created_at: "2026-01-01T00:00:00Z",
            updated_at: "2026-01-01T00:00:00Z",
          },
        ],
      },
    });
    const user = userEvent.setup();

    render(await SettingsPage());
    await openTab(user, "Aliases");

    const panel = activePanel();
    expect(within(panel).getByText("apteek")).toBeInTheDocument();
    expect(within(panel).getByText("Serene Pharmacy")).toBeInTheDocument();
    expect(within(panel).getByText("Health")).toBeInTheDocument();
  });

  it("keeps the active tab in sync with same-route navigation", async () => {
    signedIn();
    loadedWorkspace();

    const view = render(
      await SettingsPage({ searchParams: Promise.resolve({ tab: "categories" }) }),
    );
    expect(screen.getByRole("tab", { name: "Categories" })).toHaveAttribute(
      "aria-selected",
      "true",
    );

    view.rerender(await SettingsPage({ searchParams: Promise.resolve({ tab: "profile" }) }));
    expect(screen.getByRole("tab", { name: "Profile" })).toHaveAttribute("aria-selected", "true");
  });

  it("sorts the category table by name", async () => {
    signedIn();
    vi.mocked(getCurrentAccount).mockResolvedValue(account);
    vi.mocked(listFinancialAccounts).mockResolvedValue({
      ok: true,
      value: { accounts: [activeAccount] },
    });
    vi.mocked(listCategories).mockResolvedValue({
      ok: true,
      value: { categories: [systemCategory, travel, groceries] },
    });
    vi.mocked(listMerchantTags).mockResolvedValue({
      ok: true,
      value: { merchant_tags: [] },
    });
    const user = userEvent.setup();

    render(await SettingsPage());
    await openTab(user, "Categories");

    const table = within(activePanel()).getByRole("table");
    const rows = within(table).getAllByRole("row").slice(1);
    expect(rows.map((row) => within(row).getAllByRole("cell")[0].textContent)).toEqual([
      "Groceries",
      "Travel",
    ]);
  });

  it("moves between tabs with the arrow, Home, and End keys", async () => {
    signedIn();
    loadedWorkspace();
    const user = userEvent.setup();

    render(await SettingsPage());

    screen.getByRole("tab", { name: "Accounts" }).focus();
    await user.keyboard("{ArrowRight}");

    expect(screen.getByRole("tab", { name: "Categories" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    expect(screen.getByRole("tab", { name: "Categories" })).toHaveFocus();

    await user.keyboard("{End}");
    expect(screen.getByRole("tab", { name: "Profile" })).toHaveAttribute("aria-selected", "true");

    await user.keyboard("{Home}");
    expect(screen.getByRole("tab", { name: "Accounts" })).toHaveAttribute("aria-selected", "true");
  });

  it("opens the section named by the tab query parameter", async () => {
    signedIn();
    loadedWorkspace();

    render(await SettingsPage({ searchParams: Promise.resolve({ tab: "merchant-tags" }) }));

    expect(screen.getByRole("tab", { name: "Tags" })).toHaveAttribute("aria-selected", "true");
    expect(
      within(activePanel()).getByRole("heading", { level: 2, name: "Tags" }),
    ).toBeInTheDocument();
  });

  it("shows honest empty states when nothing exists yet", async () => {
    signedIn();
    vi.mocked(getCurrentAccount).mockResolvedValue(account);
    vi.mocked(listFinancialAccounts).mockResolvedValue({
      ok: true,
      value: { accounts: [] },
    });
    vi.mocked(listCategories).mockResolvedValue({
      ok: true,
      value: { categories: [] },
    });
    vi.mocked(listMerchantTags).mockResolvedValue({
      ok: true,
      value: { merchant_tags: [] },
    });
    const user = userEvent.setup();

    render(await SettingsPage());

    expect(
      within(activePanel()).getByText(
        "No financial accounts yet. Add your first ZAR account above.",
      ),
    ).toBeInTheDocument();

    await openTab(user, "Categories");
    expect(within(activePanel()).getByText("No categories yet.")).toBeInTheDocument();

    await openTab(user, "Tags");
    expect(
      within(activePanel()).getByText("No tags yet. Add your first tag above."),
    ).toBeInTheDocument();
  });

  it("shows the load notice when a finance list fails", async () => {
    signedIn();
    vi.mocked(getCurrentAccount).mockResolvedValue(account);
    vi.mocked(listFinancialAccounts).mockResolvedValue({ ok: false, kind: "unavailable" });
    vi.mocked(listCategories).mockResolvedValue({
      ok: true,
      value: { categories: [] },
    });
    vi.mocked(listMerchantTags).mockResolvedValue({
      ok: true,
      value: { merchant_tags: [] },
    });

    render(await SettingsPage());

    expect(
      screen.getByText("We could not load your finance workspace. Please refresh the page."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("heading", { level: 2, name: "Accounts" })).not.toBeInTheDocument();
    expect(
      within(activePanel()).getByText(
        "This section could not be loaded. Refresh the page to try again.",
      ),
    ).toBeInTheDocument();
  });

  it("redirects to login when no backend session exists", async () => {
    vi.mocked(auth).mockResolvedValue(null as never);

    render(await SettingsPage());

    expect(redirect).toHaveBeenCalledWith("/login");
  });

  it("clears a dead backend session", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "dead-backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockRejectedValue(new Error("backend session expired"));

    render(await SettingsPage());

    expect(screen.getByText("Your session has expired. Please sign in again.")).toBeInTheDocument();
  });

  it("warns before a category kind change", async () => {
    signedIn();
    loadedWorkspace();
    const user = userEvent.setup();

    render(await SettingsPage());
    await openTab(user, "Categories");

    await user.click(screen.getByRole("button", { name: "Edit Groceries" }));
    const kindSelect = screen.getByLabelText("Kind for Groceries");
    expect(screen.queryByText("Changing the kind affects your totals")).not.toBeInTheDocument();

    await user.selectOptions(kindSelect, "Income");

    expect(screen.getByText("Changing the kind affects your totals")).toBeInTheDocument();
    expect(screen.getByLabelText("I understand and want to change the kind.")).toBeInTheDocument();
  });

  it("states the exact reassignment count in the category delete confirmation", async () => {
    signedIn();
    loadedWorkspace();
    const user = userEvent.setup();

    render(await SettingsPage());
    await openTab(user, "Categories");
    await user.click(screen.getByRole("button", { name: "Edit Groceries" }));

    const confirmation = screen.getByText(
      /Deleting this category moves 2 transactions to Uncategorised/,
    );
    expect(confirmation.textContent).toContain(
      "Any suggestion aliases that point to this category are also deleted.",
    );
  });

  it("shows duplicate-name feedback when a tag create is rejected", async () => {
    signedIn();
    loadedWorkspace();
    vi.mocked(createMerchantTagAction).mockResolvedValue({
      ok: false,
      message: "A category or tag with that name already exists.",
      errorKind: "duplicate_name",
    });
    const user = userEvent.setup();

    render(await SettingsPage());
    await openTab(user, "Tags");

    await user.type(screen.getByLabelText("Tag name"), "woolworths");
    await user.click(screen.getByRole("button", { name: "Add tag" }));

    await waitFor(() =>
      expect(
        screen.getByText("A category or tag with that name already exists."),
      ).toBeInTheDocument(),
    );
  });

  it("shows success feedback after creating an account", async () => {
    signedIn();
    loadedWorkspace();
    vi.mocked(createAccountAction).mockResolvedValue({
      ok: true,
      message: "Financial account created.",
    });
    const user = userEvent.setup();

    render(await SettingsPage());

    await user.type(screen.getByLabelText("Account name"), "Savings");
    await user.click(screen.getByRole("button", { name: "Add ZAR account" }));

    await waitFor(() => expect(screen.getByText("Financial account created.")).toBeInTheDocument());
  });
});
