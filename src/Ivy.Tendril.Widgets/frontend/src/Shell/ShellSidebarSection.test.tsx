import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom";

import { ShellSidebarSection } from "./ShellSidebarSection";
import { ShellContext } from "./ShellContext";
import { ShellSectionItemDto } from "./types";

const mockItems: ShellSectionItemDto[] = [
  { id: "00001-PlanA", title: "Plan A", tag: "#1" },
  { id: "00002-PlanB", title: "Plan B", tag: "#2" },
];

describe("ShellSidebarSection", () => {
  it("renders the section header, title, and search button when searchable", () => {
    const onSearch = vi.fn();
    render(
      <ShellSidebarSection
        id="sec-1"
        title="Plans"
        items={mockItems}
        searchable={true}
        events={["OnSearch", "OnSelectItem"]}
        eventHandler={onSearch}
      />,
    );

    expect(screen.getByText("Plans")).toBeInTheDocument();
    const searchBtn = screen.getByRole("button", { name: /search plans/i });
    expect(searchBtn).toBeInTheDocument();

    fireEvent.click(searchBtn);
    expect(onSearch).toHaveBeenCalledWith("OnSearch", "sec-1", []);
  });

  it("emits OnSelectItem when an item is clicked", () => {
    const eventHandler = vi.fn();
    render(
      <ShellSidebarSection
        id="sec-1"
        title="Plans"
        items={mockItems}
        searchable={true}
        events={["OnSelectItem"]}
        eventHandler={eventHandler}
      />,
    );

    fireEvent.click(screen.getByText("Plan A"));
    expect(eventHandler).toHaveBeenCalledWith("OnSelectItem", "sec-1", ["00001-PlanA"]);
  });

  it("retains search button when props update between different views", () => {
    const { rerender } = render(
      <ShellSidebarSection
        id="sec-1"
        title="Plans"
        items={mockItems}
        searchable={true}
        events={["OnSearch"]}
        eventHandler={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: /search plans/i })).toBeInTheDocument();
    expect(screen.getByText("Plans")).toBeInTheDocument();

    const reviewItems: ShellSectionItemDto[] = [{ id: "00003-PlanC", title: "Plan C", tag: "#3" }];

    rerender(
      <ShellSidebarSection
        id="sec-1"
        title="Review"
        items={reviewItems}
        searchable={true}
        events={["OnSearch"]}
        eventHandler={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: /search plans/i })).toBeInTheDocument();
    expect(screen.getByText("Review")).toBeInTheDocument();
    expect(screen.getByText("Plan C")).toBeInTheDocument();
  });

  it("replaces the title with a full-width Search button when there is no list", () => {
    const eventHandler = vi.fn();
    render(
      <ShellSidebarSection
        id="sec-1"
        searchable={true}
        events={["OnSearch"]}
        eventHandler={eventHandler}
      />,
    );

    const searchBtn = screen.getByRole("button", { name: /search plans/i });
    expect(searchBtn).toHaveClass("tsh-section-search-button");
    expect(screen.getByText("Search")).toBeInTheDocument();
    expect(screen.getByText("K")).toBeInTheDocument();
    expect(screen.queryByText("Plans")).not.toBeInTheDocument();

    fireEvent.click(searchBtn);
    expect(eventHandler).toHaveBeenCalledWith("OnSearch", "sec-1", []);
  });

  it("replaces the title with the Search button when the list is empty", () => {
    render(
      <ShellSidebarSection
        id="sec-1"
        title="Review"
        items={[]}
        searchable={true}
        events={["OnSearch"]}
        eventHandler={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: /search plans/i })).toHaveClass(
      "tsh-section-search-button",
    );
    expect(screen.queryByText("Review")).not.toBeInTheDocument();
  });

  it("keeps the title and icon button while the list has items", () => {
    render(
      <ShellSidebarSection
        id="sec-1"
        title="Review"
        items={mockItems}
        searchable={true}
        events={["OnSearch"]}
        eventHandler={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: /search plans/i })).toHaveClass("tsh-section-search");
    expect(screen.getByText("Review")).toBeInTheDocument();
    expect(screen.queryByText("Search")).not.toBeInTheDocument();
  });

  it("opens search on Cmd/Ctrl+K, but not while typing in an input", () => {
    const eventHandler = vi.fn();
    render(
      <>
        <input aria-label="field" />
        <ShellSidebarSection
          id="sec-1"
          searchable={true}
          events={["OnSearch"]}
          eventHandler={eventHandler}
        />
      </>,
    );

    fireEvent.keyDown(document.body, { key: "k", ctrlKey: true });
    expect(eventHandler).toHaveBeenCalledWith("OnSearch", "sec-1", []);

    eventHandler.mockClear();
    fireEvent.keyDown(screen.getByLabelText("field"), { key: "k", ctrlKey: true });
    expect(eventHandler).not.toHaveBeenCalled();
  });

  it("ignores Cmd/Ctrl+K when not searchable", () => {
    const eventHandler = vi.fn();
    render(
      <ShellSidebarSection
        id="sec-1"
        items={mockItems}
        searchable={false}
        events={["OnSearch"]}
        eventHandler={eventHandler}
      />,
    );

    fireEvent.keyDown(document.body, { key: "k", ctrlKey: true });
    expect(eventHandler).not.toHaveBeenCalled();
  });

  it("renders collapsed rail view with search button", () => {
    const onSearch = vi.fn();
    render(
      <ShellContext.Provider value={{ collapsed: true, toggle: () => {} }}>
        <ShellSidebarSection
          id="sec-1"
          title="Plans"
          items={mockItems}
          searchable={true}
          events={["OnSearch"]}
          eventHandler={onSearch}
        />
      </ShellContext.Provider>,
    );

    const railSearchBtn = screen.getByRole("button", { name: /search plans/i });
    expect(railSearchBtn).toBeInTheDocument();
    expect(railSearchBtn).toHaveClass("tsh-rail-search");

    fireEvent.click(railSearchBtn);
    expect(onSearch).toHaveBeenCalledWith("OnSearch", "sec-1", []);
  });

  it("shows the rail search icon without a list", () => {
    render(
      <ShellContext.Provider value={{ collapsed: true, toggle: () => {} }}>
        <ShellSidebarSection
          id="sec-1"
          searchable={true}
          events={["OnSearch"]}
          eventHandler={vi.fn()}
        />
      </ShellContext.Provider>,
    );

    expect(screen.getByRole("button", { name: /search plans/i })).toHaveClass("tsh-rail-search");
  });

  it("search button does not have native title attribute and displays custom tooltip on hover", () => {
    vi.useFakeTimers();
    render(
      <ShellSidebarSection
        id="sec-1"
        title="Plans"
        items={mockItems}
        searchable={true}
        events={["OnSearch"]}
        eventHandler={vi.fn()}
      />,
    );

    const searchBtn = screen.getByRole("button", { name: /search plans/i });
    expect(searchBtn.getAttribute("title")).toBeNull();

    fireEvent.pointerMove(searchBtn);
    act(() => {
      vi.advanceTimersByTime(500);
    });

    const tooltip = screen.getByRole("tooltip");
    expect(tooltip).toBeInTheDocument();
    expect(tooltip).toHaveTextContent("Search plans");
    vi.useRealTimers();
  });

  it("renders a new-chat button that fires OnNew when newLabel is set", () => {
    const eventHandler = vi.fn();
    render(
      <ShellSidebarSection
        id="sec-1"
        title="Chats"
        items={mockItems}
        newLabel="New chat"
        events={["OnNew"]}
        eventHandler={eventHandler}
      />,
    );

    const newBtn = screen.getByRole("button", { name: "New chat" });
    expect(newBtn).toHaveClass("tsh-section-new");

    fireEvent.click(newBtn);
    expect(eventHandler).toHaveBeenCalledWith("OnNew", "sec-1", []);
  });

  it("does not render a new-chat button without newLabel", () => {
    render(
      <ShellSidebarSection id="sec-1" title="Chats" items={mockItems} eventHandler={vi.fn()} />,
    );

    expect(screen.queryByRole("button", { name: "New chat" })).not.toBeInTheDocument();
  });

  it("uses the title header instead of the full-width Search button when newLabel is set and the list is empty", () => {
    render(
      <ShellSidebarSection
        id="sec-1"
        title="Chats"
        items={[]}
        searchable={true}
        newLabel="New chat"
        events={["OnSearch", "OnNew"]}
        eventHandler={vi.fn()}
      />,
    );

    expect(screen.getByText("Chats")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /search plans/i })).toHaveClass("tsh-section-search");
    expect(screen.queryByRole("button", { name: /^Search$/i })).not.toBeInTheDocument();
  });

  it("renders the new-chat button on the collapsed rail", () => {
    const eventHandler = vi.fn();
    render(
      <ShellContext.Provider value={{ collapsed: true, toggle: () => {} }}>
        <ShellSidebarSection
          id="sec-1"
          title="Chats"
          items={mockItems}
          newLabel="New chat"
          events={["OnNew"]}
          eventHandler={eventHandler}
        />
      </ShellContext.Provider>,
    );

    const railNewBtn = screen.getByRole("button", { name: "New chat" });
    expect(railNewBtn).toHaveClass("tsh-rail-new");

    fireEvent.click(railNewBtn);
    expect(eventHandler).toHaveBeenCalledWith("OnNew", "sec-1", []);
  });

  it("renders the mapped icon for an item with icon: Terminal", () => {
    const itemsWithIcon: ShellSectionItemDto[] = [{ id: "term-1", title: "Terminal Session", icon: "Terminal" }];
    const { container } = render(
      <ShellSidebarSection id="sec-1" title="Chats" items={itemsWithIcon} eventHandler={vi.fn()} />,
    );

    const iconWrap = container.querySelector(".tsh-section-item-icon");
    expect(iconWrap).toBeInTheDocument();
    expect(iconWrap?.querySelector("svg")).toBeInTheDocument();
  });

  it("renders no icon for an item without a recognized icon name", () => {
    const itemsWithoutIcon: ShellSectionItemDto[] = [{ id: "plain-1", title: "Plain Item" }];
    const { container } = render(
      <ShellSidebarSection id="sec-1" title="Chats" items={itemsWithoutIcon} eventHandler={vi.fn()} />,
    );

    expect(container.querySelector(".tsh-section-item-icon")).not.toBeInTheDocument();
  });

  it("displays custom tooltip with plan title and badges when hovering collapsed rail plan item", () => {
    vi.useFakeTimers();
    const itemsWithBadges: ShellSectionItemDto[] = [
      {
        id: "00001-PlanA",
        title: "Plan A",
        tag: "#1",
        badges: [{ label: "Draft", kind: "neutral" }],
      },
    ];

    render(
      <ShellContext.Provider value={{ collapsed: true, toggle: () => {} }}>
        <ShellSidebarSection
          id="sec-1"
          title="Plans"
          items={itemsWithBadges}
          events={["OnSelectItem"]}
          eventHandler={vi.fn()}
        />
      </ShellContext.Provider>,
    );

    const railItemBtn = screen.getByRole("button", { name: "Plan A" });
    expect(railItemBtn.getAttribute("title")).toBeNull();

    fireEvent.pointerMove(railItemBtn);
    act(() => {
      vi.advanceTimersByTime(500);
    });

    const tooltip = screen.getByRole("tooltip");
    expect(tooltip).toBeInTheDocument();
    expect(tooltip).toHaveTextContent("Plan A");
    expect(tooltip).toHaveTextContent("Draft");
    expect(document.querySelector(".tsh-rail-tooltip")).toBeInTheDocument();
    vi.useRealTimers();
  });

  it("renders items without tags (icon-only terminal sessions and default chat items) in collapsed rail", () => {
    const mixedItems: ShellSectionItemDto[] = [
      { id: "term-1", title: "Terminal Chat", icon: "Terminal" },
      { id: "chat-1", title: "General Chat" },
      { id: "plan-1", title: "Plan Item", tag: "#1" },
    ];

    const { container } = render(
      <ShellContext.Provider value={{ collapsed: true, toggle: () => {} }}>
        <ShellSidebarSection id="sec-1" title="Chats" items={mixedItems} eventHandler={vi.fn()} />
      </ShellContext.Provider>,
    );

    const railList = container.querySelector(".tsh-rail-list");
    expect(railList).toBeInTheDocument();

    const buttons = railList?.querySelectorAll("button.tsh-rail-item");
    expect(buttons).toHaveLength(3);

    expect(buttons?.[0].querySelector("svg")).toBeInTheDocument();
    expect(buttons?.[0].querySelector(".tsh-rail-item-text")).not.toBeInTheDocument();

    expect(buttons?.[1].querySelector("svg")).toBeInTheDocument();
    expect(buttons?.[1].querySelector(".tsh-rail-item-text")).not.toBeInTheDocument();

    expect(buttons?.[2].querySelector(".tsh-rail-item-text")).toHaveTextContent("#1");
  });

  it("triggers OnSelectItem when a rail chat item is clicked", () => {
    const onSelect = vi.fn();
    const chatItems: ShellSectionItemDto[] = [
      { id: "chat-1", title: "General Chat" },
    ];

    render(
      <ShellContext.Provider value={{ collapsed: true, toggle: () => {} }}>
        <ShellSidebarSection
          id="sec-1"
          title="Chats"
          items={chatItems}
          events={["OnSelectItem"]}
          eventHandler={onSelect}
        />
      </ShellContext.Provider>,
    );

    const chatBtn = screen.getByRole("button", { name: "General Chat" });
    fireEvent.click(chatBtn);
    expect(onSelect).toHaveBeenCalledWith("OnSelectItem", "sec-1", ["chat-1"]);
  });

  it("displays tooltip with title and badges when hovering over a rail chat item", () => {
    vi.useFakeTimers();
    const chatItems: ShellSectionItemDto[] = [
      {
        id: "chat-1",
        title: "Project Discussion",
        badges: [{ label: "Active", kind: "success" }],
      },
    ];

    render(
      <ShellContext.Provider value={{ collapsed: true, toggle: () => {} }}>
        <ShellSidebarSection
          id="sec-1"
          title="Chats"
          items={chatItems}
          eventHandler={vi.fn()}
        />
      </ShellContext.Provider>,
    );

    const chatBtn = screen.getByRole("button", { name: "Project Discussion" });
    fireEvent.pointerMove(chatBtn);
    act(() => {
      vi.advanceTimersByTime(500);
    });

    const tooltip = screen.getByRole("tooltip");
    expect(tooltip).toBeInTheDocument();
    expect(tooltip).toHaveTextContent("Project Discussion");
    expect(tooltip).toHaveTextContent("Active");
    vi.useRealTimers();
  });
});
