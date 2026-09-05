import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
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
});
