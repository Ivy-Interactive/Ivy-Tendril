import "./ChatWidget.testUtils";
import { render, screen, fireEvent } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import "@testing-library/jest-dom";
import { ChatWidget } from "./ChatWidget";
import { setupChatWidgetTestEnvironment } from "./ChatWidget.testUtils";

describe("ChatWidget Queued Messages UI", () => {
  beforeEach(() => {
    setupChatWidgetTestEnvironment();
  });

  it("renders queued messages panel when isStreaming and user queues messages", () => {
    const handleEvent = vi.fn();
    render(
      <ChatWidget
        id="test-chat"
        isStreaming={true}
        events={["OnSendMessage"]}
        eventHandler={handleEvent}
      />,
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
      />,
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
      expect.arrayContaining([expect.objectContaining({ prompt: "message to send now" })]),
    );
    expect(screen.queryByText("Queued Messages")).not.toBeInTheDocument();
  });

  it("renders queued messages passed via props upon mount and handles session switches", () => {
    const queuedItems = [
      { id: "q-1", prompt: "persisted queued prompt 1" },
      { id: "q-2", prompt: "persisted queued prompt 2" },
    ];

    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-1"
        isStreaming={true}
        queuedMessages={queuedItems}
      />,
    );

    expect(screen.getByText("Queued Messages")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
    expect(screen.getByText("persisted queued prompt 1")).toBeInTheDocument();
    expect(screen.getByText("persisted queued prompt 2")).toBeInTheDocument();

    // Rerender with empty queue (e.g. switched to another session)
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-2"
        isStreaming={false}
        queuedMessages={[]}
      />,
    );

    expect(screen.queryByText("Queued Messages")).not.toBeInTheDocument();
    expect(screen.queryByText("persisted queued prompt 1")).not.toBeInTheDocument();
  });

  it("emits backend sync events OnDeleteQueuedMessage, OnUpdateQueuedMessage, and OnSendQueuedNow", () => {
    const handleEvent = vi.fn();
    const queuedItems = [
      { id: "q-edit", prompt: "to be edited" },
      { id: "q-del", prompt: "to be deleted" },
      { id: "q-send", prompt: "to be sent now" },
    ];

    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-1"
        sessions={[
          {
            id: "sess-1",
            title: "Session 1",
            agentId: "claude",
            modelId: "sonnet",
            createdAt: new Date().toISOString(),
            updatedAt: new Date().toISOString(),
            messages: [],
          },
        ]}
        isStreaming={true}
        queuedMessages={queuedItems}
        events={["OnDeleteQueuedMessage", "OnUpdateQueuedMessage", "OnSendQueuedNow"]}
        eventHandler={handleEvent}
      />,
    );

    // Edit item
    const editBtns = screen.getAllByRole("button", { name: /Edit message/i });
    fireEvent.click(editBtns[0]);
    const editInput = screen.getByDisplayValue("to be edited");
    fireEvent.change(editInput, { target: { value: "edited content" } });
    const saveBtn = screen.getByRole("button", { name: /Save/i });
    fireEvent.click(saveBtn);

    expect(handleEvent).toHaveBeenCalledWith("OnUpdateQueuedMessage", "test-chat", [
      ["q-edit", "edited content"],
    ]);

    // Delete item
    const deleteBtns = screen.getAllByRole("button", { name: /Delete message/i });
    fireEvent.click(deleteBtns[1]);

    expect(handleEvent).toHaveBeenCalledWith("OnDeleteQueuedMessage", "test-chat", ["q-del"]);

    // Send item now
    const sendNowBtns = screen.getAllByRole("button", { name: /Send now/i });
    fireEvent.click(sendNowBtns[1]);

    expect(handleEvent).toHaveBeenCalledWith("OnSendQueuedNow", "test-chat", ["q-send"]);
    expect(screen.getByText("to be sent now")).toBeInTheDocument();
  });

  it("preserves optimistically queued message when in-flight queuedMessages prop is empty", () => {
    const handleEvent = vi.fn();
    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        isStreaming={true}
        activeSessionId="sess-1"
        queuedMessages={[]}
        events={["OnSendMessage"]}
        eventHandler={handleEvent}
      />,
    );

    const textarea = screen.getByPlaceholderText(/Ask/i);
    const queueBtn = screen.getByRole("button", { name: /Queue/i });

    fireEvent.change(textarea, { target: { value: "optimistic prompt" } });
    fireEvent.click(queueBtn);

    expect(screen.getByText("Queued Messages")).toBeInTheDocument();
    expect(screen.getByText("optimistic prompt")).toBeInTheDocument();

    // Simulate in-flight SignalR update where server hasn't yet included the queued message
    rerender(
      <ChatWidget
        id="test-chat"
        isStreaming={true}
        activeSessionId="sess-1"
        queuedMessages={[]}
        events={["OnSendMessage"]}
        eventHandler={handleEvent}
      />,
    );

    // Should still be visible optimistically!
    expect(screen.getByText("Queued Messages")).toBeInTheDocument();
    expect(screen.getByText("optimistic prompt")).toBeInTheDocument();

    // When server confirms the queued message
    rerender(
      <ChatWidget
        id="test-chat"
        isStreaming={true}
        activeSessionId="sess-1"
        queuedMessages={[{ id: "guid-server-123", prompt: "optimistic prompt" }]}
        events={["OnSendMessage"]}
        eventHandler={handleEvent}
      />,
    );

    expect(screen.getByText("Queued Messages")).toBeInTheDocument();
    expect(screen.getByText("optimistic prompt")).toBeInTheDocument();
  });

  it("does not drop queued message when its content matches an existing historical message", () => {
    const handleEvent = vi.fn();
    const session = {
      id: "sess-repeat",
      title: "Repeat Test",
      agentId: "claude",
      modelId: "opus",
      createdAt: "2026-08-15T12:00:00Z",
      updatedAt: "2026-08-15T12:30:00Z",
      messages: [
        { id: "m-1", role: "user" as const, content: "what can you do?", timestamp: "10:00" },
      ],
      status: "generating" as const,
    };

    render(
      <ChatWidget
        id="test-chat"
        isStreaming={true}
        activeSessionId="sess-repeat"
        sessions={[session]}
        queuedMessages={[]}
        events={["OnSendMessage"]}
        eventHandler={handleEvent}
      />,
    );

    const textarea = screen.getByPlaceholderText(/Ask/i);
    const queueBtn = screen.getByRole("button", { name: /Queue/i });

    fireEvent.change(textarea, { target: { value: "what can you do?" } });
    fireEvent.click(queueBtn);

    expect(screen.getByText("Queued Messages")).toBeInTheDocument();
    // Verify it is inside the queued panel
    const queuedItem = document.querySelector(".chat-queued-item-text");
    expect(queuedItem).toHaveTextContent("what can you do?");
  });
});
