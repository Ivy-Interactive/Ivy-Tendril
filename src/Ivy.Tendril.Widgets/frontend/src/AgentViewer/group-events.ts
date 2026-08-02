import type { PresentationEvent, ToolUsePresentation } from "./types";

export type RenderNode =
  | { kind: "single"; index: number; event: PresentationEvent }
  | { kind: "tool-group"; index: number; tools: ToolUsePresentation[] };

export function groupToolUseEvents(events: PresentationEvent[]): RenderNode[] {
  const nodes: RenderNode[] = [];
  let toolRun: { startIndex: number; tools: ToolUsePresentation[] } | null = null;

  const flushToolRun = () => {
    if (toolRun !== null) {
      if (toolRun.tools.length === 1) {
        nodes.push({
          kind: "single",
          index: toolRun.startIndex,
          event: { kind: "tool-use", tool: toolRun.tools[0] },
        });
      } else {
        nodes.push({
          kind: "tool-group",
          index: toolRun.startIndex,
          tools: toolRun.tools,
        });
      }
      toolRun = null;
    }
  };

  events.forEach((event, idx) => {
    if (event.kind === "tool-use") {
      if (toolRun === null) {
        toolRun = { startIndex: idx, tools: [event.tool] };
      } else {
        toolRun.tools.push(event.tool);
      }
    } else {
      flushToolRun();
      nodes.push({ kind: "single", index: idx, event });
    }
  });

  flushToolRun();
  return nodes;
}

export function aggregateToolStatus(
  tools: ToolUsePresentation[]
): "running" | "success" | "error" {
  let hasRunning = false;
  for (const tool of tools) {
    if (tool.isError) return "error";
    if (tool.result === undefined) hasRunning = true;
  }
  return hasRunning ? "running" : "success";
}
