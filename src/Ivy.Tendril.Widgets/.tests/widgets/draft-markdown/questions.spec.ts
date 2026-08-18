import { test, expect } from "../../fixtures/widget-test.js";
import { navigateToApp, waitForDraftMarkdown } from "../../utils/ivy.js";

test.describe("DraftMarkdown Questions", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page, "draft-markdown/questions");
    await waitForDraftMarkdown(page);
  });

  test("renders the picker with a recommended chip", async ({ page, stepScreenshot }) => {
    const callout = page.locator(".pmv-questions").first();
    await expect(callout).toBeVisible();

    // Option rows, not raw YAML.
    await expect(callout.locator(".pmv-question-option").first()).toBeVisible();
    await expect(callout.locator(".pmv-question-title")).toContainText("retry budget");

    const chip = callout.locator(".pmv-question-option-recommended").first();
    await expect(chip).toBeVisible();
    await expect(chip).toHaveText("Recommended");

    await stepScreenshot("picker-rendered");
  });

  test("selecting an option round-trips through SignalR", async ({ page, stepScreenshot }) => {
    // Scoped to the pinned panel: the question's own title also contains these words.
    const panel = page.locator(".pmv-sticky");
    await expect(panel.getByText("Answers received (0)")).toBeVisible();

    const callout = page.locator(".pmv-questions").first();
    await callout.locator(".pmv-question-check").first().click();

    // The server echoes the QuestionAnswer record back into the pinned panel.
    await expect(panel.getByText("Answers received (1)")).toBeVisible({ timeout: 15_000 });
    await expect(panel.getByText("retry-scope", { exact: true })).toBeVisible({ timeout: 15_000 });
    await expect(panel.getByText("per-request", { exact: true })).toBeVisible({ timeout: 15_000 });

    // A second answer appends rather than replacing, and lands at the top (newest first).
    await callout.locator(".pmv-question-check").nth(1).click();
    await expect(panel.getByText("Answers received (2)")).toBeVisible({ timeout: 15_000 });
    await expect(panel.getByText("per-session", { exact: true })).toBeVisible({ timeout: 15_000 });

    await stepScreenshot("answer-received");
  });

  test("Clear reports the cleared state", async ({ page }) => {
    const panel = page.locator(".pmv-sticky");
    const callout = page.locator(".pmv-questions").first();
    await callout.locator(".pmv-question-clear").click();

    await expect(panel.getByText("Answers received (1)")).toBeVisible({ timeout: 15_000 });
    await expect(panel.getByText("cleared — back to unanswered")).toBeVisible({ timeout: 15_000 });
  });

  test("a documentation fence stays a code block", async ({ page }) => {
    // The `questions` fence nested inside a four-backtick fence must never become a picker.
    await expect(page.locator(".pmv-code-block")).toBeVisible();
    await expect(page.locator(".pmv-code-block")).toContainText("This one is an example");
  });

  test("the answered sample stays read-only", async ({ page, stepScreenshot }) => {
    await navigateToApp(page, "draft-markdown/questions-answered");
    await waitForDraftMarkdown(page);

    // No OnAnswersChange subscriber, so every block renders as the static callout — including the
    // ones that parse as the structured schema.
    const callouts = page.locator(".pmv-questions");
    await expect(callouts).toHaveCount(3);
    await expect(page.locator(".pmv-question-option")).toHaveCount(0);
    await expect(callouts.first().locator(".pmv-questions-content")).toBeVisible();

    // The legacy plain-text fence still reads as prose.
    await expect(callouts.nth(2)).toContainText("Should we support notification templates");

    await stepScreenshot("answered-read-only");
  });

  test("dragging across the picker raises no selection toolbar", async ({ page, stepScreenshot }) => {
    const callout = page.locator(".pmv-questions").first();
    const box = await callout.boundingBox();
    expect(box).not.toBeNull();

    await page.mouse.move(box!.x + 8, box!.y + box!.height / 2);
    await page.mouse.down();
    await page.mouse.move(box!.x + box!.width - 8, box!.y + box!.height / 2);
    await page.mouse.up();

    await expect(page.locator(".pmv-selection-toolbar")).toHaveCount(0);
    await stepScreenshot("no-toolbar-over-picker");
  });
});
