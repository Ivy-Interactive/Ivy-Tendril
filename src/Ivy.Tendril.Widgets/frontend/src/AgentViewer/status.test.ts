import { describe, expect, it } from "vitest";
import { deriveStatus } from "./status";
import type { PresentationEvent } from "./types";

describe("deriveStatus", () => {
  it("returns Completed when last event is successful result", () => {
    const events: PresentationEvent[] = [
      { kind: "assistant-text", text: "Finished job." },
      {
        kind: "result",
        wire: {
          kind: "result",
          timestamp: "2026-09-04T10:00:00Z",
          is_success: true,
        },
      },
    ];

    const status = deriveStatus(events);
    expect(status.complete).toBe(true);
    expect(status.text).toBe("Completed");
  });

  it("returns Completed even if trailing non-terminal events follow result", () => {
    const events: PresentationEvent[] = [
      {
        kind: "result",
        wire: {
          kind: "result",
          timestamp: "2026-09-04T10:00:00Z",
          is_success: true,
        },
      },
    ];

    const status = deriveStatus(events);
    expect(status.complete).toBe(true);
    expect(status.text).toBe("Completed");
  });

  it("returns Failed when result event has is_success = false", () => {
    const events: PresentationEvent[] = [
      {
        kind: "result",
        wire: {
          kind: "result",
          timestamp: "2026-09-04T10:00:00Z",
          is_success: false,
        },
      },
    ];

    const status = deriveStatus(events);
    expect(status.complete).toBe(true);
    expect(status.text).toBe("Failed");
  });

  it("returns Failed when error event is present", () => {
    const events: PresentationEvent[] = [
      { kind: "error", message: "Process crashed" },
    ];

    const status = deriveStatus(events);
    expect(status.complete).toBe(true);
    expect(status.text).toBe("Failed");
  });

  it("returns tool label when tool is active without result", () => {
    const events: PresentationEvent[] = [
      {
        kind: "tool-use",
        tool: {
          toolUseId: "t1",
          name: "Read",
          input: { file_path: "/path/to/test.ts" },
        },
      },
    ];

    const status = deriveStatus(events);
    expect(status.complete).toBe(false);
    expect(status.text).toBe("Reading test.ts");
  });
});
