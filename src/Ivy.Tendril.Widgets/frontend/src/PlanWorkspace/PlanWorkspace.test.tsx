import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom";
import { PlanWorkspace } from "./PlanWorkspace";
import { CHAT_WIDTH_STORAGE_KEY } from "./chatWidth";
import { matchesShortcut, shortcutKeys } from "./shortcuts";

const actions = [
  { tag: "Edit", label: "Edit", icon: "Pencil", shortcut: "E" },
  { tag: "Chat", label: "Update", icon: "WandSparkles", shortcut: "U", active: true, focusChat: true },
];
const menuItems = [{ tag: "Delete", label: "Delete", icon: "Trash", shortcut: "Backspace", danger: true }];
const primary = { tag: "Execute", label: "Execute", icon: "Rocket", shortcut: "x" };
const tabs = [
  { id: "plan", label: "Plan" },
  { id: "details", label: "Details", badge: "3" },
];

const renderWorkspace = (handler = vi.fn(), extra: Record<string, unknown> = {}) =>
  render(
    <PlanWorkspace
      id="w"
      planId="#59"
      title="Revamp the User Authentication Experience"
      meta="1/32 plans"
      actions={actions}
      menuItems={menuItems}
      primary={primary}
      tabs={tabs}
      selectedTab="plan"
      events={["OnAction", "OnTabSelect"]}
      eventHandler={handler}
      slots={{
        Content: [<div key="c">plan body</div>],
        Verifications: [<div key="v">verification rows</div>],
        Questions: [<div key="q">question rows</div>],
        ...(extra.slots as object),
      }}
      {...extra}
    />,
  );

