import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { Badge, CountBadge, StatusDot, formatCount } from "./Badge";
import { IconButton } from "./IconButton";
import { Kbd, formatShortcut, splitKeys } from "./Kbd";
import { StatusLine, formatElapsed, formatTokenCount } from "./StatusLine";
import { Tooltip, TooltipScope } from "./Tooltip";

describe("Kbd", () => {
  it("splits a plus-joined shortcut into its keys", () => {
    expect(splitKeys("⌘+K")).toEqual(["⌘", "K"]);
    expect(splitKeys(["Ctrl", " Alt ", ""])).toEqual(["Ctrl", "Alt"]);
  });

  it("glues single-character keys with thin spaces and names with plus signs", () => {
    expect(formatShortcut(["⌘", "⌥", "N"])).toBe("⌘ ⌥ N");
    expect(formatShortcut("Ctrl+Alt+N")).toBe("Ctrl+Alt+N");
    expect(formatShortcut([])).toBe("");
  });

  it("renders boxed as one key cap and bare as a span per key", () => {
    const { container, rerender } = render(<Kbd keys={["⌘", "K"]} />);
    const boxed = container.querySelector("kbd.tui-kbd--boxed");
    expect(boxed).toBeInTheDocument();
    expect(boxed?.textContent).toBe("⌘ K");

    rerender(<Kbd keys={["Ctrl", "K"]} variant="bare" />);
    const bare = container.querySelector(".tui-kbd--bare")!;
    expect([...bare.querySelectorAll("span")].map((s) => s.textContent)).toEqual(["Ctrl", "K"]);
  });

  it("renders nothing without keys", () => {
    const { container } = render(<Kbd keys={[]} />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe("Badge", () => {
  it("caps a count at its maximum", () => {
    expect(formatCount(7)).toBe("7");
    expect(formatCount(100)).toBe("99+");
    expect(formatCount(100, 9)).toBe("9+");
  });

  it("renders nothing for a non-positive count", () => {
    const { container } = render(<CountBadge count={0} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("tints a badge by kind", () => {
    const { container } = render(<Badge kind="warning">Draft</Badge>);
    expect(container.querySelector(".tui-badge")).toHaveAttribute("data-kind", "warning");
  });

  it("marks a pulsing status dot", () => {
    const { container } = render(<StatusDot tone="danger" pulse label="Failing" />);
    const dot = container.querySelector(".tui-dot")!;
    expect(dot).toHaveAttribute("data-tone", "danger");
    expect(dot).toHaveAttribute("data-pulse", "true");
    expect(dot).toHaveAttribute("aria-label", "Failing");
  });
});

describe("IconButton", () => {
  it("names itself from its label and fires clicks", () => {
    const onClick = vi.fn();
    render(
      <IconButton label="New chat" size="md" onClick={onClick}>
        <svg />
      </IconButton>,
    );
    const button = screen.getByRole("button", { name: "New chat" });
    expect(button).toHaveAttribute("data-size", "md");
    fireEvent.click(button);
    expect(onClick).toHaveBeenCalledOnce();
  });

  it("anchors on a wrapper when the caller declares the button disableable", () => {
    const { container, rerender } = render(
      <IconButton label="Send" disabled>
        <svg />
      </IconButton>,
    );
    const wrapper = container.querySelector(".tui-tooltip-trigger-wrap")!;
    // A disabled button emits no pointer events, so the wrapper takes the focus in its place.
    expect(wrapper).toHaveAttribute("tabindex", "0");
    expect(screen.getByRole("button", { name: "Send" })).toBeDisabled();

    rerender(
      <IconButton label="Send" disabled={false}>
        <svg />
      </IconButton>,
    );
    // Same wrapper (flipping it would remount the button), but no longer a tab stop of its own.
    expect(container.querySelector(".tui-tooltip-trigger-wrap")).toBe(wrapper);
    expect(wrapper).not.toHaveAttribute("tabindex");
  });

  it("renders a bare button when the tooltip is suppressed", () => {
    const { container } = render(
      <IconButton label="Send" tooltip={false}>
        <svg />
      </IconButton>,
    );
    expect(container.querySelector(".tui-tooltip-trigger-wrap")).not.toBeInTheDocument();
  });
});

describe("Tooltip", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    act(() => {
      vi.runOnlyPendingTimers();
    });
    vi.useRealTimers();
  });

  it("shows the label and the shortcut key cap", () => {
    render(
      <Tooltip content="Open plans" shortcut={["⌘", "N"]} defaultOpen>
        <button>Open</button>
      </Tooltip>,
    );
    const tooltip = screen.getByRole("tooltip");
    expect(tooltip).toHaveTextContent("Open plans");
    expect(tooltip.querySelector(".tui-kbd")?.textContent).toBe("⌘ N");
  });

  it("opens inside a shared scope", () => {
    render(
      <TooltipScope>
        <Tooltip content="Scoped" defaultOpen>
          <button>Trigger</button>
        </Tooltip>
      </TooltipScope>,
    );
    expect(screen.getByRole("tooltip")).toHaveTextContent("Scoped");
  });

  it("renders the trigger alone when disabled or empty", () => {
    const { container, rerender } = render(
      <Tooltip content="Hidden" enabled={false}>
        <button>Trigger</button>
      </Tooltip>,
    );
    expect(container.querySelector(".tui-tooltip-content")).not.toBeInTheDocument();
    rerender(
      <Tooltip>
        <button>Trigger</button>
      </Tooltip>,
    );
    expect(screen.getByRole("button", { name: "Trigger" })).toBeInTheDocument();
  });
});

describe("StatusLine", () => {
  it("formats elapsed time the way the terminal status line does", () => {
    expect(formatElapsed(4_200)).toBe("4s");
    expect(formatElapsed(742_000)).toBe("12m 22s");
    expect(formatElapsed(3_780_000)).toBe("1h 3m");
    expect(formatElapsed(-5)).toBe("0s");
  });

  it("abbreviates token counts", () => {
    expect(formatTokenCount(842)).toBe("842");
    expect(formatTokenCount(16_800)).toBe("16.8k");
    expect(formatTokenCount(1_000)).toBe("1k");
    expect(formatTokenCount(1_240_000)).toBe("1.2M");
  });

  it("reads out the elapsed time, the token count and the message", () => {
    render(
      <StatusLine
        statusText="Waiting for Claude…"
        startedAt={new Date(Date.now() - 742_000).toISOString()}
        tokens={16_800}
      />,
    );
    const status = screen.getByRole("status");
    expect(status).toHaveTextContent("12m 22s");
    expect(status).toHaveTextContent("16.8k tokens");
    expect(status).toHaveTextContent("Waiting for Claude…");
    expect(status).toHaveAttribute("data-complete", "false");
  });

  it("keeps the ticking metrics out of the live region", () => {
    render(<StatusLine statusText="Working…" startedAt={Date.now()} tokens={1_000} />);
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-live", "polite");
    // Announcing the elapsed time would re-read the whole line once a second.
    expect(status.querySelector(".tui-status-metrics")).toHaveAttribute("aria-hidden", "true");
  });

  it("falls back to first sight when the agent's clock runs ahead of ours", () => {
    vi.useFakeTimers();
    try {
      // A server five minutes ahead would otherwise pin the timer at "0s" for five minutes.
      render(<StatusLine statusText="Working…" startedAt={Date.now() + 300_000} />);
      expect(screen.getByRole("status")).toHaveTextContent("0s");
      act(() => {
        vi.advanceTimersByTime(3000);
      });
      expect(screen.getByRole("status")).toHaveTextContent("3s");
    } finally {
      vi.useRealTimers();
    }
  });

  it("marks an estimated token count", () => {
    render(<StatusLine statusText="Working…" tokens={2_400} tokensEstimated />);
    expect(screen.getByText("~2.4k tokens")).toBeInTheDocument();
  });

  it("ticks the elapsed time once a second while running", () => {
    vi.useFakeTimers();
    try {
      const started = Date.now();
      render(<StatusLine statusText="Working…" startedAt={started} />);
      expect(screen.getByText("0s")).toBeInTheDocument();
      act(() => {
        vi.advanceTimersByTime(3000);
      });
      expect(screen.getByText("3s")).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it("freezes a finished run at its reported elapsed time", () => {
    render(<StatusLine statusText="Completed" isComplete elapsedMs={5_000} />);
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("data-complete", "true");
    expect(status).toHaveTextContent("5s");
  });
});
