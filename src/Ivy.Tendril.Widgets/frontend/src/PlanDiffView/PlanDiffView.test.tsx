import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";
import { parseDiff, getChangeKey } from "react-diff-view";

import { PlanDiffView } from "./PlanDiffView";

describe("PlanDiffView", () => {
  const diff = [
    "diff --git a/a.txt b/a.txt",
    "--- a/a.txt",
    "+++ b/a.txt",
    "@@ -1 +1 @@",
    "-old",
    "+new",
    "",
  ].join("\n");

  const files = parseDiff(diff);
  const change = files[0]?.hunks?.[0]?.changes?.[1];
  const changeKey = change ? getChangeKey(change) : "new-1";

  it("renders saved comment as markdown", () => {
    render(
      <PlanDiffView
        id="pdv-1"
        onIvyEvent={vi.fn()}
        diff={diff}
        comments={[
          {
            filePath: "a.txt",
            changeKey: changeKey,
            content: "**change**",
            lineNumber: 1,
          },
        ]}
      />
    );

    const strong = document.querySelector(".diff-comment-markdown strong");
    expect(strong).toBeTruthy();
    expect(strong?.textContent).toBe("change");
    expect(screen.queryByText("**change**")).toBeNull();
  });

  it("renders markdown in Preview tab", () => {
    const onIvyEvent = vi.fn();
    render(
      <PlanDiffView
        id="pdv-1"
        onIvyEvent={onIvyEvent}
        diff={diff}
        comments={[
          {
            filePath: "a.txt",
            changeKey: changeKey,
            content: "initial",
            lineNumber: 1,
          },
        ]}
      />
    );

    const editButton = screen.getByRole("button", { name: /edit/i });
    fireEvent.click(editButton);

    const textarea = screen.getByPlaceholderText(/Enter instruction for the agent/i) as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: "**change**" } });

    const previewButton = screen.getByRole("button", { name: /preview/i });
    fireEvent.click(previewButton);

    const strong = document.querySelector(".diff-comment-markdown strong");
    expect(strong).toBeTruthy();
    expect(strong?.textContent).toBe("change");
  });

  it("keeps raw source in Write tab after Preview", () => {
    const onIvyEvent = vi.fn();
    render(
      <PlanDiffView
        id="pdv-1"
        onIvyEvent={onIvyEvent}
        diff={diff}
        comments={[
          {
            filePath: "a.txt",
            changeKey: changeKey,
            content: "initial",
            lineNumber: 1,
          },
        ]}
      />
    );

    const editButton = screen.getByRole("button", { name: /edit/i });
    fireEvent.click(editButton);

    const textarea = screen.getByPlaceholderText(/Enter instruction for the agent/i) as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: "**change**" } });

    const previewButton = screen.getByRole("button", { name: /preview/i });
    fireEvent.click(previewButton);

    const writeButton = screen.getByRole("button", { name: /write/i });
    fireEvent.click(writeButton);

    const textareaAfter = screen.getByPlaceholderText(/Enter instruction for the agent/i) as HTMLTextAreaElement;
    expect(textareaAfter.value).toBe("**change**");
  });

  it("shows placeholder when preview is empty", () => {
    const onIvyEvent = vi.fn();
    render(
      <PlanDiffView
        id="pdv-1"
        onIvyEvent={onIvyEvent}
        diff={diff}
        comments={[
          {
            filePath: "a.txt",
            changeKey: changeKey,
            content: "initial",
            lineNumber: 1,
          },
        ]}
      />
    );

    const editButton = screen.getByRole("button", { name: /edit/i });
    fireEvent.click(editButton);

    const textarea = screen.getByPlaceholderText(/Enter instruction for the agent/i) as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: "" } });

    const previewButton = screen.getByRole("button", { name: /preview/i });
    fireEvent.click(previewButton);

    expect(screen.getByText(/Nothing to preview/i)).toBeInTheDocument();
    expect(document.querySelector(".diff-comment-markdown strong")).toBeNull();
  });

  it("renders markdown lists with list items", () => {
    const onIvyEvent = vi.fn();
    render(
      <PlanDiffView
        id="pdv-1"
        onIvyEvent={onIvyEvent}
        diff={diff}
        comments={[
          {
            filePath: "a.txt",
            changeKey: changeKey,
            content: "initial",
            lineNumber: 1,
          },
        ]}
      />
    );

    const editButton = screen.getByRole("button", { name: /edit/i });
    fireEvent.click(editButton);

    const textarea = screen.getByPlaceholderText(/Enter instruction for the agent/i) as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: "- one\n- two" } });

    const previewButton = screen.getByRole("button", { name: /preview/i });
    fireEvent.click(previewButton);

    const listItems = document.querySelectorAll(".diff-comment-markdown ul li");
    expect(listItems.length).toBe(2);
  });
});

