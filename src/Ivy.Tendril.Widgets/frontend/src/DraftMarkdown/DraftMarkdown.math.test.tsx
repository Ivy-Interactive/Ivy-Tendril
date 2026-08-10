import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { DraftMarkdown } from "./DraftMarkdown";

const renderContent = (content: string) => {
  const { container } = render(<DraftMarkdown id="w1" content={content} />);
  return container;
};

describe("DraftMarkdown math", () => {
  it("renders block math as a KaTeX display block", () => {
    const container = renderContent("$$\n\\sum_{i=1}^n i\n$$");
    expect(container.querySelector(".katex-display")).not.toBeNull();
    expect(container.querySelector(".katex-error")).toBeNull();
  });

  it("renders math inside a paragraph inline", () => {
    const container = renderContent("the relation $$E = mc^2$$ holds");
    expect(container.querySelector(".katex")).not.toBeNull();
    expect(container.querySelector(".katex-display")).toBeNull();
    expect(container.textContent).toContain("holds");
  });

  it("keeps prose dollar signs as literal text", () => {
    const container = renderContent("bash expands $env:PORT so $ survives, costs $5");
    expect(container.querySelector(".katex")).toBeNull();
    expect(container.textContent).toContain("$env:PORT");
    expect(container.textContent).toContain("$5");
  });

  it("still renders ordinary markdown alongside math", () => {
    const container = renderContent("# Heading\n\n$$x^2$$");
    expect(container.querySelector("h1")?.textContent).toBe("Heading");
    expect(container.querySelector(".katex")).not.toBeNull();
  });
});
