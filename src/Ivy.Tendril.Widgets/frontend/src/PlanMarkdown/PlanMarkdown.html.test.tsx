import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { PlanMarkdown as DraftMarkdown } from "./PlanMarkdown";

const renderContent = (content: string, props: Record<string, unknown> = {}) => {
  const { container } = render(<DraftMarkdown id="w1" content={content} {...props} />);
  return container;
};

describe("DraftMarkdown collapsible sections", () => {
  it("renders <details>/<summary> as real elements", () => {
    const container = renderContent(
      "<details>\n<summary>Should we use X?</summary>\n\nYes, because Y.\n\n</details>",
    );

    const details = container.querySelector("details");
    expect(details).not.toBeNull();
    expect(details!.querySelector("summary")?.textContent).toContain("Should we use X?");
    expect(details!.textContent).toContain("Yes, because Y.");
    // The tags must not leak into the page as literal text.
    expect(container.textContent).not.toContain("<details>");
  });

  it("honours the open attribute", () => {
    const closed = renderContent("<details>\n<summary>Closed</summary>\n\nBody\n\n</details>");
    expect(closed.querySelector("details")!.hasAttribute("open")).toBe(false);

    const open = renderContent("<details open>\n<summary>Open</summary>\n\nBody\n\n</details>");
    expect(open.querySelector("details")!.hasAttribute("open")).toBe(true);
  });

  it("renders markdown inside the body", () => {
    const container = renderContent(
      "<details>\n<summary>Details</summary>\n\n- one\n- two\n\n`code` and **bold**\n\n</details>",
    );

    const details = container.querySelector("details")!;
    expect(details.querySelectorAll("li")).toHaveLength(2);
    expect(details.querySelector("code")?.textContent).toBe("code");
    expect(details.querySelector("strong")?.textContent).toBe("bold");
  });

  it("renders nested collapsible sections", () => {
    const container = renderContent(
      "<details>\n<summary>Outer</summary>\n\n<details>\n<summary>Inner</summary>\n\nBody\n\n</details>\n\n</details>",
    );

    expect(container.querySelectorAll("details")).toHaveLength(2);
    expect(container.querySelector("details details summary")?.textContent).toContain("Inner");
  });
});

describe("DraftMarkdown raw HTML sanitisation", () => {
  it("drops script tags and their contents", () => {
    const container = renderContent("<details><summary>S</summary></details>\n\n<script>alert(1)</script>");

    expect(container.querySelector("script")).toBeNull();
    expect(container.textContent).not.toContain("alert(1)");
  });

  it("drops iframes, styles and object embeds", () => {
    const container = renderContent(
      '<details><summary>S</summary></details>\n\n<iframe src="https://evil.test"></iframe>\n<style>body{display:none}</style>\n<object data="x"></object>',
    );

    expect(container.querySelector("iframe")).toBeNull();
    expect(container.querySelector("style")).toBeNull();
    expect(container.querySelector("object")).toBeNull();
    // Dropping an element normally keeps its children; for script and style the
    // contents are code and must go with it rather than land on the page.
    expect(container.textContent).not.toContain("display:none");
  });

  it("strips event handler and inline style attributes", () => {
    const container = renderContent(
      '<details onclick="alert(1)" style="color:red"><summary>S</summary></details>',
    );

    const details = container.querySelector("details")!;
    expect(details.getAttribute("onclick")).toBeNull();
    expect(details.getAttribute("style")).toBeNull();
  });

  it("neutralises javascript: hrefs in raw HTML", () => {
    const container = renderContent(
      '<details><summary>S</summary></details>\n\n<a href="javascript:alert(1)">click</a>',
    );

    const href = container.querySelector("a")?.getAttribute("href");
    expect(href === null || href === "").toBe(true);
  });

  it("keeps http links in raw HTML", () => {
    const container = renderContent(
      '<details><summary>S</summary>\n\n<a href="https://example.test/x">link</a>\n\n</details>',
    );

    expect(container.querySelector("a")?.getAttribute("href")).toBe("https://example.test/x");
  });
});

describe("DraftMarkdown features alongside raw HTML", () => {
  // The raw-HTML pass adds a sanitisation step to the pipeline; these guard
  // against the allow-list quietly stripping what the other features rely on.
  it("still renders math", () => {
    const container = renderContent(
      "<details><summary>S</summary></details>\n\n$$\n\\frac{a}{b}\n$$",
    );

    expect(container.querySelector(".katex")).not.toBeNull();
    expect(container.querySelector(".katex-error")).toBeNull();
  });

  it("still renders highlighted code blocks", () => {
    const container = renderContent(
      "<details><summary>S</summary></details>\n\n```xml\n<note id=\"1\">x</note>\n```",
    );

    expect(container.querySelector(".pmv-code-block")).not.toBeNull();
    expect(container.querySelectorAll(".token").length).toBeGreaterThan(0);
  });

  it("still renders GFM task lists and tables", () => {
    const container = renderContent(
      "<details><summary>S</summary></details>\n\n- [x] done\n- [ ] todo\n\n| A | B |\n| - | - |\n| 1 | 2 |",
    );

    expect(container.querySelectorAll("li.task-list-item")).toHaveLength(2);
    expect(container.querySelectorAll('input[type="checkbox"]')).toHaveLength(2);
    expect(container.querySelector("table td")?.textContent).toBe("1");
  });

  it("still renders local file links when they are allowed", () => {
    const container = renderContent(
      "<details><summary>S</summary></details>\n\n[log](file:///D:/tmp/run.log)",
      { dangerouslyAllowLocalFiles: true },
    );

    expect(container.querySelector("a")?.getAttribute("href")).toBe("file:///D:/tmp/run.log");
  });
});
