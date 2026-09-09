import React, { useMemo, useState } from "react";
import { ChevronRight, Coins, LoaderCircle, Timer } from "lucide-react";
import { parseEventWireStream } from "../AgentViewer/parse-events";
import { deriveStatus } from "../AgentViewer/status";
import { AnimatedStatus } from "../AgentViewer/animated-status";
import { ToolUseCard } from "../AgentViewer/tool-use-card";
import type { PresentationEvent, ResultWire, ToolUsePresentation } from "../AgentViewer/types";
import { BlockMarkdown } from "../BlockMarkdown";
import "../AgentViewer/agent-output.css";

export type ActivityEntry =
  | { kind: "tool"; tool: ToolUsePresentation }
  | { kind: "thinking"; text: string };

export type TurnBlock = { kind: "text"; text: string } | { kind: "error"; message: string };

export interface TurnSummary {
  /** Tool calls and thinking in stream order; the collapsed activity list. */
  activity: ActivityEntry[];
  toolCount: number;
  /** What the reader sees expanded: the agent's prose and any failure. */
  blocks: TurnBlock[];
  result?: ResultWire;
}

/**
 * Folds one agent turn into the shape the chat shows: every tool call behind one collapsed line,
 * the prose in order, and the final response once. A text immediately followed by a result that
 * carries a response is that response repeated, so only the result's copy is kept.
 */
export function summarizeTurn(events: PresentationEvent[]): TurnSummary {
  const activity: ActivityEntry[] = [];
  const blocks: TurnBlock[] = [];
  let result: ResultWire | undefined;
  let toolCount = 0;

  events.forEach((event, index) => {
    switch (event.kind) {
      case "tool-use":
        activity.push({ kind: "tool", tool: event.tool });
        toolCount++;
        break;
      case "thinking":
        if (event.text.trim()) activity.push({ kind: "thinking", text: event.text });
        break;
      case "assistant-text": {
        const next = events[index + 1];
        const repeatedByResult =
          next?.kind === "result" && Boolean(next.wire.response && next.wire.response.trim().length > 0);
        if (!repeatedByResult && event.text.trim()) blocks.push({ kind: "text", text: event.text });
        break;
      }
      case "result":
        result = event.wire;
        if (event.wire.response?.trim()) blocks.push({ kind: "text", text: event.wire.response });
        if (!event.wire.is_success) blocks.push({ kind: "error", message: event.wire.error || "The agent run failed." });
        break;
      case "error":
        blocks.push({ kind: "error", message: event.message });
        break;
      default:
        break;
    }
  });

  return { activity, toolCount, blocks, result };
}

export const formatDuration = (ms: number): string => `${(ms / 1000).toFixed(1)}s`;

const formatTokens = (count: number): string => count.toLocaleString("en-US");

const formatCost = (cost: number): string => `$${cost.toFixed(cost < 1 ? 3 : 2)}`;

const TurnMeta: React.FC<{ wire: ResultWire }> = ({ wire }) => {
  const usage = wire.usage;
  const items: React.ReactNode[] = [];

  if (wire.duration_ms != null && wire.duration_ms > 0) {
    items.push(
      <span key="duration" className="chat-turn-meta-item" title="Duration">
        <Timer size={14} />
        {formatDuration(wire.duration_ms)}
      </span>,
    );
  }
  if (usage != null && (usage.input_tokens > 0 || usage.output_tokens > 0)) {
    items.push(
      <span key="tokens" className="chat-turn-meta-item" title="Tokens in / out">
        <Coins size={14} />
        {formatTokens(usage.input_tokens)} / {formatTokens(usage.output_tokens)}
      </span>,
    );
  }
  if (usage?.cost_usd != null && usage.cost_usd > 0) {
    items.push(
      <span key="cost" className="chat-turn-meta-item" title="Cost">
        {formatCost(usage.cost_usd)}
      </span>,
    );
  }

  if (items.length === 0) return null;
  return <div className="chat-turn-meta">{items}</div>;
};

const ActivityDisclosure: React.FC<{ activity: ActivityEntry[]; toolCount: number }> = ({ activity, toolCount }) => {
  const [open, setOpen] = useState(false);
  const running = activity.some((entry) => entry.kind === "tool" && entry.tool.result === undefined);
  const failed = activity.some((entry) => entry.kind === "tool" && entry.tool.isError);

  return (
    <div className="chat-tools" data-open={open}>
      <button
        type="button"
        className="chat-tools-toggle"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        {running && <LoaderCircle size={14} className="spin chat-tools-spinner" />}
        <span className="chat-tools-label">
          {toolCount} tool call{toolCount === 1 ? "" : "s"}
        </span>
        {failed && <span className="chat-tools-failed">with errors</span>}
        <ChevronRight size={16} className="chat-tools-chevron" />
      </button>
      {open && (
        <div className="aov-shell chat-tools-list">
          {activity.map((entry, index) =>
            entry.kind === "tool" ? (
              <ToolUseCard
                key={entry.tool.toolUseId || index}
                tool={{
                  name: entry.tool.name,
                  input: entry.tool.input,
                  result: entry.tool.result,
                  isError: entry.tool.isError,
                }}
              />
            ) : (
              <div key={`thinking-${index}`} className="chat-tools-thinking">
                <span className="chat-tools-thinking-label">Thinking</span>
                <div className="chat-tools-thinking-text">{entry.text}</div>
              </div>
            ),
          )}
        </div>
      )}
    </div>
  );
};

export interface AssistantTurnProps {
  /** The turn's event stream, one JSON event per line. */
  stream: string;
  /** Still arriving: shows the animated status instead of the finished turn's metrics. */
  live?: boolean;
}

/**
 * One assistant turn: the collapsed tool-call line, the prose, and either the animated status
 * (while streaming) or the duration and token figures of the finished run.
 */
export const AssistantTurn: React.FC<AssistantTurnProps> = ({ stream, live = false }) => {
  const events = useMemo(() => (stream ? parseEventWireStream(stream) : []), [stream]);
  const turn = useMemo(() => summarizeTurn(events), [events]);
  const status = useMemo(() => deriveStatus(events), [events]);

  return (
    <div className="chat-turn">
      {turn.toolCount > 0 && <ActivityDisclosure activity={turn.activity} toolCount={turn.toolCount} />}
      {turn.blocks.map((block, index) =>
        block.kind === "text" ? (
          <div key={index} className="chat-markdown-body">
            <BlockMarkdown content={block.text} />
          </div>
        ) : (
          <div key={index} className="chat-turn-error" role="alert">
            {block.message}
          </div>
        ),
      )}
      {live && !status.complete && (
        <div className="aov-shell chat-turn-status">
          <AnimatedStatus statusText="Thinking" isComplete={false} />
        </div>
      )}
      {!live && turn.result && <TurnMeta wire={turn.result} />}
    </div>
  );
};