describe("PlanDiffView unified column collapse", () => {
  const insertOnlyDiff = `diff --git a/file.txt b/file.txt
index abc1234..def5678 100644
--- a/file.txt
+++ b/file.txt
@@ -1,0 +1,2 @@
+line 1
+line 2`;

  const deleteOnlyDiff = `diff --git a/file.txt b/file.txt
index abc1234..def5678 100644
--- a/file.txt
+++ b/file.txt
@@ -1,2 +1,0 @@
-line 1
-line 2`;

  const mixedDiff = `diff --git a/file.txt b/file.txt
index abc1234..def5678 100644
--- a/file.txt
+++ b/file.txt
@@ -1,3 +1,3 @@
 context
-old line
+new line`;

  const emptyDiff = `diff --git a/file.txt b/file.txt
old mode 100644
new mode 100755`;

  it("renders insert-only diff with diff-no-deletions class", () => {
    const { container } = render(<PlanDiffView id="test-1" diff={insertOnlyDiff} viewType="Unified" />);
    const table = container.querySelector(".diff");
    expect(table).toHaveClass("diff-unified-view");
    expect(table).toHaveClass("diff-no-deletions");
    expect(table).not.toHaveClass("diff-no-additions");
  });

  it("renders delete-only diff with diff-no-additions class", () => {
    const { container } = render(<PlanDiffView id="test-2" diff={deleteOnlyDiff} viewType="Unified" />);
    const table = container.querySelector(".diff");
    expect(table).toHaveClass("diff-unified-view");
    expect(table).toHaveClass("diff-no-additions");
    expect(table).not.toHaveClass("diff-no-deletions");
  });

  it("renders mixed diff with neither marker class", () => {
    const { container } = render(<PlanDiffView id="test-3" diff={mixedDiff} viewType="Unified" />);
    const table = container.querySelector(".diff");
    expect(table).toHaveClass("diff-unified-view");
    expect(table).not.toHaveClass("diff-no-deletions");
    expect(table).not.toHaveClass("diff-no-additions");
  });

  it("renders zero-change diff with neither marker class", () => {
    const { container } = render(<PlanDiffView id="test-4" diff={emptyDiff} viewType="Unified" />);
    const table = container.querySelector(".diff");
    expect(table).toHaveClass("diff-unified-view");
    expect(table).not.toHaveClass("diff-no-deletions");
    expect(table).not.toHaveClass("diff-no-additions");
  });

  it("emits a three-col colgroup and diff-line rows with three td children", () => {
    const { container } = render(<PlanDiffView id="test-5" diff={insertOnlyDiff} viewType="Unified" />);
    const colgroup = container.querySelector(".diff colgroup");
    expect(colgroup).toBeTruthy();
    const cols = colgroup?.querySelectorAll("col");
    expect(cols?.length).toBe(3);

    const diffLine = container.querySelector(".diff-line");
    expect(diffLine).toBeTruthy();
    const cells = diffLine?.querySelectorAll("td");
    expect(cells?.length).toBe(3);
  });
});

describe("PlanDiffView kebab menu", () => {
  const singleFileDiff = [
    "diff --git a/a.txt b/a.txt",
    "--- a/a.txt",
    "+++ b/a.txt",
    "@@ -1 +1 @@",
    "-old",
    "+new",
    "",
  ].join("\n");

  const twoFileDiff = [
    "diff --git a/a.txt b/a.txt",
    "--- a/a.txt",
    "+++ b/a.txt",
    "@@ -1 +1 @@",
    "-old a",
    "+new a",
    "diff --git a/b.txt b/b.txt",
    "--- a/b.txt",
    "+++ b/b.txt",
    "@@ -1 +1 @@",
    "-old b",
    "+new b",
    "",
  ].join("\n");

  it("renders the more-actions menu in a document.body portal", () => {
    render(<PlanDiffView id="pdv-1" diff={singleFileDiff} collapsible />);

    fireEvent.click(screen.getByRole("button", { name: /more actions/i }));

    const viewFileItem = screen.getByText("View file");
    expect(viewFileItem).toBeInTheDocument();
    expect(viewFileItem.closest(".ivy-diff-view")).toBeNull();
    expect(document.body.contains(viewFileItem)).toBe(true);
  });

  it("menu style is fixed positioned with a high z-index", () => {
    render(<PlanDiffView id="pdv-1" diff={singleFileDiff} collapsible />);

    fireEvent.click(screen.getByRole("button", { name: /more actions/i }));

    const menu = screen.getByText("View file").closest(".diff-more-actions-menu") as HTMLElement;
    expect(menu.style.position).toBe("fixed");
    expect(Number(menu.style.zIndex)).toBeGreaterThanOrEqual(1000);
  });

  it("menu action still dispatches for the right file", () => {
    const onIvyEvent = vi.fn();
    render(<PlanDiffView id="pdv-1" diff={twoFileDiff} onIvyEvent={onIvyEvent} collapsible />);

    const moreActionsButtons = screen.getAllByRole("button", { name: /more actions/i });
    expect(moreActionsButtons.length).toBe(2);
    fireEvent.click(moreActionsButtons[1]);

    fireEvent.click(screen.getByText("View file"));

    expect(onIvyEvent).toHaveBeenCalledWith("OnViewFile", "pdv-1", ["b.txt"]);
  });

  it("clicking inside the portaled menu does not swallow the action", () => {
    const onIvyEvent = vi.fn();
    render(<PlanDiffView id="pdv-1" diff={singleFileDiff} onIvyEvent={onIvyEvent} collapsible />);

    fireEvent.click(screen.getByRole("button", { name: /more actions/i }));
    fireEvent.click(screen.getByText("Edit file"));

    expect(onIvyEvent).toHaveBeenCalledWith("OnEditFile", "pdv-1", ["a.txt"]);
    expect(screen.queryByText("Edit file")).toBeNull();
  });
});
