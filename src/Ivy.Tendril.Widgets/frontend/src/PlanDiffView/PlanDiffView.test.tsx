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

  it("allows editing and updating a comment", () => {
    const onIvyEvent = vi.fn();
    render(
      <PlanDiffView
        id="pdv-1"
        onIvyEvent={onIvyEvent}
        diff={diff}
        filePath="a.txt"
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
    fireEvent.change(textarea, { target: { value: "updated text" } });

    const updateButton = screen.getByRole("button", { name: /update comment/i });
    fireEvent.click(updateButton);

    expect(onIvyEvent).toHaveBeenCalledWith("OnUpdateComment", "pdv-1", [
      {
        filePath: "a.txt",
        changeKey: changeKey,
        content: "updated text",
        lineNumber: 1,
      },
    ]);
  });

  it("renders markdown lists with list items in saved comments", () => {
    render(
      <PlanDiffView
        id="pdv-1"
        onIvyEvent={vi.fn()}
        diff={diff}
        comments={[
          {
            filePath: "a.txt",
            changeKey: changeKey,
            content: "- one\n- two",
            lineNumber: 1,
          },
        ]}
      />
    );

    const listItems = document.querySelectorAll(".diff-comment-markdown ul li");
    expect(listItems.length).toBe(2);
  });

  it("does not render any off-scale text size classes", () => {
    render(
      <PlanDiffView
        id="pdv-1"
        onIvyEvent={vi.fn()}
        diff={diff}
        comments={[
          {
            filePath: "a.txt",
            changeKey: changeKey,
            content: "a comment",
            lineNumber: 1,
          },
        ]}
      />
    );

    expect(document.querySelectorAll('[class*="text-[10px]"], [class*="text-[11px]"]').length).toBe(0);
  });

  it("renders comment author name and initials avatar", () => {
    render(
      <PlanDiffView
        id="pdv-author"
        diff={diff}
        comments={[
          {
            filePath: "a.txt",
            changeKey: changeKey,
            content: "Check this logic",
            lineNumber: 1,
            author: "Calm Niels",
          },
        ]}
      />
    );

    expect(screen.getByText("Calm Niels")).toBeInTheDocument();
    const avatar = document.querySelector(".pmv-comment-avatar");
    expect(avatar).not.toBeNull();
    expect(avatar?.textContent).toBe("CN");
    expect(avatar?.getAttribute("title")).toBe("Calm Niels");
  });

  it("passes currentAuthor when adding a new comment", () => {
    const onIvyEvent = vi.fn();
    render(
      <PlanDiffView
        id="pdv-add-author"
        onIvyEvent={onIvyEvent}
        diff={diff}
        filePath="a.txt"
        currentAuthor="Calm Niels"
      />
    );

    // Click on gutter line to open comment form
    const gutter = document.querySelector("td.diff-gutter-insert")!;
    expect(gutter).toBeTruthy();
    fireEvent.click(gutter);

    const textarea = screen.getByPlaceholderText(/Enter instruction for the agent/i);
    fireEvent.change(textarea, { target: { value: "New comment by reviewer" } });

    const addButton = screen.getByRole("button", { name: /add comment/i });
    fireEvent.click(addButton);

    expect(onIvyEvent).toHaveBeenCalledWith("OnAddComment", "pdv-add-author", [
      {
        filePath: "a.txt",
        changeKey: changeKey,
        content: "New comment by reviewer",
        lineNumber: 1,
        author: "Calm Niels",
      },
    ]);
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

describe("PlanDiffView collapse scoping", () => {
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

  it("keys collapsed state by file path and resets when diff/filePath changes", () => {
    const { rerender } = render(
      <PlanDiffView id="pdv-1" diff={twoFileDiff} collapsible />
    );

    const checkboxes = screen.getAllByRole("checkbox");
    expect(checkboxes.length).toBe(2);
    expect(checkboxes[0]).toHaveAttribute("aria-checked", "false");
    expect(checkboxes[1]).toHaveAttribute("aria-checked", "false");

    // Click Viewed on first file (a.txt)
    fireEvent.click(checkboxes[0]);
    expect(checkboxes[0]).toHaveAttribute("aria-checked", "true");
    expect(checkboxes[1]).toHaveAttribute("aria-checked", "false");

    // Re-render with a new diff/file at position 0 (e.g. c.txt)
    const newDiff = [
      "diff --git a/c.txt b/c.txt",
      "--- a/c.txt",
      "+++ b/c.txt",
      "@@ -1 +1 @@",
      "-old c",
      "+new c",
      "",
    ].join("\n");

    rerender(<PlanDiffView id="pdv-1" diff={newDiff} collapsible />);

    const newCheckboxes = screen.getAllByRole("checkbox");
    expect(newCheckboxes.length).toBe(1);
    // New file at position 0 must NOT be checked/collapsed
    expect(newCheckboxes[0]).toHaveAttribute("aria-checked", "false");
  });

  it("directly couples collapsed state to viewed state", () => {
    render(<PlanDiffView id="pdv-2" diff={twoFileDiff} collapsible />);

    const checkboxes = screen.getAllByRole("checkbox");
    expect(checkboxes.length).toBe(2);
    expect(checkboxes[0]).toHaveAttribute("aria-checked", "false");

    // Initially expanded - diff content is in document
    expect(screen.getByText("old a")).toBeInTheDocument();

    // Clicking Viewed marks it as viewed and collapses the file
    fireEvent.click(checkboxes[0]);
    expect(checkboxes[0]).toHaveAttribute("aria-checked", "true");
    expect(screen.queryByText("old a")).not.toBeInTheDocument();

    // Clicking Viewed again expands it
    fireEvent.click(checkboxes[0]);
    expect(checkboxes[0]).toHaveAttribute("aria-checked", "false");
    expect(screen.getByText("old a")).toBeInTheDocument();
  });

  it("renders header and viewed checkbox when filePath or collapsible is provided", () => {
    const singleFileDiff = [
      "diff --git a/src/test.txt b/src/test.txt",
      "--- a/src/test.txt",
      "+++ b/src/test.txt",
      "@@ -1 +1 @@",
      "-old raw",
      "+new raw",
      "",
    ].join("\n");

    render(
      <PlanDiffView
        id="pdv-3"
        diff={singleFileDiff}
        filePath="src/test.txt"
        collapsible
      />
    );

    expect(screen.getByText("src/")).toBeInTheDocument();
    expect(screen.getByText("test.txt")).toBeInTheDocument();
    const checkbox = screen.getByRole("checkbox");
    expect(checkbox).toBeInTheDocument();
    expect(checkbox).toHaveAttribute("aria-checked", "false");
  });
});
