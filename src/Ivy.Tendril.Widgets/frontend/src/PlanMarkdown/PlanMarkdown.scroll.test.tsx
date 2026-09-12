import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { PlanMarkdown } from "./PlanMarkdown";

// #2650: the Plans app used to swap the document for a "Loading..." placeholder on every background
// revalidation, unmounting PlanMarkdown and its .pmv-shell scroll box with it and throwing the reader
// back to the top of the plan. The server-side fix keeps PlanMarkdown mounted across a revalidation;
// this test asserts the invariant that fix relies on - a mounted PlanMarkdown does not reset its own
// scroll offset when its content/annotations props change underneath it.
describe("PlanMarkdown scroll offset across a revalidation", () => {
  it("keeps .pmv-shell scrollTop when content and annotations props change", () => {
    const { container, rerender } = render(
      <PlanMarkdown id="w1" content="# First\n\nSome content." annotations={[]} />,
    );

    const shell = container.querySelector(".pmv-shell") as HTMLDivElement;
    expect(shell).not.toBeNull();

    let scrollTop = 240;
    Object.defineProperty(shell, "scrollTop", {
      get: () => scrollTop,
      set: (v: number) => {
        scrollTop = v;
      },
      configurable: true,
    });

    rerender(
      <PlanMarkdown id="w1" content="# First\n\nSome different content." annotations={[]} />,
    );

    expect(shell.scrollTop).toBe(240);
  });
});
