import { test, expect } from "../../fixtures/widget-test.js";
import { navigateToApp, waitForPlanWorkspace } from "../../utils/ivy.js";

test.describe("PlanWorkspace Layout", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page, "plan-workspace/demo");
    await waitForPlanWorkspace(page);
  });

  test("chat and splitter are present with accessible labels", async ({ page }) => {
    const chat = page.locator('.pws-chat[aria-label="Plan chat"]');
    await expect(chat).toBeVisible();

    const resizer = page.locator('[role="separator"][aria-label="Resize chat"]');
    await expect(resizer).toBeVisible();

    // Check default width
    const chatBox = await chat.boundingBox();
    expect(chatBox).not.toBeNull();
    expect(chatBox!.width).toBeCloseTo(420, 10);
  });

  test("drag to resize changes chat width and sets localStorage", async ({ page, stepScreenshot }) => {
    const resizer = page.locator('[role="separator"][aria-label="Resize chat"]');
    const chat = page.locator(".pws-chat");

    // Get initial width
    const initialBox = await chat.boundingBox();
    expect(initialBox).not.toBeNull();
    const initialWidth = initialBox!.width;

    // Get resizer center
    const resizerBox = await resizer.boundingBox();
    expect(resizerBox).not.toBeNull();
    const centerX = resizerBox!.x + resizerBox!.width / 2;
    const centerY = resizerBox!.y + resizerBox!.height / 2;

    // Check dragging flag before
    await expect(page.locator(".pws-root")).toHaveAttribute("data-dragging", "false");

    // Start drag
    await page.mouse.move(centerX, centerY);
    await page.mouse.down();

    // Check dragging flag during
    await expect(page.locator(".pws-root")).toHaveAttribute("data-dragging", "true");

    // Move left in steps
    const targetX = centerX - 220;
    const steps = 12;
    for (let i = 1; i <= steps; i++) {
      const x = centerX + ((targetX - centerX) * i) / steps;
      await page.mouse.move(x, centerY);
      await page.waitForTimeout(20);
    }

    await page.mouse.up();

    // Check dragging flag after
    await expect(page.locator(".pws-root")).toHaveAttribute("data-dragging", "false");

    await stepScreenshot("chat-resized");

    // Check width grew
    const finalBox = await chat.boundingBox();
    expect(finalBox).not.toBeNull();
    const finalWidth = finalBox!.width;
    expect(finalWidth).toBeGreaterThan(initialWidth);

    // Check localStorage matches
    const storedWidth = await page.evaluate(() => localStorage["tendril.plan.chatWidth"]);
    expect(storedWidth).toBe(String(Math.round(finalWidth)));
  });

  test("chat width survives reload", async ({ page }) => {
    const chat = page.locator(".pws-chat");

    // Resize first
    const resizer = page.locator('[role="separator"][aria-label="Resize chat"]');
    const resizerBox = await resizer.boundingBox();
    expect(resizerBox).not.toBeNull();

    const centerX = resizerBox!.x + resizerBox!.width / 2;
    const centerY = resizerBox!.y + resizerBox!.height / 2;

    await page.mouse.move(centerX, centerY);
    await page.mouse.down();
    await page.mouse.move(centerX - 220, centerY);
    await page.mouse.up();

    // Get width after resize
    const boxBeforeReload = await chat.boundingBox();
    expect(boxBeforeReload).not.toBeNull();
    const widthBeforeReload = boxBeforeReload!.width;

    // Reload
    await page.reload();
    await waitForPlanWorkspace(page);

    // Check width matches
    const boxAfterReload = await chat.boundingBox();
    expect(boxAfterReload).not.toBeNull();
    const widthAfterReload = boxAfterReload!.width;
    expect(widthAfterReload).toBeCloseTo(widthBeforeReload, 1);
  });

  test("narrow flag at 1000px viewport width", async ({ page, stepScreenshot }) => {
    await page.setViewportSize({ width: 1000, height: 900 });
    await waitForPlanWorkspace(page);

    const root = page.locator(".pws-root");
    await expect(root).toHaveAttribute("data-narrow", "true");
    await expect(root).toHaveAttribute("data-compact", "false");

    // Icon buttons still visible in narrow mode
    await expect(page.locator(".pws-icon-btn")).toHaveCount(4);

    await stepScreenshot("narrow-mode");
  });

  test("compact flag at 480px viewport width", async ({ page, stepScreenshot }) => {
    await page.setViewportSize({ width: 480, height: 900 });
    await waitForPlanWorkspace(page);

    const root = page.locator(".pws-root");
    await expect(root).toHaveAttribute("data-narrow", "true");
    await expect(root).toHaveAttribute("data-compact", "true");

    // Icon buttons fold into overflow menu in compact mode
    await expect(page.locator(".pws-icon-btn")).toHaveCount(0);

    // More actions button still present
    await expect(page.getByRole("button", { name: "More actions" })).toBeVisible();

    await stepScreenshot("compact-mode");
  });

  test("responsive flags driven by root width not viewport", async ({ page }) => {
    // At 1920px viewport, root width is less due to shell sidebar
    await page.setViewportSize({ width: 1920, height: 1080 });
    await waitForPlanWorkspace(page);

    const root = page.locator(".pws-root");
    const rootBox = await root.boundingBox();
    expect(rootBox).not.toBeNull();

    // Root width should be less than viewport width
    expect(rootBox!.width).toBeLessThan(1920);

    // Check flags match root width, not viewport width
    await expect(root).toHaveAttribute("data-narrow", "false");
    await expect(root).toHaveAttribute("data-compact", "false");
  });
});
