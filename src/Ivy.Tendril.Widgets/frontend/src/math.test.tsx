import { describe, it, expect } from "vitest";
import { renderToStaticMarkup } from "react-dom/server";
import Markdown from "react-markdown";
import { getMarkdownPlugins, hasMath } from "./math";

const render = (content: string) =>
  renderToStaticMarkup(<Markdown {...getMarkdownPlugins(content)}>{content}</Markdown>);

describe("hasMath", () => {
  it("detects $$ delimiters", () => {
    expect(hasMath("$$E = mc^2$$")).toBe(true);
    expect(hasMath("the relation $$E = mc^2$$ holds")).toBe(true);
  });

  it("does not treat single dollar signs as math", () => {
    expect(hasMath("costs $5")).toBe(false);
    expect(hasMath("$env:PORT and $IsMacOS")).toBe(false);
    expect(hasMath("inline $x^2$ here")).toBe(false);
  });
});

describe("getMarkdownPlugins", () => {
  it("always includes a remark plugin for GFM", () => {
    expect(getMarkdownPlugins("plain text").remarkPlugins).toHaveLength(1);
  });

  it("adds no rehype plugins when the content has no math", () => {
    expect(getMarkdownPlugins("plain text").rehypePlugins).toHaveLength(0);
  });

  it("adds the math plugins when the content has math", () => {
    const plugins = getMarkdownPlugins("$$x^2$$");
    expect(plugins.remarkPlugins).toHaveLength(2);
    expect(plugins.rehypePlugins).toHaveLength(1);
  });
});

describe("math rendering", () => {
  it("renders $$...$$ standing alone as a KaTeX display block", () => {
    const html = render("$$\n\\sum_{i=1}^n i = \\frac{n(n+1)}{2}\n$$");
    expect(html).toContain("katex-display");
    expect(html).not.toContain("katex-error");
    // The MathML annotation carries the original TeX source.
    expect(html).toContain("\\frac{n(n+1)}{2}");
  });

  it("renders $$...$$ inside a paragraph inline, not as a display block", () => {
    const html = render("the relation $$E = mc^2$$ holds");
    expect(html).toContain("katex");
    expect(html).not.toContain("katex-display");
    expect(html).toContain("holds");
  });

  it("renders \\frac as math", () => {
    const html = render("$$\\frac{a}{b}$$");
    expect(html).toContain("katex");
    expect(html).not.toContain("katex-error");
  });

  it("leaves prose dollar signs as literal text", () => {
    const prose = "bash expands any $ (e.g. $env:PORT, vars like $IsMacOS) so $ survives, costs $5";
    const html = render(prose);
    expect(html).not.toContain("katex");
    expect(html).toContain("$env:PORT");
    expect(html).toContain("$5");
  });

  it("leaves single-dollar spans as literal text rather than rendering them as math", () => {
    const html = render("inline $x^2$ here");
    expect(html).not.toContain("katex");
    expect(html).toContain("$x^2$");
  });

  it("leaves dollar signs inside code untouched", () => {
    const fenced = render("```bash\necho $PATH\n```");
    expect(fenced).not.toContain("katex");
    expect(fenced).toContain("$PATH");

    const inline = render("use `$env:PORT` here");
    expect(inline).not.toContain("katex");
    expect(inline).toContain("<code>$env:PORT</code>");
  });

  it("renders malformed TeX as a visible KaTeX error instead of throwing", () => {
    const html = render("$$\\frac{a}{$$");
    expect(html).toContain("katex-error");
  });

  it("still renders GFM features when math is present", () => {
    const html = render("- [x] done\n\n$$x^2$$");
    expect(html).toContain("katex");
    expect(html).toContain('type="checkbox"');
  });
});
