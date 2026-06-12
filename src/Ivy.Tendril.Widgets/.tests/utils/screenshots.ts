import type { Page } from "@playwright/test";
import fs from "fs";
import path from "path";
import { getArtifactsDir } from "./server.js";

export function getScreenshotsDir(): string {
  return path.join(getArtifactsDir(), "screenshots");
}

export async function takeStepScreenshot(
  page: Page,
  widget: string,
  spec: string,
  stepNumber: number,
  description: string,
): Promise<string | null> {
  const hasContent = await page.evaluate(() => {
    const text = document.body.innerText || "";
    const elements = document.body.querySelectorAll("*").length;
    return text.trim().length > 20 || elements > 5;
  });

  if (!hasContent) return null;

  const dir = path.join(getScreenshotsDir(), widget, spec);
  fs.mkdirSync(dir, { recursive: true });

  const paddedStep = String(stepNumber).padStart(2, "0");
  const filename = `${paddedStep}-${description}.png`;
  const filepath = path.join(dir, filename);

  await page.screenshot({ path: filepath, fullPage: false });

  const meta = {
    timestamp: new Date().toISOString(),
    url: page.url(),
    viewport: page.viewportSize(),
    step: stepNumber,
    description,
  };
  fs.writeFileSync(filepath.replace(".png", ".json"), JSON.stringify(meta, null, 2));

  return filepath;
}
