import { defineConfig } from "@playwright/test";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  testDir: path.join(__dirname, "widgets"),
  outputDir: path.join(__dirname, "test-results"),
  fullyParallel: false,
  workers: 1,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  globalSetup: path.join(__dirname, "global-setup.ts"),
  globalTeardown: path.join(__dirname, "global-teardown.ts"),
  use: {
    ignoreHTTPSErrors: true,
    viewport: { width: 1920, height: 1080 },
    screenshot: "on",
    trace: "retain-on-failure",
  },
  reporter: [
    ["list"],
    ["json", { outputFile: path.join(__dirname, "artifacts", "test-results.json") }],
  ],
});
