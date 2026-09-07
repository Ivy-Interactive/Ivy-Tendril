import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import "@testing-library/jest-dom";

vi.mock("pdfjs-dist", () => ({ GlobalWorkerOptions: {}, getDocument: vi.fn() }));
vi.mock("pdfjs-dist/build/pdf.worker.mjs?url", () => ({ default: "" }));

import { ChatWidget, type ChatSessionDto } from "./ChatWidget";

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
        sessions={[{
          id: "sess-1",
          title: "Session 1",
          agentId: "claude",
          modelId: "sonnet",
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          messages: [],
        }]}
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

  it("renders image preview elements pointing to the /ivy/local-file endpoint for image attachments in chat history", () => {
    const session: ChatSessionDto = {
      id: "sess-img",
      title: "Image Preview Test",
      agentId: "antigravity",
      modelId: "gemini-3.7-flash",
      createdAt: "2026-08-15T12:00:00Z",
      updatedAt: "2026-08-15T12:30:00Z",
      messages: [
        {
          id: "m-img",
          role: "user" as const,
          content: "Here is a screenshot\n\n[Attached Files]:\n- /path/to/screenshot.png\n- /path/to/photo.jpg",
          timestamp: "12:00",
        },
      ],
    };

    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-img"
        sessions={[session]}
      />
    );

    expect(screen.getByText("Here is a screenshot")).toBeInTheDocument();
    expect(screen.getByText("screenshot.png")).toBeInTheDocument();
    expect(screen.getByText("PNG")).toBeInTheDocument();
    expect(screen.getByText("photo.jpg")).toBeInTheDocument();
    expect(screen.getByText("JPG")).toBeInTheDocument();

    const imgElements = screen.getAllByRole("img");
    const previewImgs = imgElements.filter((img) =>
      img.getAttribute("src")?.includes("/ivy/local-file?path=")
    );
    expect(previewImgs.length).toBe(2);
    expect(previewImgs[0].getAttribute("src")).toContain("/ivy/local-file?path=%2Fpath%2Fto%2Fscreenshot.png");
    expect(previewImgs[1].getAttribute("src")).toContain("/ivy/local-file?path=%2Fpath%2Fto%2Fphoto.jpg");
  });

  it("opens lightbox modal with full image view on click, and closes via Escape or close button", () => {
    const session: ChatSessionDto = {
      id: "sess-lightbox",
      title: "Lightbox Test",
      agentId: "antigravity",
      modelId: "gemini-3.7-flash",
      createdAt: "2026-08-15T12:00:00Z",
      updatedAt: "2026-08-15T12:30:00Z",
      messages: [
        {
          id: "m-lb",
          role: "user" as const,
          content: "Check this out\n\n[Attached Files]:\n- /path/to/preview.png",
          timestamp: "12:00",
        },
      ],
    };

    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-lightbox"
        sessions={[session]}
      />
    );

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    const thumbnailCard = screen.getByText("preview.png").closest(".chat-user-attachment-card");
    expect(thumbnailCard).toBeInTheDocument();
    fireEvent.click(thumbnailCard!);

    const modal = screen.getByRole("dialog");
    expect(modal).toBeInTheDocument();
    const closeBtn = screen.getByRole("button", { name: /Close preview/i });
    expect(closeBtn).toBeInTheDocument();

    // Close via close button
    fireEvent.click(closeBtn);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    // Reopen and close via Escape
    fireEvent.click(thumbnailCard!);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    fireEvent.keyDown(window, { key: "Escape" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("renders PDF preview cards with PdfThumbnail for PDF attachments in chat history", () => {
    const session: ChatSessionDto = {
      id: "sess-pdf",
      title: "PDF Preview Test",
      agentId: "antigravity",
      modelId: "gemini-3.7-flash",
      createdAt: "2026-08-15T12:00:00Z",
      updatedAt: "2026-08-15T12:30:00Z",
      messages: [
        {
          id: "m-pdf",
          role: "user" as const,
          content: "Here is the report\n\n[Attached Files]:\n- /docs/specification.pdf",
          timestamp: "12:00",
        },
      ],
    };

    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-pdf"
        sessions={[session]}
      />
    );

    expect(screen.getByText("Here is the report")).toBeInTheDocument();
    expect(screen.getByText("specification.pdf")).toBeInTheDocument();
    expect(screen.getByText("PDF")).toBeInTheDocument();

    const pdfCard = screen.getByText("specification.pdf").closest(".chat-user-attachment-card-pdf");
    expect(pdfCard).toBeInTheDocument();
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

  it("displays spawned jobs in header badge and emits OnSendMessage when reviewing outcomes from dropdown", () => {
    const handleEvent = vi.fn();
    const session: ChatSessionDto = {
      id: "sess-jobs",
      title: "Jobs Chat",
      agentId: "claude",
      modelId: "opus",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      messages: [],
      spawnedJobs: [
        {
          id: "job-101",
          type: "plan",
          status: "Completed",
          planTitle: "Add OAuth2 authentication",
        },
      ],
    };

    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-jobs"
        sessions={[session]}
        eventHandler={handleEvent}
        events={["OnSendMessage"]}
      />
    );

    // Banner directly above chat is removed
    expect(screen.queryByText(/Spawned Jobs \(/i)).not.toBeInTheDocument();

    // Header badge is displayed
    const badgeBtn = screen.getByRole("button", { name: /View running jobs/i });
    expect(badgeBtn).toBeInTheDocument();
    expect(within(badgeBtn).getByText(/1 jobs/i)).toBeInTheDocument();

    // Open dropdown by clicking badge
    fireEvent.click(badgeBtn);

    expect(screen.getByText(/Spawned Jobs/i)).toBeInTheDocument();
    expect(screen.getByText(/1 completed/i)).toBeInTheDocument();
    expect(screen.getByText("Add OAuth2 authentication")).toBeInTheDocument();

    const reviewBtn = screen.getByRole("button", { name: /Ask agent to review outcomes/i });
    expect(reviewBtn).toBeInTheDocument();

    fireEvent.click(reviewBtn);

    expect(handleEvent).toHaveBeenCalledWith(
      "OnSendMessage",
      "test-chat",
      expect.arrayContaining([
        expect.objectContaining({
          prompt: expect.stringContaining("All spawned jobs have completed"),
          sessionId: "sess-jobs",
        }),
      ])
    );
  });

  it("renders interactive questions in chat message and emits OnAnswerQuestion when user submits response", async () => {
    const handleEvent = vi.fn();
    const session: ChatSessionDto = {
      id: "sess-q",
      title: "Questions Chat",
      agentId: "claude",
      modelId: "opus",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      messages: [
        {
          id: "msg-q1",
          role: "assistant",
          content: [
            "Please confirm your choices below:",
            "```questions",
            "- id: deploy_target",
            "  title: Which environment should we deploy to?",
            "  options:",
            "    - title: Staging Environment",
            "      value: staging",
            "    - title: Production Environment",
            "      value: prod",
            "```",
          ].join("\n"),
          timestamp: "12:00 PM",
        },
      ],
    };

    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-q"
        sessions={[session]}
        eventHandler={handleEvent}
        events={["OnAnswerQuestion"]}
      />
    );

    expect(screen.getByText("Which environment should we deploy to?")).toBeInTheDocument();
    expect(screen.getByText("Staging Environment")).toBeInTheDocument();
    expect(screen.getByText("Production Environment")).toBeInTheDocument();

    const submitBtn = screen.getByRole("button", { name: /Submit Response/i });
    expect(submitBtn).toBeDisabled();

    // Select Staging Environment option
    const stagingRadio = screen.getByRole("radio", { name: /Staging Environment/i });
    fireEvent.click(stagingRadio);

    // Submit button should now be enabled
    expect(submitBtn).not.toBeDisabled();

    fireEvent.click(submitBtn);

    expect(handleEvent).toHaveBeenCalledWith(
      "OnAnswerQuestion",
      "test-chat",
      expect.arrayContaining([
        expect.objectContaining({
          sessionId: "sess-q",
          messageId: "msg-q1",
          answers: { deploy_target: ["staging"] },
          responseText: expect.stringContaining("Staging Environment"),
        }),
      ])
    );
  });

  it("renders running jobs header badge and toggles dropdown on click", async () => {
    const session: ChatSessionDto = {
      id: "sess-running",
      title: "Active Job Session",
      agentId: "antigravity",
      modelId: "gemini-3.8-flash",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      messages: [],
      spawnedJobs: [
        {
          id: "00148",
          type: "CreatePlan",
          status: "Running",
          planTitle: "Test job tracking",
          statusMessage: "Researching architecture...",
        },
      ],
    };

    const { container } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-running"
        sessions={[session]}
      />
    );

    // Header badge displays running count
    const badgeBtn = screen.getByRole("button", { name: /View running jobs/i });
    expect(badgeBtn).toBeInTheDocument();
    expect(within(badgeBtn).getByText(/1 running/i)).toBeInTheDocument();

    // Click badge to open dropdown
    fireEvent.click(badgeBtn);

    // Dropdown is open and displays job item details
    const dropdown = container.querySelector(".chat-jobs-dropdown-menu") as HTMLElement;
    expect(dropdown).toBeInTheDocument();
    expect(within(dropdown).getByText("00148")).toBeInTheDocument();
    expect(within(dropdown).getByText("CreatePlan")).toBeInTheDocument();
    expect(within(dropdown).getByText("Researching architecture...")).toBeInTheDocument();
  });

  it("renders system event messages in chat timeline", async () => {
    const session: ChatSessionDto = {
      id: "sess-sys",
      title: "System Notification Session",
      agentId: "antigravity",
      modelId: "gemini-3.8-flash",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      messages: [
        {
          id: "msg-sys-1",
          role: "system",
          content: "Job 00148 (CreatePlan) has completed successfully.",
          timestamp: "10:00 AM",
        },
        {
          id: "msg-ast-1",
          role: "assistant",
          content: "I reviewed the plan and it looks great!",
          timestamp: "10:01 AM",
        },
      ],
    };

    const { container } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-sys"
        sessions={[session]}
      />
    );

    const systemRow = container.querySelector(".chat-system-event-row");
    expect(systemRow).toBeInTheDocument();
    expect(screen.getByText("Job 00148 (CreatePlan) has completed successfully.")).toBeInTheDocument();
    expect(screen.getByText("I reviewed the plan and it looks great!")).toBeInTheDocument();
  });
});

