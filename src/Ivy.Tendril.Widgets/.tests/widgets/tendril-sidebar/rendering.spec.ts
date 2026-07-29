import { test, expect } from "../../fixtures/widget-test.js";
import { navigateToApp } from "../../utils/ivy.js";

test.describe("TendrilSidebar Rendering", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page, "tendril-sidebar/demo");
  });

  test("renders sidebar brand header and items", async ({ page, stepScreenshot }) => {
    const sidebar = page.locator(".tendril-sidebar");
    await expect(sidebar).toBeVisible();
    await expect(sidebar.locator(".tendril-sidebar-title")).toContainText("Ivy Tendril");
    await expect(sidebar.locator(".tendril-sidebar-version")).toContainText("v 1.0.20");
    await expect(sidebar.getByText("New Plan")).toBeVisible();
    await expect(sidebar.getByText("Claude Code")).toBeVisible();
    await expect(sidebar.getByText("Dashboard")).toBeVisible();
    await expect(sidebar.getByText("Drafts")).toBeVisible();
    await expect(sidebar.getByText("Review")).toBeVisible();
    await expect(sidebar.getByText("Recommendations")).toBeVisible();
    await expect(sidebar.getByText("Jobs")).toBeVisible();
    await expect(sidebar.getByText("Pull Requests")).toBeVisible();
    await expect(sidebar.getByText("Icebox")).toBeVisible();
    await expect(sidebar.getByText("Help Requests")).toBeVisible();
    await stepScreenshot("sidebar-rendered");
  });

  test("expands and collapses jobs subitems", async ({ page, stepScreenshot }) => {
    const sidebar = page.locator(".tendril-sidebar");
    await expect(sidebar.getByText("acme")).toBeVisible();
    await expect(sidebar.getByText("geo-corp")).toBeVisible();
    await expect(sidebar.getByText("untitled")).toBeVisible();

    // Click Jobs header to collapse subitems
    await sidebar.locator(".tendril-sidebar-group-header").click();
    await expect(sidebar.getByText("acme")).not.toBeVisible();
    await stepScreenshot("jobs-collapsed");

    // Click Jobs header again to expand
    await sidebar.locator(".tendril-sidebar-group-header").click();
    await expect(sidebar.getByText("acme")).toBeVisible();
    await stepScreenshot("jobs-expanded");
  });

  test("triggers selection events", async ({ page, stepScreenshot }) => {
    const sidebar = page.locator(".tendril-sidebar");
    await sidebar.getByText("Drafts").click();
    await expect(page.getByText("Selected: drafts")).toBeVisible();
    await stepScreenshot("item-selected");
  });

  test("toggles collapse state", async ({ page, stepScreenshot }) => {
    const sidebar = page.locator(".tendril-sidebar");
    await sidebar.locator(".tendril-sidebar-collapse-btn").click();
    await expect(sidebar).toHaveClass(/collapsed/);
    await stepScreenshot("sidebar-collapsed");
  });
});
