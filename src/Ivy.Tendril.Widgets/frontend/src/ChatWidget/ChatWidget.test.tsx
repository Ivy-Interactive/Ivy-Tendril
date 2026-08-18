import { render, screen, fireEvent } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import "@testing-library/jest-dom";
import { ChatWidget } from "./ChatWidget";

describe("ChatWidget Queued Messages UI", () => {
  beforeEach(() => {
    window.ResizeObserver = class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    } as any;
    window.HTMLElement.prototype.scrollIntoView = vi.fn();
  });

  it("renders queued messages panel when isStreaming and user queues messages", () => {
    const handleEvent = vi.fn();
    render(
      <ChatWidget
        id="test-chat"
        isStreaming={true}
        events={["OnSendMessage"]}
        eventHandler={handleEvent}
      />
    );

    const textarea = screen.getByPlaceholderText(/Ask/i);
    const queueBtn = screen.getByRole("button", { name: /Queue/i });

    // Queue first message
    fireEvent.change(textarea, { target: { value: "test message 1" } });
    fireEvent.click(queueBtn);

    // Queue second message
    fireEvent.change(textarea, { target: { value: "test message 2" } });
    fireEvent.click(queueBtn);

    expect(screen.getByText("Queued Messages")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
    expect(screen.getByText("Sends after agent finishes working")).toBeInTheDocument();

    expect(screen.getByText("test message 1")).toBeInTheDocument();
    expect(screen.getByText("test message 2")).toBeInTheDocument();
  });

  it("allows collapsing and expanding the queued messages list", () => {
    render(<ChatWidget id="test-chat" isStreaming={true} />);

    const textarea = screen.getByPlaceholderText(/Ask/i);
    const queueBtn = screen.getByRole("button", { name: /Queue/i });

    fireEvent.change(textarea, { target: { value: "queued task" } });
    fireEvent.click(queueBtn);

    const toggleBtn = screen.getByRole("button", { name: /Collapse queued messages/i });
    fireEvent.click(toggleBtn);

    expect(screen.queryByText("queued task")).not.toBeInTheDocument();

    const expandBtn = screen.getByRole("button", { name: /Expand queued messages/i });
    fireEvent.click(expandBtn);

    expect(screen.getByText("queued task")).toBeInTheDocument();
  });

  it("supports editing a queued message", () => {
    render(<ChatWidget id="test-chat" isStreaming={true} />);

    const textarea = screen.getByPlaceholderText(/Ask/i);
    const queueBtn = screen.getByRole("button", { name: /Queue/i });

    fireEvent.change(textarea, { target: { value: "original prompt" } });
    fireEvent.click(queueBtn);

    const editBtn = screen.getByRole("button", { name: /Edit message/i });
    fireEvent.click(editBtn);

    const editInput = screen.getByDisplayValue("original prompt");
    fireEvent.change(editInput, { target: { value: "updated prompt" } });

    const saveBtn = screen.getByRole("button", { name: /Save/i });
    fireEvent.click(saveBtn);

    expect(screen.getByText("updated prompt")).toBeInTheDocument();
    expect(screen.queryByText("original prompt")).not.toBeInTheDocument();
  });

  it("supports sending a queued message immediately and deleting a queued message", () => {
    const handleEvent = vi.fn();
    render(
      <ChatWidget
        id="test-chat"
        isStreaming={true}
        events={["OnSendMessage"]}
        eventHandler={handleEvent}
      />
    );

    const textarea = screen.getByPlaceholderText(/Ask/i);
    const queueBtn = screen.getByRole("button", { name: /Queue/i });

    fireEvent.change(textarea, { target: { value: "message to send now" } });
    fireEvent.click(queueBtn);

    fireEvent.change(textarea, { target: { value: "message to delete" } });
    fireEvent.click(queueBtn);

    expect(screen.getByText("2")).toBeInTheDocument();

    // Delete second message
    const deleteBtns = screen.getAllByRole("button", { name: /Delete message/i });
    fireEvent.click(deleteBtns[1]);

    expect(screen.queryByText("message to delete")).not.toBeInTheDocument();
    expect(screen.getByText("1")).toBeInTheDocument();

    // Send first message now
    const sendNowBtn = screen.getByRole("button", { name: /Send now/i });
    fireEvent.click(sendNowBtn);

    expect(handleEvent).toHaveBeenCalledWith(
      "OnSendMessage",
      "test-chat",
      expect.arrayContaining([expect.objectContaining({ prompt: "message to send now" })])
    );
    expect(screen.queryByText("Queued Messages")).not.toBeInTheDocument();
  });

  it("renders delete button next to chat title and emits OnDeleteSession", () => {
    const handleEvent = vi.fn();
    const session = {
      id: "sess-123",
      title: "My Great Chat",
      agentId: "antigravity",
      modelId: "gemini-3.7-flash",
      createdAt: "2026-08-15T12:00:00Z",
      updatedAt: "2026-08-15T12:30:00Z",
      messages: [],
    };

    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-123"
        sessions={[session]}
        events={["OnDeleteSession"]}
        eventHandler={handleEvent}
      />
    );

    const deleteBtn = screen.getByRole("button", { name: /Delete chat session/i });
    expect(deleteBtn).toBeInTheDocument();

    fireEvent.click(deleteBtn);

    expect(handleEvent).toHaveBeenCalledWith(
      "OnDeleteSession",
      "test-chat",
      ["sess-123"]
    );
  });

  it("renders effort picker and emits OnEffortChanged when changed", () => {
    const handleEvent = vi.fn();
    const efforts = [
      { id: "default", displayName: "Default" },
      { id: "low", displayName: "Low" },
      { id: "high", displayName: "High" },
      { id: "max", displayName: "Max" },
    ];

    render(
      <ChatWidget
        id="test-chat"
        efforts={efforts}
        selectedEffort="high"
        supportsEffort={true}
        events={["OnEffortChanged"]}
        eventHandler={handleEvent}
      />
    );

    const effortTrigger = screen.getByTitle("Effort Level");
    expect(effortTrigger).toBeInTheDocument();
    expect(screen.getByText("High")).toBeInTheDocument();

    fireEvent.click(effortTrigger.querySelector("button")!);
    const maxOption = screen.getByRole("button", { name: /Max/i });
    fireEvent.click(maxOption);

    expect(handleEvent).toHaveBeenCalledWith(
      "OnEffortChanged",
      "test-chat",
      ["max"]
    );
  });
});
