// Downloads the esbuild binary for every RID we publish a tool package for.
//
// esbuild ships as a standalone Go binary -- no node runtime involved -- which is exactly
// why it is the bundler for this tool. We pull the platform tarballs straight from the
// npm registry over plain HTTPS; no npm client is required here or at runtime.
//
// These get embedded per-RID via <ToolPackageRuntimeIdentifiers>, so an installed tool
// carries only its own ~11 MB binary and `setup`/`serve` never touch the network.
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import * as tar from "tar";
import { pathToFileURL } from "node:url";

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1"));
const outDir = path.resolve(here, "../../artifacts/esbuild");
const tmpDir = path.join(here, ".esbuild-tmp");

export const ESBUILD_VERSION = JSON.parse(
  fs.readFileSync(path.join(here, "node_modules/esbuild/package.json"), "utf8")
).version;

// .NET RID -> npm platform package. esbuild's Linux binaries are statically linked Go,
// so the same artifact serves glibc and musl; there is no separate musl package.
export const RID_MAP = {
  "win-x64": { pkg: "@esbuild/win32-x64", entry: "esbuild.exe" },
  "win-arm64": { pkg: "@esbuild/win32-arm64", entry: "esbuild.exe" },
  "linux-x64": { pkg: "@esbuild/linux-x64", entry: "bin/esbuild" },
  "linux-arm64": { pkg: "@esbuild/linux-arm64", entry: "bin/esbuild" },
  "linux-musl-x64": { pkg: "@esbuild/linux-x64", entry: "bin/esbuild" },
  "osx-x64": { pkg: "@esbuild/darwin-x64", entry: "bin/esbuild" },
  "osx-arm64": { pkg: "@esbuild/darwin-arm64", entry: "bin/esbuild" },
};

async function tarballFor(pkg, version) {
  const res = await fetch(`https://registry.npmjs.org/${pkg}`);
  if (!res.ok) throw new Error(`registry ${res.status} for ${pkg}`);
  const doc = await res.json();
  const v = doc.versions[version];
  if (!v) throw new Error(`${pkg} has no version ${version}`);
  return { url: v.dist.tarball, integrity: v.dist.integrity };
}

/** Verifies an npm "sha512-<base64>" integrity string. */
function verifyIntegrity(bytes, integrity) {
  const [algo, expected] = integrity.split("-", 2);
  const actual = crypto.createHash(algo).update(bytes).digest("base64");
  if (actual !== expected) {
    throw new Error(`integrity mismatch: expected ${algo}-${expected}, got ${algo}-${actual}`);
  }
}

export async function fetchEsbuild(version = ESBUILD_VERSION) {
  fs.rmSync(outDir, { recursive: true, force: true });
  fs.rmSync(tmpDir, { recursive: true, force: true });
  fs.mkdirSync(outDir, { recursive: true });
  fs.mkdirSync(tmpDir, { recursive: true });

  const report = [];
  const cache = new Map();

  for (const [rid, { pkg, entry }] of Object.entries(RID_MAP)) {
    if (!cache.has(pkg)) {
      const { url, integrity } = await tarballFor(pkg, version);
      const bytes = Buffer.from(await (await fetch(url)).arrayBuffer());
      verifyIntegrity(bytes, integrity);

      const tgz = path.join(tmpDir, `${pkg.replace("/", "__")}.tgz`);
      const dir = path.join(tmpDir, pkg.replace("/", "__"));
      fs.writeFileSync(tgz, bytes);
      fs.mkdirSync(dir, { recursive: true });
      await tar.x({ file: tgz, cwd: dir });
      cache.set(pkg, { dir, integrity });
    }

    const { dir, integrity } = cache.get(pkg);
    // npm tarballs nest everything under "package/".
    const src = path.join(dir, "package", entry);
    if (!fs.existsSync(src)) throw new Error(`${pkg}: no ${entry} in tarball`);

    const ridDir = path.join(outDir, rid);
    fs.mkdirSync(ridDir, { recursive: true });
    const dst = path.join(ridDir, rid.startsWith("win") ? "esbuild.exe" : "esbuild");
    fs.copyFileSync(src, dst);

    report.push({ rid, pkg, bytes: fs.statSync(dst).size, integrity });
  }

  fs.rmSync(tmpDir, { recursive: true, force: true });
  return { version, report };
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const { version, report } = await fetchEsbuild();
  console.log(`  esbuild ${version}`);
  for (const r of report) {
    console.log(`    ${r.rid.padEnd(16)} ${(r.bytes / 1024 / 1024).toFixed(1).padStart(5)} MB  ${r.pkg}`);
  }
}
