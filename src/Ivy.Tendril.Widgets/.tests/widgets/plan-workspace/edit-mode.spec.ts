import { test, expect } from "../../fixtures/widget-test.js";
import { navigateToApp, waitForPlanWorkspace } from "../../utils/ivy.js";

test.describe("PlanWorkspace Edit Mode", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page, "plan-workspace/demo");
    await waitForPlanWorkspace(page);
  });

  test("Edit button mounts CodeMirror editor and swaps action buttons", async ({
    page,
    stepScreenshot,
  }) => {
    await page.locator('.pws-icon-btn[data-tag="Edit"]').click();

    // Editor mounts
    await expect(page.locator(".pws-content .cm-content")).toHaveCount(1);
    await expect(page.locator(".pws-content .monaco-editor")).toHaveCount(0);

    // Action buttons swap
    await expect(page.locator('button[data-tag="Cancel"]')).toBeVisible();
    await expect(page.locator('button[data-tag="Save"]')).toBeVisible();

    // Icon actions are gone
    await expect(page.locator('.pws-icon-btn[data-tag="Edit"]')).toHaveCount(0);
    await expect(page.locator('.pws-icon-btn[data-tag="Update"]')).toHaveCount(0);
    await expect(page.locator('.pws-icon-btn[data-tag="Expand"]')).toHaveCount(0);
    await expect(page.locator('.pws-icon-btn[data-tag="Share"]')).toHaveCount(0);

    await stepScreenshot("edit-mode-active");
  });

  test("Plan 00428 pin: editor content survives tab switch", async ({ page, stepScreenshot }) => {
    // Open editor
    await page.locator('.pws-icon-btn[data-tag="Edit"]').click();
    await expect(page.locator(".pws-content .cm-content")).toBeVisible();

    // Click first line, go to start, type marker
    await page.locator(".pws-content .cm-line").first().click();
    await page.keyboard.press("Home");
    await page.keyboard.type("MARKER42 ");

    // Verify marker inserted
    const firstLine = page.locator(".pws-content .cm-line").first();
    await expect(firstLine).toContainText("MARKER42 # Summary");
    await stepScreenshot("marker-inserted");

    // Switch to Details tab
    await page.getByRole("tab", { name: "Details" }).click();
    await expect(page.locator(".pws-content .cm-content")).toHaveCount(0);

    // Switch back to Plan tab
    await page.getByRole("tab", { name: "Plan" }).click();

    // Editor should be back with content preserved
    await expect(page.locator(".pws-content .cm-content")).toHaveCount(1);
    const firstLineAfter = page.locator(".pws-content .cm-line").first();
    await expect(firstLineAfter).toContainText("MARKER42 # Summary");
    await stepScreenshot("marker-preserved-after-tab-switch");
  });

  test("E shortcut opens edit mode", async ({ page }) => {
    // Click away from any editable element
    await page.locator(".pws-content").click();
    await page.keyboard.press("e");

    await expect(page.locator(".pws-content .cm-content")).toBeVisible();
  });

  test("E key inserts character when caret is in editor", async ({ page }) => {
    // Open editor
    await page.locator('.pws-icon-btn[data-tag="Edit"]').click();
    await expect(page.locator(".pws-content .cm-content")).toBeVisible();

    // Click in editor, press e
    await page.locator(".pws-content .cm-line").first().click();
    await page.keyboard.press("End");
    await page.keyboard.press("e");

    // The character should be inserted
    const firstLine = page.locator(".pws-content .cm-line").first();
    await expect(firstLine).toContainText("# Summarye");
  });

  test("Cancel button closes editor and returns to markdown", async ({ page, stepScreenshot }) => {
    // Open editor
    await page.locator('.pws-icon-btn[data-tag="Edit"]').click();
    await expect(page.locator(".pws-content .cm-content")).toBeVisible();

    // Click Cancel
    await page.locator('button[data-tag="Cancel"]').click();

    // Editor should be gone, markdown should be back
    await expect(page.locator(".pws-content .cm-content")).toHaveCount(0);
    await expect(page.locator(".pws-content .pmv-markdown")).toBeVisible();
    await stepScreenshot("edit-cancelled");
  });
});
