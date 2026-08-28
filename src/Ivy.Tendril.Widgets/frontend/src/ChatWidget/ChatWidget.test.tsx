import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
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

  it("renders delete button next to chat title, opens confirmation dialog, and emits OnDeleteSession upon confirmation", () => {
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
      />,
    );

    const deleteBtn = screen.getByRole("button", { name: /Delete chat session/i });
    expect(deleteBtn).toBeInTheDocument();

    fireEvent.click(deleteBtn);

    // Dialog is open
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText("Delete Chat")).toBeInTheDocument();
    expect(within(dialog).getByText(/Are you sure you want to delete/i)).toBeInTheDocument();
    expect(within(dialog).getByText(/My Great Chat/i)).toBeInTheDocument();
    expect(handleEvent).not.toHaveBeenCalled();

    // Confirm deletion
    const confirmDeleteBtn = within(dialog).getByRole("button", { name: /Delete/i });
    fireEvent.click(confirmDeleteBtn);

    expect(handleEvent).toHaveBeenCalledWith(
      "OnDeleteSession",
      "test-chat",
      ["sess-123"]
    );
    expect(screen.queryByText("Delete Chat")).not.toBeInTheDocument();
  });

  it("dismisses delete confirmation dialog when Cancel is clicked, Escape is pressed, or backdrop is clicked without emitting OnDeleteSession", () => {
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

    // 1. Cancel button
    fireEvent.click(deleteBtn);
    expect(screen.getByText("Delete Chat")).toBeInTheDocument();

    const cancelBtn = screen.getByRole("button", { name: /Cancel/i });
    fireEvent.click(cancelBtn);
    expect(screen.queryByText("Delete Chat")).not.toBeInTheDocument();
    expect(handleEvent).not.toHaveBeenCalled();

    // 2. Escape key
    fireEvent.click(deleteBtn);
    expect(screen.getByText("Delete Chat")).toBeInTheDocument();

    fireEvent.keyDown(window, { key: "Escape" });
    expect(screen.queryByText("Delete Chat")).not.toBeInTheDocument();
    expect(handleEvent).not.toHaveBeenCalled();

    // 3. Backdrop click
    fireEvent.click(deleteBtn);
    const dialogOverlay = screen.getByRole("dialog");
    fireEvent.click(dialogOverlay);
    expect(screen.queryByText("Delete Chat")).not.toBeInTheDocument();
    expect(handleEvent).not.toHaveBeenCalled();
  });

  it("confirms deletion via Ctrl+Enter and Cmd+Enter keyboard shortcuts", () => {
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

    // Ctrl+Enter
    fireEvent.click(deleteBtn);
    expect(screen.getByText("Delete Chat")).toBeInTheDocument();

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });
    expect(handleEvent).toHaveBeenCalledTimes(1);
    expect(handleEvent).toHaveBeenCalledWith("OnDeleteSession", "test-chat", ["sess-123"]);
    expect(screen.queryByText("Delete Chat")).not.toBeInTheDocument();

    // Cmd+Enter
    fireEvent.click(deleteBtn);
    expect(screen.getByText("Delete Chat")).toBeInTheDocument();

    fireEvent.keyDown(window, { key: "Enter", metaKey: true });
    expect(handleEvent).toHaveBeenCalledTimes(2);
    expect(handleEvent).toHaveBeenLastCalledWith("OnDeleteSession", "test-chat", ["sess-123"]);
    expect(screen.queryByText("Delete Chat")).not.toBeInTheDocument();
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
      />,
    );

    const effortTrigger = screen.getByTitle("Effort Level");
    expect(effortTrigger).toBeInTheDocument();
    expect(screen.getByText("High")).toBeInTheDocument();

    fireEvent.click(effortTrigger.querySelector("button")!);
    const maxOption = screen.getByRole("button", { name: /Max/i });
    fireEvent.click(maxOption);

    expect(handleEvent).toHaveBeenCalledWith("OnEffortChanged", "test-chat", ["max"]);
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
});

describe("ChatWidget Background Activity Tracking UI", () => {
  beforeEach(() => {
    window.ResizeObserver = class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    } as any;
    window.HTMLElement.prototype.scrollIntoView = vi.fn();
  });

  it("does not render activity badge when there are no tracked jobs or plans", () => {
    render(<ChatWidget id="test-chat" trackedJobs={[]} trackedPlans={[]} />);
    expect(
      screen.queryByRole("button", { name: /View background jobs and plans/i }),
    ).not.toBeInTheDocument();
  });

  it("renders activity badge with running status and counts when jobs are running", () => {
    const trackedJobs = [
      {
        id: "00042",
        type: "ExecutePlan",
        planId: "00013",
        planTitle: "Track Background Jobs",
        status: "Running",
        statusMessage: "Building project...",
        duration: "15s",
      },
    ];
    const trackedPlans = [
      {
        id: "00013",
        title: "Track Background Jobs",
        folderName: "00013-TrackBackgroundJobs",
        status: "Executing",
      },
    ];

    render(<ChatWidget id="test-chat" trackedJobs={trackedJobs} trackedPlans={trackedPlans} />);

    const badge = screen.getByRole("button", { name: /View background jobs and plans/i });
    expect(badge).toBeInTheDocument();
    expect(badge).toHaveClass("running");
    expect(screen.getByText(/1 Running/i)).toBeInTheDocument();
    expect(screen.getByText(/1 Job · 1 Plan/i)).toBeInTheDocument();
  });

  it("opens popover on badge click and triggers navigation events for jobs and plans", () => {
    const handleEvent = vi.fn();
    const trackedJobs = [
      {
        id: "00042",
        type: "ExecutePlan",
        planId: "00013",
        planTitle: "Track Background Jobs",
        status: "Running",
        statusMessage: "Running verifications",
        duration: "25s",
      },
    ];
    const trackedPlans = [
      {
        id: "00013",
        title: "Track Background Jobs",
        folderName: "00013-TrackBackgroundJobs",
        status: "Executing",
      },
    ];

    render(
      <ChatWidget
        id="test-chat"
        trackedJobs={trackedJobs}
        trackedPlans={trackedPlans}
        events={["OnNavigateJob", "OnNavigatePlan"]}
        eventHandler={handleEvent}
      />,
    );

    // Click badge to open popover
    const badge = screen.getByRole("button", { name: /View background jobs and plans/i });
    fireEvent.click(badge);

    expect(screen.getByText("Background Activity")).toBeInTheDocument();
    expect(screen.getByText("Jobs (1)")).toBeInTheDocument();
    expect(screen.getByText("#00042")).toBeInTheDocument();
    expect(screen.getByText("Running verifications")).toBeInTheDocument();
    expect(screen.getByText("Plans (1)")).toBeInTheDocument();
    expect(screen.getByText("00013")).toBeInTheDocument();

    // Click job item to trigger OnNavigateJob
    const jobButton = screen.getByRole("button", { name: /View job #00042/i });
    fireEvent.click(jobButton);

    expect(handleEvent).toHaveBeenCalledWith("OnNavigateJob", "test-chat", ["00042"]);

    // Click plan item to trigger OnNavigatePlan
    const planButton = screen.getByRole("button", { name: /Open plan 00013/i });
    fireEvent.click(planButton);

    expect(handleEvent).toHaveBeenCalledWith("OnNavigatePlan", "test-chat", [
      "00013-TrackBackgroundJobs",
    ]);
  });
});
