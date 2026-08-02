import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";

import { PlanDiffView } from "./PlanDiffView";

// Minimal fixture verified working on react-diff-view 3.3.3
const fixture = `diff --git a/foo.ts b/foo.ts
index 0000001..1111111 100644
--- a/foo.ts
+++ b/foo.ts
@@ -1,2 +1,2 @@
-old line
+new line
 context
`;

describe("PlanDiffView", () => {
  it("uses eventHandler prop for OnAddComment (regression guard)", async () => {
    const eventHandler = vi.fn();
    render(<PlanDiffView id="pdv-1" diff={fixture} filePath="foo.ts" eventHandler={eventHandler} />);

    // Open comment form by clicking a gutter cell
    const gutter = document.querySelector(".diff-gutter") as HTMLElement;
    expect(gutter).toBeTruthy();
    fireEvent.click(gutter);

    // Type into the textarea
    const textarea = await screen.findByPlaceholderText(/Enter instruction for the agent/i);
    fireEvent.change(textarea, { target: { value: "Test comment" } });

    // Click Add Comment
    const addButton = screen.getByText("Add Comment");
    fireEvent.click(addButton);

    // Assert the spy was called with correct event shape
    expect(eventHandler).toHaveBeenCalledWith(
      "OnAddComment",
      "pdv-1",
      [expect.objectContaining({
        filePath: "foo.ts",
        content: "Test comment",
        changeKey: expect.any(String),
        lineNumber: expect.any(Number)
      })]
    );
  });

  it("uses onIvyEvent alias for OnAddComment (back-compat guard)", async () => {
    const onIvyEvent = vi.fn();
    render(<PlanDiffView id="pdv-2" diff={fixture} filePath="foo.ts" onIvyEvent={onIvyEvent} />);

    // Open comment form
    const gutter = document.querySelector(".diff-gutter") as HTMLElement;
    fireEvent.click(gutter);

    // Type and submit
    const textarea = await screen.findByPlaceholderText(/Enter instruction for the agent/i);
    fireEvent.change(textarea, { target: { value: "Legacy test" } });
    const addButton = screen.getByText("Add Comment");
    fireEvent.click(addButton);

    // Assert same call structure
    expect(onIvyEvent).toHaveBeenCalledWith(
      "OnAddComment",
      "pdv-2",
      [expect.objectContaining({
        filePath: "foo.ts",
        content: "Legacy test",
        changeKey: expect.any(String),
        lineNumber: expect.any(Number)
      })]
    );
  });

  it("does not crash when no dispatch prop is provided", async () => {
    // Render with neither eventHandler nor onIvyEvent
    render(<PlanDiffView id="pdv-3" diff={fixture} filePath="foo.ts" />);

    // Open comment form
    const gutter = document.querySelector(".diff-gutter") as HTMLElement;
    fireEvent.click(gutter);

    // Type and submit (should not throw)
    const textarea = await screen.findByPlaceholderText(/Enter instruction for the agent/i);
    fireEvent.change(textarea, { target: { value: "No handler" } });
    const addButton = screen.getByText("Add Comment");

    expect(() => {
      fireEvent.click(addButton);
    }).not.toThrow();

    // Widget should still render after the operation
    expect(document.querySelector(".ivy-diff-view")).toBeInTheDocument();
  });

  it("dispatches OnViewFile when View file is clicked (covers issue #1838)", async () => {
    const eventHandler = vi.fn();
    render(<PlanDiffView id="pdv-4" diff={fixture} filePath="foo.ts" eventHandler={eventHandler} />);

    // Click the More actions button
    const moreButton = screen.getByLabelText("More actions");
    fireEvent.click(moreButton);

    // Wait for dropdown and click View file
    await waitFor(() => {
      expect(screen.getByText("View file")).toBeInTheDocument();
    });

    const viewFileButton = screen.getByText("View file");
    fireEvent.click(viewFileButton);

    // Assert dispatch call
    expect(eventHandler).toHaveBeenCalledWith("OnViewFile", "pdv-4", ["foo.ts"]);
  });
});
