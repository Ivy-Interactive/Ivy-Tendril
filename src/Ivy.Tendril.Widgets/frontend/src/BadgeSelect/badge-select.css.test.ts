import { describe, it, expect } from "vitest";
import { readFileSync } from "fs";
import { fileURLToPath } from "url";
import { dirname, join } from "path";

const cssPath = join(dirname(fileURLToPath(import.meta.url)), "badge-select.css");
const css = readFileSync(cssPath, "utf-8");

describe("badge-select.css dropdown search input styling", () => {
  it("forces 2.25rem left padding on search input to prevent overlapping the icon", () => {
    expect(css).toContain("padding-left: 2.25rem !important;");
  });

  it("vertically centers search icon and disables pointer events", () => {
    expect(css).toContain("top: 50% !important;");
    expect(css).toContain("transform: translateY(-50%) !important;");
    expect(css).toContain("pointer-events: none !important;");
  });
});
