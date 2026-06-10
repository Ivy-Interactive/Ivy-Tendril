import { test as base, type Page } from "@playwright/test";
import fs from "fs";
import path from "path";
import { getArtifactsDir } from "../utils/server.js";
import { takeStepScreenshot } from "../utils/screenshots.js";

function sanitizeFilename(name: string): string {
  return name.replace(/[<>:"/\\|?*]/g, "-").replace(/-{2,}/g, "-").slice(0, 200);
}

interface ConsoleEntry {
  type: string;
  text: string;
  timestamp: string;
}

export interface WidgetTestFixtures {
  consoleLogs: ConsoleEntry[];
  pageErrors: Error[];
  stepScreenshot: (description: string) => Promise<string | null>;
}

export const test = base.extend<WidgetTestFixtures>({
  consoleLogs: async ({}, use) => {
    const logs: ConsoleEntry[] = [];
    await use(logs);
  },

  pageErrors: async ({}, use) => {
    const errors: Error[] = [];
    await use(errors);
  },

  stepScreenshot: async ({ page }, use, testInfo) => {
    let stepCounter = 0;
    const widget = path.basename(path.dirname(testInfo.file));
    const spec = path.basename(testInfo.file, ".spec.ts");

    const fn = async (description: string): Promise<string | null> => {
      stepCounter++;
      return takeStepScreenshot(page, widget, spec, stepCounter, description);
    };

    await use(fn);
  },

  page: async ({ page, consoleLogs, pageErrors }, use, testInfo) => {
    page.on("console", (msg) => {
      consoleLogs.push({
        type: msg.type(),
        text: msg.text(),
        timestamp: new Date().toISOString(),
      });
    });

    page.on("pageerror", (error) => {
      pageErrors.push(error);
    });

    await use(page);

    // After test: write console logs to file
    const logsDir = path.join(getArtifactsDir(), "logs", "console");
    fs.mkdirSync(logsDir, { recursive: true });
    const safeName = sanitizeFilename(testInfo.titlePath.join(" - "));
    const logFile = path.join(logsDir, `${safeName}.log`);
    const content = consoleLogs
      .map((e) => `[${e.timestamp}] [${e.type}] ${e.text}`)
      .join("\n");
    if (content) {
      fs.writeFileSync(logFile, content);
    }

    // Write error summary if there were errors
    if (pageErrors.length > 0) {
      const errFile = path.join(logsDir, `${safeName}.errors.log`);
      const errContent = pageErrors
        .map((e) => `[pageerror] ${e.message}\n${e.stack || ""}`)
        .join("\n---\n");
      fs.writeFileSync(errFile, errContent);
    }
  },
});

export { expect } from "@playwright/test";
