import { test, expect } from "../../fixtures/widget-test.js";
import { navigateToApp, waitForPlanWorkspace } from "../../utils/ivy.js";

test.describe("PlanWorkspace Panels", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page, "plan-workspace/demo");
    await waitForPlanWorkspace(page);
  });

  test("tool buttons have accessible labels and are collapsed", async ({ page }) => {
    const verificationsBtn = page.locator('.pws-tool-btn[aria-label="Verifications"]');
    const questionsBtn = page.locator('.pws-tool-btn[aria-label*="Questions"]');

    await expect(verificationsBtn).toBeVisible();
    await expect(questionsBtn).toBeVisible();

    await expect(verificationsBtn).toHaveAttribute("aria-expanded", "false");
    await expect(questionsBtn).toHaveAttribute("aria-expanded", "false");
  });

  test("clicking verifications tool opens dropdown with verification items", async ({
    page,
    stepScreenshot,
  }) => {
    await page.locator('.pws-tool-btn[aria-label="Verifications"]').click();

    const dropdown = page.locator(".pws-dropdown");
    await expect(dropdown).toBeVisible();

    const title = dropdown.locator(".pws-dropdown-title");
    await expect(title).toHaveText("Verifications");

    // Check for verification items
    await expect(dropdown).toContainText("Build");
    await expect(dropdown).toContainText("Lint");
    await expect(dropdown).toContainText("Test");
    await expect(dropdown).toContainText("Screenshot");
    await expect(dropdown).toContainText("Pass");
    await expect(dropdown).toContainText("CheckResults");
    await expect(dropdown).toContainText("Fail");

    await stepScreenshot("verifications-panel-open");
  });

  test("unread indicator appears before opening questions panel and disappears after", async ({
    page,
  }) => {
    // Check indicator before opening
    const questionsBtn = page.locator('.pws-tool-btn[aria-label*="Questions"]');
    await expect(questionsBtn).toHaveAttribute("data-indicator", "true");

    const dotBefore = page.locator(".pws-tool-dot");
    await expect(dotBefore).toHaveCount(1);

    // Open questions panel
    await questionsBtn.click();

    // Check indicator after opening
    await expect(questionsBtn).toHaveAttribute("data-indicator", "false");

    const dotAfter = page.locator(".pws-tool-dot");
    await expect(dotAfter).toHaveCount(0);
  });

  test("question link in panel returns to Plan tab", async ({ page, stepScreenshot }) => {
    // Switch to Summary tab
    await page.getByRole("tab", { name: "Summary" }).click();
    await expect(page.getByRole("tab", { name: "Summary" })).toHaveAttribute("aria-selected", "true");

    // Open questions panel
    await page.locator('.pws-tool-btn[aria-label*="Questions"]').click();

    // Click a question link (Text.Rich().Link renders as styled span, not <a>)
    const questionLink = page
      .locator(".pws-dropdown-body")
      .getByText("Should existing sessions be kept when the modal replaces the login page?");
    await questionLink.click();

    // Should return to Plan tab
    await expect(page.getByRole("tab", { name: "Plan" })).toHaveAttribute("aria-selected", "true");
    await stepScreenshot("returned-to-plan-tab");
  });

  test("overflow menu exposes all menu items and closes on Escape", async ({
    page,
    stepScreenshot,
  }) => {
    const moreActionsBtn = page.getByRole("button", { name: "More actions" });
    await expect(moreActionsBtn).toHaveAttribute("aria-haspopup", "menu");

    // Open menu
    await moreActionsBtn.click();

    const menu = page.locator(".pws-menu");
    await expect(menu).toBeVisible();

    // Check all 9 menu items
    const items = menu.locator(".pws-menu-item");
    await expect(items).toHaveCount(9);

    const expectedTags = [
      "Split",
      "Delete",
      "CreateIssue",
      "Discuss",
      "OpenInExplorer",
      "OpenInTerminal",
      "OpenInEditor",
      "CopyPath",
      "OpenPlanYaml",
    ];

    for (const tag of expectedTags) {
      await expect(menu.locator(`.pws-menu-item[data-tag="${tag}"]`)).toBeVisible();
    }

    // Check Split is disabled
    await expect(menu.locator('.pws-menu-item[data-tag="Split"]')).toBeDisabled();

    // Check Delete has danger flag
    await expect(menu.locator('.pws-menu-item[data-tag="Delete"]')).toHaveAttribute(
      "data-danger",
      "true",
    );

    await stepScreenshot("overflow-menu-open");

    // Close with Escape
    await page.keyboard.press("Escape");
    await expect(menu).toHaveCount(0);
    await expect(moreActionsBtn).toHaveAttribute("aria-expanded", "false");
  });

  test("Execute button shows loading state", async ({ page, stepScreenshot }) => {
    const executeBtn = page.locator('button[data-tag="Execute"]');
    await executeBtn.click();

    // Check loading state
    const spinner = page.locator(".pws-spin");
    await expect(spinner).toHaveCount(1);
    await expect(executeBtn).toBeDisabled();

    await stepScreenshot("execute-loading");

    // Wait for loading to clear (sample has 2.5s timer, use 6s timeout)
    await expect(spinner).toHaveCount(0, { timeout: 6000 });
    await expect(executeBtn).not.toBeDisabled();
  });

  test("share mode swaps action buttons and shows persona", async ({ page, stepScreenshot }) => {
    // Click floating "Share mode" button
    await page.getByText("Share mode").click();

    // Check action buttons swapped
    await expect(page.locator('button[data-tag="SharePlan"]')).toBeVisible();
    await expect(page.locator('button[data-tag="CopyPlan"]')).toBeVisible();

    // Labelled buttons gone
    await expect(page.locator('button[data-tag="Execute"]')).toHaveCount(0);
    await expect(page.locator('button[data-tag="UpdatePlan"]')).toHaveCount(0);

    // Overflow menu gone
    await expect(page.getByRole("button", { name: "More actions" })).toHaveCount(0);

    // Persona visible
    await expect(page.locator(".pws-persona-name")).toContainText("Curious Otter");
    await expect(page.locator(".pws-avatar")).toContainText("CO");

    await stepScreenshot("share-mode-active");

    // Leave share mode
    await page.getByText("Leave share mode").click();

    // Icon actions back
    await expect(page.locator('.pws-icon-btn[data-tag="Edit"]')).toBeVisible();
    await expect(page.locator('.pws-icon-btn[data-tag="Update"]')).toBeVisible();
    await expect(page.locator('.pws-icon-btn[data-tag="Expand"]')).toBeVisible();
    await expect(page.locator('.pws-icon-btn[data-tag="Share"]')).toBeVisible();
  });
});
