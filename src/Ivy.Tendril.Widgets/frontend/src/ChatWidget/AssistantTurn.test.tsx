import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { AssistantTurn, formatDuration, summarizeTurn } from "./AssistantTurn";
import { parseEventWireStream } from "../AgentViewer/parse-events";

const wire = (events: object[]) => events.map((e) => JSON.stringify(e)).join("\n");

describe("summarizeTurn", () => {
  it("keeps the result's copy of a text the stream also delivered as a delta", () => {
    const events = parseEventWireStream(
      wire([
        { kind: "text", timestamp: "t1", text: "Hello", delta: true },
        { kind: "text", timestamp: "t2", text: " world", delta: true },
        { kind: "result", timestamp: "t3", response: "Hello world", is_success: true },
      ]),
    );
    const turn = summarizeTurn(events);
    expect(turn.blocks).toEqual([{ kind: "text", text: "Hello world" }]);
    expect(turn.toolCount).toBe(0);
  });

  it("keeps intermediate prose, counts tools, and interleaves thinking in the activity", () => {
    const events = parseEventWireStream(
      wire([
        { kind: "thinking", timestamp: "t0", content: "Let me look." },
        { kind: "text", timestamp: "t1", text: "Checking the repo.", delta: false },
        { kind: "tool_call", timestamp: "t2", tool_use_id: "a", tool_name: "Grep", input: { pattern: "x" } },
        { kind: "tool_result", timestamp: "t3", tool_use_id: "a", output: "none", is_error: false },
        { kind: "error", timestamp: "t4", message: "rate limited", is_retryable: true, is_auth_error: false },
      ]),
    );
    const turn = summarizeTurn(events);
    expect(turn.activity.map((entry) => entry.kind)).toEqual(["thinking", "tool"]);
    expect(turn.toolCount).toBe(1);
    expect(turn.blocks).toEqual([
      { kind: "text", text: "Checking the repo." },
      { kind: "error", message: "rate limited" },
    ]);
  });

  it("formats durations in seconds with one decimal", () => {
    expect(formatDuration(125200)).toBe("125.2s");
    expect(formatDuration(900)).toBe("0.9s");
  });
});

describe("AssistantTurn", () => {
  it("shows the animated status while live and the metrics once finished", () => {
    const stream = wire([
      { kind: "tool_call", timestamp: "t1", tool_use_id: "a", tool_name: "Read", input: { file_path: "/a.ts" } },
    ]);
    const { rerender } = render(<AssistantTurn stream={stream} live />);
    expect(screen.getByText("Thinking")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /1 tool call$/ })).toBeInTheDocument();

    const finished = wire([
      { kind: "tool_call", timestamp: "t1", tool_use_id: "a", tool_name: "Read", input: { file_path: "/a.ts" } },
      { kind: "tool_result", timestamp: "t2", tool_use_id: "a", output: "ok", is_error: false },
      {
        kind: "result",
        timestamp: "t3",
        response: "Done.",
        is_success: true,
        duration_ms: 3100,
        usage: { input_tokens: 1200, output_tokens: 80, cache_read_tokens: 0, cache_write_tokens: 0, reasoning_tokens: 0, cost_usd: 0.0421 },
      },
    ]);
    rerender(<AssistantTurn stream={finished} />);
    expect(screen.queryByText("Reading a.ts")).not.toBeInTheDocument();
    expect(screen.getByText("Done.")).toBeInTheDocument();
    expect(screen.getByText("3.1s")).toBeInTheDocument();
    expect(screen.getByText("1,200 / 80")).toBeInTheDocument();
    expect(screen.getByText("$0.042")).toBeInTheDocument();
  });

  it("renders an empty live stream as the Thinking status only", () => {
    const { container } = render(<AssistantTurn stream="" live />);
    expect(screen.getByText("Thinking")).toBeInTheDocument();
    expect(container.querySelector(".chat-tools")).toBeNull();
  });
});
