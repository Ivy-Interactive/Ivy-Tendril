import "./ChatWidget.testUtils";
import { render, screen, fireEvent, within, act } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import "@testing-library/jest-dom";
import { ChatWidget, type ChatSessionDto } from "./ChatWidget";
import { setupChatWidgetTestEnvironment } from "./ChatWidget.testUtils";

describe("ChatWidget Running Jobs Badge and Spinner", () => {
  beforeEach(() => {
    setupChatWidgetTestEnvironment();
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
      />,
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

    render(<ChatWidget id="test-chat" activeSessionId="sess-1" sessions={[session]} />);

    const badge = screen.getByRole("button", { name: /View running jobs/i });
    expect(badge).toBeInTheDocument();
    expect(badge).toHaveClass("chat-jobs-badge", "running");
    expect(within(badge).getByText(/1 running/i)).toBeInTheDocument();

    const spinLoader = badge.querySelector(".spin");
    expect(spinLoader).toBeInTheDocument();

    const pulseDot = badge.querySelector(".chat-jobs-pulse-dot");
    expect(pulseDot).toBeInTheDocument();
  });

  it("renders running jobs badge to the left of action buttons in DOM order", () => {
    const session: ChatSessionDto = {
      id: "sess-order",
      title: "Session Order",
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
        activeSessionId="sess-order"
        sessions={[session]}
        runningJobs={runningJobs}
      />,
    );

    const badge = screen.getByRole("button", { name: /View running jobs/i });
    const newChatBtn = screen.getByRole("button", { name: /New chat/i });
    const chatOptionsBtn = screen.getByRole("button", { name: /Chat options/i });

    expect(badge).toBeInTheDocument();
    expect(newChatBtn).toBeInTheDocument();
    expect(chatOptionsBtn).toBeInTheDocument();

    const actionsContainer = badge.closest(".chat-header-actions");
    expect(actionsContainer).toBeInTheDocument();
    expect(actionsContainer).toContainElement(badge);
    expect(actionsContainer).toContainElement(newChatBtn);
    expect(actionsContainer).toContainElement(chatOptionsBtn);

    expect(
      badge.compareDocumentPosition(newChatBtn) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(
      newChatBtn.compareDocumentPosition(chatOptionsBtn) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });
});

describe("ChatWidget redesign", () => {
  beforeEach(() => {
    setupChatWidgetTestEnvironment();
  });

  const baseSession = (overrides: Partial<ChatSessionDto>): ChatSessionDto => ({
    id: "sess-redesign",
    title: "Redesign Chat",
    agentId: "claude",
    modelId: "opus",
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    messages: [],
    ...overrides,
  });

  const wire = (events: object[]) => events.map((e) => JSON.stringify(e)).join("\n");

  it("renames the chat through the options menu and emits OnRenameSession", () => {
    const handleEvent = vi.fn();
    render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-redesign"
        sessions={[baseSession({})]}
        events={["OnRenameSession"]}
        eventHandler={handleEvent}
      />,
    );

    expect(screen.getByRole("heading", { name: "Redesign Chat" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Chat options/i }));
    fireEvent.click(screen.getByRole("menuitem", { name: /Edit name/i }));

    const input = screen.getByRole("textbox", { name: /Chat name/i });
    fireEvent.change(input, { target: { value: "Dark mode toggle" } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(handleEvent).toHaveBeenCalledWith("OnRenameSession", "test-chat", [
      ["sess-redesign", "Dark mode toggle"],
    ]);
    expect(screen.getByRole("heading", { name: "Dark mode toggle" })).toBeInTheDocument();
  });

  it("emits OnCreateSession from the header's new chat button", () => {
    const handleEvent = vi.fn();
    render(<ChatWidget id="test-chat" events={["OnCreateSession"]} eventHandler={handleEvent} />);

    fireEvent.click(screen.getByRole("button", { name: /New chat/i }));
    expect(handleEvent).toHaveBeenCalledWith("OnCreateSession", "test-chat", []);
  });

  it("offers Delete chat in the header options menu and emits OnDeleteSession upon click", () => {
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

    expect(screen.queryByRole("menuitem", { name: /Delete chat/i })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Chat options/i }));

    const deleteBtn = screen.getByRole("menuitem", { name: /Delete chat/i });
    expect(deleteBtn).toBeInTheDocument();

    fireEvent.click(deleteBtn);
    expect(screen.queryByRole("menuitem", { name: /Delete chat/i })).not.toBeInTheDocument();

    expect(handleEvent).toHaveBeenCalledTimes(1);
    expect(handleEvent).toHaveBeenCalledWith("OnDeleteSession", "test-chat", ["sess-123"]);
  });

  it("renders effort picker and emits OnEffortChanged for the selected agent when changed", () => {
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
        selectedAgent="claude"
        selectedEffort="high"
        supportsEffort={true}
        events={["OnEffortChanged"]}
        eventHandler={handleEvent}
      />
    );

    // The effort select lives in the selected agent's options panel, behind its row's options button.
    fireEvent.click(screen.getByRole("button", { name: /^Agent:/i }));
    fireEvent.click(screen.getByRole("button", { name: "claude options" }));
    const effortSelect = screen.getByLabelText("Effort Level");
    expect(effortSelect).toHaveValue("high");

    fireEvent.change(effortSelect, { target: { value: "max" } });

    expect(handleEvent).toHaveBeenCalledWith(
      "OnEffortChanged",
      "test-chat",
      [["claude", "max"]]
    );
  });

  it("collapses a turn's tool calls behind one line and lists them with their in/out on demand", () => {
    const rawStream = wire([
      { kind: "session_init", timestamp: "t0", session_id: "s1", model: "opus" },
      {
        kind: "tool_call",
        timestamp: "t1",
        tool_use_id: "tu1",
        tool_name: "Read",
        input: { file_path: "/src/theme.ts" },
      },
      {
        kind: "tool_result",
        timestamp: "t2",
        tool_use_id: "tu1",
        output: "export const theme = 1;",
        is_error: false,
      },
      {
        kind: "tool_call",
        timestamp: "t3",
        tool_use_id: "tu2",
        tool_name: "Bash",
        input: { command: "pnpm test" },
      },
      {
        kind: "tool_result",
        timestamp: "t4",
        tool_use_id: "tu2",
        output: "42 passed",
        is_error: false,
      },
      { kind: "text", timestamp: "t5", text: "Plan 00059 started.", delta: false },
      {
        kind: "result",
        timestamp: "t6",
        response: "Plan 00059 started.",
        is_success: true,
        duration_ms: 125200,
        usage: {
          input_tokens: 140284,
          output_tokens: 23009,
          cache_read_tokens: 0,
          cache_write_tokens: 0,
          reasoning_tokens: 0,
        },
      },
    ]);
    const session = baseSession({
      messages: [
        {
          id: "m-turn",
          role: "assistant",
          content: "Plan 00059 started.",
          timestamp: "10:00",
          rawStream,
        },
      ],
    });

    const { container } = render(
      <ChatWidget id="test-chat" activeSessionId="sess-redesign" sessions={[session]} />,
    );

    const toggle = screen.getByRole("button", { name: /2 tool calls/i });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByText("Bash")).not.toBeInTheDocument();
    expect(screen.getAllByText("Plan 00059 started.")).toHaveLength(1);
    expect(screen.getByText("125.2s")).toBeInTheDocument();
    expect(screen.getByText("140,284 / 23,009")).toBeInTheDocument();

    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText("Read")).toBeInTheDocument();
    expect(screen.getByText("Bash")).toBeInTheDocument();

    fireEvent.click(screen.getByText("Bash").closest(".aov-tool-header")!);
    expect(screen.getByText("IN")).toBeInTheDocument();
    expect(screen.getByText("OUT")).toBeInTheDocument();
    expect(container.querySelector(".aov-tool-pre")).toHaveTextContent("pnpm test");
  });

  it("condenses a job completion system event into a plan link that emits OnOpenPlan", () => {
    const handleEvent = vi.fn();
    const session = baseSession({
      messages: [
        {
          id: "m-sys",
          role: "system",
          content:
            "[System Event] Job 00148 (ExecutePlan) for '00059: Add dark mode toggle to vault theme settings' has finished with status: Completed (Completed successfully). Please inspect the outcome and guide the user.",
          timestamp: "10:00",
        },
      ],
    });

    const { container } = render(
      <ChatWidget
        id="test-chat"
        activeSessionId="sess-redesign"
        sessions={[session]}
        events={["OnOpenPlan"]}
        eventHandler={handleEvent}
      />,
    );

    const row = container.querySelector(".chat-system-event-row") as HTMLElement;
    expect(row).toHaveAttribute("data-kind", "completed");
    expect(row).toHaveTextContent(
      "Completed plan #59 Add dark mode toggle to vault theme settings.",
    );
    expect(screen.queryByText(/Please inspect/)).not.toBeInTheDocument();

    fireEvent.click(
      screen.getByRole("button", { name: "#59 Add dark mode toggle to vault theme settings" }),
    );
    expect(handleEvent).toHaveBeenCalledWith("OnOpenPlan", "test-chat", ["00059"]);
  });

  it("selects an agent from the picker and remembers a model or effort per agent from its options panel", () => {
    const handleEvent = vi.fn();
    const agents = [
      { id: "claude", label: "Claude Code", icon: "ClaudeCode", supportsEffort: true },
      { id: "codex", label: "Codex", icon: "OpenAI", models: [{ id: "gpt-5", displayName: "GPT-5" }], selectedModel: "gpt-5" },
    ];
    render(
      <ChatWidget
        id="test-chat"
        agents={agents}
        models={[{ id: "opus", displayName: "Opus" }]}
        efforts={[{ id: "default", displayName: "Default" }, { id: "max", displayName: "Max" }]}
        selectedAgent="claude"
        selectedModel="opus"
        selectedEffort="max"
        events={["OnAgentChanged", "OnModelChanged", "OnEffortChanged"]}
        eventHandler={handleEvent}
      />
    );

    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Agent: Claude Code" }));

    expect(screen.getByRole("menuitemradio", { name: "Claude Code" })).toHaveAttribute("aria-checked", "true");
    expect(screen.queryByLabelText("Model")).not.toBeInTheDocument();

    // A row's options button opens that agent's panel; its model is remembered without selecting it.
    fireEvent.click(screen.getByRole("button", { name: "Codex options" }));
    expect(screen.getByRole("group", { name: "Codex settings" })).toBeInTheDocument();
    const codexModel = screen.getByLabelText("Model");
    expect(codexModel).toHaveValue("gpt-5");
    expect(codexModel.tagName).toBe("SELECT");
    fireEvent.change(codexModel, { target: { value: "gpt-5" } });
    expect(handleEvent).toHaveBeenCalledWith("OnModelChanged", "test-chat", [["codex", "gpt-5"]]);
    expect(handleEvent).not.toHaveBeenCalledWith("OnAgentChanged", expect.anything(), expect.anything());
    expect(screen.queryByLabelText("Effort Level")).not.toBeInTheDocument();

    // The selected agent's panel shows the host's model and effort; an effort change is remembered for it.
    fireEvent.click(screen.getByRole("button", { name: "Claude Code options" }));
    expect(screen.getByLabelText("Model")).toHaveValue("opus");
    expect(screen.getByLabelText("Effort Level")).toHaveValue("max");
    fireEvent.change(screen.getByLabelText("Effort Level"), { target: { value: "default" } });
    expect(handleEvent).toHaveBeenCalledWith("OnEffortChanged", "test-chat", [["claude", "default"]]);

    // Clicking a row selects that agent and closes the menu.
    fireEvent.click(screen.getByRole("menuitemradio", { name: "Codex" }));
    expect(handleEvent).toHaveBeenLastCalledWith("OnAgentChanged", "test-chat", ["codex"]);
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Agent: Claude Code" }));
    expect(screen.getByRole("menu")).toBeInTheDocument();
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
  });

  it("scrolls a just-sent message to the top of the thread and keeps it there while the reply grows", () => {
    const layout = { spacerTop: 360 };
    Object.defineProperty(HTMLElement.prototype, "offsetTop", {
      configurable: true,
      get(this: HTMLElement) {
        if (this.dataset.messageId) return 300;
        if (this.classList.contains("chat-scroll-spacer")) return layout.spacerTop;
        return 0;
      },
    });

    try {
      const session = baseSession({});
      const { rerender } = render(
        <ChatWidget
          id="test-chat"
          activeSessionId="sess-redesign"
          sessions={[session]}
          events={["OnSendMessage"]}
          eventHandler={vi.fn()}
        />,
      );

      const container = document.querySelector(".chat-messages-container") as HTMLDivElement;
      const spacer = document.querySelector(".chat-scroll-spacer") as HTMLDivElement;
      let scrollTop = 0;
      Object.defineProperty(container, "clientHeight", { value: 400, configurable: true });
      // The spacer is part of the scrollable content, as it would be in a browser.
      Object.defineProperty(container, "scrollHeight", {
        get: () => layout.spacerTop + parseFloat(spacer.style.height || "0"),
        configurable: true,
      });
      Object.defineProperty(container, "scrollTop", {
        get: () => scrollTop,
        set: (v: number) => {
          scrollTop = v;
        },
        configurable: true,
      });

      fireEvent.change(screen.getByPlaceholderText(/Ask/i), { target: { value: "pin me" } });
      fireEvent.click(screen.getByRole("button", { name: /Send/i }));

      // 400 viewport - (360 - 300) of content below the message - 10 padding = 330 of spacer.
      expect(spacer.style.height).toBe("330px");
      expect(scrollTop).toBe(290);

      // The reply streams in below: the spacer gives way, the scroll offset stays.
      layout.spacerTop = 500;
      rerender(
        <ChatWidget
          id="test-chat"
          activeSessionId="sess-redesign"
          sessions={[session]}
          isStreaming={true}
          streamingText={JSON.stringify({
            kind: "text",
            timestamp: "t",
            text: "working",
            delta: false,
          })}
          events={["OnSendMessage"]}
          eventHandler={vi.fn()}
        />,
      );
      expect(spacer.style.height).toBe("190px");
      expect(scrollTop).toBe(290);
    } finally {
      delete (HTMLElement.prototype as any).offsetTop;
    }
  });
});

