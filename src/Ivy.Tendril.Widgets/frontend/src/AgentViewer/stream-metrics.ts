import type { EventWire } from "./types";

/** The usual rule of thumb for English prose and code; see `tokensEstimated` below. */
const CHARS_PER_TOKEN = 4;

export interface StreamMetrics {
  /** Timestamp of the first event, so a caller can tick an elapsed timer against it. */
  startedAt?: string;
  /** Tokens consumed so far, or undefined when the stream says nothing yet. */
  tokens?: number;
  /**
   * True when the figure was derived from the stream's own text. Only the final `result` event
   * carries a reported usage, so a run in flight can be counted no other way — callers render
   * an estimate with a "~".
   */
  tokensEstimated: boolean;
}

/**
 * What a live run can say about itself: when it started and how much it has consumed. Both come
 * from the events already on the wire, so no agent has to report progress for the status line
 * to move.
 */
export function deriveStreamMetrics(wires: EventWire[]): StreamMetrics {
  let startedAt: string | undefined;
  let characters = 0;
  let reported = 0;

  for (const wire of wires) {
    if (!startedAt && wire.timestamp) startedAt = wire.timestamp;

    switch (wire.kind) {
      case "text":
        characters += wire.text?.length ?? 0;
        break;
      case "thinking":
        characters += wire.content?.length ?? 0;
        break;
      case "tool_call":
        characters += wire.tool_name.length + (wire.input ? JSON.stringify(wire.input).length : 0);
        break;
      case "tool_result":
        characters += wire.output?.length ?? 0;
        break;
      case "result":
        if (wire.usage) {
          reported += (wire.usage.input_tokens ?? 0) + (wire.usage.output_tokens ?? 0);
        }
        break;
      default:
        break;
    }
  }

  if (reported > 0) return { startedAt, tokens: reported, tokensEstimated: false };
  if (characters > 0) {
    return { startedAt, tokens: Math.round(characters / CHARS_PER_TOKEN), tokensEstimated: true };
  }
  return { startedAt, tokensEstimated: true };
}
