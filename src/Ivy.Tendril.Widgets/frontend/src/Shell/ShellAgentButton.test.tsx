import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ShellAgentButton } from "./ShellAgentButton";
import { ShellContext } from "./ShellContext";
import { ShellSectionItemDto } from "./types";

const chats: ShellSectionItemDto[] = [
  { id: "c1", title: "Chat one" },
  { id: "c2", title: "Chat two" },
  { id: "c3", title: "Chat three" },
];

const renderOnRail = (props: Partial<React.ComponentProps<typeof ShellAgentButton>> = {}) => {
  const eventHandler = props.eventHandler ?? vi.fn();
  const utils = render(
    <ShellContext.Provider value={{ collapsed: true, toggle: () => {} }}>
      <ShellAgentButton
        id="agent"
        label="Chat"
        listTitle="Chats"
        items={chats}
        selectedId="c2"
        events={["OnOpen", "OnNewChat", "OnSelectItem"]}
        eventHandler={eventHandler}
        {...props}
      />
    </ShellContext.Provider>,
  );
  return { ...utils, eventHandler, row: screen.getByRole("button", { name: "Chat" }) };
};

const hover = (element: HTMLElement) => {
  vi.useFakeTimers();
  fireEvent.pointerEnter(element);
  act(() => {
    vi.advanceTimersByTime(200);
  });
  vi.useRealTimers();
};

