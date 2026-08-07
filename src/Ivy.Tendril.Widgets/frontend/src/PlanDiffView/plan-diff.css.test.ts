import { describe, it, expect } from "vitest";
import { readFileSync } from "fs";
import { fileURLToPath } from "url";
import { dirname, join } from "path";

const cssPath = join(dirname(fileURLToPath(import.meta.url)), "plan-diff.css");
const css = readFileSync(cssPath, "utf-8");

function gutterAfterBlock(source: string): string {
  const start = source.indexOf("td.diff-gutter-normal::after,");
  const end = source.indexOf("}", start);
  return source.slice(start, end + 1);
}

describe("plan-diff.css gutter add-comment button", () => {
  const block = gutterAfterBlock(css);

  it("does not draw the plus with a text glyph", () => {
    expect(block).not.toContain('content: "+"');
  });

  it("draws the plus geometrically with centered background bars", () => {
    expect(block).toMatch(/background-size:\s*8px 2px,\s*2px 8px/);
    expect(block).toMatch(/background-position:\s*center center/);
  });
});
