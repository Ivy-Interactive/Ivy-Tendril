import { test, expect } from "../../fixtures/widget-test.js";
import { navigateToApp, waitForPlanWorkspace } from "../../utils/ivy.js";

test.describe("PlanWorkspace Tabs", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page, "plan-workspace/demo");
    await waitForPlanWorkspace(page);
  });

  test("seven tabs are visible with accessible names", async ({ page }) => {
    const summaryTab = page.getByRole("tab", { name: "Summary" });
    const planTab = page.getByRole("tab", { name: "Plan" });
    const detailsTab = page.getByRole("tab", { name: "Details" });

    await expect(summaryTab).toBeVisible();
    await expect(planTab).toBeVisible();
    await expect(detailsTab).toBeVisible();

    // Tabs with badges - match exactly one each
    await expect(page.getByRole("tab", { name: /^Git/ })).toHaveCount(1);
    await expect(page.getByRole("tab", { name: /^Changes/ })).toHaveCount(1);
    await expect(page.getByRole("tab", { name: /^Artifacts/ })).toHaveCount(1);
    await expect(page.getByRole("tab", { name: /^Recommendations/ })).toHaveCount(1);
  });

  test("Plan tab starts selected", async ({ page }) => {
    await expect(page.getByRole("tab", { name: "Plan" })).toHaveAttribute("aria-selected", "true");
  });

  test("clicking Summary tab moves selection and swaps content", async ({ page, stepScreenshot }) => {
    await page.getByRole("tab", { name: "Summary" }).click();

    await expect(page.getByRole("tab", { name: "Summary" })).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".pws-content")).toContainText("The summary tab content renders here");
    await stepScreenshot("summary-selected");
  });

  test("switching back to Plan tab re-renders markdown", async ({ page, stepScreenshot }) => {
    await page.getByRole("tab", { name: "Summary" }).click();
    await page.getByRole("tab", { name: "Plan" }).click();

    await expect(page.getByRole("tab", { name: "Plan" })).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".pws-content .pmv-markdown")).toBeVisible();
    await stepScreenshot("plan-reselected");
  });

  test("badged tabs render badges", async ({ page }) => {
    const badges = page.locator(".pws-tab-badge");
    await expect(badges).toHaveCount(3);
  });

  test("no page errors on load", async ({ pageErrors }) => {
    expect(pageErrors).toHaveLength(0);
  });

  test("no console errors on load", async ({ page }) => {
    const errors: string[] = [];
    const warnings: string[] = [];

    page.on("console", (msg) => {
      if (msg.type() === "error") {
        errors.push(msg.text());
      } else if (msg.type() === "warning") {
        warnings.push(msg.text());
      }
    });

    await page.waitForTimeout(1000);

    expect(errors).toHaveLength(0);
    expect(warnings).toHaveLength(0);
  });
});
