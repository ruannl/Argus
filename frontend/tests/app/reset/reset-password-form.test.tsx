import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/server/account-actions", () => ({
  resetPasswordAction: vi.fn(),
}));

import ResetPasswordForm from "@/app/reset/reset-password-form";
import { resetPasswordAction } from "@/lib/server/account-actions";

const token = "a".repeat(64);
const password = "a compliant password";

describe("reset password form", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", `/reset?token=${token}`);
    vi.mocked(resetPasswordAction).mockReset();
  });

  afterEach(() => {
    window.history.replaceState(null, "", "/reset");
  });

  it("submits the token read from the URL and replaces it after success", async () => {
    vi.mocked(resetPasswordAction).mockResolvedValue({
      ok: true,
      message: "Your password has been reset. You can sign in.",
    });
    const user = userEvent.setup();
    render(<ResetPasswordForm />);

    await user.type(screen.getByLabelText("New password"), password);
    await user.click(screen.getByRole("button", { name: "Reset password" }));

    expect(await screen.findByText("Your password has been reset. You can sign in.")).toBeInTheDocument();
    expect(resetPasswordAction).toHaveBeenCalledWith(token, password);
    expect(window.location.pathname).toBe("/reset");
    expect(window.location.search).toBe("");
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", "/login");
    expect(screen.queryByRole("button", { name: "Reset password" })).not.toBeInTheDocument();
  });

  it.each([
    ["policy", { ok: false, message: "That password could not be accepted." }],
    ["rate limit", { ok: false, message: "Please try again later." }],
  ] as const)("keeps the form for a retryable %s result", async (_name, result) => {
    vi.mocked(resetPasswordAction).mockResolvedValueOnce(result).mockResolvedValueOnce({
      ok: true,
      message: "Your password has been reset. You can sign in.",
    });
    const user = userEvent.setup();
    render(<ResetPasswordForm />);
    const input = screen.getByLabelText("New password");

    await user.type(input, password);
    await user.click(screen.getByRole("button", { name: "Reset password" }));
    expect(await screen.findByText(result.message)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reset password" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Reset password" }));
    expect(await screen.findByText("Your password has been reset. You can sign in.")).toBeInTheDocument();
    expect(resetPasswordAction).toHaveBeenCalledTimes(2);
  });

  it("does not call the action for an empty password", async () => {
    render(<ResetPasswordForm />);
    const form = screen.getByRole("button", { name: "Reset password" }).closest("form");

    form?.dispatchEvent(new SubmitEvent("submit", { bubbles: true, cancelable: true }));

    expect(await screen.findByText("That password could not be accepted.")).toBeInTheDocument();
    expect(resetPasswordAction).not.toHaveBeenCalled();
  });

  it("keeps the first terminal result when a second submit is attempted while pending", async () => {
    let resolveFirst!: (result: { ok: true; message: string }) => void;
    vi.mocked(resetPasswordAction).mockReturnValueOnce(
      new Promise((resolve) => {
        resolveFirst = resolve;
      }),
    );
    vi.mocked(resetPasswordAction).mockResolvedValueOnce({
      ok: false,
      message: "This reset link is invalid or has expired.",
    });
    const user = userEvent.setup();
    render(<ResetPasswordForm />);
    const input = screen.getByLabelText("New password");
    const button = screen.getByRole("button", { name: "Reset password" });

    await user.type(input, password);
    const firstSubmit = user.click(button);
    await waitFor(() => expect(button).toBeDisabled());
    await user.click(button);
    expect(resetPasswordAction).toHaveBeenCalledTimes(1);

    resolveFirst({ ok: true, message: "Your password has been reset. You can sign in." });
    await firstSubmit;
    expect(await screen.findByText("Your password has been reset. You can sign in.")).toBeInTheDocument();
    expect(screen.queryByText("This reset link is invalid or has expired.")).not.toBeInTheDocument();
  });
});
