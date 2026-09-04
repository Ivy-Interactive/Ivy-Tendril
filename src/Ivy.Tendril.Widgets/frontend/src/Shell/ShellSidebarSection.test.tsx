import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom";

import { ShellSidebarSection } from "./ShellSidebarSection";
import { ShellContext } from "./ShellContext";
import { ShellSectionItemDto } from "./types";

const mockItems: ShellSectionItemDto[] = [
  { id: "00001-PlanA", title: "Plan A", tag: "#1" },
  { id: "00002-PlanB", title: "Plan B", tag: "#2" },
];

const actionItems: ShellSectionItemDto[] = [
  {
    id: "00001-PlanA",
    title: "Plan A",
    tag: "#1",
    badges: [{ label: "Ivy", kind: "project" }],
    actions: [{ id: "execute", label: "Execute", icon: "Rocket", primary: true }],
  },
  { id: "00002-PlanB", title: "Plan B", tag: "#2" },
];

const renderRail = (items: ShellSectionItemDto[], eventHandler = vi.fn(), events = ["OnItemAction"]) =>
  render(
    <ShellContext.Provider value={{ collapsed: true, toggle: () => {} }}>
      <ShellSidebarSection
        id="sec-1"
        title="Plans"
        items={items}
        searchable={true}
        events={events}
        eventHandler={eventHandler}
      />
    </ShellContext.Provider>,
  );

describe("ShellSidebarSection", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

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

    const reviewItems: ShellSectionItemDto[] = [
      { id: "00003-PlanC", title: "Plan C", tag: "#3" },
    ];

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

  it("renders collapsed rail view with search button", () => {
    const onSearch = vi.fn();
    renderRail(mockItems, onSearch, ["OnSearch"]);

    const railSearchBtn = screen.getByRole("button", { name: /search plans/i });
    expect(railSearchBtn).toBeInTheDocument();
    expect(railSearchBtn).toHaveClass("tsh-rail-search");

    fireEvent.click(railSearchBtn);
    expect(onSearch).toHaveBeenCalledWith("OnSearch", "sec-1", []);
  });

  it("shows title, badges, and action buttons in the rail flyout", () => {
    renderRail(actionItems);

    fireEvent.mouseEnter(screen.getByRole("button", { name: "#1" }));

    const flyout = screen.getByRole("tooltip");
    expect(flyout).toHaveTextContent("Plan A");
    expect(flyout).toHaveTextContent("Ivy");
    const executeBtn = screen.getByRole("button", { name: /execute/i });
    expect(executeBtn).toHaveAttribute("data-primary", "true");
  });

  it("emits OnItemAction with the item and action ids and closes the flyout", () => {
    const eventHandler = vi.fn();
    renderRail(actionItems, eventHandler);

    fireEvent.mouseEnter(screen.getByRole("button", { name: "#1" }));
    fireEvent.click(screen.getByRole("button", { name: /execute/i }));

    expect(eventHandler).toHaveBeenCalledWith("OnItemAction", "sec-1", [
      { itemId: "00001-PlanA", actionId: "execute" },
    ]);
    expect(screen.queryByRole("tooltip")).toBeNull();
  });

  it("renders no action buttons for items without actions", () => {
    renderRail(actionItems);

    fireEvent.mouseEnter(screen.getByRole("button", { name: "#2" }));

    expect(screen.getByRole("tooltip")).toHaveTextContent("Plan B");
    expect(screen.queryByRole("button", { name: /execute/i })).toBeNull();
  });

  it("keeps the flyout open while the pointer moves into it", () => {
    vi.useFakeTimers();
    renderRail(actionItems);

    const chip = screen.getByRole("button", { name: "#1" });
    fireEvent.mouseEnter(chip);
    fireEvent.mouseLeave(chip);
    fireEvent.mouseEnter(screen.getByRole("tooltip"));
    act(() => {
      vi.advanceTimersByTime(1000);
    });

    expect(screen.getByRole("tooltip")).toBeInTheDocument();

    fireEvent.mouseLeave(screen.getByRole("tooltip"));
    act(() => {
      vi.advanceTimersByTime(1000);
    });

    expect(screen.queryByRole("tooltip")).toBeNull();
  });
});
