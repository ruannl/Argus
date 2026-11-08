import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import Home from "@/app/page";
import { PRODUCT_NAME } from "@/lib/brand";
import {
  AUDIENCE_OUTCOME_DETAIL,
  AUDIENCE_OUTCOME_LEAD,
  FEATURE_HIGHLIGHTS,
  FORBIDDEN_UI_PHRASES,
  HOW_IT_WORKS_STEPS,
  PRIMARY_CTA_LABEL,
  PRODUCT_TAGLINE,
  STATUS_EXAMPLE,
} from "@/lib/landing-content";

describe("landing page", () => {
  it("presents the Argus finance story and tagline", () => {
    render(<Home />);

    expect(screen.getByRole("heading", { level: 1, name: PRODUCT_NAME })).toBeInTheDocument();
    expect(screen.getByText(PRODUCT_TAGLINE)).toBeInTheDocument();
    expect(screen.getByText(AUDIENCE_OUTCOME_LEAD)).toBeInTheDocument();
    expect(screen.getByText(AUDIENCE_OUTCOME_DETAIL)).toBeInTheDocument();

    for (const feature of FEATURE_HIGHLIGHTS) {
      expect(screen.getByRole("heading", { level: 3, name: feature.heading })).toBeInTheDocument();
    }

    for (const step of HOW_IT_WORKS_STEPS) {
      expect(screen.getByText(step)).toBeInTheDocument();
    }

    expect(screen.getByText(STATUS_EXAMPLE)).toBeInTheDocument();
  });

  it("points the primary call to action at registration", () => {
    render(<Home />);

    const cta = screen.getByRole("link", { name: PRIMARY_CTA_LABEL });
    expect(cta).toHaveAttribute("href", "/register");
  });

  it("does not render conversion or unsupported-claim language", () => {
    render(<Home />);

    const renderedText = document.body.textContent ?? "";
    for (const phrase of FORBIDDEN_UI_PHRASES) {
      expect(renderedText).not.toContain(phrase);
    }
  });
});
