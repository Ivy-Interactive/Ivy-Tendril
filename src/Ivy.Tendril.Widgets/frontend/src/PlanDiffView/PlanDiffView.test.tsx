import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";
import { parseDiff, getChangeKey } from "react-diff-view";

import PlanDiffView from "./PlanDiffView";

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
