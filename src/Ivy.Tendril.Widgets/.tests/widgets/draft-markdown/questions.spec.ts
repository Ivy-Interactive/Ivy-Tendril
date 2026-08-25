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

    // Option descriptions are block markdown: this one carries a snippet.
    const snippet = callout.locator(".pmv-question-option-description .pmv-code-block").first();
    await expect(snippet).toBeVisible();
    await expect(snippet).toContainText("client.SendAsync");

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

  test("a multi-select answer accumulates", async ({ page }) => {
    const callout = page.locator(".pmv-questions").nth(1);

    await callout.locator(".pmv-question-check").first().click();
    await expect(callout.locator(".pmv-question-option--selected")).toHaveCount(1, { timeout: 15_000 });

    // multiple: true, so the second option joins the first instead of replacing it.
    await callout.locator(".pmv-question-check").nth(1).click();
    await expect(callout.locator(".pmv-question-option--selected")).toHaveCount(2, { timeout: 15_000 });
  });

  test("both questions of a block are on screen at once", async ({ page, stepScreenshot }) => {
    // The third block holds two free-text questions. They stack rather than tab, so both are
    // answerable without first finding the second one.
    const callout = page.locator(".pmv-questions").nth(2);
    await expect(callout.locator(".pmv-question")).toHaveCount(2);
    await expect(callout.locator(".pmv-questions-tab")).toHaveCount(0);

    // Each header renders as the eyebrow over its question.
    await expect(callout.locator(".pmv-question-header")).toHaveText(["Name", "Owner"]);

    // The second ships with `answer: null` — asked and deliberately skipped.
    await expect(callout.locator(".pmv-question-skipped")).toHaveCount(1);

    // Two questions, one shared Clear for the block.
    await expect(callout.locator(".pmv-question-clear")).toHaveCount(1);

    await stepScreenshot("stacked-questions");

    // Answering the first leaves the second alone.
    await callout.locator(".pmv-question-other-input").first().fill("dispatch");
    await expect(page.locator(".pmv-sticky").getByText("service-name").first()).toBeVisible({
      timeout: 15_000,
    });
    await expect(callout.locator(".pmv-question-skipped")).toHaveCount(1);
  });

  test("Clear appears only once there is an answer, and reports the cleared state", async ({ page }) => {
    const panel = page.locator(".pmv-sticky");
    const callout = page.locator(".pmv-questions").first();

    // Nothing answered yet, so there is nothing to clear.
    await expect(callout.locator(".pmv-question-clear")).toHaveCount(0);

    await callout.locator(".pmv-question-check").first().click();
    await expect(callout.locator(".pmv-question-clear")).toHaveCount(1, { timeout: 15_000 });

    await callout.locator(".pmv-question-clear").click();
    await expect(panel.getByText("Answers received (2)")).toBeVisible({ timeout: 15_000 });
    await expect(panel.getByText("cleared — back to unanswered")).toBeVisible({ timeout: 15_000 });

    // Cleared back to unanswered, so the button retires again.
    await expect(callout.locator(".pmv-question-clear")).toHaveCount(0, { timeout: 15_000 });
  });

  test("a documentation fence stays a code block", async ({ page }) => {
    // The `questions` fence nested inside a longer fence must never become a picker. Filtered
    // rather than bare: option descriptions carry code blocks of their own now.
    const documentation = page
      .locator(".pmv-code-block")
      .filter({ hasText: "This one is an example" });

    await expect(documentation).toBeVisible();
    await expect(documentation.locator(".pmv-question-option")).toHaveCount(0);
  });

  test("a seeded annotation never highlights inside a question block", async ({ page }) => {
    // The sample seeds three: one on prose, one aimed at the question block, one after it. Only
    // the two outside the block may highlight.
    const highlights = page.locator(".pmv-annotation-highlight");
    await expect(highlights).toHaveCount(2);

    await expect(page.locator(".pmv-questions .pmv-annotation-highlight")).toHaveCount(0);

    // The third proves a block's text still advances the offset counter — if it did not, this
    // annotation would have drifted off "separate consumer".
    await expect(highlights.nth(0)).toHaveText("Where the budget lives");
    await expect(highlights.nth(1)).toHaveText("separate consumer");
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
