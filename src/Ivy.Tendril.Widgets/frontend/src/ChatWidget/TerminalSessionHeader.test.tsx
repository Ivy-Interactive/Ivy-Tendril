import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";

import { TerminalSessionHeader } from "./TerminalSessionHeader";

describe("TerminalSessionHeader", () => {
  it("renames the session and emits OnRenameSession with one array argument", () => {
    const handleEvent = vi.fn();
    render(
      <TerminalSessionHeader
        id="term-header"
        sessionId="sess-1"
        title="My Session"
        events={["OnRenameSession"]}
        eventHandler={handleEvent}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /Chat options/i }));
    fireEvent.click(screen.getByRole("menuitem", { name: /Edit name/i }));

    const input = screen.getByRole("textbox", { name: /Chat name/i });
    fireEvent.change(input, { target: { value: "Renamed Session" } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(handleEvent).toHaveBeenCalledWith("OnRenameSession", "term-header", [["sess-1", "Renamed Session"]]);
  });

  it("deletes the session and emits OnDeleteSession", () => {
    const handleEvent = vi.fn();
    render(
      <TerminalSessionHeader
        id="term-header"
        sessionId="sess-2"
        title="My Session"
        events={["OnDeleteSession"]}
        eventHandler={handleEvent}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /Chat options/i }));
    fireEvent.click(screen.getByRole("menuitem", { name: /Delete chat/i }));

    expect(handleEvent).toHaveBeenCalledWith("OnDeleteSession", "term-header", ["sess-2"]);
  });

  it("emits OnCreateSession from the new chat button", () => {
    const handleEvent = vi.fn();
    render(
      <TerminalSessionHeader
        id="term-header"
        sessionId="sess-3"
        events={["OnCreateSession"]}
        eventHandler={handleEvent}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /New chat/i }));
    expect(handleEvent).toHaveBeenCalledWith("OnCreateSession", "term-header", []);
  });

  it("renders the terminal variant host and header classes", () => {
    const { container } = render(<TerminalSessionHeader id="term-header" sessionId="sess-4" />);
    expect(container.querySelector(".chat-header-host.chat-header-host--terminal")).toBeInTheDocument();
    expect(container.querySelector(".chat-header.chat-header--terminal")).toBeInTheDocument();
  });
});
