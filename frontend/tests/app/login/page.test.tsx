import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  redirect: vi.fn(),
  useRouter: () => ({ replace: vi.fn() }),
}));

vi.mock("@/lib/server/backend-client", () => ({
  getCurrentAccount: vi.fn(),
}));

vi.mock("@/lib/server/account-actions", () => ({
  endSessionAction: vi.fn(),
  loginAction: vi.fn(),
}));

import LoginPage from "@/app/login/page";
import { auth } from "@/auth";
import { getCurrentAccount } from "@/lib/server/backend-client";
import { endSessionAction } from "@/lib/server/account-actions";
import { redirect } from "next/navigation";

describe("login page", () => {
  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(getCurrentAccount).mockReset();
    vi.mocked(endSessionAction).mockReset();
    vi.mocked(redirect).mockReset();
  });

  it("renders the form and clears a dead backend session without redirecting home", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "dead-backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockRejectedValue(new Error("backend session expired"));

    render(await LoginPage());

    expect(screen.getByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
    expect(screen.getByLabelText("Email")).toBeInTheDocument();
    expect(screen.getByLabelText("Password")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Forgot password?" })).toHaveAttribute(
      "href",
      "/forgot-password",
    );
    expect(screen.getByText("Your session has expired. Please sign in again.")).toBeInTheDocument();
    expect(redirect).not.toHaveBeenCalled();
    await waitFor(() => expect(endSessionAction).toHaveBeenCalledTimes(1));
  });

  it("redirects a valid backend session to home", async () => {
    vi.mocked(auth).mockResolvedValue({
      expires: "2030-01-01T00:00:00.000Z",
      authenticated: true,
      user: { backendToken: "backend-token" },
    } as never);
    vi.mocked(getCurrentAccount).mockResolvedValue({
      id: "account-id",
      email: "customer@example.test",
      email_verified: true,
      subscriber_active: true,
      subscription_started_at: null,
      next_billing_at: null,
      lifetime: false,
      admin: false,
    });

    await LoginPage();

    expect(redirect).toHaveBeenCalledWith("/home");
  });
});
