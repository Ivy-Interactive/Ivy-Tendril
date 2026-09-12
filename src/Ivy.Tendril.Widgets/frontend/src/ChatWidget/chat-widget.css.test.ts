import { describe, expect, it } from "vitest";
import { readFileSync } from "fs";
import { join } from "path";
import { PIN_TOP_PADDING } from "./useThreadScroll";

describe("chat-widget.css", () => {
  const css = readFileSync(join(__dirname, "chat-widget.css"), "utf-8");

  describe("embedded thread", () => {
    it("has top padding matching PIN_TOP_PADDING constant", () => {
      const embeddedThreadBlock = css.slice(
        css.indexOf('.chat-widget-root[data-embedded="true"] .chat-thread {'),
        css.indexOf("}", css.indexOf('.chat-widget-root[data-embedded="true"] .chat-thread {')) + 1
      );

      expect(embeddedThreadBlock).toContain(`padding: ${PIN_TOP_PADDING}px`);
    });

    it("does not have zero top padding", () => {
      const embeddedThreadBlock = css.slice(
        css.indexOf('.chat-widget-root[data-embedded="true"] .chat-thread {'),
        css.indexOf("}", css.indexOf('.chat-widget-root[data-embedded="true"] .chat-thread {')) + 1
      );

      expect(embeddedThreadBlock).not.toMatch(/padding:\s*0\s/);
    });
  });

  describe("sample prompts", () => {
    it("centers the base rule's chip rows", () => {
      const baseBlock = css.slice(
        css.indexOf(".chat-sample-prompts {"),
        css.indexOf("}", css.indexOf(".chat-sample-prompts {")) + 1
      );

      expect(baseBlock).toContain("justify-content: center");
    });

    it("keeps the embedded scroller flush left and non-wrapping", () => {
      const embeddedSamplePromptsBlock = css.slice(
        css.indexOf('.chat-widget-root[data-embedded="true"] .chat-sample-prompts {'),
        css.indexOf(
          "}",
          css.indexOf('.chat-widget-root[data-embedded="true"] .chat-sample-prompts {')
        ) + 1
      );

      expect(embeddedSamplePromptsBlock).toContain("flex-wrap: nowrap");
      expect(embeddedSamplePromptsBlock).toContain("justify-content: flex-start");
    });

    it("hovers with the defined surface/faint tokens, not the removed ones", () => {
      const hoverBlock = css.slice(
        css.indexOf(".chat-sample-prompt:hover {"),
        css.indexOf("}", css.indexOf(".chat-sample-prompt:hover {")) + 1
      );

      expect(hoverBlock).toContain("var(--tch-surface-hover)");
      expect(hoverBlock).toContain("var(--tch-faint)");
      expect(hoverBlock).not.toContain("--tch-bg-hover");
      expect(hoverBlock).not.toContain("--tch-fg-faint");
    });
  });

  describe("token integrity", () => {
    it("defines every --tch-* custom property referenced by a var()", () => {
      const referenced = new Set(
        Array.from(css.matchAll(/var\(\s*(--tch-[a-z0-9-]+)/g)).map((m) => m[1])
      );
      const defined = new Set(
        Array.from(css.matchAll(/^\s*(--tch-[a-z0-9-]+)\s*:/gm)).map((m) => m[1])
      );

      const undefinedTokens = [...referenced].filter((name) => !defined.has(name));

      expect(undefinedTokens).toEqual([]);
    });
  });
});
