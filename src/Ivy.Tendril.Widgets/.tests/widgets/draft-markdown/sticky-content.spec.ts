import { test, expect } from "../../fixtures/widget-test.js";
import { navigateToApp, waitForDraftMarkdown } from "../../utils/ivy.js";

test.describe("DraftMarkdown Sticky Content", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page, "draft-markdown/sticky-content");
    await waitForDraftMarkdown(page);
  });

  test("sticky sidebar renders alongside markdown", async ({ page, stepScreenshot }) => {
    const shell = page.locator(".pmv-shell");
    const sticky = shell.locator(".pmv-sticky");
    await expect(sticky).toBeVisible({ timeout: 10_000 });
    await expect(sticky).toContainText("Table of Contents");
    await stepScreenshot("sticky-sidebar-visible");
  });

  test("markdown body renders content", async ({ page, stepScreenshot }) => {
    const body = page.locator(".pmv-body");
    await expect(body).toContainText("Document with Fixed Sidebar");
    await expect(body).toContainText("Section One");
    await stepScreenshot("markdown-body-rendered");
  });

  test("sticky content positions at top on mobile", async ({ page, stepScreenshot }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    const shell = page.locator(".pmv-shell");
    const sticky = shell.locator(".pmv-sticky");
    const body = shell.locator(".pmv-body");

    await expect(sticky).toBeVisible({ timeout: 10_000 });
    await stepScreenshot("mobile-layout");

    const stickyBox = await sticky.boundingBox();
    const bodyBox = await body.boundingBox();
    expect(stickyBox).not.toBeNull();
    expect(bodyBox).not.toBeNull();
    expect(stickyBox!.y + stickyBox!.height).toBeLessThanOrEqual(bodyBox!.y);

    const position = await sticky.evaluate((el) => window.getComputedStyle(el).position);
    expect(position).toBe("static");
  });
});
