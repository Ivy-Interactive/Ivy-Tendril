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
});
