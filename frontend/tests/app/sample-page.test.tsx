import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import SamplePage from "@/app/sample-page/page";
import { PRODUCT_NAME } from "@/lib/brand";
import {
  FORBIDDEN_UI_PHRASES,
  ILLUSTRATIVE_SAMPLE_LABEL,
  SAMPLE_PAGE_CONTENT,
  SAMPLE_PAGE_FOOTER,
} from "@/lib/landing-content";

describe("illustrative sample page", () => {
  it("renders a complete, clearly labelled sample directly", () => {
    render(<SamplePage />);

    expect(
      screen.getByRole("heading", {
        level: 1,
        name: SAMPLE_PAGE_CONTENT.title,
      }),
    ).toBeInTheDocument();
    expect(screen.getByText(ILLUSTRATIVE_SAMPLE_LABEL)).toBeInTheDocument();
    expect(screen.getByText(SAMPLE_PAGE_CONTENT.context)).toBeInTheDocument();
    expect(screen.getByText(SAMPLE_PAGE_FOOTER)).toBeInTheDocument();

    for (const section of SAMPLE_PAGE_CONTENT.sections) {
      expect(
        screen.getByRole("heading", { level: 2, name: section.heading }),
      ).toBeInTheDocument();
      for (const paragraph of section.paragraphs) {
        expect(screen.getByText(paragraph)).toBeInTheDocument();
      }
    }
  });

  it("provides a real link back to the landing page", () => {
    render(<SamplePage />);

    expect(
      screen.getByRole("link", { name: `Back to ${PRODUCT_NAME}` }),
    ).toHaveAttribute("href", "/");
  });

  it("does not render conversion or unsupported-claim language", () => {
    render(<SamplePage />);

    const renderedText = document.body.textContent ?? "";
    for (const phrase of FORBIDDEN_UI_PHRASES) {
      expect(renderedText).not.toContain(phrase);
    }
  });
});
