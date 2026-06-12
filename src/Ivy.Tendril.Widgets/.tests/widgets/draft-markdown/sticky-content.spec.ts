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
});
