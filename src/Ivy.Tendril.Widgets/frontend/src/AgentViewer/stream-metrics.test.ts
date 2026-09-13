import { describe, it, expect } from "vitest";
import { deriveStreamMetrics } from "./stream-metrics";
import { parseEventWires } from "./parse-events";
import type { EventWire } from "./types";

const wire = (events: unknown[]): string => events.map((e) => JSON.stringify(e)).join("\n");

describe("deriveStreamMetrics", () => {
  it("reports nothing for an empty stream", () => {
    expect(deriveStreamMetrics([])).toEqual({ startedAt: undefined, tokensEstimated: true });
  });

  it("takes the start time from the first event", () => {
    const events = parseEventWires(
      wire([
        { kind: "session_init", timestamp: "2026-09-09T10:00:00Z", session_id: "s1" },
        { kind: "text", timestamp: "2026-09-09T10:00:05Z", text: "hi", delta: false },
      ]),
    );
    expect(deriveStreamMetrics(events).startedAt).toBe("2026-09-09T10:00:00Z");
  });

  it("estimates tokens from the text, thinking and tool payloads of a run in flight", () => {
    const events = parseEventWires(
      wire([
        { kind: "text", timestamp: "t1", text: "a".repeat(400), delta: true },
        { kind: "thinking", timestamp: "t2", content: "b".repeat(400) },
        { kind: "tool_result", timestamp: "t3", tool_use_id: "x", output: "c".repeat(400), is_error: false },
      ]),
    );
    const metrics = deriveStreamMetrics(events);
    expect(metrics.tokensEstimated).toBe(true);
    // 1200 characters of payload at ~4 characters per token.
    expect(metrics.tokens).toBe(300);
  });

  it("prefers the usage a finished run reports over the estimate", () => {
    const events = parseEventWires(
      wire([
        { kind: "text", timestamp: "t1", text: "a".repeat(4000), delta: false },
        {
          kind: "result",
          timestamp: "t2",
          is_success: true,
          usage: {
            input_tokens: 1200,
            output_tokens: 80,
            cache_read_tokens: 0,
            cache_write_tokens: 0,
            reasoning_tokens: 0,
          },
        },
      ]),
    );
    const metrics = deriveStreamMetrics(events);
    expect(metrics.tokensEstimated).toBe(false);
    expect(metrics.tokens).toBe(1280);
  });

  it("ignores events it cannot count", () => {
    const events: EventWire[] = [
      { kind: "error", timestamp: "t1", message: "boom", is_retryable: false, is_auth_error: false },
    ];
    expect(deriveStreamMetrics(events)).toEqual({ startedAt: "t1", tokensEstimated: true });
  });
});
