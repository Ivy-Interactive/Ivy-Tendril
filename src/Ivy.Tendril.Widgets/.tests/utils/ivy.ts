import type { Locator, Page } from "@playwright/test";
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

/**
 * Drags across the first `chars` characters of `target`'s first text node and
 * returns the resulting selection. Anchored to the first client rect of a Range
 * rather than the element's bounding box: the box midpoint of a wrapped
 * paragraph falls between two lines, where a drag selects nothing and the
 * widget correctly ignores the collapsed selection.
 */
export async function selectTextByDrag(page: Page, target: Locator, chars = 25): Promise<string> {
  await target.scrollIntoViewIfNeeded();
  const rect = await target.evaluate((el, n) => {
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    let node: Node | null;
    while ((node = walker.nextNode())) {
      if ((node.textContent ?? "").trim().length > 3) break;
    }
    if (!node?.textContent) return null;
    const range = document.createRange();
    range.setStart(node, 0);
    range.setEnd(node, Math.min(n, node.textContent.length));
    const first = range.getClientRects()[0];
    return first ? { x: first.x, y: first.y, width: first.width, height: first.height } : null;
  }, chars);
  if (!rect) throw new Error("selectTextByDrag: target has no measurable text rect");

  const y = rect.y + rect.height / 2;
  await page.mouse.move(rect.x + 1, y);
  await page.mouse.down();
  await page.mouse.move(rect.x + rect.width - 1, y);
  await page.mouse.up();
  return page.evaluate(() => String(window.getSelection()));
}