describe("ChatWidget embedded mode", () => {
  beforeEach(() => {
    setupChatWidgetTestEnvironment();
  });

  it("drops the title bar and shows the plan greeting above the headline", () => {
    const { container } = render(
      <ChatWidget
        id="embedded"
        embedded
        greeting="#59 Revamp the User Authentication Experience"
        headline="Ask Tendril to Change Anything"
      />,
    );

    expect(container.querySelector(".chat-widget-root")).toHaveAttribute("data-embedded", "true");
    expect(container.querySelector(".chat-header")).toBeNull();
    expect(screen.queryByRole("button", { name: "New chat" })).not.toBeInTheDocument();
    expect(screen.getByText("#59 Revamp the User Authentication Experience")).toBeInTheDocument();
    expect(screen.getByText("Ask Tendril to Change Anything")).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/Ask Tendril anything/)).toBeInTheDocument();
  });

  it("keeps the jobs menu reachable without the title bar", () => {
    const session: ChatSessionDto = {
      id: "s1",
      title: "Plan chat",
      agentId: "claude",
      modelId: "opus",
      createdAt: "",
      updatedAt: "",
      messages: [{ id: "m1", role: "user", content: "hi", timestamp: "" }],
      spawnedJobs: [{ id: "00148", type: "ExecutePlan", status: "Running" }],
    };
    const { container } = render(
      <ChatWidget id="embedded" embedded activeSessionId="s1" sessions={[session]} />,
    );

    expect(container.querySelector(".chat-header--embedded")).not.toBeNull();
    expect(screen.getByRole("button", { name: "View running jobs" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Chat options" })).not.toBeInTheDocument();
  });

  it("opens the plan a spawned job reported from the header jobs menu", () => {
    const handleEvent = vi.fn();
    const session: ChatSessionDto = {
      id: "s1",
      title: "Plan chat",
      agentId: "claude",
      modelId: "opus",
      createdAt: "",
      updatedAt: "",
      messages: [{ id: "m1", role: "user", content: "hi", timestamp: "" }],
      spawnedJobs: [
        { id: "00148", type: "ExecutePlan", status: "Completed", planId: "00148", planTitle: "Add login" },
        { id: "00149", type: "ExecutePlan", status: "Running" },
      ],
    };
    render(
      <ChatWidget
        id="chat"
        activeSessionId="s1"
        sessions={[session]}
        events={["OnOpenPlan"]}
        eventHandler={handleEvent}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "View running jobs" }));
    expect(screen.getByText("Spawned Jobs (2)")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Add login/ }));
    expect(handleEvent).toHaveBeenCalledWith("OnOpenPlan", "chat", ["00148"]);
    expect(screen.queryByText("Spawned Jobs (2)")).not.toBeInTheDocument();
  });

  it("shows the new-chat chord in the header button's tooltip", () => {
    vi.useFakeTimers();
    const session: ChatSessionDto = {
      id: "s1",
      title: "Plan chat",
      agentId: "claude",
      modelId: "opus",
      createdAt: "",
      updatedAt: "",
      messages: [],
    };
    render(<ChatWidget id="chat" activeSessionId="s1" sessions={[session]} />);

    fireEvent.pointerMove(screen.getByRole("button", { name: "New chat" }));
    act(() => {
      vi.advanceTimersByTime(600);
    });

    const tooltip = screen.getByRole("tooltip");
    expect(tooltip).toHaveTextContent("New chat");
    expect(tooltip.querySelector(".tui-kbd")?.textContent).toBe("Ctrl+Alt+A");
    vi.useRealTimers();
  });
});

