import { test, expect } from "../../fixtures/widget-test.js";
import { navigateToApp, waitForDraftMarkdown } from "../../utils/ivy.js";

test.describe("DraftMarkdown Collapsible Sections", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page, "draft-markdown/collapsible");
    await waitForDraftMarkdown(page);
  });

  test("details blocks render as disclosure widgets, not literal text", async ({
    page,
    stepScreenshot,
  }) => {
    const widgetPane = page.locator(".pmv-markdown");
    await expect(widgetPane.locator("details").first()).toBeVisible();
    await expect(widgetPane.locator("summary").first()).toContainText(
      "What database does the plan target?",
    );
    await expect(widgetPane).not.toContainText("<summary>");
    await stepScreenshot("details-rendered");
  });

  test("a closed section hides its body until clicked", async ({ page, stepScreenshot }) => {
    const details = page.locator(".pmv-markdown details").first();
    const body = details.getByText("PostgreSQL 16", { exact: false });

    await expect(details).not.toHaveAttribute("open", /.*/);
    await expect(body).toBeHidden();
    await stepScreenshot("section-closed");

    await details.locator("summary").click();
    await expect(details).toHaveAttribute("open", /.*/);
    await expect(body).toBeVisible();
    await stepScreenshot("section-open");
  });

  test("a section marked open starts expanded", async ({ page }) => {
    const openSection = page.locator(".pmv-markdown details[open]").first();
    await expect(openSection.locator("summary")).toContainText("Still relevant?");
    await expect(openSection.getByText("The `open` attribute", { exact: false })).toBeVisible();
  });

  test("rich content renders inside a section body", async ({ page, stepScreenshot }) => {
    const details = page
      .locator(".pmv-markdown details")
      .filter({ has: page.locator("summary", { hasText: "Migration steps" }) });

    await details.locator("summary").click();
    await expect(details.locator("table")).toBeVisible();
    await expect(details.locator("ol li")).toHaveCount(3);
    await expect(details.locator(".pmv-code-block")).toBeVisible();
    await expect(details.locator(".pmv-alert")).toBeVisible();
    await stepScreenshot("rich-body");
  });

  test("sections nest", async ({ page }) => {
    const outer = page
      .locator(".pmv-markdown details")
      .filter({ has: page.locator("summary", { hasText: "Rejected alternatives" }) });

    await outer.locator("summary").first().click();
    await expect(outer.locator("details")).toHaveCount(2);
    await expect(outer.locator("details summary").first()).toContainText("Dual-write");
  });

  test("unsafe raw HTML is stripped from the page", async ({ page }) => {
    const widgetPane = page.locator(".pmv-markdown");

    await expect(widgetPane.locator("script")).toHaveCount(0);
    await expect(widgetPane.locator("iframe")).toHaveCount(0);
    await expect(widgetPane.locator("style")).toHaveCount(0);
    // Script and style contents go with the element rather than landing on the
    // page as prose, which is what dropping an element normally does.
    await expect(widgetPane).not.toContainText("display: none");

    const unsafe = widgetPane
      .locator("details")
      .filter({ has: page.locator("summary", { hasText: "Event handlers" }) });
    await expect(unsafe).not.toHaveAttribute("onclick", /.*/);
    await expect(unsafe).not.toHaveAttribute("style", /.*/);

    // Safe inline markup in the same block survives.
    await unsafe.locator("summary").click();
    await expect(unsafe.locator("b")).toContainText("bold");
    await expect(unsafe.locator("kbd")).toContainText("Ctrl");
    await expect(unsafe.locator('a[href="https://ivy.app"]')).toBeVisible();
  });

  test("javascript: hrefs are neutralised", async ({ page }) => {
    const section = page
      .locator(".pmv-markdown details")
      .filter({ has: page.locator("summary", { hasText: "Unsafe link protocols" }) });

    await section.locator("summary").click();
    const link = section.getByText("javascript: link");
    await expect(link).toBeVisible();
    await expect(page.locator('.pmv-markdown a[href^="javascript:"]')).toHaveCount(0);
  });
});
