import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/server/account-actions", () => ({
  registerAccountAction: vi.fn(),
  resendVerificationAction: vi.fn(),
}));

import RegisterPage from "@/app/register/page";
import { PRODUCT_NAME } from "@/lib/brand";

describe("register page", () => {
  it("renders only the email and password fields", () => {
    render(<RegisterPage />);

    expect(
      screen.getByRole("heading", { level: 1, name: "Create an account" }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Email")).toHaveAttribute("autocomplete", "email");
    expect(screen.getByLabelText("Password")).toHaveAttribute("autocomplete", "new-password");
    expect(screen.getByRole("button", { name: "Create account" })).toBeInTheDocument();
    expect(document.querySelectorAll("input")).toHaveLength(2);
  });

  it("keeps the account journey discoverable from the page", () => {
    render(<RegisterPage />);

    expect(screen.getByRole("link", { name: `Back to ${PRODUCT_NAME}` })).toHaveAttribute(
      "href",
      "/",
    );
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", "/login");
  });
});
