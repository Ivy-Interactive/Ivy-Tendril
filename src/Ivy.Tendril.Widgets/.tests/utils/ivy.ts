import type { Page } from "@playwright/test";
import { getBaseUrl } from "./server.js";

export async function navigateToApp(page: Page, appId: string): Promise<void> {
  const baseUrl = getBaseUrl();
  await page.goto(`${baseUrl}/${appId}`);
}

export async function waitForDraftMarkdown(page: Page): Promise<void> {
  await page.waitForFunction(
    () => {
      const shell = document.querySelector(".pmv-shell");
      if (!shell) return false;
      const markdown = shell.querySelector(".pmv-markdown");
      return markdown !== null && markdown.children.length > 0;
    },
    { timeout: 20_000 },
  );
}

export async function waitForPageReady(page: Page): Promise<void> {
  await page.waitForFunction(
    () => document.querySelector("[data-ivy-ready]") !== null || document.body.innerText.length > 20,
    { timeout: 20_000 },
  );
}