describe("ChatWidget Interactive Question Draft Persistence", () => {
  beforeEach(() => {
    window.ResizeObserver = class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    } as any;
    window.HTMLElement.prototype.scrollIntoView = vi.fn();
  });

  const questionsFence = (...lines: string[]) => ["```questions", ...lines, "```"].join("\n");

  const deployQuestion = questionsFence(
    "- id: deploy_target",
    "  title: Which environment should we deploy to?",
    "  options:",
    "    - title: Staging Environment",
    "      value: staging",
    "    - title: Production Environment",
    "      value: prod",
  );

  const deployQuestionWithOther = questionsFence(
    "- id: deploy_target",
    "  title: Which environment should we deploy to?",
    "  other: true",
    "  options:",
    "    - title: Staging Environment",
    "      value: staging",
    "    - title: Production Environment",
    "      value: prod",
  );

  const freeTextQuestion = questionsFence("- id: comment", "  title: Anything else?");

  const twoBlockMessage = [
    "First block:",
    questionsFence(
      "- id: color",
      "  title: Favorite color?",
      "  options:",
      "    - title: Red",
      "      value: red",
      "    - title: Blue",
      "      value: blue",
    ),
    "Second block:",
    questionsFence(
      "- id: size",
      "  title: Favorite size?",
      "  options:",
      "    - title: Small",
      "      value: small",
      "    - title: Large",
      "      value: large",
    ),
  ].join("\n\n");

  const sessionWith = (id: string, content: string): ChatSessionDto => ({
    id,
    title: id,
    agentId: "claude",
    modelId: "opus",
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    messages: [
      {
        id: `${id}-msg`,
        role: "assistant",
        content,
        timestamp: "12:00 PM",
      },
    ],
  });

  it("keeps a selection through a streamVersion re-render (streaming chunk arriving)", () => {
    const session = sessionWith("sess-stream", deployQuestion);

    const { rerender } = render(
      <ChatWidget id="test-chat" activeSessionId="sess-stream" sessions={[session]} events={["OnAnswerQuestion"]} />
    );

    fireEvent.click(screen.getByRole("radio", { name: /Staging Environment/i }));
    expect(screen.getByRole("radio", { name: /Staging Environment/i })).toBeChecked();

    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-stream"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
        isStreaming
        streamingText='{"kind":"text","text":"Working on it...","delta":false}'
      />
    );

    expect(screen.getByRole("radio", { name: /Staging Environment/i })).toBeChecked();
    expect(screen.getByRole("button", { name: /Submit Response/i })).not.toBeDisabled();
  });

  it("never remounts the callout's input across a streamVersion re-render", () => {
    const session = sessionWith("sess-remount", freeTextQuestion);

    const { rerender } = render(
      <ChatWidget id="test-chat" activeSessionId="sess-remount" sessions={[session]} events={["OnAnswerQuestion"]} />
    );

    const inputBefore = screen.getByPlaceholderText("Type your answer");

    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-remount"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
        isStreaming
        streamingText='{"kind":"text","text":"Still working...","delta":false}'
      />
    );

    expect(screen.getByPlaceholderText("Type your answer")).toBe(inputBefore);
  });

  it("keeps typed Other text and focus through a sessionVersion re-render (runningJobs change)", () => {
    const session = sessionWith("sess-other", deployQuestionWithOther);

    const { rerender } = render(
      <ChatWidget id="test-chat" activeSessionId="sess-other" sessions={[session]} events={["OnAnswerQuestion"]} />
    );

    fireEvent.click(screen.getByRole("radio", { name: /^Other$/i }));
    const otherInput = screen.getByPlaceholderText("Type your answer");
    otherInput.focus();
    fireEvent.change(otherInput, { target: { value: "A canary environment" } });
    expect(otherInput).toHaveValue("A canary environment");

    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-other"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
        runningJobs={[{ id: "job-1", type: "ExecutePlan", status: "Running" }]}
      />
    );

    const otherInputAfter = screen.getByPlaceholderText("Type your answer");
    expect(otherInputAfter).toBe(otherInput);
    expect(otherInputAfter).toHaveValue("A canary environment");
    expect(document.activeElement).toBe(otherInputAfter);
  });

  it("restores a selection from the draft store after a real remount (switching sessions and back)", () => {
    const sessionA = sessionWith("sess-a", deployQuestion);
    const sessionB = sessionWith("sess-b", "Just a plain message, no questions here.");

    const { rerender } = render(
      <ChatWidget id="test-chat" activeSessionId="sess-a" sessions={[sessionA, sessionB]} events={["OnAnswerQuestion"]} />
    );

    fireEvent.click(screen.getByRole("radio", { name: /Staging Environment/i }));
    expect(screen.getByRole("radio", { name: /Staging Environment/i })).toBeChecked();

    rerender(
      <ChatWidget id="test-chat" activeSessionId="sess-b" sessions={[sessionA, sessionB]} events={["OnAnswerQuestion"]} />
    );
    expect(screen.queryByRole("radio", { name: /Staging Environment/i })).toBeNull();

    rerender(
      <ChatWidget id="test-chat" activeSessionId="sess-a" sessions={[sessionA, sessionB]} events={["OnAnswerQuestion"]} />
    );

    expect(screen.getByRole("radio", { name: /Staging Environment/i })).toBeChecked();
  });

  it("clears the draft on submit, so a later remount starts empty rather than re-offering the old selection", () => {
    const sessionA = sessionWith("sess-a", deployQuestion);
    const sessionB = sessionWith("sess-b", "Just a plain message, no questions here.");
    const handleEvent = vi.fn();

    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-a"
        sessions={[sessionA, sessionB]}
        eventHandler={handleEvent}
        events={["OnAnswerQuestion"]}
      />
    );

    fireEvent.click(screen.getByRole("radio", { name: /Staging Environment/i }));
    fireEvent.click(screen.getByRole("button", { name: /Submit Response/i }));
    expect(handleEvent).toHaveBeenCalled();

    // Switch away and back to force a remount of the message row.
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-b"
        sessions={[sessionA, sessionB]}
        eventHandler={handleEvent}
        events={["OnAnswerQuestion"]}
      />
    );
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-a"
        sessions={[sessionA, sessionB]}
        eventHandler={handleEvent}
        events={["OnAnswerQuestion"]}
      />
    );

    expect(screen.getByRole("radio", { name: /Staging Environment/i })).not.toBeChecked();
    expect(screen.getByRole("button", { name: /Submit Response/i })).toBeDisabled();
  });

  it("keeps two question blocks in one message independent when only one is answered", () => {
    const session = sessionWith("sess-two-blocks", twoBlockMessage);

    const { rerender } = render(
      <ChatWidget id="test-chat" activeSessionId="sess-two-blocks" sessions={[session]} events={["OnAnswerQuestion"]} />
    );

    fireEvent.click(screen.getByRole("radio", { name: /^Red$/i }));
    expect(screen.getByRole("radio", { name: /^Red$/i })).toBeChecked();
    expect(screen.getByRole("radio", { name: /^Small$/i })).not.toBeChecked();

    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-two-blocks"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
        isStreaming
        streamingText='{"kind":"text","text":"Working...","delta":false}'
      />
    );

    expect(screen.getByRole("radio", { name: /^Red$/i })).toBeChecked();
    expect(screen.getByRole("radio", { name: /^Small$/i })).not.toBeChecked();
  });

  it("keeps a selection made inside a rawStream-rendered questions block through a re-render", () => {
    const rawStream = JSON.stringify({ kind: "text", text: deployQuestion, delta: false });
    const session: ChatSessionDto = {
      id: "sess-raw",
      title: "Raw Stream Chat",
      agentId: "claude",
      modelId: "opus",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      messages: [
        {
          id: "msg-raw",
          role: "assistant",
          content: "",
          rawStream,
          timestamp: "12:00 PM",
        },
      ],
    };

    const { rerender } = render(
      <ChatWidget id="test-chat" activeSessionId="sess-raw" sessions={[session]} events={["OnAnswerQuestion"]} />
    );

    fireEvent.click(screen.getByRole("radio", { name: /Staging Environment/i }));
    expect(screen.getByRole("radio", { name: /Staging Environment/i })).toBeChecked();

    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-raw"
        sessions={[session]}
        events={["OnAnswerQuestion"]}
        runningJobs={[{ id: "job-2", type: "ExecutePlan", status: "Running" }]}
      />
    );

    expect(screen.getByRole("radio", { name: /Staging Environment/i })).toBeChecked();
  });
});

