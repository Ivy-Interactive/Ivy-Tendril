import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";

import {
  TendrilShell,
  SIDEBAR_COLLAPSED_STORAGE_KEY,
  SIDEBAR_WIDTH_STORAGE_KEY,
  DEFAULT_SIDEBAR_WIDTH,
  MIN_SIDEBAR_WIDTH,
  MAX_SIDEBAR_WIDTH,
} from "./TendrilShell";
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

  it("content in slots.Content receives collapsed: false even when sidebar is collapsed", () => {
    let observedCollapsed: boolean | undefined;
    const TestContent = () => {
      const { collapsed } = useShell();
      observedCollapsed = collapsed;
      return <div data-testid="test-content">Content (collapsed={String(collapsed)})</div>;
    };

    render(
      <TendrilShell
        id="shell-test"
        collapsed={true}
        eventHandler={vi.fn()}
        slots={{
          Content: <TestContent />,
        }}
      />
    );

    expect(observedCollapsed).toBe(false);
  });

  it("TendrilShell respects localStorage sidebarWidth on mount", () => {
    window.localStorage.setItem(SIDEBAR_WIDTH_STORAGE_KEY, "280");
    const { container } = render(
      <TendrilShell id="shell-1" eventHandler={vi.fn()} />
    );
    const root = container.querySelector(".tsh-root") as HTMLElement;
    expect(root.style.getPropertyValue("--tsh-sidebar-width")).toBe("280px");
  });

  it("TendrilShell falls back to default width when localStorage is empty", () => {
    const { container } = render(
      <TendrilShell id="shell-1" eventHandler={vi.fn()} />
    );
    const root = container.querySelector(".tsh-root") as HTMLElement;
    expect(root.style.getPropertyValue("--tsh-sidebar-width")).toBe(`${DEFAULT_SIDEBAR_WIDTH}px`);
  });

  it("dragging sidebar resizer updates width and persists to localStorage", () => {
    const { container } = render(
      <TendrilShell id="shell-1" eventHandler={vi.fn()} />
    );
    const root = container.querySelector(".tsh-root") as HTMLElement;
    const resizer = container.querySelector(".tsh-sidebar-resizer") as HTMLElement;
    expect(resizer).not.toBeNull();

    fireEvent.pointerDown(resizer, { button: 0, clientX: 320, pointerId: 1 });
    expect(root).toHaveAttribute("data-resizing", "true");

    fireEvent.pointerMove(resizer, { clientX: 400, pointerId: 1 });
    expect(root.style.getPropertyValue("--tsh-sidebar-width")).toBe("400px");

    fireEvent.pointerUp(resizer, { pointerId: 1 });
    expect(root).toHaveAttribute("data-resizing", "false");
    expect(window.localStorage.getItem(SIDEBAR_WIDTH_STORAGE_KEY)).toBe("400");
  });

  it("sidebar resizer clamps between MIN_SIDEBAR_WIDTH and window boundary", () => {
    const { container } = render(
      <TendrilShell id="shell-1" eventHandler={vi.fn()} />
    );
    const root = container.querySelector(".tsh-root") as HTMLElement;
    const resizer = container.querySelector(".tsh-sidebar-resizer") as HTMLElement;

    // Drag far to the left
    fireEvent.pointerDown(resizer, { button: 0, clientX: 320, pointerId: 1 });
    fireEvent.pointerMove(resizer, { clientX: 50, pointerId: 1 });
    expect(root.style.getPropertyValue("--tsh-sidebar-width")).toBe(`${MIN_SIDEBAR_WIDTH}px`);
    fireEvent.pointerUp(resizer, { pointerId: 1 });
    expect(window.localStorage.getItem(SIDEBAR_WIDTH_STORAGE_KEY)).toBe(`${MIN_SIDEBAR_WIDTH}`);

    // Drag far to the right
    fireEvent.pointerDown(resizer, { button: 0, clientX: 200, pointerId: 1 });
    fireEvent.pointerMove(resizer, { clientX: 2000, pointerId: 1 });
    const expectedMax = Math.min(MAX_SIDEBAR_WIDTH, Math.max(MIN_SIDEBAR_WIDTH, window.innerWidth - 300));
    expect(root.style.getPropertyValue("--tsh-sidebar-width")).toBe(`${expectedMax}px`);
    fireEvent.pointerUp(resizer, { pointerId: 1 });
    expect(window.localStorage.getItem(SIDEBAR_WIDTH_STORAGE_KEY)).toBe(`${expectedMax}`);
  });

  it("double-clicking sidebar resizer resets to default width and clears localStorage", () => {
    window.localStorage.setItem(SIDEBAR_WIDTH_STORAGE_KEY, "450");
    const { container } = render(
      <TendrilShell id="shell-1" eventHandler={vi.fn()} />
    );
    const root = container.querySelector(".tsh-root") as HTMLElement;
    const resizer = container.querySelector(".tsh-sidebar-resizer") as HTMLElement;

    expect(root.style.getPropertyValue("--tsh-sidebar-width")).toBe("450px");

    fireEvent.doubleClick(resizer);

    expect(root.style.getPropertyValue("--tsh-sidebar-width")).toBe(`${DEFAULT_SIDEBAR_WIDTH}px`);
    expect(window.localStorage.getItem(SIDEBAR_WIDTH_STORAGE_KEY)).toBeNull();
  });

  it("sidebar resizer is omitted when sidebar is collapsed", () => {
    const { container } = render(
      <TendrilShell id="shell-1" collapsed={true} eventHandler={vi.fn()} />
    );
    expect(container.querySelector(".tsh-sidebar-resizer")).toBeNull();
  });
});
