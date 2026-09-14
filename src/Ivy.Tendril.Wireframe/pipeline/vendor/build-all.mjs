// Produces everything under ../../artifacts/ that the .NET tool embeds.
//
// Run this on a machine with node whenever the pinned package versions change. The
// artifacts/ directory is checked into git so CI, contributors and end users never need
// node -- ARTIFACTS.lock.json lets CI verify the checked-in output still matches.
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { pathToFileURL } from "node:url";

import { buildVendor } from "./build-vendor.mjs";
import { fetchFonts, copyLibraryCss } from "./fetch-fonts.mjs";
import { buildCss } from "./build-css.mjs";
import { collectTypes } from "./collect-types.mjs";
import { fetchEsbuild, ESBUILD_VERSION } from "./fetch-esbuild.mjs";

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1"));
const artifacts = path.resolve(here, "../../artifacts");

const step = (name) => console.log(`\n> ${name}`);

/** Copies the library's generated component/prop manifest, which drives agent-readme. */
function copyManifest() {
  const src = path.join(here, "node_modules/tendril-wireframes/dist/tendril.manifest.json");
  const dst = path.join(artifacts, "tendril.manifest.json");
  const raw = fs.readFileSync(src, "utf8");
  const doc = JSON.parse(raw);
  // Re-serialize compactly: this ships inside the assembly and is only ever machine-read.
  fs.writeFileSync(dst, JSON.stringify(doc));
  const total = doc.components.length;
  const publicCount = doc.components.filter((c) => !c.internal).length;
  return { bytes: fs.statSync(dst).size, total, publicCount };
}

/** Walks artifacts/ and records a SHA-256 per file, so CI can detect drift. */
function writeLock(extra) {
  const files = {};
  const walk = (dir) => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) walk(p);
      else if (e.name !== "ARTIFACTS.lock.json") {
        const rel = path.relative(artifacts, p).split(path.sep).join("/");
        files[rel] = crypto.createHash("sha256").update(fs.readFileSync(p)).digest("hex");
      }
    }
  };
  walk(artifacts);

  const lock = {
    generated: "Do not edit. Produced by build/vendor/build-all.mjs.",
    inputs: extra,
    fileCount: Object.keys(files).length,
    totalBytes: Object.keys(files).reduce((n, f) => n + fs.statSync(path.join(artifacts, f)).size, 0),
    files,
  };
  fs.writeFileSync(path.join(artifacts, "ARTIFACTS.lock.json"), JSON.stringify(lock, null, 2) + "\n");
  return lock;
}

export async function buildAll() {
  fs.mkdirSync(artifacts, { recursive: true });

  step("vendor bundle (react, react-dom, tendril-wireframes, lucide-react)");
  const vendor = await buildVendor();
  console.log(`  ${vendor.sizes.length} files, ${(vendor.total / 1024 / 1024).toFixed(2)} MB`);

  step("library stylesheets (Google Fonts @import stripped)");
  for (const c of copyLibraryCss()) {
    console.log(`  ${c.name.padEnd(24)} ${(c.bytes / 1024).toFixed(1)} KB  (-${c.strippedBytes} B @import)`);
  }

  step("self-hosted Balsamiq Sans");
  const fonts = await fetchFonts();
  console.log(`  ${fonts.files.length} woff2 files, ${fonts.faces.length} @font-face rules`);

  step("Tailwind utility superset");
  const css = buildCss();
  const missing = Object.entries(css.has).filter(([, ok]) => !ok).map(([c]) => c);
  if (missing.length) throw new Error(`utility superset is missing: ${missing.join(", ")}`);
  console.log(`  wireframe-utilities.css  ${(css.bytes / 1024).toFixed(1)} KB, smoke test passed`);

  step("TypeScript definitions");
  for (const r of collectTypes()) console.log(`  ${r.pkg.padEnd(24)} ${r.files} files`);

  step("component manifest");
  const man = copyManifest();
  console.log(`  ${man.publicCount} public components (${man.total} total), ${(man.bytes / 1024).toFixed(1)} KB`);

  // No esbuild binaries and no Studio UI here. Tendril is a portable dotnet tool, so
  // EsbuildProvisioner downloads the one binary the running platform needs on first use
  // (ESBUILD_VERSION below is still the pinned version), and Studio is not part of Tendril.
  // fetch-esbuild.mjs stays so the standalone Wireframe-Tools pipeline and this one agree
  // on that version.

  step("lockfile");
  const lock = writeLock({
    ...vendor.manifest.versions,
    esbuild: ESBUILD_VERSION,
    tailwindcss: JSON.parse(
      fs.readFileSync(path.join(here, "node_modules/tailwindcss/package.json"), "utf8")
    ).version,
  });
  console.log(`  ${lock.fileCount} files, ${(lock.totalBytes / 1024 / 1024).toFixed(1)} MB total`);
  console.log(`\n  pinned: ${Object.entries(lock.inputs).map(([k, v]) => `${k}@${v}`).join("  ")}`);

  return lock;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  await buildAll();
}
