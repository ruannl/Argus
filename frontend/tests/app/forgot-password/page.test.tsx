import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/server/account-actions", () => ({
  requestPasswordResetAction: vi.fn(),
}));

import ForgotPasswordPage from "@/app/forgot-password/page";

describe("forgot-password page", () => {
  it("renders an email-only request form and a login path", () => {
    render(<ForgotPasswordPage />);

    expect(
      screen.getByRole("heading", { level: 1, name: "Forgot your password?" }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Email")).toHaveAttribute("autocomplete", "email");
    expect(screen.getByRole("button", { name: "Request password reset" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to sign in" })).toHaveAttribute(
      "href",
      "/login",
    );
    expect(document.querySelectorAll("input")).toHaveLength(1);
  });
});
