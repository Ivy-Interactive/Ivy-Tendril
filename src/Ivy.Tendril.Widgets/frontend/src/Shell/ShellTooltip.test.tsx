import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ShellTooltip, formatShortcut } from "./ShellTooltip";

describe("formatShortcut", () => {
  it("formats single-character shortcuts with thin spaces", () => {
    expect(formatShortcut(["⌘", "⌥", "N"])).toBe("⌘\u2009⌥\u2009N");
    expect(formatShortcut("⌘+B")).toBe("⌘\u2009B");
  });

  it("formats multi-character shortcuts with plus signs", () => {
    expect(formatShortcut(["Ctrl", "Alt", "N"])).toBe("Ctrl+Alt+N");
    expect(formatShortcut("Ctrl+B")).toBe("Ctrl+B");
    expect(formatShortcut("Ctrl+Alt+A")).toBe("Ctrl+Alt+A");
  });

  it("returns empty string for empty input", () => {
    expect(formatShortcut("")).toBe("");
    expect(formatShortcut([])).toBe("");
  });
});

describe("ShellTooltip", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    act(() => {
      vi.runOnlyPendingTimers();
    });
    vi.useRealTimers();
  });

  it("renders trigger content correctly", () => {
    render(
      <ShellTooltip content="Tooltip text">
        <button>Trigger Button</button>
      </ShellTooltip>,
    );

    expect(screen.getByRole("button", { name: /trigger button/i })).toBeInTheDocument();
  });

  it("does not wrap or render tooltip when disabled", () => {
    render(
      <ShellTooltip content="Tooltip text" enabled={false}>
        <button>Trigger Button</button>
      </ShellTooltip>,
    );

    const button = screen.getByRole("button", { name: /trigger button/i });
    fireEvent.pointerEnter(button);
    act(() => {
      vi.advanceTimersByTime(600);
    });

    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
  });

  it("displays tooltip content on hover with proper ARIA attributes", () => {
    render(
      <ShellTooltip content="Helpful information" delayDuration={0}>
        <button>Hover me</button>
      </ShellTooltip>,
    );

    const button = screen.getByRole("button", { name: /hover me/i });
    fireEvent.pointerMove(button);
    act(() => {
      vi.advanceTimersByTime(50);
    });

    const tooltip = screen.getByRole("tooltip");
    expect(tooltip).toBeInTheDocument();
    expect(tooltip).toHaveTextContent("Helpful information");
    expect(document.querySelector(".tui-tooltip-content")).toBeInTheDocument();
  });

  it("displays tooltip content on focus", () => {
    render(
      <ShellTooltip content="Focused info" delayDuration={0}>
        <button>Focus me</button>
      </ShellTooltip>,
    );

    const button = screen.getByRole("button", { name: /focus me/i });
    fireEvent.focus(button);
    act(() => {
      vi.advanceTimersByTime(50);
    });

    const tooltip = screen.getByRole("tooltip");
    expect(tooltip).toBeInTheDocument();
    expect(tooltip).toHaveTextContent("Focused info");
  });

  it("renders platform shortcut keys correctly inside the tooltip", () => {
    render(
      <ShellTooltip content="Open plans" shortcut={["⌘", "⌥", "N"]} defaultOpen={true}>
        <button>Open</button>
      </ShellTooltip>,
    );

    const tooltip = screen.getByRole("tooltip");
    expect(tooltip).toBeInTheDocument();
    expect(tooltip).toHaveTextContent("Open plans");
    const kbd = tooltip.querySelector(".tui-kbd");
    expect(kbd).toBeInTheDocument();
    expect(kbd?.textContent).toBe("⌘\u2009⌥\u2009N");
  });
});
