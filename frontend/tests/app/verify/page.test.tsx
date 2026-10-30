import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/server/backend-client", () => ({
  registerAccount: vi.fn(),
  resendVerification: vi.fn(),
  verifyEmail: vi.fn(),
}));

vi.mock("@/lib/server/account-actions", () => ({
  confirmVerification: vi.fn(),
}));

import VerifyPage from "@/app/verify/page";
import { verifyEmail } from "@/lib/server/backend-client";

const token = "a".repeat(64);

async function renderVerify(searchParams: { token?: string | string[] } = {}) {
  render(await VerifyPage({ searchParams: Promise.resolve(searchParams) }));
}

describe("verify page", () => {
  it.each([
    {},
    { token: "" },
    { token: "abc" },
    { token: "a".repeat(63) },
    { token: "A".repeat(64) },
    { token: [token] },
  ])("shows the invalid-link copy and does not call the backend for %#", async (searchParams) => {
    await renderVerify(searchParams);

    expect(
      screen.getByText("This verification link is invalid or has expired."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Verify email" })).not.toBeInTheDocument();
    expect(verifyEmail).not.toHaveBeenCalled();
  });

  it("shows a confirm control for a well-formed token without consuming it on GET", async () => {
    await renderVerify({ token });

    expect(screen.getByRole("button", { name: "Verify email" })).toBeInTheDocument();
    expect(verifyEmail).not.toHaveBeenCalled();
    expect(document.body.innerHTML).not.toContain(token);
  });
});
