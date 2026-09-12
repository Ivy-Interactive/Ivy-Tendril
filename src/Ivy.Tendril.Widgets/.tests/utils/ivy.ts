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

/**
 * The workspace is ready once its tab strip is laid out AND rendering text. An inactive shell pane
 * stays mounted with `visibility: hidden`, which keeps `aria-selected` and `textContent` intact while
 * emptying `innerText` and making every click time out, so presence alone is not readiness.
 */
export async function waitForPlanWorkspace(page: Page): Promise<void> {
  await page.waitForFunction(
    () => {
      const root = document.querySelector(".pws-root");
      if (!root) return false;
      const tabs = [...root.querySelectorAll(".pws-tab")];
      return tabs.length > 0 && tabs.every((tab) => (tab as HTMLElement).innerText.trim().length > 0);
    },
    { timeout: 20_000 },
  );
}
