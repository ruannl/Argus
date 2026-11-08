import { render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
  signOut: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  getCurrentAccount: vi.fn(),
}));

vi.mock("@/lib/server/account-actions", () => ({
  endSessionAction: vi.fn(),
}));

import { redirect } from "next/navigation";

import HomePage from "@/app/home/page";
import { auth } from "@/auth";
import { getCurrentAccount } from "@/lib/server/backend-client";
import { endSessionAction } from "@/lib/server/account-actions";

const account = {
  id: "account-id",
  email: "customer@example.test",
  email_verified: true,
  subscriber_active: true,
  subscription_started_at: "2030-01-01T00:00:00Z",
  next_billing_at: "2030-02-01T00:00:00Z",
  lifetime: false,
  admin: false,
};

function signedIn(admin = false) {
  vi.mocked(auth).mockResolvedValue({
    expires: "2030-01-01T00:00:00.000Z",
    authenticated: true,
    user: { backendToken: "backend-token" },
  } as never);
  vi.mocked(getCurrentAccount).mockResolvedValue({ ...account, admin });
}

describe("home page", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(endSessionAction).mockReset();
    vi.mocked(redirect).mockReset();
  });

  it("renders the Overview workspace with persistent navigation", async () => {
    signedIn();

    render(await HomePage());

    expect(getCurrentAccount).toHaveBeenCalledWith("backend-token");
    expect(screen.getByRole("heading", { level: 1, name: "Overview" })).toBeInTheDocument();
    expect(screen.getByText("No transactions yet")).toBeInTheDocument();
    expect(screen.getByText("Summary")).toBeInTheDocument();
    expect(screen.getByText("Charts")).toBeInTheDocument();

    const nav = screen.getByRole("navigation", { name: "Workspace" });
    expect(within(nav).getByRole("link", { name: "Overview" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(within(nav).getByRole("link", { name: "Transactions" })).toHaveAttribute(
      "href",
      "/transactions",
    );
    expect(within(nav).getByRole("link", { name: "Imports" })).toHaveAttribute("href", "/imports");
    expect(within(nav).getByRole("link", { name: "Settings" })).toHaveAttribute(
      "href",
      "/settings",
    );

    // Profile details live in Settings, not on Overview.
    expect(screen.queryByText("customer@example.test")).not.toBeInTheDocument();
    expect(redirect).not.toHaveBeenCalled();
  });

  it("renders the Charts view selector when requested", async () => {
    signedIn();

    render(await HomePage({ searchParams: Promise.resolve({ view: "charts" }) }));

    expect(screen.getByText("No charts yet")).toBeInTheDocument();
    const nav = screen.getByRole("navigation", { name: "Overview views" });
    expect(within(nav).getByRole("link", { name: "Charts" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(within(nav).getByRole("link", { name: "Summary" })).not.toHaveAttribute("aria-current");
  });

  it("shows the expired-session child without account fields when /v1/me fails", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "dead-backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockRejectedValue(new Error("backend session expired"));

    render(await HomePage());

    expect(screen.getByText("Your session has expired. Please sign in again.")).toBeInTheDocument();
    expect(screen.queryByText("customer@example.test")).not.toBeInTheDocument();
    await waitFor(() => expect(endSessionAction).toHaveBeenCalledTimes(1));
  });

  it("redirects to login when no backend session exists", async () => {
    vi.mocked(auth).mockResolvedValue(null as never);

    render(await HomePage());

    expect(redirect).toHaveBeenCalledWith("/login");
  });

  it("shows an admin link only for admin accounts", async () => {
    signedIn(true);

    render(await HomePage());

    expect(screen.getByRole("link", { name: "Admin" })).toHaveAttribute("href", "/admin");
  });

  it("hides the admin link for non-admin accounts", async () => {
    signedIn(false);

    render(await HomePage());

    expect(screen.queryByRole("link", { name: "Admin" })).not.toBeInTheDocument();
  });
});
