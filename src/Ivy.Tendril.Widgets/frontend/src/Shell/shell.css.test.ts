import { describe, it, expect } from "vitest";
import { readFileSync } from "fs";
import { fileURLToPath } from "url";
import { dirname, join } from "path";

const cssPath = join(dirname(fileURLToPath(import.meta.url)), "shell.css");
const css = readFileSync(cssPath, "utf-8");

/** The rail tooltip's own rule, not the theme-token block that shares its class. */
function railTooltipRule(source: string): string {
  const start = source.indexOf(".tui-tooltip-content.tsh-rail-tooltip {");
  expect(start).toBeGreaterThan(-1);
  return source.slice(start, source.indexOf("}", start) + 1);
}

describe("shell.css rail plan tooltip", () => {
  const rule = railTooltipRule(css);

  it("sizes itself instead of relying on the popper wrapper", () => {
    expect(rule).toMatch(/width:\s*max-content/);
    expect(rule).toMatch(/max-width:\s*260px/);
  });

  /* position or transform on the shared tooltip content empties the wrapper Radix positions it
     with, collapsing the card to its min-content width - issue #2444. */
  it("leaves positioning to the popper wrapper", () => {
    expect(rule).not.toMatch(/position:/);
    expect(rule).not.toMatch(/transform:/);
  });
});
