import { describe, it, expect } from "vitest";
import { groupToolUseEvents, aggregateToolStatus } from "./group-events";
import type { PresentationEvent, ToolUsePresentation } from "./types";

describe("groupToolUseEvents", () => {
  it("groups consecutive tool-use events into a single tool-group", () => {
    const events: PresentationEvent[] = [
      { kind: "tool-use", tool: { toolUseId: "1", name: "Read", input: {}, result: "OK" } },
      { kind: "tool-use", tool: { toolUseId: "2", name: "Write", input: {}, result: "OK" } },
      { kind: "tool-use", tool: { toolUseId: "3", name: "Bash", input: {}, result: "OK" } },
    ];

    const nodes = groupToolUseEvents(events);

    expect(nodes).toHaveLength(1);
    expect(nodes[0].kind).toBe("tool-group");
    if (nodes[0].kind === "tool-group") {
      expect(nodes[0].tools).toHaveLength(3);
      expect(nodes[0].index).toBe(0);
      expect(nodes[0].tools[0].name).toBe("Read");
      expect(nodes[0].tools[1].name).toBe("Write");
      expect(nodes[0].tools[2].name).toBe("Bash");
    }
  });

  it("keeps a single tool-use event as a single node", () => {
    const events: PresentationEvent[] = [
      { kind: "assistant-text", text: "Let me check." },
      { kind: "tool-use", tool: { toolUseId: "1", name: "Read", input: {}, result: "OK" } },
      { kind: "assistant-text", text: "Done." },
    ];

    const nodes = groupToolUseEvents(events);

    expect(nodes).toHaveLength(3);
    expect(nodes[0].kind).toBe("single");
    expect(nodes[1].kind).toBe("single");
    expect(nodes[2].kind).toBe("single");
    if (nodes[1].kind === "single" && nodes[1].event.kind === "tool-use") {
      expect(nodes[1].event.tool.name).toBe("Read");
    }
  });

  it("preserves original indices for groups separated by text", () => {
    const events: PresentationEvent[] = [
      { kind: "tool-use", tool: { toolUseId: "1", name: "Read", input: {} } },
      { kind: "tool-use", tool: { toolUseId: "2", name: "Write", input: {} } },
      { kind: "assistant-text", text: "Checking..." },
      { kind: "tool-use", tool: { toolUseId: "3", name: "Bash", input: {} } },
      { kind: "tool-use", tool: { toolUseId: "4", name: "Grep", input: {} } },
    ];

    const nodes = groupToolUseEvents(events);

    expect(nodes).toHaveLength(3);
    expect(nodes[0].kind).toBe("tool-group");
    expect(nodes[1].kind).toBe("single");
    expect(nodes[2].kind).toBe("tool-group");

    if (nodes[0].kind === "tool-group") {
      expect(nodes[0].index).toBe(0);
    }
    if (nodes[1].kind === "single") {
      expect(nodes[1].index).toBe(2);
    }
    if (nodes[2].kind === "tool-group") {
      expect(nodes[2].index).toBe(3);
    }
  });
});

describe("aggregateToolStatus", () => {
  it("returns error if any tool has isError", () => {
    const tools: ToolUsePresentation[] = [
      { toolUseId: "1", name: "Read", input: {}, result: "OK" },
      { toolUseId: "2", name: "Write", input: {}, result: "Failed", isError: true },
      { toolUseId: "3", name: "Bash", input: {}, result: "OK" },
    ];

    expect(aggregateToolStatus(tools)).toBe("error");
  });

  it("returns running if any tool has undefined result", () => {
    const tools: ToolUsePresentation[] = [
      { toolUseId: "1", name: "Read", input: {}, result: "OK" },
      { toolUseId: "2", name: "Write", input: {} },
      { toolUseId: "3", name: "Bash", input: {}, result: "OK" },
    ];

    expect(aggregateToolStatus(tools)).toBe("running");
  });

  it("returns success if all tools have results and no errors", () => {
    const tools: ToolUsePresentation[] = [
      { toolUseId: "1", name: "Read", input: {}, result: "OK" },
      { toolUseId: "2", name: "Write", input: {}, result: "OK" },
      { toolUseId: "3", name: "Bash", input: {}, result: "OK" },
    ];

    expect(aggregateToolStatus(tools)).toBe("success");
  });

  it("prioritizes error over running", () => {
    const tools: ToolUsePresentation[] = [
      { toolUseId: "1", name: "Read", input: {} },
      { toolUseId: "2", name: "Write", input: {}, result: "Failed", isError: true },
    ];

    expect(aggregateToolStatus(tools)).toBe("error");
  });
});
