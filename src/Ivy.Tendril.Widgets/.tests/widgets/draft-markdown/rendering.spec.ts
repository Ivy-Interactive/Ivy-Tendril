import { test, expect } from "../../fixtures/widget-test.js";
import { navigateToApp, waitForDraftMarkdown } from "../../utils/ivy.js";

test.describe("DraftMarkdown Rendering Comparison", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page, "draft-markdown/comparison");
    await waitForDraftMarkdown(page);
  });

  test("both panes render with labels", async ({ page, stepScreenshot }) => {
    await expect(page.getByText("Markdown (Framework)")).toBeVisible();
    await expect(page.getByText("DraftMarkdown (Widget)")).toBeVisible();
    await stepScreenshot("both-panes-labeled");
  });

  test("headings render in DraftMarkdown", async ({ page, stepScreenshot }) => {
    const widgetPane = page.locator(".pmv-markdown");
    await expect(widgetPane.locator("h1")).toContainText("Plan: Migrate to Event-Driven Architecture");
    await expect(widgetPane.locator("h2").first()).toContainText("Overview");
    await stepScreenshot("headings-rendered");
  });

  test("tables render in DraftMarkdown", async ({ page, stepScreenshot }) => {
    const widgetPane = page.locator(".pmv-markdown");
    const table = widgetPane.locator("table");
    await expect(table).toBeVisible();
    await expect(table.locator("th").first()).toContainText("Step");
    await expect(table.locator("td").first()).toContainText("Define event schemas");
    await stepScreenshot("table-rendered");
  });

  test("code blocks render with syntax highlighting", async ({ page, stepScreenshot }) => {
    const widgetPane = page.locator(".pmv-markdown");
    const codeBlock = widgetPane.locator(".pmv-code-block").first();
    await expect(codeBlock).toBeVisible();
    await expect(codeBlock).toContainText("order-api");
    await expect(codeBlock.locator(".pmv-code-copy")).toHaveCount(1);
    await stepScreenshot("code-block-rendered");
  });

  test("mermaid diagrams render as SVG", async ({ page, stepScreenshot }) => {
    const widgetPane = page.locator(".pmv-markdown");
    const diagramContainer = widgetPane.locator(".pmv-diagram-container").first();
    await expect(diagramContainer).toBeVisible({ timeout: 15_000 });
    await expect(diagramContainer.locator("svg")).toBeVisible({ timeout: 15_000 });
    await stepScreenshot("mermaid-rendered");
  });

  test("graphviz diagrams render as SVG", async ({ page, stepScreenshot }) => {
    const widgetPane = page.locator(".pmv-markdown");
    const diagrams = widgetPane.locator(".pmv-diagram-container");
    // The dot diagram is the second diagram container (after the two mermaid ones)
    const graphvizDiagram = diagrams.nth(1);
    await expect(graphvizDiagram).toBeVisible({ timeout: 15_000 });
    await expect(graphvizDiagram.locator("svg")).toBeVisible({ timeout: 15_000 });
    await stepScreenshot("graphviz-rendered");
  });

  test("callout alerts render with styling", async ({ page, stepScreenshot }) => {
    const widgetPane = page.locator(".pmv-markdown");
    const alert = widgetPane.locator(".pmv-alert");
    await expect(alert).toBeVisible();
    await expect(alert.locator(".pmv-alert-title")).toContainText("Note");
    await stepScreenshot("callout-rendered");
  });

  test("task lists render with checkboxes", async ({ page, stepScreenshot }) => {
    const widgetPane = page.locator(".pmv-markdown");
    const checkboxes = widgetPane.locator("input[type='checkbox']");
    await expect(checkboxes.first()).toBeVisible();
    // Checked items
    const checked = widgetPane.locator("input[type='checkbox'][checked]");
    expect(await checked.count()).toBeGreaterThan(0);
    await stepScreenshot("task-list-rendered");
  });

  test("links render correctly", async ({ page, stepScreenshot }) => {
    const widgetPane = page.locator(".pmv-markdown");
    const link = widgetPane.locator("a").first();
    await expect(link).toBeVisible();
    await expect(link).toContainText("Plan #01205");
    await stepScreenshot("links-rendered");
  });

  test("no console errors during render", async ({ pageErrors }) => {
    expect(pageErrors).toHaveLength(0);
  });

  test("full visual comparison screenshot", async ({ page, stepScreenshot }) => {
    await page.waitForTimeout(2000);
    await stepScreenshot("full-comparison");
  });
});