describe("ShellAgentButton", () => {
  it("fires OnNewChat on Ctrl+Alt+A", () => {
    const handler = vi.fn();
    render(
      <ShellAgentButton
        id="agent"
        events={["OnNewChat"]}
        eventHandler={handler}
        label="Claude Code"
      />,
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
      />,
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
      </div>,
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

  it("renders three-segment hint and does not use native title attribute", () => {
    const { container } = render(
      <ShellAgentButton
        id="agent"
        events={["OnNewChat"]}
        eventHandler={vi.fn()}
        label="Claude Code"
      />,
    );

    const button = container.querySelector("button.tsh-agent") as HTMLButtonElement;
    expect(button.getAttribute("title")).toBeNull();
    expect(button).toHaveAttribute("aria-label", "Claude Code");

    const kbd = container.querySelector(".tsh-kbd")!;
    const spans = kbd.querySelectorAll("span");
    expect(spans).toHaveLength(3);
    expect(spans[0].textContent).toBe("Ctrl");
    expect(spans[1].textContent).toBe("Alt");
    expect(spans[2].textContent).toBe("A");
  });

  it("names the tooltip New Chat with the three-key shortcut", () => {
    vi.useFakeTimers();
    render(
      <ShellAgentButton
        id="agent"
        events={["OnNewChat"]}
        eventHandler={vi.fn()}
        label="Claude Code"
      />,
    );

    const button = screen.getByRole("button", { name: "Claude Code" });
    fireEvent.pointerMove(button);
    act(() => {
      vi.advanceTimersByTime(500);
    });

    const tooltip = screen.getByRole("tooltip");
    expect(tooltip).toBeInTheDocument();
    expect(tooltip).toHaveTextContent("New Chat");
    const kbd = tooltip.querySelector(".tui-kbd");
    expect(kbd).toBeInTheDocument();
    expect(kbd?.textContent).toBe("Ctrl+Alt+A");
    vi.useRealTimers();
  });

  describe("on the collapsed rail with a chat list", () => {
    it("floats the list on hover, with a pin glyph and a count pill on the row", () => {
      const { row, container } = renderOnRail();
      expect(container.querySelector(".tsh-agent-count")).toHaveTextContent("3");
      expect(container.querySelector(".tsh-agent-pin")).toBeInTheDocument();
      expect(screen.queryByText("Chat one")).not.toBeInTheDocument();

      hover(row);
      expect(screen.getByText("Chat one")).toBeInTheDocument();
      expect(row).toHaveAttribute("data-open", "true");
      expect(row).toHaveAttribute("data-pinned", "false");

      vi.useFakeTimers();
      fireEvent.pointerLeave(row);
      act(() => {
        vi.advanceTimersByTime(300);
      });
      vi.useRealTimers();
      expect(screen.queryByText("Chat one")).not.toBeInTheDocument();
    });

    it("pins the open list on click instead of opening the chat page", () => {
      const { row, eventHandler } = renderOnRail();
      hover(row);
      fireEvent.click(row);

      expect(row).toHaveAttribute("data-pinned", "true");
      expect(eventHandler).not.toHaveBeenCalledWith("OnOpen", "agent", []);

      vi.useFakeTimers();
      fireEvent.pointerLeave(row);
      act(() => {
        vi.advanceTimersByTime(500);
      });
      vi.useRealTimers();
      expect(screen.getByText("Chat one")).toBeInTheDocument();

      fireEvent.click(row);
      expect(screen.queryByText("Chat one")).not.toBeInTheDocument();
    });

    it("swaps the pin for a slashed pin while the list is pinned", () => {
      const { row, container } = renderOnRail();
      expect(container.querySelector(".tsh-agent-pin .lucide-pin")).toBeInTheDocument();
      fireEvent.click(row);
      expect(container.querySelector(".tsh-agent-pin .lucide-pin-off")).toBeInTheDocument();
      expect(container.querySelector(".tsh-agent-pin .lucide-pin")).toBeNull();
    });

    it("offers rename and delete on flyout rows when the host listens", () => {
      const { row, eventHandler } = renderOnRail({
        events: ["OnSelectItem", "OnRenameItem", "OnDeleteItem"],
      });
      fireEvent.click(row);
      fireEvent.click(screen.getByRole("button", { name: "Chat two options" }));
      fireEvent.click(screen.getByRole("menuitem", { name: /Delete/ }));
      expect(eventHandler).toHaveBeenCalledWith("OnDeleteItem", "agent", ["c2"]);
    });

    it("replaces the close button with a New chat button that fires OnNewChat", () => {
      const { row, eventHandler } = renderOnRail();
      fireEvent.click(row);

      expect(screen.queryByRole("button", { name: "Close" })).not.toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: "New chat" }));
      expect(eventHandler).toHaveBeenCalledWith("OnNewChat", "agent", []);
    });

    it("selects a picked chat through OnSelectItem and steps with the arrows", () => {
      const { row, eventHandler } = renderOnRail();
      fireEvent.click(row);

      fireEvent.click(screen.getByText("Chat three"));
      expect(eventHandler).toHaveBeenCalledWith("OnSelectItem", "agent", ["c3"]);

      fireEvent.click(screen.getByRole("button", { name: "Previous" }));
      expect(eventHandler).toHaveBeenLastCalledWith("OnSelectItem", "agent", ["c1"]);
    });

    it("shows no tooltip while the row owns the flyout", () => {
      vi.useFakeTimers();
      const { row } = renderOnRail();
      fireEvent.pointerMove(row);
      act(() => {
        vi.advanceTimersByTime(600);
      });
      expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
      vi.useRealTimers();
    });

    it("keeps the plain row while the list is empty", () => {
      const { container } = renderOnRail({ items: [] });
      expect(container.querySelector(".tsh-agent-count")).toBeNull();
      expect(container.querySelector(".tsh-agent-pin")).toBeNull();
      expect(screen.getByRole("button", { name: "Chat" })).toHaveAttribute("data-has-list", "false");
    });

    it("keeps the plain row when the sidebar is expanded", () => {
      const eventHandler = vi.fn();
      const { container } = render(
        <ShellContext.Provider value={{ collapsed: false, toggle: () => {} }}>
          <ShellAgentButton
            id="agent"
            label="Chat"
            listTitle="Chats"
            items={chats}
            events={["OnOpen"]}
            eventHandler={eventHandler}
          />
        </ShellContext.Provider>,
      );

      expect(container.querySelector(".tsh-agent-count")).toBeNull();
      fireEvent.click(screen.getByRole("button", { name: "Chat" }));
      expect(eventHandler).toHaveBeenCalledWith("OnOpen", "agent", []);
    });
  });
});
