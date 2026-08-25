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
    // Scoped to the pinned panel: the question's own title also contains these words. Within the
    // panel the event stream renders above the block source, and a highlighted YAML token can carry
    // the same text, so first() pins each assertion to the stream entry.
    const panel = page.locator(".pmv-sticky");
    await expect(panel.getByText("Answers received (0)")).toBeVisible();

    const callout = page.locator(".pmv-questions").first();
    await callout.locator(".pmv-question-check").first().click();

    // The server echoes the QuestionAnswer record back into the pinned panel.
    await expect(panel.getByText("Answers received (1)")).toBeVisible({ timeout: 15_000 });
    await expect(panel.getByText("retry-scope", { exact: true }).first()).toBeVisible({ timeout: 15_000 });
    await expect(panel.getByText("per-request", { exact: true }).first()).toBeVisible({ timeout: 15_000 });

    // A second answer appends rather than replacing, and lands at the top (newest first).
    await callout.locator(".pmv-question-check").nth(1).click();
    await expect(panel.getByText("Answers received (2)")).toBeVisible({ timeout: 15_000 });
    await expect(panel.getByText("per-session", { exact: true }).first()).toBeVisible({ timeout: 15_000 });

    await stepScreenshot("answer-received");
  });

  test("an answer is merged back into the document", async ({ page, stepScreenshot }) => {
    // This sample persists: it feeds every event through QuestionAnswers.Apply and hands the widget
    // the updated markdown, so a selection has to survive the round trip and come back rendered.
    const callout = page.locator(".pmv-questions").first();
    await expect(callout.locator(".pmv-question-option--selected")).toHaveCount(0);

    await callout.locator(".pmv-question-check").first().click();
    await expect(callout.locator(".pmv-question-option--selected")).toHaveCount(1, { timeout: 15_000 });

    // Single-select, so the second choice replaces the first rather than joining it.
    await callout.locator(".pmv-question-check").nth(1).click();
    await expect(callout.locator(".pmv-question-option--selected")).toHaveCount(1, { timeout: 15_000 });
    await expect(callout.locator(".pmv-question-option").nth(1)).toHaveClass(/pmv-question-option--selected/);

    await stepScreenshot("answer-merged");

    // Clear takes the answer key back out, so nothing is selected again.
    await callout.locator(".pmv-question-clear").click();
    await expect(callout.locator(".pmv-question-option--selected")).toHaveCount(0, { timeout: 15_000 });
  });

  test("a multi-select answer accumulates and badges its tab", async ({ page }) => {
    const callout = page.locator(".pmv-questions").nth(1);

    await callout.locator(".pmv-question-check").first().click();
    await expect(callout.locator(".pmv-question-option--selected")).toHaveCount(1, { timeout: 15_000 });

    // multiple: true, so the second option joins the first instead of replacing it.
    await callout.locator(".pmv-question-check").nth(1).click();
    await expect(callout.locator(".pmv-question-option--selected")).toHaveCount(2, { timeout: 15_000 });
  });

  test("an answered question badges its tab", async ({ page }) => {
    // The third block holds two free-text questions, so it renders a tab strip.
    const callout = page.locator(".pmv-questions").nth(2);
    await expect(callout.locator(".pmv-questions-tab")).toHaveCount(2);

    // The second question ships with `answer: null`, so only it starts badged.
    await expect(callout.locator(".pmv-questions-tab-badge")).toHaveCount(1);

    await callout.locator(".pmv-question-other-input").fill("dispatch");
    await expect(callout.locator(".pmv-questions-tab-badge")).toHaveCount(2, { timeout: 15_000 });
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
