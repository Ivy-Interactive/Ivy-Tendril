import { describe, it, expect } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { AssistantTurn, formatDuration, formatClockTime, summarizeTurn } from "./AssistantTurn";
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
    expect(turn.segments).toEqual([{ kind: "text", text: "Hello world" }]);
    expect(turn.toolCount).toBe(0);
  });

  it("places each run of tool calls where it happened between the prose", () => {
    const events = parseEventWireStream(
      wire([
        { kind: "text", timestamp: "t1", text: "Looking around.", delta: false },
        { kind: "tool_call", timestamp: "t2", tool_use_id: "a", tool_name: "Grep", input: { pattern: "x" } },
        { kind: "tool_result", timestamp: "t3", tool_use_id: "a", output: "none", is_error: false },
        { kind: "text", timestamp: "t4", text: "Nothing there, trying the tests.", delta: false },
        { kind: "tool_call", timestamp: "t5", tool_use_id: "b", tool_name: "Read", input: { file_path: "/a.ts" } },
        { kind: "tool_call", timestamp: "t6", tool_use_id: "c", tool_name: "Bash", input: { command: "pnpm test" } },
        { kind: "text", timestamp: "t7", text: "All green.", delta: false },
      ]),
    );
    const turn = summarizeTurn(events);
    expect(turn.segments.map((segment) => (segment.kind === "activity" ? `tools:${segment.toolCount}` : segment.kind))).toEqual([
      "text",
      "tools:1",
      "text",
      "tools:2",
      "text",
    ]);
    expect(turn.toolCount).toBe(3);
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
    expect(turn.toolCount).toBe(1);
    // The thinking came before the prose, so it has no tool call of its own to sit under.
    expect(turn.segments).toEqual([
      { kind: "text", text: "Checking the repo." },
      { kind: "activity", toolCount: 1, entries: [{ kind: "tool", tool: expect.objectContaining({ name: "Grep" }) }] },
      { kind: "error", message: "rate limited" },
    ]);
  });

  it("formats durations in seconds with one decimal", () => {
    expect(formatDuration(125200)).toBe("125.2s");
    expect(formatDuration(900)).toBe("0.9s");
  });
});

describe("AssistantTurn", () => {
  it("shows the live status line while live and the metrics once finished", () => {
    const stream = wire([
      { kind: "tool_call", timestamp: "t1", tool_use_id: "a", tool_name: "Read", input: { file_path: "/a.ts" } },
    ]);
    const { rerender } = render(<AssistantTurn stream={stream} live />);
    expect(screen.getByText("Reading a.ts")).toBeInTheDocument();
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

  it("renders the tool rows without card chrome, the chevron sharing the status dot's slot", () => {
    const stream = wire([
      { kind: "tool_call", timestamp: "t1", tool_use_id: "a", tool_name: "Read", input: { file_path: "/a.ts" } },
      { kind: "tool_result", timestamp: "t2", tool_use_id: "a", output: "ok", is_error: false },
    ]);
    const { container } = render(<AssistantTurn stream={stream} />);
    fireEvent.click(screen.getByRole("button", { name: /1 tool call$/ }));

    const row = container.querySelector(".aov-tool") as HTMLElement;
    expect(row).toHaveClass("aov-tool--minimal");
    const mark = row.querySelector(".aov-tool-mark") as HTMLElement;
    expect(mark.querySelector(".aov-tool-status--success")).toBeInTheDocument();
    expect(mark.querySelector(".aov-tool-chevron")).toBeInTheDocument();
  });

  it("renders an empty live stream as the starting status only", () => {
    const { container } = render(<AssistantTurn stream="" live />);
    expect(screen.getByText("Starting…")).toBeInTheDocument();
    expect(container.querySelector(".chat-tools")).toBeNull();
  });

  it("shows the live run's elapsed time and estimated token count", () => {
    const startedAt = new Date(Date.now() - 82_000).toISOString();
    const stream = wire([
      { kind: "text", timestamp: startedAt, text: "x".repeat(4000), delta: false },
    ]);
    render(<AssistantTurn stream={stream} live />);
    expect(screen.getByText("1m 22s")).toBeInTheDocument();
    expect(screen.getByText("~1k tokens")).toBeInTheDocument();
  });
});

describe("formatClockTime", () => {
  it("formats valid ISO strings according to viewer locale", () => {
    const iso = new Date(2026, 8, 12, 9, 5).toISOString();
    const formatted = formatClockTime(iso);
    expect(formatted).toBeTruthy();
    expect(formatted).toMatch(/\d{1,2}:\d{2}/);
  });

  it("returns empty string for unparseable input", () => {
    expect(formatClockTime("not-a-date")).toBe("");
    expect(formatClockTime("")).toBe("");
  });
});

describe("AssistantTurn with completedAt", () => {
  it("shows the formatted completion time in the meta row when completedAt is provided", () => {
    const stream = wire([
      { kind: "result", timestamp: "t1", response: "Done.", is_success: true, duration_ms: 2400, usage: { input_tokens: 500, output_tokens: 50, cache_read_tokens: 0, cache_write_tokens: 0, reasoning_tokens: 0, cost_usd: 0.012 } },
    ]);
    const completedAt = new Date(2026, 8, 12, 14, 30).toISOString();
    render(<AssistantTurn stream={stream} completedAt={completedAt} />);

    expect(screen.getByText("2.4s")).toBeInTheDocument();
    expect(screen.getByText("500 / 50")).toBeInTheDocument();
    const clockTime = formatClockTime(completedAt);
    expect(screen.getByText(clockTime)).toBeInTheDocument();
  });

  it("renders meta row without clock item when completedAt is absent", () => {
    const stream = wire([
      { kind: "result", timestamp: "t1", response: "Done.", is_success: true, duration_ms: 2400, usage: { input_tokens: 500, output_tokens: 50, cache_read_tokens: 0, cache_write_tokens: 0, reasoning_tokens: 0, cost_usd: 0.012 } },
    ]);
    render(<AssistantTurn stream={stream} />);

    expect(screen.getByText("2.4s")).toBeInTheDocument();
    expect(screen.getByText("500 / 50")).toBeInTheDocument();
    const { container } = render(<AssistantTurn stream={stream} />);
    const metaItems = container.querySelectorAll(".chat-turn-meta-item");
    expect(metaItems.length).toBe(3);
  });
});
