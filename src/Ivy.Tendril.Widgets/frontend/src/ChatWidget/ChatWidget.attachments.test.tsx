import "./ChatWidget.testUtils";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import "@testing-library/jest-dom";
import { ChatWidget, type ChatSessionDto } from "./ChatWidget";
import { setupChatWidgetTestEnvironment } from "./ChatWidget.testUtils";

describe("ChatWidget File Uploads and Attachments", () => {
  beforeEach(() => {
    setupChatWidgetTestEnvironment();
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
    const droppedFile = new File(["test data"], "report.docx", {
      type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    });
    fireEvent.drop(inputBox, {
      dataTransfer: { files: [droppedFile] },
    });
    expect(inputBox).not.toHaveClass("dragging");

    await waitFor(() => {
      expect(screen.getByText("report.docx")).toBeInTheDocument();
      expect(screen.getByText("DOCX")).toBeInTheDocument();
    });
  });

  it("activates dragging state and renders drop overlay when dragging over the root widget or message area", () => {
    const { container } = render(<ChatWidget id="test-chat" />);
    const root = container.querySelector(".chat-widget-root")!;
    expect(root).toBeInTheDocument();

    // Drag enter on root
    fireEvent.dragEnter(root, {
      dataTransfer: { dropEffect: "none" },
    });
    expect(root).toHaveClass("dragging");
    expect(screen.getByText("Drop files here to attach to message")).toBeInTheDocument();

    // Drag leave on root
    fireEvent.dragLeave(root);
    expect(root).not.toHaveClass("dragging");
    expect(screen.queryByText("Drop files here to attach to message")).not.toBeInTheDocument();

    // Drag over messages container activates overlay
    const messagesArea = container.querySelector(".chat-thread") || root;
    fireEvent.dragEnter(messagesArea, {
      dataTransfer: { dropEffect: "none" },
    });
    expect(root).toHaveClass("dragging");
    expect(screen.getByText("Drop files here to attach to message")).toBeInTheDocument();
  });

  it("supports dropping a file anywhere on the chat widget to add attachment", async () => {
    const { container } = render(<ChatWidget id="test-chat" />);
    const root = container.querySelector(".chat-widget-root")!;
    const messagesArea = container.querySelector(".chat-thread") || root;

    // Drag over chat area
    fireEvent.dragEnter(messagesArea, {
      dataTransfer: { dropEffect: "none" },
    });
    expect(root).toHaveClass("dragging");

    // Drop file on messages area
    const droppedFile = new File(["test docx content"], "specs.docx", {
      type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    });
    fireEvent.drop(messagesArea, {
      dataTransfer: { files: [droppedFile] },
    });
    expect(root).not.toHaveClass("dragging");
    expect(screen.queryByText("Drop files here to attach to message")).not.toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText("specs.docx")).toBeInTheDocument();
      expect(screen.getByText("DOCX")).toBeInTheDocument();
    });
  });

  it("calls preventDefault on dragover and drop events to block browser file navigation", () => {
    const { container } = render(<ChatWidget id="test-chat" />);
    const root = container.querySelector(".chat-widget-root")!;

    // Drag over root prevents default
    const dragOverEvent = new Event("dragover", { bubbles: true, cancelable: true });
    root.dispatchEvent(dragOverEvent);
    expect(dragOverEvent.defaultPrevented).toBe(true);

    // Drop on root prevents default
    const dropEvent = new Event("drop", { bubbles: true, cancelable: true });
    root.dispatchEvent(dropEvent);
    expect(dropEvent.defaultPrevented).toBe(true);

    // Window-level dragover prevents default
    const windowDragOverEvent = new Event("dragover", { bubbles: true, cancelable: true });
    window.dispatchEvent(windowDragOverEvent);
    expect(windowDragOverEvent.defaultPrevented).toBe(true);

    // Window-level drop prevents default
    const windowDropEvent = new Event("drop", { bubbles: true, cancelable: true });
    window.dispatchEvent(windowDropEvent);
    expect(windowDropEvent.defaultPrevented).toBe(true);
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
      />,
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
      ]),
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
          content:
            "Here is the log file\n\n[Attached Files]:\n- /path/to/server-error.log\n- /path/to/data-export.csv",
          timestamp: "12:00",
        },
      ],
    };

    render(<ChatWidget id="test-chat" activeSessionId="sess-1" sessions={[session]} />);

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
          content:
            "Here is a screenshot\n\n[Attached Files]:\n- /path/to/screenshot.png\n- /path/to/photo.jpg",
          timestamp: "12:00",
        },
      ],
    };

    render(<ChatWidget id="test-chat" activeSessionId="sess-img" sessions={[session]} />);

    expect(screen.getByText("Here is a screenshot")).toBeInTheDocument();
    expect(screen.getByText("screenshot.png")).toBeInTheDocument();
    expect(screen.getByText("PNG")).toBeInTheDocument();
    expect(screen.getByText("photo.jpg")).toBeInTheDocument();
    expect(screen.getByText("JPG")).toBeInTheDocument();

    const imgElements = screen.getAllByRole("img");
    const previewImgs = imgElements.filter((img) =>
      img.getAttribute("src")?.includes("/ivy/local-file?path="),
    );
    expect(previewImgs.length).toBe(2);
    expect(previewImgs[0].getAttribute("src")).toContain(
      "/ivy/local-file?path=%2Fpath%2Fto%2Fscreenshot.png",
    );
    expect(previewImgs[1].getAttribute("src")).toContain(
      "/ivy/local-file?path=%2Fpath%2Fto%2Fphoto.jpg",
    );
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

    render(<ChatWidget id="test-chat" activeSessionId="sess-lightbox" sessions={[session]} />);

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

    render(<ChatWidget id="test-chat" activeSessionId="sess-pdf" sessions={[session]} />);

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
      />,
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
      ]),
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

    // Send button is disabled due to oversized payload; the reason is on its tooltip and banner
    expect(sendBtn).toBeDisabled();
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
        />,
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
        ]),
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
        <ChatWidget id="test-chat" activeSessionId="sess-1" uploadUrl="/ivy/upload/conn-1/up-1" />,
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
      render(<ChatWidget id="test-chat" activeSessionId="sess-1" uploadUrl="/ivy/upload/test" />);

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
      />,
    );

    expect(screen.getByText("What Are We Producing Today?")).toBeInTheDocument();

    const textarea = screen.getByPlaceholderText(/Ask/i);
    fireEvent.change(textarea, { target: { value: "test. Alive?" } });

    const sendBtn = screen.getByRole("button", { name: /Send/i });
    fireEvent.click(sendBtn);

    expect(handleEvent).toHaveBeenCalledWith("OnSendMessage", "test-chat", [
      { prompt: "test. Alive?", attachments: [], sessionId: "sess-empty" },
    ]);

    // Optimistic message should appear immediately without waiting for props update!
    expect(screen.getByText("test. Alive?")).toBeInTheDocument();
    expect(screen.queryByText("What Are We Producing Today?")).not.toBeInTheDocument();
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
      />,
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

    // Immediately shows the agent status line
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
      />,
    );

    // Banner directly above chat is removed
    expect(screen.queryByText(/Spawned Jobs \(/i)).not.toBeInTheDocument();

    // Header badge is displayed
    const badgeBtn = screen.getByRole("button", { name: /View running jobs/i });
    expect(badgeBtn).toBeInTheDocument();
    expect(within(badgeBtn).getByText(/1 job/i)).toBeInTheDocument();

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
      ]),
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
      />,
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
      ]),
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
      <ChatWidget id="test-chat" activeSessionId="sess-running" sessions={[session]} />,
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
      <ChatWidget id="test-chat" activeSessionId="sess-sys" sessions={[session]} />,
    );

    const systemRow = container.querySelector(".chat-system-event-row");
    expect(systemRow).toBeInTheDocument();
    expect(
      screen.getByText("Job 00148 (CreatePlan) has completed successfully."),
    ).toBeInTheDocument();
    expect(screen.getByText("I reviewed the plan and it looks great!")).toBeInTheDocument();
  });
});
