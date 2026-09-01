import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { DraftMarkdown } from "./DraftMarkdown";

const renderContent = (content: string) => {
  const { container } = render(<DraftMarkdown id="w1" content={content} />);
  return container;
};

describe("DraftMarkdown code block rendering and width constraints", () => {
  it("renders a markdown document with long code lines without outer nested pre structure", () => {
    const longLine = "const veryLongVariableNameThatExtendsFarBeyondNormalWidth = 'some-very-long-string-value-that-would-cause-horizontal-overflow-if-unconstrained';";
    const markdown = "```typescript\n" + longLine + "\n```";
    const container = renderContent(markdown);

    const markdownBody = container.querySelector(".pmv-markdown");
    expect(markdownBody).not.toBeNull();

    // The code block should be a direct child under .pmv-markdown, not wrapped in an outer <pre>
    const outerPre = markdownBody?.querySelector(":scope > pre");
    expect(outerPre).toBeNull();

    const codeBlock = container.querySelector(".pmv-code-block");
    expect(codeBlock).not.toBeNull();

    // Verify there is only one pre tag inside the code block (inner pre), not nested <pre><div ...><pre>
    const allPres = container.querySelectorAll("pre");
    expect(allPres.length).toBe(1);
    expect(codeBlock?.contains(allPres[0])).toBe(true);
  });

  it("renders code block container with inner pre configured for horizontal scrolling", () => {
    const markdown = "```javascript\nfunction test() {\n  return 42;\n}\n```";
    const container = renderContent(markdown);

    const codeBlock = container.querySelector(".pmv-code-block");
    expect(codeBlock).not.toBeNull();

    const innerPre = codeBlock?.querySelector("pre");
    expect(innerPre).not.toBeNull();
    expect(innerPre?.style.overflowX).toBe("auto");
    expect(innerPre?.style.minWidth).toBe("0px");
    expect(innerPre?.style.maxWidth).toBe("100%");
  });

  it("renders fenced code blocks without language and with specified languages correctly as .pmv-code-block", () => {
    const markdownWithLang = "```csharp\npublic class Foo { }\n```";
    const markdownNoLang = "```\nplain text content\n```";

    const containerWithLang = renderContent(markdownWithLang);
    const codeBlockWithLang = containerWithLang.querySelector(".pmv-code-block");
    expect(codeBlockWithLang).not.toBeNull();
    expect(containerWithLang.querySelectorAll("pre").length).toBe(1);

    const containerNoLang = renderContent(markdownNoLang);
    const codeBlockNoLang = containerNoLang.querySelector(".pmv-code-block");
    expect(codeBlockNoLang).not.toBeNull();
    expect(containerNoLang.querySelectorAll("pre").length).toBe(1);
    expect(codeBlockNoLang?.textContent).toContain("plain text content");
  });
});
