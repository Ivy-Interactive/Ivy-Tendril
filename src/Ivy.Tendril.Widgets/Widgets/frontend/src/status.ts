import type { PresentationEvent } from "./agent-output-types";

function basename(p: string): string {
  const i = Math.max(p.lastIndexOf("/"), p.lastIndexOf("\\"));
  return i >= 0 ? p.slice(i + 1) : p;
}

export interface DerivedStatus {
  text: string;
  complete: boolean;
}

export function deriveStatus(events: PresentationEvent[]): DerivedStatus {
  if (events.length === 0) return { text: "Starting…", complete: false };

  const last = events[events.length - 1];
  if (last.kind === "result") {
    return { text: last.wire.is_success ? "Completed" : "Failed", complete: true };
  }

  for (let i = events.length - 1; i >= 0; i--) {
    const e = events[i];
    if (e.kind === "tool-use" && e.tool.result === undefined) {
      return { text: labelForTool(e.tool.name, e.tool.input), complete: false };
    }
  }

  if (last.kind === "assistant-text") return { text: "Thinking…", complete: false };
  if (last.kind === "thinking") return { text: "Thinking…", complete: false };
  return { text: "Working…", complete: false };
}

function labelForTool(name: string, input: Record<string, unknown>): string {
  const filePath = typeof input.file_path === "string" ? input.file_path : undefined;
  switch (name) {
    case "Bash":
      return "Running command";
    case "Read":
      return filePath ? `Reading ${basename(filePath)}` : "Reading";
    case "Edit":
    case "Write":
      return filePath ? `Editing ${basename(filePath)}` : "Editing";
    case "Glob":
    case "Grep":
      return "Searching";
    default:
      return `Running ${name}`;
  }
}
