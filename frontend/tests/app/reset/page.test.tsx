import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/server/account-actions", () => ({
  resetPasswordAction: vi.fn(),
}));

import ResetPage from "@/app/reset/page";
import { resetPasswordAction } from "@/lib/server/account-actions";

const token = "a".repeat(64);

async function renderReset(searchParams: { token?: string | string[] } = {}) {
  render(await ResetPage({ searchParams: Promise.resolve(searchParams) }));
}

describe("reset page", () => {
  it.each([
    {},
    { token: "" },
    { token: "abc" },
    { token: "a".repeat(63) },
    { token: "A".repeat(64) },
    { token: [token] },
  ])("shows invalid-link copy and does not call the action on GET for %#", async (searchParams) => {
    await renderReset(searchParams);

    expect(
      screen.getByText("This reset link is invalid or has expired."),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText("New password")).not.toBeInTheDocument();
    expect(resetPasswordAction).not.toHaveBeenCalled();
  });

  it("renders the password form for a well-formed token without consuming it or exposing it", async () => {
    await renderReset({ token });

    expect(screen.getByLabelText("New password")).toHaveAttribute(
      "autocomplete",
      "new-password",
    );
    expect(screen.getByLabelText("New password")).toHaveAttribute("minlength", "12");
    expect(screen.getByRole("button", { name: "Reset password" })).toBeInTheDocument();
    expect(resetPasswordAction).not.toHaveBeenCalled();
    expect(document.body.innerHTML).not.toContain(token);
  });
});