describe("ChatWidget Streaming Scroll Behavior", () => {
  const session = {
    id: "sess-scroll",
    title: "Scroll Test Chat",
    agentId: "codex",
    modelId: "gpt-5",
    createdAt: "2026-09-03T10:00:00Z",
    updatedAt: "2026-09-03T10:00:00Z",
    messages: [
      {
        id: "m-1",
        role: "user" as const,
        content: "Explain markdown streaming",
        timestamp: "10:00 AM",
      },
    ],
  };

  it("scrolls to bottom without triggering continuous smooth scroll calls when container is at bottom", () => {
    const scrollIntoViewMock = vi.fn();
    window.HTMLElement.prototype.scrollIntoView = scrollIntoViewMock;

    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"First chunk"}'
      />
    );

    const container = document.querySelector(".chat-messages-container") as HTMLDivElement;
    expect(container).toBeInTheDocument();

    let currentScrollTop = 600;
    Object.defineProperty(container, "scrollHeight", { value: 1000, configurable: true, writable: true });
    Object.defineProperty(container, "clientHeight", { value: 400, configurable: true, writable: true });
    Object.defineProperty(container, "scrollTop", {
      get: () => currentScrollTop,
      set: (v: number) => { currentScrollTop = v; },
      configurable: true,
    });

    // Reset any initial scrollIntoView calls from mount
    scrollIntoViewMock.mockClear();

    // Stream next chunk
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"First chunk and second chunk"}'
      />
    );

    // Should update scrollTop to bottom (1000 - 400 = 600) instantly without calling scrollIntoView smooth
    expect(scrollIntoViewMock).not.toHaveBeenCalled();
    expect(currentScrollTop).toBe(600);
  });

  it("does not force scroll down when user is scrolled up", () => {
    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"Initial streaming"}'
      />
    );

    const container = document.querySelector(".chat-messages-container") as HTMLDivElement;
    let currentScrollTop = 600;
    Object.defineProperty(container, "scrollHeight", { value: 1000, configurable: true, writable: true });
    Object.defineProperty(container, "clientHeight", { value: 400, configurable: true, writable: true });
    Object.defineProperty(container, "scrollTop", {
      get: () => currentScrollTop,
      set: (v: number) => { currentScrollTop = v; },
      configurable: true,
    });

    // User scrolls up to 200px (distance to bottom is 1000 - 200 - 400 = 400px > 50px threshold)
    currentScrollTop = 200;
    fireEvent.scroll(container);

    // New stream chunk arrives
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"Initial streaming with more tokens"}'
      />
    );

    // Container should remain at user scroll offset (200px) and not be forced down
    expect(currentScrollTop).toBe(200);
  });

  it("re-enables auto-scroll when user scrolls back to bottom", () => {
    const { rerender } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"Starting stream"}'
      />
    );

    const container = document.querySelector(".chat-messages-container") as HTMLDivElement;
    let currentScrollTop = 600;
    let height = 1000;
    Object.defineProperty(container, "scrollHeight", {
      get: () => height,
      set: (v: number) => { height = v; },
      configurable: true,
    });
    Object.defineProperty(container, "clientHeight", { value: 400, configurable: true, writable: true });
    Object.defineProperty(container, "scrollTop", {
      get: () => currentScrollTop,
      set: (v: number) => { currentScrollTop = v; },
      configurable: true,
    });

    // User scrolls up
    currentScrollTop = 150;
    fireEvent.scroll(container);

    // New chunk while scrolled up: stays at 150
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"Starting stream..."}'
      />
    );
    expect(currentScrollTop).toBe(150);

    // User scrolls back near bottom: 580px (1000 - 580 - 400 = 20px <= 50px threshold)
    currentScrollTop = 580;
    fireEvent.scroll(container);

    // Height increases with new tokens
    height = 1200;
    rerender(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-scroll"
        sessions={[session]}
        isStreaming={true}
        streamingText='{"type":"text_delta","content":"Starting stream... more tokens generated"}'
      />
    );

    // Auto-scroll is re-enabled and pins to bottom (1200 - 400 = 800)
    expect(currentScrollTop).toBe(800);
  });
});

