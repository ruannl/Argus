import { render, screen, waitFor } from "@testing-library/react";
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

import HomePage from "@/app/home/page";
import { auth, signOut } from "@/auth";
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

describe("home page", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(signOut).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(endSessionAction).mockReset();
  });

  it("renders only the backend current-account state for a valid session", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue(account);

    render(await HomePage());

    expect(getCurrentAccount).toHaveBeenCalledWith("backend-token");
    expect(screen.getByText("customer@example.test")).toBeInTheDocument();
    expect(screen.getByText("Subscriber active")).toBeInTheDocument();
    expect(screen.getByText("Admin account")).toBeInTheDocument();
    expect(screen.queryByText("account-id")).not.toBeInTheDocument();
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
    expect(signOut).not.toHaveBeenCalled();
    await waitFor(() => expect(endSessionAction).toHaveBeenCalledTimes(1));
  });

  it("shows an admin link only for admin accounts", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue({ ...account, admin: true });

    render(await HomePage());

    expect(screen.getByRole("link", { name: "Admin" })).toHaveAttribute("href", "/admin");
  });

  it("hides the admin link for non-admin accounts", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue(account);

    render(await HomePage());

    expect(screen.queryByRole("link", { name: "Admin" })).not.toBeInTheDocument();
  });
});
