import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  getCurrentAccount: vi.fn(),
}));

vi.mock("@/lib/server/account-actions", () => ({
  endSessionAction: vi.fn(),
}));

import { redirect } from "next/navigation";

import AdminPage from "@/app/admin/page";
import { auth } from "@/auth";
import { getCurrentAccount } from "@/lib/server/backend-client";

const adminAccount = {
  id: "account-id",
  email: "admin@example.test",
  email_verified: true,
  subscriber_active: true,
  subscription_started_at: "2030-01-01T00:00:00Z",
  next_billing_at: "2030-02-01T00:00:00Z",
  lifetime: true,
  admin: true,
};

describe("admin page", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(redirect).mockReset();
  });

  it("renders the ADMIN banner and account state for an admin session", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue(adminAccount);

    render(await AdminPage());

    expect(screen.getByText("ADMIN")).toBeInTheDocument();
    expect(screen.getByText("admin@example.test")).toBeInTheDocument();
    expect(screen.getByText("Admin account")).toBeInTheDocument();
    expect(screen.queryByText("account-id")).not.toBeInTheDocument();
    expect(redirect).not.toHaveBeenCalled();
  });

  it("redirects non-admin accounts to the customer home", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue({ ...adminAccount, admin: false });

    render(await AdminPage());

    expect(redirect).toHaveBeenCalledWith("/home");
  });

  it("shows the expired-session child when /v1/me fails", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "dead-backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockRejectedValue(new Error("backend session expired"));

    render(await AdminPage());

    expect(screen.getByText("Your session has expired. Please sign in again.")).toBeInTheDocument();
    expect(screen.queryByText("admin@example.test")).not.toBeInTheDocument();
  });
});
