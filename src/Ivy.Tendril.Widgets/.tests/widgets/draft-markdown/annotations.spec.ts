import { test, expect } from "../../fixtures/widget-test.js";
import { navigateToApp, waitForDraftMarkdown } from "../../utils/ivy.js";

test.describe("DraftMarkdown Annotations", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page, "draft-markdown/annotations");
    await waitForDraftMarkdown(page);
  });

  test("renders markdown content", async ({ page, stepScreenshot }) => {
    const markdown = page.locator(".pmv-markdown");
    await expect(markdown).toBeVisible();
    await expect(markdown.locator("h1")).toContainText("Feature Specification");
    await expect(markdown.locator("h2").first()).toContainText("Overview");
    await stepScreenshot("initial-render");
  });

  test("shows instruction text when no annotations", async ({ page, stepScreenshot }) => {
    await expect(page.getByText("Select text in the markdown to add annotations")).toBeVisible();
    await stepScreenshot("instruction-text-visible");
  });

  test("text selection shows toolbar", async ({ page, stepScreenshot }) => {
    await stepScreenshot("before-selection");

    // Find a text node to select — target the "Overview" heading text
    const heading = page.locator(".pmv-markdown h2").first();
    const box = await heading.boundingBox();
    expect(box).not.toBeNull();

    // Click-drag to select text
    await page.mouse.move(box!.x + 5, box!.y + box!.height / 2);
    await page.mouse.down();
    await page.mouse.move(box!.x + box!.width - 5, box!.y + box!.height / 2);
    await page.mouse.up();

    // Toolbar should appear
    const toolbar = page.locator(".pmv-selection-toolbar");
    await expect(toolbar).toBeVisible({ timeout: 5000 });
    await stepScreenshot("selection-toolbar-visible");
  });

  test("add annotation flow", async ({ page, stepScreenshot }) => {
    // Select text in the first paragraph
    const paragraph = page.locator(".pmv-markdown p").first();
    const box = await paragraph.boundingBox();
    expect(box).not.toBeNull();

    await page.mouse.move(box!.x + 5, box!.y + box!.height / 2);
    await page.mouse.down();
    await page.mouse.move(box!.x + 200, box!.y + box!.height / 2);
    await page.mouse.up();

    // Wait for toolbar
    const toolbar = page.locator(".pmv-selection-toolbar");
    await expect(toolbar).toBeVisible({ timeout: 5000 });
    await stepScreenshot("text-selected");

    // Click "Add Comment"
    await toolbar.locator("button").click();
    await stepScreenshot("add-comment-popover");

    // Verify popover appears with textarea
    const popover = page.locator(".pmv-popover");
    await expect(popover).toBeVisible();
    const textarea = popover.locator("textarea");
    await expect(textarea).toBeVisible();

    // Type a comment and submit
    await textarea.fill("This needs clarification");
    await stepScreenshot("comment-typed");

    await popover.locator("button", { hasText: "Add" }).click();

    // Wait for server round-trip — "Annotations (1)" confirms state updated
    await expect(page.getByText("Annotations (1)")).toBeVisible({ timeout: 15000 });

    // Verify highlight appears after state round-trip
    const highlight = page.locator("mark[data-annotation-id]");
    await expect(highlight.first()).toBeVisible({ timeout: 5000 });
    await stepScreenshot("annotation-highlight-applied");
  });

  test("annotation state updates after adding", async ({ page, stepScreenshot }) => {
    // Add an annotation
    const paragraph = page.locator(".pmv-markdown p").first();
    const box = await paragraph.boundingBox();
    expect(box).not.toBeNull();

    await page.mouse.move(box!.x + 5, box!.y + box!.height / 2);
    await page.mouse.down();
    await page.mouse.move(box!.x + 150, box!.y + box!.height / 2);
    await page.mouse.up();

    const toolbar = page.locator(".pmv-selection-toolbar");
    await expect(toolbar).toBeVisible({ timeout: 5000 });
    await toolbar.locator("button").click();

    const popover = page.locator(".pmv-popover");
    await popover.locator("textarea").fill("Test annotation");
    await popover.locator("button", { hasText: "Add" }).click();

    // Wait for state to update — side panel shows "Annotations (1)"
    await expect(page.getByText("Annotations (1)")).toBeVisible({ timeout: 15000 });
    await stepScreenshot("state-shows-annotation-count");

    // Side panel should show the annotation comment and selected text
    await expect(page.getByText("Test annotation")).toBeVisible();
    await stepScreenshot("side-panel-shows-annotation");
  });

  test("click highlight opens edit popover", async ({ page, stepScreenshot }) => {
    // First add an annotation
    const paragraph = page.locator(".pmv-markdown p").first();
    const box = await paragraph.boundingBox();
    expect(box).not.toBeNull();

    await page.mouse.move(box!.x + 5, box!.y + box!.height / 2);
    await page.mouse.down();
    await page.mouse.move(box!.x + 150, box!.y + box!.height / 2);
    await page.mouse.up();

    const toolbar = page.locator(".pmv-selection-toolbar");
    await expect(toolbar).toBeVisible({ timeout: 5000 });
    await toolbar.locator("button").click();

    const addPopover = page.locator(".pmv-popover");
    await addPopover.locator("textarea").fill("Edit me later");
    await addPopover.locator("button", { hasText: "Add" }).click();

    // Wait for server round-trip to complete
    await expect(page.getByText("Annotations (1)")).toBeVisible({ timeout: 15000 });

    // Wait for highlight to appear
    const highlight = page.locator("mark[data-annotation-id]");
    await expect(highlight.first()).toBeVisible({ timeout: 5000 });
    await stepScreenshot("highlight-present");

    // Click on the highlight
    await highlight.first().click();

    // Edit popover should appear
    const editPopover = page.locator(".pmv-popover");
    await expect(editPopover).toBeVisible();
    const textarea = editPopover.locator("textarea");
    await expect(textarea).toHaveValue("Edit me later");
    await stepScreenshot("edit-popover-opened");
  });

  test("edit annotation", async ({ page, stepScreenshot }) => {
    // Add an annotation
    const paragraph = page.locator(".pmv-markdown p").first();
    const box = await paragraph.boundingBox();
    expect(box).not.toBeNull();

    await page.mouse.move(box!.x + 5, box!.y + box!.height / 2);
    await page.mouse.down();
    await page.mouse.move(box!.x + 150, box!.y + box!.height / 2);
    await page.mouse.up();

    const toolbar = page.locator(".pmv-selection-toolbar");
    await expect(toolbar).toBeVisible({ timeout: 5000 });
    await toolbar.locator("button").click();

    const addPopover = page.locator(".pmv-popover");
    await addPopover.locator("textarea").fill("Original comment");
    await addPopover.locator("button", { hasText: "Add" }).click();

    // Wait for server round-trip
    await expect(page.getByText("Annotations (1)")).toBeVisible({ timeout: 15000 });

    // Click highlight to open edit popover
    const highlight = page.locator("mark[data-annotation-id]");
    await expect(highlight.first()).toBeVisible({ timeout: 5000 });
    await highlight.first().click();

    // Edit the comment
    const editPopover = page.locator(".pmv-popover");
    const textarea = editPopover.locator("textarea");
    await textarea.clear();
    await textarea.fill("Updated comment");
    await stepScreenshot("comment-edited");

    await editPopover.locator("button", { hasText: "Save" }).click();

    // Verify state updated via server round-trip — side panel shows updated comment
    await expect(page.getByText("Updated comment")).toBeVisible({ timeout: 15000 });
    await stepScreenshot("edit-saved");
  });

  test("remove annotation", async ({ page, stepScreenshot }) => {
    // Add an annotation
    const paragraph = page.locator(".pmv-markdown p").first();
    const box = await paragraph.boundingBox();
    expect(box).not.toBeNull();

    await page.mouse.move(box!.x + 5, box!.y + box!.height / 2);
    await page.mouse.down();
    await page.mouse.move(box!.x + 150, box!.y + box!.height / 2);
    await page.mouse.up();

    const toolbar = page.locator(".pmv-selection-toolbar");
    await expect(toolbar).toBeVisible({ timeout: 5000 });
    await toolbar.locator("button").click();

    const addPopover = page.locator(".pmv-popover");
    await addPopover.locator("textarea").fill("To be removed");
    await addPopover.locator("button", { hasText: "Add" }).click();

    // Wait for server round-trip
    await expect(page.getByText("Annotations (1)")).toBeVisible({ timeout: 15000 });

    // Verify highlight exists
    const highlight = page.locator("mark[data-annotation-id]");
    await expect(highlight.first()).toBeVisible({ timeout: 5000 });
    await stepScreenshot("before-remove");

    // Click highlight, then remove
    await highlight.first().click();
    const editPopover = page.locator(".pmv-popover");
    await editPopover.locator("button", { hasText: "Remove" }).click();

    // Verify highlight gone and state back to empty
    await expect(highlight).toHaveCount(0, { timeout: 15000 });
    await expect(page.getByText("Select text in the markdown to add annotations")).toBeVisible({ timeout: 5000 });
    await stepScreenshot("annotation-removed");
  });

  test("multiple annotations coexist", async ({ page, stepScreenshot }) => {
    // Add first annotation on h2
    const h2 = page.locator(".pmv-markdown h2").first();
    const h2Box = await h2.boundingBox();
    expect(h2Box).not.toBeNull();

    await page.mouse.move(h2Box!.x + 5, h2Box!.y + h2Box!.height / 2);
    await page.mouse.down();
    await page.mouse.move(h2Box!.x + 80, h2Box!.y + h2Box!.height / 2);
    await page.mouse.up();

    let toolbar = page.locator(".pmv-selection-toolbar");
    await expect(toolbar).toBeVisible({ timeout: 5000 });
    await toolbar.locator("button").click();

    let popover = page.locator(".pmv-popover");
    await popover.locator("textarea").fill("First annotation");
    await popover.locator("button", { hasText: "Add" }).click();

    await expect(page.getByText("Annotations (1)")).toBeVisible({ timeout: 15000 });
    await stepScreenshot("first-annotation-added");

    // Add second annotation on a paragraph
    const para = page.locator(".pmv-markdown p").nth(1);
    const paraBox = await para.boundingBox();
    expect(paraBox).not.toBeNull();

    await page.mouse.move(paraBox!.x + 5, paraBox!.y + paraBox!.height / 2);
    await page.mouse.down();
    await page.mouse.move(paraBox!.x + 200, paraBox!.y + paraBox!.height / 2);
    await page.mouse.up();

    toolbar = page.locator(".pmv-selection-toolbar");
    await expect(toolbar).toBeVisible({ timeout: 5000 });
    await toolbar.locator("button").click();

    popover = page.locator(".pmv-popover");
    await popover.locator("textarea").fill("Second annotation");
    await popover.locator("button", { hasText: "Add" }).click();

    // Both annotations exist
    await expect(page.getByText("Annotations (2)")).toBeVisible({ timeout: 15000 });
    const highlights = page.locator("mark[data-annotation-id]");
    await expect(highlights).toHaveCount(2);
    await stepScreenshot("both-annotations-visible");
  });
});
