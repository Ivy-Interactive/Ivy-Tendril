import { describe, it, expect } from "vitest";
import { readFileSync } from "fs";
import { fileURLToPath } from "url";
import { dirname, join } from "path";

const cssPath = join(dirname(fileURLToPath(import.meta.url)), "dashboard.css");
const css = readFileSync(cssPath, "utf-8");

/** Every `.tdb-kpis { ... }` declaration block, base rule and container overrides alike. */
const kpiGridBlocks = [...css.matchAll(/\.tdb-kpis\s*\{([^}]*)\}/g)].map((m) => m[1]);

describe("dashboard.css KPI grid", () => {
  it("lays four cards out across four columns when full width", () => {
    expect(css).toContain("grid-template-columns: repeat(4, minmax(0, 1fr));");
  });

  it("folds to a balanced 2x2 grid on narrower containers", () => {
    expect(kpiGridBlocks.length).toBeGreaterThan(1);
    const twoColBlocks = kpiGridBlocks.filter((block) => block.includes("repeat(2,"));
    expect(twoColBlocks.length).toBeGreaterThanOrEqual(1);
  });

  it("never falls back to a three column grid, which would leave the fourth card alone", () => {
    for (const block of kpiGridBlocks) {
      expect(block).not.toContain("repeat(3,");
    }
  });

  it("styles the hint as a footnote under the value", () => {
    expect(css).toContain(".tdb-kpi-hint {");
    expect(css).toMatch(/\.tdb-kpi-hint\s*\{[^}]*opacity: 0\.7;/);
  });
});
