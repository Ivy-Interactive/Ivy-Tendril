import type { EventWire, PresentationEvent, ToolUsePresentation } from "./agent-output-types";

export function parseEventWireStream(jsonStream: string): PresentationEvent[] {
  const lines = jsonStream.split("\n");
  const events: EventWire[] = [];

  for (const line of lines) {
    const trimmed = line.trim();
    if (trimmed.length === 0) continue;
    try {
      const parsed = JSON.parse(trimmed);
      if (parsed && typeof parsed === "object" && "kind" in parsed) {
        events.push(parsed as EventWire);
      }
    } catch {
      // Skip malformed lines gracefully
    }
  }

  const toolMap = new Map<string, ToolUsePresentation>();
  const out: PresentationEvent[] = [];
  let pendingText: string | null = null;

  const flushText = () => {
    if (pendingText !== null && pendingText.trim().length > 0) {
      out.push({ kind: "assistant-text", text: pendingText });
    }
    pendingText = null;
  };

  for (const evt of events) {
    switch (evt.kind) {
      case "session_init":
        flushText();
        out.push({ kind: "system", model: evt.model, sessionId: evt.session_id });
        break;

      case "text":
        if (evt.delta) {
          pendingText = (pendingText ?? "") + evt.text;
        } else {
          flushText();
          pendingText = evt.text;
        }
        break;

      case "thinking":
        flushText();
        out.push({ kind: "thinking", text: evt.content });
        break;

      case "tool_call": {
        flushText();
        const tool: ToolUsePresentation = {
          toolUseId: evt.tool_use_id,
          name: evt.tool_name,
          description: evt.description,
          input: evt.input ?? {},
        };
        toolMap.set(evt.tool_use_id, tool);
        out.push({ kind: "tool-use", tool });
        break;
      }

      case "tool_result": {
        const existing = toolMap.get(evt.tool_use_id);
        if (existing) {
          existing.result = evt.output ?? "";
          existing.isError = evt.is_error;
        }
        break;
      }

      case "result":
        flushText();
        out.push({ kind: "result", wire: evt });
        break;

      case "error":
        flushText();
        out.push({ kind: "error", message: evt.message });
        break;

      // file_change, permission_request, permission_denial, user_question
      // are not rendered as visible events in the output view
      default:
        break;
    }
  }

  flushText();
  return out;
}
