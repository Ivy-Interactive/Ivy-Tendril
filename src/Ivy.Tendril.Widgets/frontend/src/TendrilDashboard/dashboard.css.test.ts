import { describe, it, expect } from "vitest";
import { readFileSync } from "fs";
import { fileURLToPath } from "url";
import { dirname, join } from "path";

const cssPath = join(dirname(fileURLToPath(import.meta.url)), "dashboard.css");
const css = readFileSync(cssPath, "utf-8");

/** Every `.tdb-kpis { ... }` declaration block, base rule and container overrides alike. */
const kpiGridBlocks = [...css.matchAll(/\.tdb-kpis\s*\{([^}]*)\}/g)].map((m) => m[1]);

describe("dashboard.css KPI grid", () => {
  it("lays the cards out by available width rather than a hard column count", () => {
    // The card list grew from four to five with the forecast, and a fixed count either squeezes
    // the row or orphans the extra.
    expect(css).toContain("grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));");
  });

  it("never falls back to a two column grid, which would leave the fifth card alone", () => {
    expect(kpiGridBlocks.length).toBeGreaterThan(1);
    for (const block of kpiGridBlocks) {
      expect(block).not.toContain("repeat(2,");
    }
  });

  it("styles the hint as a footnote under the value", () => {
    expect(css).toContain(".tdb-kpi-hint {");
    expect(css).toMatch(/\.tdb-kpi-hint\s*\{[^}]*opacity: 0\.7;/);
  });
});
