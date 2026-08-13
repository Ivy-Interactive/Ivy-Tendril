import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { DraftMarkdown } from "./DraftMarkdown";

const renderContent = (content: string) => {
  const { container } = render(<DraftMarkdown id="w1" content={content} />);
  return container;
};

describe("DraftMarkdown questions callout", () => {
  it("renders a questions fence as a callout containing the fence text", () => {
    const content = "```questions\nShould the retry budget be per-request or per-session?\n```";
    const container = renderContent(content);

    const callout = container.querySelector(".pmv-questions");
    expect(callout).not.toBeNull();
    expect(callout?.textContent).toContain("Should the retry budget be per-request or per-session?");
  });

  it("renders no code block or Prism tokens, proving it bypassed Prism", () => {
    const content = "```questions\nWhat is the retention policy for read notifications?\n```";
    const container = renderContent(content);

    expect(container.querySelector(".pmv-code-block")).toBeNull();
    expect(container.querySelectorAll(".token").length).toBe(0);
  });

  it("still renders a regular js fence as a code block, so the dispatch does not regress", () => {
    const content = "```questions\nWhat about retries?\n```\n\n```js\nconst x = 1;\n```";
    const container = renderContent(content);

    expect(container.querySelector(".pmv-questions")).not.toBeNull();
    expect(container.querySelector(".pmv-code-block")).not.toBeNull();
  });

  it("keeps line breaks for multi-line fence content", () => {
    const content = "```questions\nFirst question?\nSecond question?\n```";
    const container = renderContent(content);

    const content_ = container.querySelector(".pmv-questions-content");
    expect(content_).not.toBeNull();
    expect(content_?.textContent).toBe("First question?\nSecond question?");
  });
});
