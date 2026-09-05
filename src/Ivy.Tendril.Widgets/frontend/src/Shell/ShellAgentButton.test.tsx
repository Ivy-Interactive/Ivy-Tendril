import { describe, it, expect, vi } from "vitest";
import { render, fireEvent } from "@testing-library/react";
import { ShellAgentButton } from "./ShellAgentButton";

describe("ShellAgentButton", () => {
  it("fires OnNewChat on Ctrl+Alt+A", () => {
    const handler = vi.fn();
    render(
      <ShellAgentButton
        id="agent"
        events={["OnNewChat"]}
        eventHandler={handler}
        label="Claude Code"
      />
    );

    fireEvent.keyDown(document.body, {
      key: "a",
      code: "KeyA",
      ctrlKey: true,
      altKey: true,
    });

    expect(handler).toHaveBeenCalledWith("OnNewChat", "agent", []);
  });

  it("does NOT fire OnNewChat on plain Ctrl+A (regression guard for #2338)", () => {
    const handler = vi.fn();
    render(
      <ShellAgentButton
        id="agent"
        events={["OnNewChat"]}
        eventHandler={handler}
        label="Claude Code"
      />
    );

    const notPrevented = fireEvent.keyDown(document.body, {
      key: "a",
      code: "KeyA",
      ctrlKey: true,
    });

    expect(notPrevented).toBe(true); // event was NOT prevented
    expect(handler).not.toHaveBeenCalled();
  });

  it("ignores the chord while typing", () => {
    const handler = vi.fn();
    const { container } = render(
      <div>
        <input aria-label="field" />
        <ShellAgentButton
          id="agent"
          events={["OnNewChat"]}
          eventHandler={handler}
          label="Claude Code"
        />
      </div>
    );

    const input = container.querySelector('input[aria-label="field"]')!;
    fireEvent.keyDown(input, {
      key: "a",
      code: "KeyA",
      ctrlKey: true,
      altKey: true,
    });

    expect(handler).not.toHaveBeenCalled();
  });

  it("renders three-segment hint and tooltip", () => {
    const { container } = render(
      <ShellAgentButton
        id="agent"
        events={["OnNewChat"]}
        eventHandler={vi.fn()}
        label="Claude Code"
      />
    );

    const button = container.querySelector("button.tsh-agent") as HTMLButtonElement;
    expect(button.title).toBe("Claude Code (Ctrl+Alt+A)");

    const kbd = container.querySelector(".tsh-kbd")!;
    const spans = kbd.querySelectorAll("span");
    expect(spans).toHaveLength(3);
    expect(spans[0].textContent).toBe("Ctrl");
    expect(spans[1].textContent).toBe("Alt");
    expect(spans[2].textContent).toBe("A");
  });
});
