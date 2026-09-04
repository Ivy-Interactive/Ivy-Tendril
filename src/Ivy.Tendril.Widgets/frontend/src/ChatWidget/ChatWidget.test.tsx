import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import "@testing-library/jest-dom";

vi.mock("pdfjs-dist", () => ({ GlobalWorkerOptions: {}, getDocument: vi.fn() }));
vi.mock("pdfjs-dist/build/pdf.worker.mjs?url", () => ({ default: "" }));

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
      />
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
      />
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
        isStreaming={true}
        queuedMessages={queuedItems}
        events={["OnDeleteQueuedMessage", "OnUpdateQueuedMessage", "OnSendQueuedNow"]}
        eventHandler={handleEvent}
      />
    );

    // Edit item
    const editBtns = screen.getAllByRole("button", { name: /Edit message/i });
    fireEvent.click(editBtns[0]);
    const editInput = screen.getByDisplayValue("to be edited");
    fireEvent.change(editInput, { target: { value: "edited content" } });
    const saveBtn = screen.getByRole("button", { name: /Save/i });
    fireEvent.click(saveBtn);

    expect(handleEvent).toHaveBeenCalledWith(
      "OnUpdateQueuedMessage",
      "test-chat",
      ["q-edit", "edited content"]
    );

    // Delete item
    const deleteBtns = screen.getAllByRole("button", { name: /Delete message/i });
    fireEvent.click(deleteBtns[1]);

    expect(handleEvent).toHaveBeenCalledWith(
      "OnDeleteQueuedMessage",
      "test-chat",
      ["q-del"]
    );

    // Send item now
    const sendNowBtns = screen.getAllByRole("button", { name: /Send now/i });
    fireEvent.click(sendNowBtns[1]);

    expect(handleEvent).toHaveBeenCalledWith(
      "OnSendQueuedNow",
      "test-chat",
      ["q-send"]
    );
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
      />
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
      />
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
      />
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
      messages: [{ id: "m-1", role: "user" as const, content: "what can you do?", timestamp: "10:00" }],
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
      />
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

  it("renders delete button next to chat title and directly emits OnDeleteSession upon click", () => {
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

    expect(handleEvent).toHaveBeenCalledTimes(1);
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

describe("ChatWidget File Uploads and Attachments", () => {
  beforeEach(() => {
    window.ResizeObserver = class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    } as any;
    window.HTMLElement.prototype.scrollIntoView = vi.fn();
  });

  it("attaches non-image files (e.g. PDF and text files) on paste and prevents default text insertion", async () => {
    render(<ChatWidget id="test-chat" />);
    const textarea = screen.getByPlaceholderText(/Ask/i);

    const pdfFile = new File(["dummy pdf content"], "sample.pdf", { type: "application/pdf" });
    const textFile = new File(["line1\nline2\nline3"], "notes.txt", { type: "text/plain" });

    const pasteEvent = {
      clipboardData: {
        files: [pdfFile, textFile],
        items: [],
      },
    };

    fireEvent.paste(textarea, pasteEvent);

    await waitFor(() => {
      expect(screen.getByTitle("sample.pdf")).toBeInTheDocument();
      expect(screen.getByText("notes.txt")).toBeInTheDocument();
      expect(screen.getByText("PDF")).toBeInTheDocument();
      expect(screen.getByText("TXT")).toBeInTheDocument();
    });
  });

  it("supports dragging and dropping files onto chat input container with drag styling", async () => {
    const { container } = render(<ChatWidget id="test-chat" />);
    const inputBox = container.querySelector(".chat-input-box")!;
    expect(inputBox).toBeInTheDocument();

    // Drag enter
    fireEvent.dragEnter(inputBox, {
      dataTransfer: { dropEffect: "none" },
    });
    expect(inputBox).toHaveClass("dragging");

    // Drag leave
    fireEvent.dragLeave(inputBox);
    expect(inputBox).not.toHaveClass("dragging");

    // Drag over
    fireEvent.dragOver(inputBox, {
      dataTransfer: { dropEffect: "none" },
    });
    expect(inputBox).toHaveClass("dragging");

    // Drop file
    const droppedFile = new File(["test data"], "report.docx", { type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document" });
    fireEvent.drop(inputBox, {
      dataTransfer: { files: [droppedFile] },
    });
    expect(inputBox).not.toHaveClass("dragging");

    await waitFor(() => {
      expect(screen.getByText("report.docx")).toBeInTheDocument();
      expect(screen.getByText("DOCX")).toBeInTheDocument();
    });
  });

  it("supports removing an attached file via its thumbnail remove button", async () => {
    render(<ChatWidget id="test-chat" />);
    const textarea = screen.getByPlaceholderText(/Ask/i);

    const file = new File(["content"], "delete-me.txt", { type: "text/plain" });
    fireEvent.paste(textarea, {
      clipboardData: { files: [file], items: [] },
      preventDefault: vi.fn(),
    });

    await waitFor(() => {
      expect(screen.getByText("delete-me.txt")).toBeInTheDocument();
    });

    const removeBtn = screen.getByRole("button", { name: /Remove attachment/i });
    fireEvent.click(removeBtn);

    expect(screen.queryByText("delete-me.txt")).not.toBeInTheDocument();
  });

  it("includes attachments in OnSendMessage event payload", async () => {
    const handleEvent = vi.fn();
    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-1"
        events={["OnSendMessage"]}
        eventHandler={handleEvent}
      />
    );

    const textarea = screen.getByPlaceholderText(/Ask/i);
    const sendBtn = screen.getByRole("button", { name: /Send/i });

    const file = new File(["hello world"], "hello.py", { type: "text/x-python" });
    fireEvent.paste(textarea, {
      clipboardData: { files: [file], items: [] },
      preventDefault: vi.fn(),
    });

    await waitFor(() => {
      expect(screen.getByText("hello.py")).toBeInTheDocument();
    });

    fireEvent.change(textarea, { target: { value: "Please review this code" } });
    fireEvent.click(sendBtn);

    expect(handleEvent).toHaveBeenCalledWith(
      "OnSendMessage",
      "test-chat",
      expect.arrayContaining([
        expect.objectContaining({
          prompt: "Please review this code",
          sessionId: "sess-1",
          attachments: expect.arrayContaining([
            expect.objectContaining({
              name: "hello.py",
              contentType: "text/x-python",
            }),
          ]),
        }),
      ])
    );
  });

  it("renders user messages with attachments displaying clean badges instead of raw paths", () => {
    const session = {
      id: "sess-1",
      title: "Chat with Files",
      agentId: "antigravity",
      modelId: "gemini-3.7-flash",
      createdAt: "2026-08-15T12:00:00Z",
      updatedAt: "2026-08-15T12:30:00Z",
      messages: [
        {
          id: "m-1",
          role: "user" as const,
          content: "Here is the log file\n\n[Attached Files]:\n- /path/to/server-error.log\n- /path/to/data-export.csv",
          timestamp: "12:00",
        },
      ],
    };

    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-1"
        sessions={[session]}
      />
    );

    expect(screen.getByText("Here is the log file")).toBeInTheDocument();
    expect(screen.getByText("server-error.log")).toBeInTheDocument();
    expect(screen.getByText("LOG")).toBeInTheDocument();
    expect(screen.getByText("data-export.csv")).toBeInTheDocument();
    expect(screen.getByText("CSV")).toBeInTheDocument();
    expect(screen.queryByText("[Attached Files]:")).not.toBeInTheDocument();
  });

  it("submitting a message with an attached file and empty text prompt emits OnSendMessage with empty prompt", async () => {
    const handleEvent = vi.fn();
    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-1"
        events={["OnSendMessage"]}
        eventHandler={handleEvent}
      />
    );

    const textarea = screen.getByPlaceholderText(/Ask/i);
    const sendBtn = screen.getByRole("button", { name: /Send/i });

    // Initially disabled when empty and no attachments
    expect(sendBtn).toBeDisabled();

    const imageFile = new File(["dummy-image-bytes"], "screenshot.png", { type: "image/png" });
    fireEvent.paste(textarea, {
      clipboardData: { files: [imageFile], items: [] },
      preventDefault: vi.fn(),
    });

    await waitFor(() => {
      expect(screen.getByTitle("screenshot.png")).toBeInTheDocument();
    });

    // Send button should be enabled even without prompt text
    expect(sendBtn).not.toBeDisabled();
    fireEvent.click(sendBtn);

    expect(handleEvent).toHaveBeenCalledWith(
      "OnSendMessage",
      "test-chat",
      expect.arrayContaining([
        expect.objectContaining({
          prompt: "",
          sessionId: "sess-1",
          attachments: expect.arrayContaining([
            expect.objectContaining({
              name: "screenshot.png",
              contentType: "image/png",
            }),
          ]),
        }),
      ])
    );
  });

  it("displays payload size warning banner and disables Send button when attachments exceed 50 MB", async () => {
    render(<ChatWidget id="test-chat" />);
    const textarea = screen.getByPlaceholderText(/Ask/i);
    const sendBtn = screen.getByRole("button", { name: /Send/i });

    // Create a 51 MB dummy file
    const largeFile = new File(["x"], "large-dataset.bin", { type: "application/octet-stream" });
    Object.defineProperty(largeFile, "size", { value: 51 * 1024 * 1024 });

    fireEvent.paste(textarea, {
      clipboardData: { files: [largeFile], items: [] },
      preventDefault: vi.fn(),
    });

    await waitFor(() => {
      expect(screen.getByText("large-dataset.bin")).toBeInTheDocument();
    });

    // Warning banner is displayed
    const warning = screen.getByRole("alert");
    expect(warning).toBeInTheDocument();
    expect(warning).toHaveTextContent(/Attachments exceed the 50 MB limit/i);

    // Send button is disabled due to oversized payload
    expect(sendBtn).toBeDisabled();
    expect(sendBtn).toHaveAttribute("title", "Attachments exceed the 50 MB limit");
  });

  it("uploads files via HTTP multipart POST when uploadUrl is provided and sends metadata without base64", async () => {
    const handleEvent = vi.fn();
    let capturedXhr: any = null;

    class MockXMLHttpRequest {
      open = vi.fn();
      send = vi.fn();
      upload = { onprogress: null as any };
      onload: any = null;
      onerror: any = null;
      status = 200;
      constructor() {
        capturedXhr = this;
      }
    }

    const origXHR = window.XMLHttpRequest;
    (window as any).XMLHttpRequest = MockXMLHttpRequest;

    try {
      render(
        <ChatWidget
          id="test-chat"
          activeSessionId="sess-1"
          uploadUrl="/ivy/upload/conn-1/up-1"
          events={["OnSendMessage"]}
          eventHandler={handleEvent}
        />
      );

      const textarea = screen.getByPlaceholderText(/Ask/i);
      const sendBtn = screen.getByRole("button", { name: /Send/i });

      const file = new File(["test-image-content"], "photo.png", { type: "image/png" });
      fireEvent.paste(textarea, {
        clipboardData: { files: [file], items: [] },
        preventDefault: vi.fn(),
      });

      await waitFor(() => {
        expect(capturedXhr).not.toBeNull();
        expect(capturedXhr.open).toHaveBeenCalledWith("POST", "/ivy/upload/conn-1/up-1", true);
        expect(capturedXhr.send).toHaveBeenCalledWith(expect.any(FormData));
      });

      // While uploading, send button should be disabled
      expect(sendBtn).toBeDisabled();

      // Complete the upload
      capturedXhr.status = 200;
      capturedXhr.onload();

      await waitFor(() => {
        expect(sendBtn).not.toBeDisabled();
      });

      fireEvent.change(textarea, { target: { value: "Look at this photo" } });
      fireEvent.click(sendBtn);

      expect(handleEvent).toHaveBeenCalledWith(
        "OnSendMessage",
        "test-chat",
        expect.arrayContaining([
          expect.objectContaining({
            prompt: "Look at this photo",
            sessionId: "sess-1",
            attachments: expect.arrayContaining([
              expect.objectContaining({
                name: "photo.png",
                contentType: "image/png",
                base64Data: undefined,
              }),
            ]),
          }),
        ])
      );
    } finally {
      window.XMLHttpRequest = origXHR;
    }
  });

  it("displays upload progress and failure state on HTTP upload error", async () => {
    let capturedXhr: any = null;

    class MockXMLHttpRequest {
      open = vi.fn();
      send = vi.fn();
      upload = { onprogress: null as any };
      onload: any = null;
      onerror: any = null;
      status = 500;
      constructor() {
        capturedXhr = this;
      }
    }

    const origXHR = window.XMLHttpRequest;
    (window as any).XMLHttpRequest = MockXMLHttpRequest;

    try {
      render(
        <ChatWidget
          id="test-chat"
          activeSessionId="sess-1"
          uploadUrl="/ivy/upload/conn-1/up-1"
        />
      );

      const textarea = screen.getByPlaceholderText(/Ask/i);
      const sendBtn = screen.getByRole("button", { name: /Send/i });

      const file = new File(["sample data"], "data.csv", { type: "text/csv" });
      fireEvent.paste(textarea, {
        clipboardData: { files: [file], items: [] },
        preventDefault: vi.fn(),
      });

      await waitFor(() => {
        expect(capturedXhr).not.toBeNull();
      });

      // Trigger progress
      capturedXhr.upload.onprogress?.({ lengthComputable: true, loaded: 50, total: 100 });

      await waitFor(() => {
        expect(screen.getByText("50%")).toBeInTheDocument();
      });

      // Trigger failure
      capturedXhr.status = 500;
      capturedXhr.onload();

      await waitFor(() => {
        expect(screen.getByText("Failed")).toBeInTheDocument();
      });

      // Send button remains disabled when there is no text or valid finished attachments
      expect(sendBtn).toBeDisabled();
    } finally {
      window.XMLHttpRequest = origXHR;
    }
  });

  it("handles 20MB large image upload without crashing and downscales before uploading", async () => {
    let capturedXhr: any = null;

    class MockXMLHttpRequest {
      open = vi.fn();
      send = vi.fn();
      upload = { onprogress: null as any };
      onload: any = null;
      onerror: any = null;
      status = 200;
      constructor() {
        capturedXhr = this;
      }
    }

    const origXHR = window.XMLHttpRequest;
    (window as any).XMLHttpRequest = MockXMLHttpRequest;

    try {
      render(
        <ChatWidget
          id="test-chat"
          activeSessionId="sess-1"
          uploadUrl="/ivy/upload/test"
        />
      );

      const textarea = screen.getByPlaceholderText(/Ask/i);
      const sendBtn = screen.getByRole("button", { name: /Send/i });

      const largeImage = new File(["dummy-data"], "large-photo.jpg", { type: "image/jpeg" });
      Object.defineProperty(largeImage, "size", { value: 20 * 1024 * 1024 });

      fireEvent.paste(textarea, {
        clipboardData: { files: [largeImage], items: [] },
        preventDefault: vi.fn(),
      });

      await waitFor(() => {
        expect(capturedXhr).not.toBeNull();
        expect(capturedXhr.open).toHaveBeenCalledWith("POST", "/ivy/upload/test", true);
      });

      capturedXhr.status = 200;
      capturedXhr.onload();

      await waitFor(() => {
        expect(sendBtn).not.toBeDisabled();
      });
    } finally {
      window.XMLHttpRequest = origXHR;
    }
  });

  it("optimistically displays user message immediately upon clicking Send", async () => {
    const handleEvent = vi.fn();
    const session = {
      id: "sess-empty",
      title: "New Chat",
      agentId: "codex",
      modelId: "gpt-5.6-sol",
      createdAt: "2026-09-03T10:00:00Z",
      updatedAt: "2026-09-03T10:00:00Z",
      messages: [],
    };

    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-empty"
        sessions={[session]}
        eventHandler={handleEvent}
        events={["OnSendMessage"]}
      />
    );

    expect(screen.getByText("Start a conversation")).toBeInTheDocument();

    const textarea = screen.getByPlaceholderText(/Ask/i);
    fireEvent.change(textarea, { target: { value: "test. Alive?" } });

    const sendBtn = screen.getByRole("button", { name: /Send/i });
    fireEvent.click(sendBtn);

    expect(handleEvent).toHaveBeenCalledWith("OnSendMessage", "test-chat", [
      { prompt: "test. Alive?", attachments: [], sessionId: "sess-empty" },
    ]);

    // Optimistic message should appear immediately without waiting for props update!
    expect(screen.getByText("test. Alive?")).toBeInTheDocument();
    expect(screen.queryByText("Start a conversation")).not.toBeInTheDocument();
  });

  it("optimistically displays assistant Starting status and switches Send button to Stop/Queue immediately upon clicking Send", async () => {
    const handleEvent = vi.fn();
    const session = {
      id: "sess-1",
      title: "Active Chat",
      agentId: "codex",
      modelId: "gpt-5.6-sol",
      createdAt: "2026-09-03T10:00:00Z",
      updatedAt: "2026-09-03T10:00:00Z",
      messages: [],
    };

    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-1"
        selectedAgent="codex"
        sessions={[session]}
        eventHandler={handleEvent}
        events={["OnSendMessage", "OnCancelStream"]}
      />
    );

    const textarea = screen.getByPlaceholderText(/Ask/i);
    fireEvent.change(textarea, { target: { value: "retry again" } });

    const sendBtn = screen.getByRole("button", { name: /Send/i });
    expect(sendBtn).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Stop/i })).not.toBeInTheDocument();

    fireEvent.click(sendBtn);

    // Immediately shows Stop and Queue buttons without waiting for server props
    expect(screen.getByRole("button", { name: /Stop/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Queue/i })).toBeInTheDocument();

    // Immediately shows the Starting... status indicator
    expect(screen.getByText("Starting…")).toBeInTheDocument();

    // Clicking Stop cancels optimistic stream
    const stopBtn = screen.getByRole("button", { name: /Stop/i });
    fireEvent.click(stopBtn);
    expect(handleEvent).toHaveBeenCalledWith("OnCancelStream", "test-chat", []);
    expect(screen.queryByRole("button", { name: /Stop/i })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Send/i })).toBeInTheDocument();
  });
});

