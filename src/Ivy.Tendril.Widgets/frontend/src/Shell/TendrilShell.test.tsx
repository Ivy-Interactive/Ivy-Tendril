import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";

import { TendrilShell, SIDEBAR_COLLAPSED_STORAGE_KEY } from "./TendrilShell";
import { useShell } from "./ShellContext";

const ToggleButton = () => {
  const { collapsed, toggle } = useShell();
  return (
    <button onClick={toggle} data-testid="toggle-btn">
      {collapsed ? "Expand" : "Collapse"}
    </button>
  );
};

describe("TendrilShell", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("TendrilShell respects localStorage on mount", () => {
    window.localStorage.setItem(SIDEBAR_COLLAPSED_STORAGE_KEY, "true");
    const { container } = render(
      <TendrilShell
        id="shell-1"
        collapsed={false}
        eventHandler={vi.fn()}
      />
    );
    const root = container.querySelector(".tsh-root");
    expect(root).toHaveAttribute("data-collapsed", "true");

    window.localStorage.setItem(SIDEBAR_COLLAPSED_STORAGE_KEY, "false");
    const { container: container2 } = render(
      <TendrilShell
        id="shell-2"
        collapsed={true}
        eventHandler={vi.fn()}
      />
    );
    const root2 = container2.querySelector(".tsh-root");
    expect(root2).toHaveAttribute("data-collapsed", "false");
  });

  it("TendrilShell falls back to collapsed prop when localStorage is empty", () => {
    const { container: container1 } = render(
      <TendrilShell
        id="shell-1"
        collapsed={false}
        eventHandler={vi.fn()}
      />
    );
    expect(container1.querySelector(".tsh-root")).toHaveAttribute("data-collapsed", "false");

    const { container: container2 } = render(
      <TendrilShell
        id="shell-2"
        collapsed={true}
        eventHandler={vi.fn()}
      />
    );
    expect(container2.querySelector(".tsh-root")).toHaveAttribute("data-collapsed", "true");
  });

  it("TendrilShell writes to localStorage and notifies server on toggle", () => {
    const eventHandler = vi.fn();
    const { container } = render(
      <TendrilShell
        id="shell-1"
        collapsed={false}
        events={["OnCollapsedChanged"]}
        eventHandler={eventHandler}
        slots={{
          SidebarHeader: <ToggleButton />,
        }}
      />
    );

    const root = container.querySelector(".tsh-root");
    expect(root).toHaveAttribute("data-collapsed", "false");
    expect(window.localStorage.getItem(SIDEBAR_COLLAPSED_STORAGE_KEY)).toBeNull();

    fireEvent.click(screen.getByTestId("toggle-btn"));

    expect(root).toHaveAttribute("data-collapsed", "true");
    expect(window.localStorage.getItem(SIDEBAR_COLLAPSED_STORAGE_KEY)).toBe("true");
    expect(eventHandler).toHaveBeenCalledWith("OnCollapsedChanged", "shell-1", [true]);

    fireEvent.click(screen.getByTestId("toggle-btn"));

    expect(root).toHaveAttribute("data-collapsed", "false");
    expect(window.localStorage.getItem(SIDEBAR_COLLAPSED_STORAGE_KEY)).toBe("false");
    expect(eventHandler).toHaveBeenCalledWith("OnCollapsedChanged", "shell-1", [false]);
  });
});
