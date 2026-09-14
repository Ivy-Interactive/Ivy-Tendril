// Vendors the .d.ts closure so editors give the agent IntelliSense with no node_modules.
//
// This works because VS Code ships its own tsserver -- the editor needs the type files on
// disk and a tsconfig `paths` mapping to find them, but it does not need node installed.
// `wireframe setup` writes .wireframe/types/** from these artifacts and points tsconfig at
// them; see TypeDefinitionWriter on the C# side.
//
// Note this buys IntelliSense, not enforcement: nothing runs tsc. Prop-level correctness
// is covered separately by the manifest-driven checks.
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1"));
const nm = path.join(here, "node_modules");
const outDir = path.resolve(here, "../../artifacts/types");

/** Packages whose .d.ts files we copy wholesale. */
const PACKAGES = [
  { from: "@types/react", to: "react" },
  { from: "@types/react-dom", to: "react-dom" },
  { from: "csstype", to: "csstype" },
  { from: "tendril-wireframes/dist", to: "tendril-wireframes" },
];

/** Bare specifiers reachable from user code that have no real types to vendor. */
const SHIMS = {
  "lucide-react": [
    "// Minimal ambient shim -- lucide-react's own types are large and mostly icon names.",
    'import type { FC, SVGProps } from "react";',
    "export interface LucideProps extends SVGProps<SVGSVGElement> {",
    "  size?: string | number;",
    "  absoluteStrokeWidth?: boolean;",
    "}",
    "export type LucideIcon = FC<LucideProps>;",
    "declare const icons: Record<string, LucideIcon>;",
    "export { icons };",
    "export const _default: Record<string, LucideIcon>;",
    "export default _default;",
  ].join("\n"),
};

function copyDts(srcDir, dstDir) {
  let count = 0;
  for (const entry of fs.readdirSync(srcDir, { withFileTypes: true })) {
    const src = path.join(srcDir, entry.name);
    const dst = path.join(dstDir, entry.name);
    if (entry.isDirectory()) {
      count += copyDts(src, dst);
    } else if (entry.name.endsWith(".d.ts") || entry.name === "package.json") {
      fs.mkdirSync(dstDir, { recursive: true });
      fs.copyFileSync(src, dst);
      count++;
    }
  }
  return count;
}

export function collectTypes() {
  fs.rmSync(outDir, { recursive: true, force: true });
  fs.mkdirSync(outDir, { recursive: true });

  const report = [];
  for (const { from, to } of PACKAGES) {
    const src = path.join(nm, from);
    if (!fs.existsSync(src)) throw new Error(`missing type source: ${src}`);
    const n = copyDts(src, path.join(outDir, to));
    report.push({ pkg: to, files: n });
  }

  for (const [name, source] of Object.entries(SHIMS)) {
    const dir = path.join(outDir, name);
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, "index.d.ts"), source + "\n");
    report.push({ pkg: name, files: 1, shim: true });
  }

  return report;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  for (const r of collectTypes()) {
    console.log(`  ${r.pkg.padEnd(22)} ${String(r.files).padStart(3)} files${r.shim ? "  (shim)" : ""}`);
  }
}
