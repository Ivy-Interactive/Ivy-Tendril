import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom";

import { ShellRailList } from "./ShellRailList";
import { ShellSectionItemDto } from "./types";

const items: ShellSectionItemDto[] = [
  { id: "a", title: "Plan A", tag: "#1", badges: [{ label: "Draft", kind: "neutral" }] },
  { id: "b", title: "Plan B", tag: "#2" },
  { id: "c", title: "Plan C", tag: "#3" },
];

const renderRailList = (props: Partial<React.ComponentProps<typeof ShellRailList>> = {}) => {
  const onSelect = props.onSelect ?? vi.fn();
  const utils = render(
    <ShellRailList title="Plans" items={items} onSelect={onSelect} {...props} />,
  );
  return { ...utils, onSelect, toggle: screen.getByRole("button", { name: /^Show/ }) };
};

const hover = (element: HTMLElement) => {
  vi.useFakeTimers();
  fireEvent.pointerEnter(element);
  act(() => {
    vi.advanceTimersByTime(200);
  });
  vi.useRealTimers();
};

describe("ShellRailList", () => {
  afterEach(() => vi.useRealTimers());

  it("opens the flyout on hover and closes it again when the pointer leaves", () => {
    const { toggle } = renderRailList();
    expect(screen.queryByText("Plan A")).not.toBeInTheDocument();

    hover(toggle);
    expect(screen.getByText("Plan A")).toBeInTheDocument();

    vi.useFakeTimers();
    fireEvent.pointerLeave(toggle);
    act(() => {
      vi.advanceTimersByTime(300);
    });
    vi.useRealTimers();

    expect(screen.queryByText("Plan A")).not.toBeInTheDocument();
  });

  it("pins the flyout on click so it survives the pointer leaving", () => {
    const { toggle } = renderRailList();

    fireEvent.click(toggle);
    expect(screen.getByText("Plan A")).toBeInTheDocument();

    vi.useFakeTimers();
    fireEvent.pointerLeave(toggle);
    act(() => {
      vi.advanceTimersByTime(500);
    });
    vi.useRealTimers();

    expect(screen.getByText("Plan A")).toBeInTheDocument();
  });

  it("shows the close button only while pinned and closes on click", () => {
    const { toggle } = renderRailList();

    hover(toggle);
    expect(screen.queryByRole("button", { name: "Close" })).not.toBeInTheDocument();

    fireEvent.click(toggle);
    fireEvent.click(screen.getByRole("button", { name: "Close" }));

    expect(screen.queryByText("Plan A")).not.toBeInTheDocument();
  });

  it("closes a pinned flyout on Escape and on an outside click", () => {
    const { toggle } = renderRailList();

    fireEvent.click(toggle);
    fireEvent.keyDown(window, { key: "Escape" });
    expect(screen.queryByText("Plan A")).not.toBeInTheDocument();

    fireEvent.click(toggle);
    expect(screen.getByText("Plan A")).toBeInTheDocument();
    fireEvent.pointerDown(document.body);
    expect(screen.queryByText("Plan A")).not.toBeInTheDocument();
  });

  it("steps the selection through the list with the bar arrows", () => {
    const onSelect = vi.fn();
    const { toggle } = renderRailList({ selectedId: "b", onSelect });
    fireEvent.click(toggle);

    fireEvent.click(screen.getByRole("button", { name: "Previous" }));
    expect(onSelect).toHaveBeenLastCalledWith("a");

    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(onSelect).toHaveBeenLastCalledWith("c");
  });

  it("disables the arrow that would run past either end of the list", () => {
    const { toggle, rerender } = renderRailList({ selectedId: "a" });
    fireEvent.click(toggle);

    expect(screen.getByRole("button", { name: "Previous" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Next" })).toBeEnabled();

    rerender(<ShellRailList title="Plans" items={items} selectedId="c" onSelect={vi.fn()} />);

    expect(screen.getByRole("button", { name: "Previous" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();
  });

  it("closes after a pick while hovering, but stays open while pinned", () => {
    const onSelect = vi.fn();
    const { toggle } = renderRailList({ onSelect });

    hover(toggle);
    fireEvent.click(screen.getByText("Plan B"));
    expect(onSelect).toHaveBeenCalledWith("b");
    expect(screen.queryByText("Plan A")).not.toBeInTheDocument();

    fireEvent.click(toggle);
    fireEvent.click(screen.getByText("Plan B"));
    expect(screen.getByText("Plan A")).toBeInTheDocument();
  });

  it("counts the rows in a pill on the button", () => {
    const { container } = renderRailList();
    expect(container.querySelector(".tsh-rail-list-count")).toHaveTextContent("3");
  });

  it("keeps the item badges in the flyout", () => {
    const { toggle } = renderRailList();
    hover(toggle);
    expect(screen.getByText("Draft")).toBeInTheDocument();
  });
});
