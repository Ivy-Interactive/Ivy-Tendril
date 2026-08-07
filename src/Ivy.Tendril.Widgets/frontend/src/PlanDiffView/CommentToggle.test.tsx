import { describe, it, expect, vi } from "vitest";
import { render, fireEvent, within } from "@testing-library/react";
import { PlanDiffView } from "./PlanDiffView";

const DIFF_FIXTURE = `diff --git a/index.html b/index.html
index 0000001..1111111 100644
--- a/index.html
+++ b/index.html
@@ -1,2 +1,2 @@
-  <main id="app">
+  <div id="login">
 </body>`;

const COMMENT_FIXTURE = {
  filePath: "index.html",
  changeKey: "I1",
  content: "please fix",
  lineNumber: 1,
};

describe("PlanDiffView comment toggle", () => {
  it("is disabled with no comments", () => {
    const { getByLabelText } = render(
      <PlanDiffView
        id="test-widget"
        diff={DIFF_FIXTURE}
        filePath="index.html"
        comments={[]}
      />
    );

    const button = getByLabelText("Hide comments") as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(button.title).toBe("No comments on this file");
  });

  it("shows the count", () => {
    const { getByLabelText } = render(
      <PlanDiffView
        id="test-widget"
        diff={DIFF_FIXTURE}
        filePath="index.html"
        comments={[COMMENT_FIXTURE]}
      />
    );

    const button = getByLabelText("Hide comments") as HTMLButtonElement;
    expect(button.disabled).toBe(false);
    expect(button.getAttribute("aria-pressed")).toBe("true");
    // Check the button contains a count span
    const countSpan = button.querySelector("span.font-mono.text-\\[10px\\]");
    expect(countSpan).toBeTruthy();
    expect(countSpan?.textContent).toBe("1");
  });

  it("reserves the comment count column with no comments", () => {
    const empty = render(
      <PlanDiffView
        id="test-widget-empty"
        diff={DIFF_FIXTURE}
        filePath="index.html"
        comments={[]}
      />
    );
    const withComment = render(
      <PlanDiffView
        id="test-widget-with-comment"
        diff={DIFF_FIXTURE}
        filePath="index.html"
        comments={[COMMENT_FIXTURE]}
      />
    );

    const emptyButton = within(empty.container).getByLabelText("Hide comments") as HTMLButtonElement;
    const withCommentButton = within(withComment.container).getByLabelText("Hide comments") as HTMLButtonElement;

    expect(emptyButton.children.length).toBe(withCommentButton.children.length);

    const emptyCountSpan = emptyButton.querySelector("span.font-mono.text-\\[10px\\]");
    const withCommentCountSpan = withCommentButton.querySelector("span.font-mono.text-\\[10px\\]");
    expect(emptyCountSpan).toBeTruthy();
    expect(withCommentCountSpan).toBeTruthy();
    expect(emptyCountSpan?.textContent).toBe("");
    expect(withCommentCountSpan?.textContent).toBe("1");

    // Stat wrapper reserves width for the +N / -N spans
    const statWrapper = withComment.container.querySelector(".min-w-\\[4\\.5rem\\]");
    expect(statWrapper).toBeTruthy();
    expect(statWrapper?.textContent).toContain("+");
  });

  it("toggles visibility both ways", () => {
    const { getByLabelText, getByText, queryByText } = render(
      <PlanDiffView
        id="test-widget"
        diff={DIFF_FIXTURE}
        filePath="index.html"
        comments={[COMMENT_FIXTURE]}
      />
    );

    // Initially visible
    expect(getByText("please fix")).toBeTruthy();

    // Click to hide
    const hideButton = getByLabelText("Hide comments");
    fireEvent.click(hideButton);

    // Comment should be hidden
    expect(queryByText("please fix")).toBeNull();

    // Click to show
    const showButton = getByLabelText("Show comments");
    fireEvent.click(showButton);

    // Comment should be visible again
    expect(getByText("please fix")).toBeTruthy();
  });

  it("does not collapse the file and does not dispatch", () => {
    const eventHandler = vi.fn();
    const { getByLabelText, container } = render(
      <PlanDiffView
        id="test-widget"
        diff={DIFF_FIXTURE}
        filePath="index.html"
        comments={[COMMENT_FIXTURE]}
        eventHandler={eventHandler}
      />
    );

    // Click the toggle
    const button = getByLabelText("Hide comments");
    fireEvent.click(button);

    // Diff rows should still be present (file not collapsed)
    const diffCells = container.querySelectorAll("td.diff-code");
    expect(diffCells.length).toBeGreaterThan(0);

    // No events should be dispatched
    expect(eventHandler).not.toHaveBeenCalled();
  });

  it("an open compose form survives the toggle", () => {
    const { getByLabelText, container } = render(
      <PlanDiffView
        id="test-widget"
        diff={DIFF_FIXTURE}
        filePath="index.html"
        comments={[COMMENT_FIXTURE]}
      />
    );

    // Click a gutter cell to open the form
    const gutterCell = container.querySelector("td.diff-gutter");
    if (gutterCell) {
      fireEvent.click(gutterCell);
    }

    // Wait for the form to appear
    const textarea = container.querySelector("textarea");
    expect(textarea).toBeTruthy();

    // Type some text
    if (textarea) {
      fireEvent.change(textarea, { target: { value: "test comment" } });
    }

    // Click the toggle
    const button = getByLabelText("Hide comments");
    fireEvent.click(button);

    // The textarea should still be present with its text
    const textareaAfter = container.querySelector("textarea") as HTMLTextAreaElement;
    expect(textareaAfter).toBeTruthy();
    expect(textareaAfter?.value).toBe("test comment");
  });
});
