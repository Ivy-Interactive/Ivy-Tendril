import { describe, expect, it } from "vitest";
import { parseEventWireStream } from "./parse-events";

describe("parseEventWireStream", () => {
  it("marks tool as success when tool_result has output", () => {
    const stream = [
      '{"kind":"tool_call","timestamp":"T","tool_use_id":"t1","tool_name":"Read","input":{"file_path":"x.ts"}}',
      '{"kind":"tool_result","timestamp":"T","tool_use_id":"t1","output":"file contents","is_error":false}',
    ].join("\n");

    const events = parseEventWireStream(stream);
    const toolEvent = events.find((e) => e.kind === "tool-use");
    expect(toolEvent).toBeDefined();
    if (toolEvent?.kind === "tool-use") {
      expect(toolEvent.tool.result).toBe("file contents");
      expect(toolEvent.tool.isError).toBe(false);
    }
  });

  it("marks tool as success with empty string when tool_result output is null", () => {
    const stream = [
      '{"kind":"tool_call","timestamp":"T","tool_use_id":"t1","tool_name":"Edit","input":{"file_path":"x.ts"}}',
      '{"kind":"tool_result","timestamp":"T","tool_use_id":"t1","output":null,"is_error":false}',
    ].join("\n");

    const events = parseEventWireStream(stream);
    const toolEvent = events.find((e) => e.kind === "tool-use");
    expect(toolEvent).toBeDefined();
    if (toolEvent?.kind === "tool-use") {
      expect(toolEvent.tool.result).toBe("");
      expect(toolEvent.tool.isError).toBe(false);
    }
  });

  it("marks tool as error when is_error is true", () => {
    const stream = [
      '{"kind":"tool_call","timestamp":"T","tool_use_id":"t1","tool_name":"Bash","input":{"command":"exit 1"}}',
      '{"kind":"tool_result","timestamp":"T","tool_use_id":"t1","output":"command failed","is_error":true}',
    ].join("\n");

    const events = parseEventWireStream(stream);
    const toolEvent = events.find((e) => e.kind === "tool-use");
    if (toolEvent?.kind === "tool-use") {
      expect(toolEvent.tool.result).toBe("command failed");
      expect(toolEvent.tool.isError).toBe(true);
    }
  });

  it("keeps tool result undefined when no tool_result arrives", () => {
    const stream =
      '{"kind":"tool_call","timestamp":"T","tool_use_id":"t1","tool_name":"Read","input":{"file_path":"x.ts"}}';

    const events = parseEventWireStream(stream);
    const toolEvent = events.find((e) => e.kind === "tool-use");
    if (toolEvent?.kind === "tool-use") {
      expect(toolEvent.tool.result).toBeUndefined();
    }
  });

  it("coalesces delta text events into a single assistant-text", () => {
    const stream = [
      '{"kind":"text","timestamp":"T","text":"Hello","delta":false}',
      '{"kind":"text","timestamp":"T","text":" world","delta":true}',
    ].join("\n");

    const events = parseEventWireStream(stream);
    expect(events).toHaveLength(1);
    if (events[0].kind === "assistant-text") {
      expect(events[0].text).toBe("Hello world");
    }
  });

  it("skips malformed JSON lines gracefully", () => {
    const stream = [
      "not json at all",
      '{"kind":"text","timestamp":"T","text":"ok","delta":false}',
      "{incomplete",
    ].join("\n");

    const events = parseEventWireStream(stream);
    expect(events).toHaveLength(1);
    expect(events[0].kind).toBe("assistant-text");
  });
});