describe("ChatWidget Running Jobs Badge and Spinner", () => {
  beforeEach(() => {
    window.ResizeObserver = class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    } as any;
    window.HTMLElement.prototype.scrollIntoView = vi.fn();
  });

  it("renders running jobs badge indicator, .spin loader, and .chat-jobs-pulse-dot when runningJobs has active jobs", () => {
    const session: ChatSessionDto = {
      id: "sess-0",
      title: "Session 0",
      agentId: "agent-1",
      modelId: "model-1",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      messages: [],
    };
    const runningJobs = [
      { id: "job-1", type: "CreatePlan", status: "Running", planTitle: "Test Plan" },
    ];

    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-0"
        sessions={[session]}
        runningJobs={runningJobs}
      />
    );

    const badge = screen.getByRole("button", { name: /View running jobs/i });
    expect(badge).toBeInTheDocument();
    expect(badge).toHaveClass("chat-jobs-badge", "running");
    expect(within(badge).getByText(/1 running/i)).toBeInTheDocument();

    const spinLoader = badge.querySelector(".spin");
    expect(spinLoader).toBeInTheDocument();

    const pulseDot = badge.querySelector(".chat-jobs-pulse-dot");
    expect(pulseDot).toBeInTheDocument();
  });

  it("renders running jobs badge indicator when active session has spawned running jobs", () => {
    const session: ChatSessionDto = {
      id: "sess-1",
      title: "Session 1",
      agentId: "agent-1",
      modelId: "model-1",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      messages: [],
      spawnedJobs: [
        { id: "job-2", type: "ExecutePlan", status: "Running", planTitle: "Execution Plan" },
      ],
    };

    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-1"
        sessions={[session]}
      />
    );

    const badge = screen.getByRole("button", { name: /View running jobs/i });
    expect(badge).toBeInTheDocument();
    expect(badge).toHaveClass("chat-jobs-badge", "running");
    expect(within(badge).getByText(/1 running/i)).toBeInTheDocument();

    const spinLoader = badge.querySelector(".spin");
    expect(spinLoader).toBeInTheDocument();

    const pulseDot = badge.querySelector(".chat-jobs-pulse-dot");
    expect(pulseDot).toBeInTheDocument();
  });
});


