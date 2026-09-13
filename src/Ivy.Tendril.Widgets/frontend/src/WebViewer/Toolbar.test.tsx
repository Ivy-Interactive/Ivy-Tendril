import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, act, cleanup } from "@testing-library/react";
import "@testing-library/jest-dom";
import { Toolbar, type ToolbarProps } from "./Toolbar";

afterEach(cleanup);

const renderToolbar = (overrides: Partial<ToolbarProps> = {}) => {
  const props: ToolbarProps = {
    url: "http://localhost:5077/chat",
    canGoBack: false,
    canGoForward: false,
    loading: false,
    device: "desktop",
    selecting: false,
    actions: [],
    onBack: vi.fn(),
    onForward: vi.fn(),
    onReload: vi.fn(),
    onNavigate: vi.fn(),
    onDevice: vi.fn(),
    onToggleSelect: vi.fn(),
    onAction: vi.fn(),
    ...overrides,
  };
  return { ...render(<Toolbar {...props} />), props };
};

describe("Toolbar navigation", () => {
  it("disables back and forward until there is history to move through", () => {
    renderToolbar();
    expect(screen.getByRole("button", { name: "Back" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Forward" })).toBeDisabled();

    cleanup();
    const { props } = renderToolbar({ canGoBack: true, canGoForward: true });
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    fireEvent.click(screen.getByRole("button", { name: "Forward" }));
    fireEvent.click(screen.getByRole("button", { name: "Reload" }));
    expect(props.onBack).toHaveBeenCalledTimes(1);
    expect(props.onForward).toHaveBeenCalledTimes(1);
    expect(props.onReload).toHaveBeenCalledTimes(1);
  });

  it("shows a progress line only while the page is loading", () => {
    const { container, rerender } = renderToolbar();
    expect(container.querySelector(".wvr-progress")).toBeNull();
    rerender(
      <Toolbar
        url={null}
        canGoBack={false}
        canGoForward={false}
        loading
        device="desktop"
        selecting={false}
        actions={[]}
        onBack={vi.fn()}
        onForward={vi.fn()}
        onReload={vi.fn()}
        onNavigate={vi.fn()}
        onDevice={vi.fn()}
        onToggleSelect={vi.fn()}
        onAction={vi.fn()}
      />,
    );
    expect(container.querySelector(".wvr-progress")).not.toBeNull();
  });
});

describe("Toolbar address bar", () => {
  it("shows the host and dims the path, without the scheme", () => {
    const { container } = renderToolbar();
    expect(container.querySelector(".wvr-address-host")?.textContent).toBe("localhost:5077");
    expect(container.querySelector(".wvr-address-path")?.textContent).toBe("/chat");
    expect(screen.getByRole("button", { name: "Address" }).textContent).not.toContain("http");
  });

  it("shows a placeholder when nothing is loaded", () => {
    renderToolbar({ url: null });
    expect(screen.getByRole("button", { name: "Address" }).textContent).toBe("Enter a URL");
  });

  it("opens the full url for editing and navigates on Enter", () => {
    const { props } = renderToolbar();
    fireEvent.click(screen.getByRole("button", { name: "Address" }));
    const input = screen.getByRole("textbox", { name: "Address" }) as HTMLInputElement;
    expect(input.value).toBe("http://localhost:5077/chat");
    expect(document.activeElement).toBe(input);

    fireEvent.change(input, { target: { value: " localhost:5077/settings " } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(props.onNavigate).toHaveBeenCalledWith("localhost:5077/settings");
    expect(screen.queryByRole("textbox", { name: "Address" })).toBeNull();
  });

  it("drops the edit on Escape and on blur", () => {
    const { props } = renderToolbar();
    fireEvent.click(screen.getByRole("button", { name: "Address" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Address" }), { target: { value: "elsewhere" } });
    fireEvent.keyDown(screen.getByRole("textbox", { name: "Address" }), { key: "Escape" });
    expect(screen.queryByRole("textbox", { name: "Address" })).toBeNull();
    expect(screen.getByRole("button", { name: "Address" }).textContent).toContain("localhost:5077");

    fireEvent.click(screen.getByRole("button", { name: "Address" }));
    fireEvent.blur(screen.getByRole("textbox", { name: "Address" }));
    expect(screen.queryByRole("textbox", { name: "Address" })).toBeNull();
    expect(props.onNavigate).not.toHaveBeenCalled();
  });

  it("does not navigate to an empty address", () => {
    const { props } = renderToolbar();
    fireEvent.click(screen.getByRole("button", { name: "Address" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Address" }), { target: { value: "   " } });
    fireEvent.keyDown(screen.getByRole("textbox", { name: "Address" }), { key: "Enter" });
    expect(props.onNavigate).not.toHaveBeenCalled();
  });
});

describe("Toolbar device menu", () => {
  it("names the current viewport and lets another be picked", () => {
    const { props } = renderToolbar({ device: "tablet" });
    const trigger = screen.getByRole("button", { name: "Viewport: Tablet" });
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("menu")).toBeNull();

    fireEvent.click(trigger);
    expect(trigger).toHaveAttribute("aria-expanded", "true");
    const items = screen.getAllByRole("menuitemradio");
    expect(items.map((item) => item.textContent)).toEqual(["Desktop", "Tablet", "Mobile"]);
    expect(items[1]).toHaveAttribute("aria-checked", "true");

    fireEvent.click(screen.getByRole("menuitemradio", { name: "Mobile" }));
    expect(props.onDevice).toHaveBeenCalledWith("mobile");
    expect(screen.queryByRole("menu")).toBeNull();
  });

  it("closes on Escape and on a click elsewhere", () => {
    renderToolbar();
    fireEvent.click(screen.getByRole("button", { name: "Viewport: Desktop" }));
    expect(screen.getByRole("menu")).not.toBeNull();
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("menu")).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Viewport: Desktop" }));
    fireEvent.mouseDown(document.body);
    expect(screen.queryByRole("menu")).toBeNull();
  });
});

describe("Toolbar tools and actions", () => {
  it("toggles select mode and reflects it as pressed", () => {
    const { props } = renderToolbar();
    const select = screen.getByRole("button", { name: "Select an element to comment on" });
    expect(select).toHaveAttribute("aria-pressed", "false");
    fireEvent.click(select);
    expect(props.onToggleSelect).toHaveBeenCalledTimes(1);

    cleanup();
    renderToolbar({ selecting: true });
    expect(screen.getByRole("button", { name: "Stop selecting" })).toHaveAttribute("aria-pressed", "true");
  });

  it("renders host actions as icon buttons with badge, primary and disabled states", () => {
    const { props } = renderToolbar({
      actions: [
        { id: "update", icon: "MessageSquare", label: "Send 3 comments", badge: "3", primary: true },
        { id: "draw", icon: "Pencil", label: "Draw", active: true },
        { id: "bug", icon: "Bug", label: "Report", disabled: true },
      ],
    });
    const update = screen.getByRole("button", { name: "Send 3 comments" });
    expect(update).toHaveAttribute("data-primary", "true");
    expect(update.querySelector(".wvr-badge")?.textContent).toBe("3");
    expect(update).not.toHaveAttribute("aria-pressed");

    expect(screen.getByRole("button", { name: "Draw" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Report" })).toBeDisabled();

    fireEvent.click(update);
    expect(props.onAction).toHaveBeenCalledWith("update");
  });

  it("does not open a tooltip for focus that came from a click", () => {
    vi.useFakeTimers();
    try {
      renderToolbar();
      const reload = screen.getByRole("button", { name: "Reload" });
      fireEvent.mouseDown(reload);
      fireEvent.focus(reload);
      act(() => {
        vi.advanceTimersByTime(500);
      });
      expect(screen.queryByRole("tooltip")).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it("keeps the viewport tooltip away while its menu is open", () => {
    vi.useFakeTimers();
    try {
      renderToolbar();
      const trigger = screen.getByRole("button", { name: "Viewport: Desktop" });
      fireEvent.click(trigger);
      fireEvent.mouseEnter(trigger.parentElement!);
      act(() => {
        vi.advanceTimersByTime(500);
      });
      expect(screen.getByRole("menu")).not.toBeNull();
      expect(screen.queryByRole("tooltip")).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it("shows a tooltip after hovering a button for a moment", () => {
    vi.useFakeTimers();
    try {
      renderToolbar();
      const reload = screen.getByRole("button", { name: "Reload" });
      fireEvent.mouseEnter(reload.parentElement!);
      expect(screen.queryByRole("tooltip")).toBeNull();
      act(() => {
        vi.advanceTimersByTime(500);
      });
      expect(screen.getByRole("tooltip").textContent).toBe("Reload");
      fireEvent.mouseLeave(reload.parentElement!);
      expect(screen.queryByRole("tooltip")).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });
});
