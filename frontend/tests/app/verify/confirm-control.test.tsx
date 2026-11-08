import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/server/account-actions", () => ({
  confirmVerification: vi.fn(),
}));

import ConfirmControl from "@/app/verify/confirm-control";
import { confirmVerification } from "@/lib/server/account-actions";

const token = "a".repeat(64);

describe("verification confirm control", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", `/verify?token=${token}`);
    vi.mocked(confirmVerification).mockReset();
  });

  afterEach(() => {
    window.history.replaceState(null, "", "/verify");
  });

  it.each([
    ["success", { ok: true, message: "Your email is verified. You can sign in." }],
    ["invalid token", { ok: false, message: "This verification link is invalid or has expired." }],
    ["rate limited", { ok: false, message: "Please try again later." }],
    ["unavailable", { ok: false, message: "Something went wrong. Please try again later." }],
  ] as const)("renders the terminal %s result", async (_name, result) => {
    vi.mocked(confirmVerification).mockResolvedValue(result);
    const user = userEvent.setup();
    render(<ConfirmControl />);

    await user.click(screen.getByRole("button", { name: "Verify email" }));

    expect(await screen.findByText(result.message)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Verify email" })).not.toBeInTheDocument();
    expect(confirmVerification).toHaveBeenCalledWith(token);
    if (result.ok) {
      expect(window.location.pathname).toBe("/verify");
      expect(window.location.search).toBe("");
      expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", "/login");
    }
  });

  it("disables while pending and keeps the first result on a double-submit", async () => {
    let resolveFirst!: (result: { ok: true; message: string }) => void;
    vi.mocked(confirmVerification).mockReturnValueOnce(
      new Promise((resolve) => {
        resolveFirst = resolve;
      }),
    );
    vi.mocked(confirmVerification).mockResolvedValueOnce({
      ok: false,
      message: "This verification link is invalid or has expired.",
    });

    const user = userEvent.setup();
    render(<ConfirmControl />);
    const button = screen.getByRole("button", { name: "Verify email" });

    const firstClick = user.click(button);
    await waitFor(() => expect(button).toBeDisabled());
    await user.click(button);
    expect(confirmVerification).toHaveBeenCalledTimes(1);

    resolveFirst({ ok: true, message: "Your email is verified. You can sign in." });
    await firstClick;
    expect(await screen.findByText("Your email is verified. You can sign in.")).toBeInTheDocument();
    expect(
      screen.queryByText("This verification link is invalid or has expired."),
    ).not.toBeInTheDocument();
  });
});
