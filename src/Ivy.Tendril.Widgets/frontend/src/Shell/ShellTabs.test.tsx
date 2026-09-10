import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";

import { ShellTabs } from "./ShellTabs";
import { ShellTabDto } from "./types";

const pageTab: ShellTabDto = { id: "$page", title: "Review", closable: false, icon: "ThumbsUp" };
const sessionTab: ShellTabDto = { id: "tab-1", title: "#74 Run" };

const renderTabs = (tabs: ShellTabDto[], selectedId?: string) => {
  const eventHandler = vi.fn();
  const utils = render(
    <ShellTabs
      id="tabs-1"
      tabs={tabs}
      selectedId={selectedId}
      events={["OnSelect", "OnClose", "OnNew"]}
      eventHandler={eventHandler}
    />,
  );
  return { ...utils, eventHandler };
};

describe("ShellTabs", () => {
  it("renders nothing when only the page tab exists", () => {
    const { container } = renderTabs([pageTab]);
    expect(container.querySelector(".tsh-tabs")).toBeNull();
  });

  it("shows the page tab beside a session tab", () => {
    renderTabs([pageTab, sessionTab], "tab-1");

    const tabs = screen.getAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual(["Review", "#74 Run"]);
    expect(tabs[0]).toHaveAttribute("aria-selected", "false");
    expect(tabs[1]).toHaveAttribute("aria-selected", "true");
  });

  it("renders the page app's own icon, and the terminal glyph for sessions", () => {
    const { container } = renderTabs([pageTab, sessionTab], "$page");

    const icons = container.querySelectorAll(".tsh-tab-icon");
    expect(icons[0]).toHaveClass("lucide-thumbs-up");
    expect(icons[1]).toHaveClass("lucide-square-terminal");
  });

  it("falls back to a neutral page glyph, not the terminal one, for an unmapped page icon", () => {
    const { container } = renderTabs([{ ...pageTab, icon: "NotARealIcon" }, sessionTab], "$page");

    const icon = container.querySelector(".tsh-tab-icon");
    expect(icon).toHaveClass("lucide-file");
    expect(icon).not.toHaveClass("lucide-square-terminal");
  });

  it("renders with nothing selected while a terminal pane is active", () => {
    // Terminal panes are reached from the Chats list and are filtered out of the strip, so
    // the server sends no selected id: the page tab must not claim to be current.
    const { container } = renderTabs([pageTab, sessionTab], undefined);

    expect(container.querySelector(".tsh-tabs")).not.toBeNull();
    expect(screen.getAllByRole("tab").every((t) => t.getAttribute("aria-selected") === "false")).toBe(
      true,
    );
  });

  it("selects the page tab on click, so a session no longer traps the user", () => {
    const { eventHandler } = renderTabs([pageTab, sessionTab], "tab-1");

    fireEvent.click(screen.getByText("Review"));

    expect(eventHandler).toHaveBeenCalledWith("OnSelect", "tabs-1", ["$page"]);
  });

  it("marks the page tab active when no session is selected", () => {
    renderTabs([pageTab, sessionTab], "$page");

    expect(screen.getAllByRole("tab")[0]).toHaveAttribute("aria-selected", "true");
  });

  it("gives the page tab no close button and ignores middle-click on it", () => {
    const { eventHandler } = renderTabs([pageTab, sessionTab], "$page");

    expect(screen.queryByLabelText("Close Review")).toBeNull();
    expect(screen.getByLabelText("Close #74 Run")).toBeInTheDocument();

    fireEvent(
      screen.getAllByRole("tab")[0],
      new MouseEvent("auxclick", { bubbles: true, button: 1 }),
    );
    expect(eventHandler).not.toHaveBeenCalledWith("OnClose", "tabs-1", ["$page"]);
  });

  it("closes a session tab from its close button without selecting it", () => {
    const { eventHandler } = renderTabs([pageTab, sessionTab], "$page");

    fireEvent.click(screen.getByLabelText("Close #74 Run"));

    expect(eventHandler).toHaveBeenCalledWith("OnClose", "tabs-1", ["tab-1"]);
    expect(eventHandler).not.toHaveBeenCalledWith("OnSelect", "tabs-1", ["tab-1"]);
  });
});
