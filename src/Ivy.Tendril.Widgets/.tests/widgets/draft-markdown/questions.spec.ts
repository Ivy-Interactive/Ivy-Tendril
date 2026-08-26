import { test, expect } from "../../fixtures/widget-test.js";
import type { Locator, Page } from "@playwright/test";
import { navigateToApp, waitForDraftMarkdown } from "../../utils/ivy.js";

/**
 * The block holding a given question, addressed by that question rather than by position — the
 * sample grows blocks, and an index would silently point at the wrong one when it does.
 */
const blockFor = (page: Page, questionId: string) =>
  page.locator(`.pmv-questions:has([data-question-id="${questionId}"])`);

/** Whether the whole block sits inside the widget's own scroll viewport. */
async function inFrame(page: Page, block: Locator): Promise<boolean> {
  const shell = page.locator(".pmv-shell");
  const [shellBox, blockBox] = await Promise.all([shell.boundingBox(), block.boundingBox()]);
  if (!shellBox || !blockBox) return false;
  return blockBox.y >= shellBox.y && blockBox.y + blockBox.height <= shellBox.y + shellBox.height;
}

test.describe("DraftMarkdown Questions", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page, "draft-markdown/questions");
    await waitForDraftMarkdown(page);
  });

  test("renders the picker with a recommended chip", async ({ page, stepScreenshot }) => {
    const callout = blockFor(page, "retry-scope");
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

  test("answering a question strikes it out of the index card", async ({ page, stepScreenshot }) => {
    // The card is built host-side from QuestionAnswers.Read, so it is the round trip made visible:
    // the answer reaches the server, is merged into the document, and comes back as an index entry
    // that is done with.
    const card = page.locator(".pmv-sticky");
    const entry = card.getByRole("button", { name: /retry budget/ });

    await expect(card.getByText("3 of 8 settled")).toBeVisible();
    await expect(entry).not.toHaveCSS("text-decoration-line", "line-through");

    await blockFor(page, "retry-scope").locator(".pmv-question-check").first().click();

    await expect(card.getByText("4 of 8 settled")).toBeVisible({ timeout: 15_000 });
    await expect(entry).toHaveCSS("text-decoration-line", "line-through", { timeout: 15_000 });

    await stepScreenshot("answered-struck-out");
  });

  test("an index entry scrolls its block into frame, and again on a repeat click", async ({
    page,
    stepScreenshot,
  }) => {
    const card = page.locator(".pmv-sticky");
    const block = blockFor(page, "service-name");

    // The last block is well below the fold on load.
    await expect(await inFrame(page, block)).toBe(false);

    await card.getByRole("button", { name: "What should the service be called?" }).click();
    await expect.poll(() => inFrame(page, block), { timeout: 15_000 }).toBe(true);

    await stepScreenshot("scrolled-to-question");

    // Scroll away and click the same entry again: the request carries a token precisely so that
    // an unchanged question id still moves the page.
    await page.locator(".pmv-shell").evaluate((shell) => shell.scrollTo({ top: 0 }));
    await expect.poll(() => inFrame(page, block)).toBe(false);

    await card.getByRole("button", { name: "What should the service be called?" }).click();
    await expect.poll(() => inFrame(page, block), { timeout: 15_000 }).toBe(true);
  });

  test("an answer is merged back into the document", async ({ page, stepScreenshot }) => {
    // This sample persists: it feeds every event through QuestionAnswers.Apply and hands the widget
    // the updated markdown, so a selection has to survive the round trip and come back rendered.
    const callout = blockFor(page, "retry-scope");
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
    const callout = blockFor(page, "launch-channels");

    await callout.locator(".pmv-question-check").first().click();
    await expect(callout.locator(".pmv-question-option--selected")).toHaveCount(1, { timeout: 15_000 });

    // multiple: true, so the second option joins the first instead of replacing it.
    await callout.locator(".pmv-question-check").nth(1).click();
    await expect(callout.locator(".pmv-question-option--selected")).toHaveCount(2, { timeout: 15_000 });
  });

  test("both questions of a block are on screen at once", async ({ page, stepScreenshot }) => {
    // The third block holds two free-text questions. They stack rather than tab, so both are
    // answerable without first finding the second one.
    const callout = blockFor(page, "service-name");
    await expect(callout.locator(".pmv-question")).toHaveCount(2);
    await expect(callout.locator(".pmv-questions-tab")).toHaveCount(0);

    // Each header renders as the eyebrow over its question.
    await expect(callout.locator(".pmv-question-header")).toHaveText(["Name", "Owner"]);

    // The second is `optional: true` — the plan is complete without it.
    await expect(callout.locator(".pmv-question-optional")).toHaveCount(1);

    // Two questions, one shared Clear for the block.
    await expect(callout.locator(".pmv-question-clear")).toHaveCount(1);

    await stepScreenshot("stacked-questions");

    // Answering the first leaves the second alone.
    await callout.locator(".pmv-question-other-input").first().fill("dispatch");
    await expect(page.locator(".pmv-sticky").getByText("4 of 8 settled")).toBeVisible({
      timeout: 15_000,
    });
    await expect(callout.locator(".pmv-question-optional")).toHaveCount(1);
  });

  test("Clear appears only once there is an answer, and retires with it", async ({ page }) => {
    const card = page.locator(".pmv-sticky");
    const callout = blockFor(page, "retry-scope");

    // Nothing answered yet, so there is nothing to clear.
    await expect(callout.locator(".pmv-question-clear")).toHaveCount(0);

    await callout.locator(".pmv-question-check").first().click();
    await expect(callout.locator(".pmv-question-clear")).toHaveCount(1, { timeout: 15_000 });
    await expect(card.getByText("4 of 8 settled")).toBeVisible({ timeout: 15_000 });

    await callout.locator(".pmv-question-clear").click();

    // Back to unanswered on both sides: the button retires and the index entry un-strikes.
    await expect(callout.locator(".pmv-question-clear")).toHaveCount(0, { timeout: 15_000 });
    await expect(card.getByText("3 of 8 settled")).toBeVisible({ timeout: 15_000 });
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

  test("the review sample presents answers instead of controls", async ({ page, stepScreenshot }) => {
    // A separate app with no OnAnswersChange handler — the Review stage, and anywhere else showing
    // a plan the reader is no longer editing.
    await navigateToApp(page, "draft-markdown/questions-review");
    await waitForDraftMarkdown(page);

    await expect(page.locator(".pmv-question-check")).toHaveCount(0);
    await expect(page.locator(".pmv-question-other-input")).toHaveCount(0);
    await expect(page.locator(".pmv-question-clear")).toHaveCount(0);

    // The chosen option's title, not the slug the YAML carries.
    const scope = blockFor(page, "delivery-scope");
    await expect(scope.locator(".pmv-question-answer-value").first()).toHaveText("Dispatch only");
    await expect(page.locator(".pmv-markdown")).not.toContainText("questions:");

    // A multi-select answer lists every value.
    await expect(
      blockFor(page, "rollout-regions").locator(".pmv-question-answer-value"),
    ).toHaveText(["EU", "US"]);

    // Unanswered questions say which kind they are — both are decisions nobody explicitly made.
    const naming = blockFor(page, "service-name");
    await expect(naming.locator(".pmv-question-answer--none")).toHaveText([
      "Not answered — not required",
      "Not answered — agent decided",
    ]);

    await stepScreenshot("review-read-only");
  });

  test("dragging across the picker raises no selection toolbar", async ({ page, stepScreenshot }) => {
    const callout = blockFor(page, "retry-scope");
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
