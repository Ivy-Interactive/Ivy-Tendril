import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { PlanMarkdown as DraftMarkdown } from "./PlanMarkdown";

const renderContent = (content: string) => {
  const { container } = render(<DraftMarkdown id="w1" content={content} />);
  return container;
};

describe("DraftMarkdown XML syntax highlighting", () => {
  it("renders XML code blocks with Prism syntax highlighting elements", () => {
    const xmlContent = "```xml\n<note id=\"1\">\n  <to>Tove</to>\n  <from>Jani</from>\n</note>\n```";
    const container = renderContent(xmlContent);

    const codeBlock = container.querySelector(".pmv-code-block");
    expect(codeBlock).not.toBeNull();

    const tokens = container.querySelectorAll(".token");
    expect(tokens.length).toBeGreaterThan(0);

    const textContent = container.textContent || "";
    expect(textContent).toContain("<note id=\"1\">");
    expect(textContent).toContain("<to>Tove</to>");
  });

  it("renders html, svg, and markup code blocks with syntax highlighting", () => {
    const htmlContent = "```html\n<div class=\"container\">\n  <p>Hello World</p>\n</div>\n```";
    const container = renderContent(htmlContent);

    expect(container.querySelector(".pmv-code-block")).not.toBeNull();
    expect(container.querySelectorAll(".token").length).toBeGreaterThan(0);
  });
});