describe("PlanWorkspace", () => {
  beforeEach(() => {
    window.ResizeObserver = class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    } as unknown as typeof ResizeObserver;
    window.localStorage.removeItem(CHAT_WIDTH_STORAGE_KEY);
  });

  afterEach(() => {
    window.localStorage.removeItem(CHAT_WIDTH_STORAGE_KEY);
  });

  it("renders the title bar, tabs and the selected tab's content", () => {
    renderWorkspace();
    expect(screen.getByText("#59")).toBeInTheDocument();
    expect(screen.getByText("Revamp the User Authentication Experience")).toBeInTheDocument();
    expect(screen.getByText("1/32 plans")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /Plan/ })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: /Details/ })).toHaveAttribute("aria-selected", "false");
    expect(screen.getByText("plan body")).toBeInTheDocument();
  });

  it("fires OnTabSelect with the tab id", () => {
    const handler = vi.fn();
    renderWorkspace(handler);
    fireEvent.click(screen.getByRole("tab", { name: /Details/ }));
    expect(handler).toHaveBeenCalledWith("OnTabSelect", "w", ["details"]);
  });

  it("fires OnAction for icon buttons, the primary button and menu items", () => {
    const handler = vi.fn();
    renderWorkspace(handler);
    fireEvent.click(screen.getByRole("button", { name: "Edit" }));
    expect(handler).toHaveBeenCalledWith("OnAction", "w", ["Edit"]);

    fireEvent.click(screen.getByRole("button", { name: /Execute/ }));
    expect(handler).toHaveBeenCalledWith("OnAction", "w", ["Execute"]);

    fireEvent.click(screen.getByRole("button", { name: "More actions" }));
    fireEvent.click(screen.getByRole("menuitem", { name: /Delete/ }));
    expect(handler).toHaveBeenCalledWith("OnAction", "w", ["Delete"]);
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
  });

  it("marks the active icon action as pressed", () => {
    renderWorkspace();
    expect(screen.getByRole("button", { name: "Update" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Edit" })).not.toHaveAttribute("aria-pressed");
  });

  it("binds shortcuts on the document but not while typing", () => {
    const handler = vi.fn();
    renderWorkspace(handler, { slots: { Content: [<textarea key="t" aria-label="editor" />] } });

    fireEvent.keyDown(document.body, { key: "x" });
    expect(handler).toHaveBeenCalledWith("OnAction", "w", ["Execute"]);

    fireEvent.keyDown(document.body, { key: "Backspace" });
    expect(handler).toHaveBeenCalledWith("OnAction", "w", ["Delete"]);

    handler.mockClear();
    fireEvent.keyDown(screen.getByLabelText("editor"), { key: "x" });
    fireEvent.keyDown(document.body, { key: "x", metaKey: true });
    expect(handler).not.toHaveBeenCalled();
  });

  it("opens the verifications and questions dropdowns from the tab strip corner", () => {
    renderWorkspace();
    expect(screen.queryByText("verification rows")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Verifications" }));
    expect(screen.getByText("verification rows")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Questions" }));
    expect(screen.queryByText("verification rows")).not.toBeInTheDocument();
    expect(screen.getByText("question rows")).toBeInTheDocument();

    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByText("question rows")).not.toBeInTheDocument();
  });

  it("marks the Questions icon until its dropdown has been opened for the plan", () => {
    renderWorkspace(vi.fn(), { unansweredQuestions: 2, planId: "#77" });
    const questionsButton = () => screen.getByRole("button", { name: /Questions/ });
    expect(questionsButton()).toHaveAttribute("data-indicator", "true");

    fireEvent.click(questionsButton());
    expect(questionsButton()).toHaveAttribute("data-indicator", "false");

    fireEvent.keyDown(document, { key: "Escape" });
    expect(questionsButton()).toHaveAttribute("data-indicator", "false");
  });

  it("shows no indicator when every question is answered", () => {
    renderWorkspace(vi.fn(), { unansweredQuestions: 0, planId: "#78" });
    expect(screen.getByRole("button", { name: /Questions/ })).toHaveAttribute("data-indicator", "false");
  });

  it("hides the dropdown icons when their slots are empty", () => {
    renderWorkspace(vi.fn(), { slots: { Content: [<div key="c" />], Verifications: [], Questions: undefined } });
    expect(screen.queryByRole("button", { name: "Verifications" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Questions" })).not.toBeInTheDocument();
  });

  it("shows the chat panel whenever a Chat slot exists", () => {
    const { rerender } = renderWorkspace();
    expect(screen.queryByLabelText("Plan chat")).not.toBeInTheDocument();

    rerender(<PlanWorkspace id="w" slots={{ Chat: [<div key="chat">chat body</div>] }} />);
    expect(screen.getByLabelText("Plan chat")).toBeInTheDocument();
    expect(screen.getByRole("separator", { name: "Resize chat" })).toBeInTheDocument();
  });

  it("puts the caret in the chat composer for a focusChat action and its shortcut", () => {
    const handler = vi.fn();
    renderWorkspace(handler, { slots: { Chat: [<textarea key="c" aria-label="composer" />] } });
    const composer = screen.getByLabelText("composer");

    fireEvent.click(screen.getByRole("button", { name: "Update" }));
    expect(composer).toHaveFocus();
    expect(handler).toHaveBeenCalledWith("OnAction", "w", ["Chat"]);

    composer.blur();
    fireEvent.keyDown(document.body, { key: "u" });
    expect(composer).toHaveFocus();
  });

  it("resizes the chat by dragging the divider and remembers the width", () => {
    const { container } = render(
      <PlanWorkspace id="w" chatWidth={420} slots={{ Chat: [<div key="chat">chat body</div>] }} />,
    );
    const root = container.querySelector(".pws-root") as HTMLElement;
    root.getBoundingClientRect = () =>
      ({ left: 0, right: 1200, top: 0, bottom: 800, width: 1200, height: 800, x: 0, y: 0, toJSON: () => ({}) }) as DOMRect;

    const handle = screen.getByRole("separator", { name: "Resize chat" });
    fireEvent.pointerDown(handle, { button: 0, clientX: 780 });
    fireEvent.pointerMove(window, { clientX: 700 });
    fireEvent.pointerUp(window, { clientX: 700 });

    expect(root.style.getPropertyValue("--pws-chat-width")).toBe("500px");
    expect(window.localStorage.getItem(CHAT_WIDTH_STORAGE_KEY)).toBe("500");

    fireEvent.doubleClick(handle);
    expect(root.style.getPropertyValue("--pws-chat-width")).toBe("420px");
    expect(window.localStorage.getItem(CHAT_WIDTH_STORAGE_KEY)).toBeNull();
  });

  it("never lets the chat grow past its share of the workspace", () => {
    const { container } = render(<PlanWorkspace id="w" slots={{ Chat: [<div key="chat">chat body</div>] }} />);
    const root = container.querySelector(".pws-root") as HTMLElement;
    root.getBoundingClientRect = () =>
      ({ left: 0, right: 1000, top: 0, bottom: 800, width: 1000, height: 800, x: 0, y: 0, toJSON: () => ({}) }) as DOMRect;

    const handle = screen.getByRole("separator", { name: "Resize chat" });
    fireEvent.pointerDown(handle, { button: 0, clientX: 580 });
    fireEvent.pointerMove(window, { clientX: 100 });
    fireEvent.pointerUp(window, { clientX: 100 });
    expect(root.style.getPropertyValue("--pws-chat-width")).toBe("600px");
  });

  it("shows the reviewer persona in share mode", () => {
    renderWorkspace(vi.fn(), { persona: "Curious Otter", personaInitials: "CO" });
    expect(screen.getByText("Curious Otter")).toBeInTheDocument();
    expect(screen.getByText("CO")).toBeInTheDocument();
  });
});

describe("shortcuts", () => {
  it("formats keys for display", () => {
    expect(shortcutKeys("E")).toEqual(["E"]);
    expect(shortcutKeys("Backspace")).toEqual(["⌫"]);
    expect(shortcutKeys("Ctrl+Enter")).toEqual(["Ctrl", "↵"]);
  });

  it("matches plain keys case-insensitively and rejects extra modifiers", () => {
    const key = (init: KeyboardEventInit) => new KeyboardEvent("keydown", init);
    expect(matchesShortcut(key({ key: "E" }), "e")).toBe(true);
    expect(matchesShortcut(key({ key: "e", ctrlKey: true }), "e")).toBe(false);
    expect(matchesShortcut(key({ key: "Enter", ctrlKey: true }), "Ctrl+Enter")).toBe(true);
    expect(matchesShortcut(key({ key: "Enter" }), "Ctrl+Enter")).toBe(false);
    expect(matchesShortcut(key({ key: "Backspace" }), "Backspace")).toBe(true);
  });
});
