import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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
  listMerchantTags: vi.fn(),
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

describe("settings page", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(listFinancialAccounts).mockReset();
    vi.mocked(listCategories).mockReset();
    vi.mocked(listMerchantTags).mockReset();
    vi.mocked(createAccountAction).mockReset();
    vi.mocked(createMerchantTagAction).mockReset();
    vi.mocked(redirect).mockReset();
  });

  it("renders Accounts, Categories, Merchant tags, and Profile", async () => {
    signedIn();
    loadedWorkspace();

    render(await SettingsPage());

    expect(screen.getByRole("heading", { level: 1, name: "Settings" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Accounts" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Categories" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Merchant tags" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Profile" })).toBeInTheDocument();

    expect(screen.getByText("Cheque")).toBeInTheDocument();
    expect(screen.getByText("Old card")).toBeInTheDocument();
    expect(screen.getAllByText("ZAR").length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText("Groceries")).toBeInTheDocument();
    expect(screen.getByText("Travel")).toBeInTheDocument();
    expect(screen.getByText(/System bucket — cannot be deleted/)).toBeInTheDocument();
    expect(screen.getByText("Woolworths")).toBeInTheDocument();

    // Profile details.
    expect(screen.getByText("customer@example.test")).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "Admin" }).length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByRole("button", { name: "Log out" }).length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText(/Theme preference/)).toBeInTheDocument();

    const nav = screen.getByRole("navigation", { name: "Workspace" });
    expect(within(nav).getByRole("link", { name: "Settings" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(redirect).not.toHaveBeenCalled();
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

    render(await SettingsPage());

    expect(screen.getByText("No financial accounts yet. Add your first ZAR account above.")).toBeInTheDocument();
    expect(screen.getByText("No categories yet.")).toBeInTheDocument();
    expect(screen.getByText("No merchant tags yet. Add your first tag above.")).toBeInTheDocument();
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

    const row = screen.getByText("Groceries").closest("li");
    expect(row).not.toBeNull();
    const kindSelect = within(row as HTMLElement).getByLabelText("Kind");
    expect(screen.queryByText("Changing the kind affects your totals")).not.toBeInTheDocument();

    await user.selectOptions(kindSelect, "Income");

    expect(screen.getByText("Changing the kind affects your totals")).toBeInTheDocument();
    expect(
      screen.getByLabelText("I understand and want to change the kind."),
    ).toBeInTheDocument();
  });

  it("states the exact reassignment count in the category delete confirmation", async () => {
    signedIn();
    loadedWorkspace();

    render(await SettingsPage());

    const row = screen.getByText("Groceries").closest("li");
    expect(
      within(row as HTMLElement).getByText(
        "Deleting this category moves 2 transactions to Uncategorised. No transactions are deleted.",
      ),
    ).toBeInTheDocument();
  });

  it("shows duplicate-name feedback when a merchant tag create is rejected", async () => {
    signedIn();
    loadedWorkspace();
    vi.mocked(createMerchantTagAction).mockResolvedValue({
      ok: false,
      message: "A category or merchant tag with that name already exists.",
      errorKind: "duplicate_name",
    });
    const user = userEvent.setup();

    render(await SettingsPage());

    await user.type(screen.getByLabelText("Merchant tag name"), "woolworths");
    await user.click(screen.getByRole("button", { name: "Add merchant tag" }));

    await waitFor(() =>
      expect(
        screen.getByText("A category or merchant tag with that name already exists."),
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

    await waitFor(() =>
      expect(screen.getByText("Financial account created.")).toBeInTheDocument(),
    );
  });
});
