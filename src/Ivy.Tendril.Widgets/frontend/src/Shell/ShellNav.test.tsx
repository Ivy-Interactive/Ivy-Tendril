import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";

import { ShellNav, NAV_HEIGHT_STORAGE_KEY } from "./ShellNav";
import { ShellContext } from "./ShellContext";
import { ShellNavItemDto } from "./types";

const items: ShellNavItemDto[] = [
  { id: "review", label: "Review", icon: "ThumbsUp" },
  { id: "drafts", label: "Drafts", icon: "Feather" },
  { id: "recommendations", label: "Recommendations", icon: "Lightbulb" },
  { id: "activity", label: "Activity", icon: "Activity" },
];

const NAV_CONTENT_HEIGHT = 160;

/** jsdom has no layout: give the nav list a content height and put the sidebar
    body far enough below that the plan-list minimum never clamps the drag. */
const layOut = (container: HTMLElement) => {
  const list = container.querySelector(".tsh-nav-items") as HTMLElement;
  Object.defineProperty(list, "scrollHeight", { configurable: true, value: NAV_CONTENT_HEIGHT });
  list.getBoundingClientRect = () =>
    ({ top: 100, bottom: 100 + NAV_CONTENT_HEIGHT, height: NAV_CONTENT_HEIGHT }) as DOMRect;
  const divider = screen.getByRole("separator") as HTMLElement;
  divider.getBoundingClientRect = () => ({ top: 260, bottom: 280, height: 20 }) as DOMRect;
  return { list, divider };
};

const renderNav = (collapsed = false) => {
  const utils = render(
    <ShellContext.Provider value={{ collapsed, toggle: () => {} }}>
      <div
        className="tsh-sidebar-body"
        ref={(el) => {
          if (el) el.getBoundingClientRect = () => ({ top: 0, bottom: 1000 }) as DOMRect;
        }}
      >
        <ShellNav
          id="nav-1"
          items={items}
          showDivider={true}
          events={["OnSelect"]}
          eventHandler={vi.fn()}
        />
      </div>
    </ShellContext.Provider>,
  );
  return { ...utils, ...layOut(utils.container) };
};

const drag = (divider: HTMLElement, from: number, to: number) => {
  fireEvent.pointerDown(divider, { button: 0, clientY: from, pointerId: 1 });
  fireEvent.pointerMove(divider, { clientY: to, pointerId: 1 });
  fireEvent.pointerUp(divider, { clientY: to, pointerId: 1 });
};

describe("ShellNav divider resizing", () => {
  beforeEach(() => window.localStorage.clear());

  it("caps the nav height when the divider is dragged up and remembers it", () => {
    const { list, divider } = renderNav();
    expect(list.style.maxHeight).toBe("");

    drag(divider, 270, 210);

    expect(list.style.maxHeight).toBe("100px");
    expect(window.localStorage.getItem(NAV_HEIGHT_STORAGE_KEY)).toBe("100");
  });

  it("never shrinks below one row", () => {
    const { list, divider } = renderNav();
    drag(divider, 270, 0);
    expect(list.style.maxHeight).toBe("38px");
  });

  it("drops the cap again once dragged back to the content height", () => {
    window.localStorage.setItem(NAV_HEIGHT_STORAGE_KEY, "100");
    const { list, divider } = renderNav();
    expect(list.style.maxHeight).toBe("100px");
    list.getBoundingClientRect = () => ({ top: 100, bottom: 200, height: 100 }) as DOMRect;

    drag(divider, 210, 400);

    expect(list.style.maxHeight).toBe("");
    expect(window.localStorage.getItem(NAV_HEIGHT_STORAGE_KEY)).toBeNull();
  });

  it("resets to the natural height on double-click", () => {
    window.localStorage.setItem(NAV_HEIGHT_STORAGE_KEY, "80");
    const { list, divider } = renderNav();
    expect(list.style.maxHeight).toBe("80px");

    fireEvent.doubleClick(divider);

    expect(list.style.maxHeight).toBe("");
    expect(window.localStorage.getItem(NAV_HEIGHT_STORAGE_KEY)).toBeNull();
  });

  it("is not draggable in the collapsed rail", () => {
    const { list, divider } = renderNav(true);
    expect(divider).toHaveAttribute("data-resizable", "false");
    drag(divider, 270, 210);
    expect(list.style.maxHeight).toBe("");
  });
});

describe("ShellNav badge rendering", () => {
  const badgeItems: ShellNavItemDto[] = [
    { id: "plans", label: "Plans", icon: "ChartBar", badge: "7", isActive: true },
    { id: "review", label: "Review", icon: "ThumbsUp", badge: "12" },
    { id: "drafts", label: "Drafts", icon: "Feather", badge: "123" },
    { id: "settings", label: "Settings", icon: "Activity" },
  ];

  const renderNavWithBadges = (collapsed = false) => {
    return render(
      <ShellContext.Provider value={{ collapsed, toggle: () => {} }}>
        <ShellNav id="nav-badges" items={badgeItems} events={["OnSelect"]} eventHandler={vi.fn()} />
      </ShellContext.Provider>,
    );
  };

  it("renders navigation items and badges in expanded mode", () => {
    renderNavWithBadges(false);
    expect(screen.getByText("Plans")).toBeInTheDocument();
    expect(screen.getByText("7")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
    expect(screen.getByText("123")).toBeInTheDocument();
  });

  it("renders navigation items and badges in collapsed mode", () => {
    renderNavWithBadges(true);
    expect(screen.getByTitle("Plans")).toBeInTheDocument();
    expect(screen.getByText("7")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
  });

  it("caps badge numbers longer than 2 characters at 99 in collapsed mode", () => {
    renderNavWithBadges(true);
    expect(screen.queryByText("123")).toBeNull();
    expect(screen.getByText("99")).toBeInTheDocument();
  });

  it("passes data-active=true for active items and preserves badge rendering", () => {
    renderNavWithBadges(false);
    const activeItem = screen.getByTitle("Plans");
    expect(activeItem).toHaveAttribute("data-active", "true");
    expect(activeItem.querySelector(".tsh-nav-badge")).toHaveTextContent("7");

    const inactiveItem = screen.getByTitle("Review");
    expect(inactiveItem).toHaveAttribute("data-active", "false");
    expect(inactiveItem.querySelector(".tsh-nav-badge")).toHaveTextContent("12");
  });
});