describe("ChatWidget Markdown Code Blocks", () => {
  beforeEach(() => {
    setupChatWidgetTestEnvironment();
  });

  it("renders assistant markdown code blocks inside pmv-code-block with copy button", () => {
    const session: ChatSessionDto = {
      id: "s-code",
      title: "Code block test",
      agentId: "claude",
      modelId: "opus",
      createdAt: "",
      updatedAt: "",
      messages: [
        {
          id: "m-user",
          role: "user",
          content: "Show me a code snippet",
          timestamp: "",
        },
        {
          id: "m-assistant",
          role: "assistant",
          content:
            "Here is a TypeScript snippet:\n\n```typescript\nconst greeting = 'hello world';\nconsole.log(greeting);\n```\n\nAnd here is an untagged block:\n\n```\necho 'plain text block'\n```\n\nAnd an inline `const foo = 42;` value.",
          timestamp: "",
        },
      ],
    };

    const { container } = render(
      <ChatWidget id="test-chat" activeSessionId="s-code" sessions={[session]} />,
    );

    const assistantRow = container.querySelector(".chat-message-row.assistant");
    expect(assistantRow).toBeInTheDocument();

    const markdownBody = assistantRow?.querySelector(".chat-markdown-body");
    expect(markdownBody).toBeInTheDocument();

    const codeBlocks = assistantRow?.querySelectorAll(".pmv-code-block");
    expect(codeBlocks?.length).toBe(2);

    // Verify first code block (tagged)
    const firstBlock = codeBlocks?.[0];
    expect(firstBlock).toBeInTheDocument();
    expect(firstBlock?.querySelector("button.pmv-code-copy")).toBeInTheDocument();
    expect(firstBlock?.querySelector("pre")).toBeInTheDocument();
    expect(firstBlock?.textContent).toContain("const greeting = 'hello world';");

    // Verify second code block (untagged)
    const secondBlock = codeBlocks?.[1];
    expect(secondBlock).toBeInTheDocument();
    expect(secondBlock?.querySelector("button.pmv-code-copy")).toBeInTheDocument();
    expect(secondBlock?.querySelector("pre")).toBeInTheDocument();
    expect(secondBlock?.querySelector("pre code")).toBeInTheDocument();
    expect(secondBlock?.textContent).toContain("echo 'plain text block'");

    // Verify inline code is rendered as standalone code tag outside pmv-code-block
    const allCodeTags = assistantRow?.querySelectorAll("code");
    const inlineCode = Array.from(allCodeTags || []).find(
      (el) => el.textContent === "const foo = 42;",
    );
    expect(inlineCode).toBeInTheDocument();
    expect(inlineCode?.closest(".pmv-code-block")).toBeNull();
  });

  it("renders assistant streaming turns with code blocks inside pmv-code-block", () => {
    const rawStream = JSON.stringify({
      kind: "text",
      text: "Streaming code block:\n\n```python\ndef add(a, b):\n    return a + b\n```",
      delta: false,
    });

    const session: ChatSessionDto = {
      id: "s-stream",
      title: "Streaming code block test",
      agentId: "claude",
      modelId: "opus",
      createdAt: "",
      updatedAt: "",
      messages: [
        {
          id: "m-stream-assistant",
          role: "assistant",
          content: "",
          rawStream,
          timestamp: "",
        },
      ],
    };

    const { container } = render(
      <ChatWidget id="test-chat" activeSessionId="s-stream" sessions={[session]} />,
    );

    const assistantRow = container.querySelector(".chat-message-row.assistant");
    expect(assistantRow).toBeInTheDocument();

    const turn = assistantRow?.querySelector(".chat-turn");
    expect(turn).toBeInTheDocument();

    const codeBlock = assistantRow?.querySelector(".pmv-code-block");
    expect(codeBlock).toBeInTheDocument();
    expect(codeBlock?.querySelector("button.pmv-code-copy")).toBeInTheDocument();
    expect(codeBlock?.textContent).toContain("def add(a, b):");
  });
});
