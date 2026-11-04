import { render, screen, waitFor, within } from "@testing-library/react";
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

import ImportsPage from "@/app/imports/page";
import { auth } from "@/auth";
import { getCurrentAccount } from "@/lib/server/backend-client";
import { endSessionAction } from "@/lib/server/account-actions";

const account = {
  id: "account-id",
  email: "customer@example.test",
  email_verified: true,
  subscriber_active: true,
  subscription_started_at: "2030-01-01T00:00:00Z",
  next_billing_at: null,
  lifetime: false,
  admin: false,
};

describe("imports page", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(endSessionAction).mockReset();
    vi.mocked(redirect).mockReset();
  });

  it("renders the Imports workspace with an honest first-run empty state", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue(account);

    render(await ImportsPage());

    expect(screen.getByRole("heading", { level: 1, name: "Imports" })).toBeInTheDocument();
    expect(screen.getByText("No statements imported yet")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /upload/i })).not.toBeInTheDocument();
    expect(document.querySelector('input[type="file"]')).toBeNull();

    const nav = screen.getByRole("navigation", { name: "Workspace" });
    expect(within(nav).getByRole("link", { name: "Imports" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(redirect).not.toHaveBeenCalled();
  });

  it("redirects to login when no backend session exists", async () => {
    vi.mocked(auth).mockResolvedValue(null as never);

    render(await ImportsPage());

    expect(redirect).toHaveBeenCalledWith("/login");
  });

  it("clears a dead backend session", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "dead-backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockRejectedValue(new Error("backend session expired"));

    render(await ImportsPage());

    expect(screen.getByText("Your session has expired. Please sign in again.")).toBeInTheDocument();
    await waitFor(() => expect(endSessionAction).toHaveBeenCalledTimes(1));
  });
});
