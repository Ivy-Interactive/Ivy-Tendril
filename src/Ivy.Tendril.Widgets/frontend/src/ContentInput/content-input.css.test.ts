import { describe, it, expect } from "vitest";
import { readFileSync } from "fs";
import { fileURLToPath } from "url";
import { dirname, join } from "path";

const cssPath = join(dirname(fileURLToPath(import.meta.url)), "content-input.css");
const css = readFileSync(cssPath, "utf-8");

describe("content-input.css theming and responsive variables", () => {
  it("uses var(--font-sans, ...) or inherit for typography", () => {
    expect(css).toContain("font-family: var(--font-sans, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, sans-serif);");
    expect(css).toMatch(/\.civ-shell button,\s*\.civ-shell input,\s*\.civ-shell textarea\s*\{[^}]*font-family:\s*inherit;/);
    expect(css).toMatch(/\.civ-textarea\s*\{[^}]*font-family:\s*inherit;/);
  });

  it("contains no hardcoded hex or rgba color literals in CSS rules", () => {
    expect(css).not.toMatch(/#[0-9a-fA-F]{3,8}/);
    expect(css).not.toMatch(/rgba?\(/);
  });

  it("applies --accent-foreground on hover/active states for interactive elements", () => {
    expect(css).toMatch(/\.civ-plus-btn:hover\s*\{[^}]*color:\s*var\(--accent-foreground\);/);
    expect(css).toMatch(/\.civ-mic-btn:hover\s*\{[^}]*color:\s*var\(--accent-foreground\);/);
    expect(css).toMatch(/\.civ-project-ghost-btn:hover\s*\{[^}]*color:\s*var\(--accent-foreground\);/);
    expect(css).toMatch(/\.civ-project-menu-item:hover\s*\{[^}]*color:\s*var\(--accent-foreground\);/);
    expect(css).toMatch(/\.civ-dropdown-item:hover\s*\{[^}]*color:\s*var\(--accent-foreground\);/);
  });

  it("uses --primary and --primary-foreground on submit buttons and split button arrows", () => {
    expect(css).toMatch(/\.civ-submit-btn\s*\{[^}]*background:\s*var\(--primary\);[^}]*color:\s*var\(--primary-foreground\);/);
    expect(css).toMatch(/\.civ-submit-btn\.civ-submit-btn-labeled\s*\{[^}]*background:\s*var\(--primary\);[^}]*color:\s*var\(--primary-foreground\);/);
    expect(css).toMatch(/\.civ-split-btn-container\s*\{[^}]*background:\s*var\(--primary\);/);
    expect(css).toMatch(/\.civ-split-btn-arrow\s*\{[^}]*color:\s*var\(--primary-foreground\);/);
  });
});
