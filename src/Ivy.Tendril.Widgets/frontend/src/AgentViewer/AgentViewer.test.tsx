import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { AgentViewer } from "./AgentViewer";

describe("AgentViewer", () => {
  const originalResizeObserver = globalThis.ResizeObserver;

  beforeEach(() => {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver;
  });

  afterEach(() => {
    globalThis.ResizeObserver = originalResizeObserver;
  });
  it("renders 'Starting…' when no stream or events are provided", () => {
    render(
      <AgentViewer
        id="viewer-1"
        eventHandler={vi.fn()}
      />
    );

    expect(screen.getByText("Starting…")).toBeInTheDocument();
  });

  it("renders pre-buffered jsonStream events", () => {
    const jsonStream = [
      JSON.stringify({ kind: "text", timestamp: "2026-09-10T12:00:00Z", text: "Hello from agent", delta: false }),
      JSON.stringify({
        kind: "tool_call",
        timestamp: "2026-09-10T12:00:01Z",
        tool_use_id: "t1",
        tool_name: "Read",
        input: { file_path: "app.ts" },
      }),
    ].join("\n");

    render(
      <AgentViewer
        id="viewer-1"
        eventHandler={vi.fn()}
        jsonStream={jsonStream}
      />
    );

    expect(screen.getByText("Hello from agent")).toBeInTheDocument();
    expect(screen.getByText("Read")).toBeInTheDocument();
  });

  it("subscribes to stream and renders streamed events live", () => {
    let streamHandler: ((data: unknown) => void) | null = null;
    const unsubscribe = vi.fn();
    const subscribeToStream = vi.fn((_streamId: string, onData: (data: unknown) => void) => {
      streamHandler = onData;
      return unsubscribe;
    });

    render(
      <AgentViewer
        id="viewer-1"
        eventHandler={vi.fn()}
        stream={{ id: "stream-123" }}
        subscribeToStream={subscribeToStream}
      />
    );

    expect(subscribeToStream).toHaveBeenCalledWith("stream-123", expect.any(Function));
    expect(screen.getByText("Starting…")).toBeInTheDocument();

    // Stream a text event
    act(() => {
      streamHandler?.(
        JSON.stringify({
          kind: "text",
          timestamp: "2026-09-10T12:00:00Z",
          text: "Analyzing project stack...",
          delta: false,
        })
      );
    });

    expect(screen.getByText("Analyzing project stack...")).toBeInTheDocument();

    // Stream a tool call event
    act(() => {
      streamHandler?.(
        JSON.stringify({
          kind: "tool_call",
          timestamp: "2026-09-10T12:00:01Z",
          tool_use_id: "t2",
          tool_name: "Bash",
          input: { command: "dotnet --version" },
        })
      );
    });

    expect(screen.getByText("Bash")).toBeInTheDocument();
  });

  it("fires OnComplete callback when result wire arrives", () => {
    let streamHandler: ((data: unknown) => void) | null = null;
    const subscribeToStream = vi.fn((_id: string, onData: (data: unknown) => void) => {
      streamHandler = onData;
      return vi.fn();
    });
    const eventHandler = vi.fn();

    render(
      <AgentViewer
        id="viewer-1"
        eventHandler={eventHandler}
        events={["OnComplete"]}
        stream={{ id: "stream-123" }}
        subscribeToStream={subscribeToStream}
      />
    );

    const resultWire = {
      kind: "result",
      timestamp: "2026-09-10T12:00:02Z",
      response: "All verifications created successfully.",
      is_success: true,
      exit_code: 0,
    };

    act(() => {
      streamHandler?.(JSON.stringify(resultWire));
    });

    expect(eventHandler).toHaveBeenCalledTimes(1);
    expect(eventHandler).toHaveBeenCalledWith("OnComplete", "viewer-1", [JSON.stringify(resultWire)]);
  });

  it("resets streamed lines when stream ID changes", () => {
    let streamHandler: ((data: unknown) => void) | null = null;
    const subscribeToStream = vi.fn((_id: string, onData: (data: unknown) => void) => {
      streamHandler = onData;
      return vi.fn();
    });

    const { rerender } = render(
      <AgentViewer
        id="viewer-1"
        eventHandler={vi.fn()}
        stream={{ id: "stream-1" }}
        subscribeToStream={subscribeToStream}
      />
    );

    act(() => {
      streamHandler?.(
        JSON.stringify({
          kind: "text",
          timestamp: "2026-09-10T12:00:00Z",
          text: "First stream output",
          delta: false,
        })
      );
    });

    expect(screen.getByText("First stream output")).toBeInTheDocument();

    // Change stream ID
    rerender(
      <AgentViewer
        id="viewer-1"
        eventHandler={vi.fn()}
        stream={{ id: "stream-2" }}
        subscribeToStream={subscribeToStream}
      />
    );

    expect(screen.queryByText("First stream output")).not.toBeInTheDocument();
    expect(screen.getByText("Starting…")).toBeInTheDocument();
  });

  it("unsubscribes on unmount", () => {
    const unsubscribe = vi.fn();
    const subscribeToStream = vi.fn(() => unsubscribe);

    const { unmount } = render(
      <AgentViewer
        id="viewer-1"
        eventHandler={vi.fn()}
        stream={{ id: "stream-1" }}
        subscribeToStream={subscribeToStream}
      />
    );

    expect(unsubscribe).not.toHaveBeenCalled();
    unmount();
    expect(unsubscribe).toHaveBeenCalledTimes(1);
  });
});
