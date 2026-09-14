import React, { useMemo, useState } from "react";
import { ChevronRight, Clock, Coins, LoaderCircle, Timer } from "lucide-react";
import { parseEventWires, presentEventWires } from "../AgentViewer/parse-events";
import { deriveStatus } from "../AgentViewer/status";
import { useHeldStatus } from "../AgentViewer/useHeldStatus";
import { deriveStreamMetrics } from "../AgentViewer/stream-metrics";
import { StatusLine } from "../ui/StatusLine";
import { Tooltip } from "../ui/Tooltip";
import { ToolUseCard } from "../AgentViewer/tool-use-card";
import type { PresentationEvent, ResultWire, ToolUsePresentation } from "../AgentViewer/types";
import { BlockMarkdown } from "../BlockMarkdown";
import "../AgentViewer/agent-output.css";

export type ActivityEntry =
  | { kind: "tool"; tool: ToolUsePresentation }
  | { kind: "thinking"; text: string };

export type ActivityGroup = { kind: "activity"; entries: ActivityEntry[]; toolCount: number };

export type TurnSegment = ActivityGroup | { kind: "text"; text: string } | { kind: "error"; message: string };

export interface TurnSummary {
  /**
   * The turn in stream order: prose, failures, and the tool calls (with their thinking) that ran
   * between them, each run of calls folded into one collapsed line.
   */
  segments: TurnSegment[];
  toolCount: number;
  result?: ResultWire;
}

/**
 * Folds one agent turn into the shape the chat shows: the prose in order with the tool calls
 * where they happened, and the final response once. A text immediately followed by a result that
 * carries a response is that response repeated, so only the result's copy is kept.
 */
export function summarizeTurn(events: PresentationEvent[]): TurnSummary {
  const segments: TurnSegment[] = [];
  let result: ResultWire | undefined;
  let toolCount = 0;

  const currentActivity = (): ActivityGroup => {
    const last = segments[segments.length - 1];
    if (last?.kind === "activity") return last;
    const group: ActivityGroup = { kind: "activity", entries: [], toolCount: 0 };
    segments.push(group);
    return group;
  };

  events.forEach((event, index) => {
    switch (event.kind) {
      case "tool-use": {
        const group = currentActivity();
        group.entries.push({ kind: "tool", tool: event.tool });
        group.toolCount++;
        toolCount++;
        break;
      }
      case "thinking":
        if (event.text.trim()) currentActivity().entries.push({ kind: "thinking", text: event.text });
        break;
      case "assistant-text": {
        const next = events[index + 1];
        const repeatedByResult =
          next?.kind === "result" && Boolean(next.wire.response && next.wire.response.trim().length > 0);
        if (!repeatedByResult && event.text.trim()) segments.push({ kind: "text", text: event.text });
        break;
      }
      case "result":
        result = event.wire;
        if (event.wire.response?.trim()) segments.push({ kind: "text", text: event.wire.response });
        if (!event.wire.is_success) segments.push({ kind: "error", message: event.wire.error || "The agent run failed." });
        break;
      case "error":
        segments.push({ kind: "error", message: event.message });
        break;
      default:
        break;
    }
  });

  // Thinking with no tool call beside it has no line to sit under.
  return {
    segments: segments.filter((segment) => segment.kind !== "activity" || segment.toolCount > 0),
    toolCount,
    result,
  };
}

export const formatDuration = (ms: number): string => `${(ms / 1000).toFixed(1)}s`;

export const formatClockTime = (iso: string): string => {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  return date.toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit", hour12: true });
};

const formatTokens = (count: number): string => count.toLocaleString("en-US");

const formatCost = (cost: number): string => `$${cost.toFixed(cost < 1 ? 3 : 2)}`;

const TurnMeta: React.FC<{ wire?: ResultWire; completedAt?: string }> = ({ wire, completedAt }) => {
  const usage = wire?.usage;
  const items: React.ReactNode[] = [];

  if (wire && wire.duration_ms != null && wire.duration_ms > 0) {
    items.push(
      <Tooltip key="duration" content="Duration">
        <span className="chat-turn-meta-item">
          <Timer size={14} />
          {formatDuration(wire.duration_ms)}
        </span>
      </Tooltip>,
    );
  }
  if (wire && usage != null && (usage.input_tokens > 0 || usage.output_tokens > 0)) {
    items.push(
      <Tooltip key="tokens" content="Tokens in / out">
        <span className="chat-turn-meta-item">
          <Coins size={14} />
          {formatTokens(usage.input_tokens)} / {formatTokens(usage.output_tokens)}
        </span>
      </Tooltip>,
    );
  }
  if (wire && usage?.cost_usd != null && usage.cost_usd > 0) {
    items.push(
      <Tooltip key="cost" content="Cost">
        <span className="chat-turn-meta-item">{formatCost(usage.cost_usd)}</span>
      </Tooltip>,
    );
  }
  if (completedAt) {
    const formattedTime = formatClockTime(completedAt);
    if (formattedTime) {
      items.push(
        <Tooltip key="completed" content="Completed">
          <span className="chat-turn-meta-item">
            <Clock size={14} />
            {formattedTime}
          </span>
        </Tooltip>,
      );
    }
  }

  if (items.length === 0) return null;
  return <div className="chat-turn-meta">{items}</div>;
};

const ActivityDisclosure: React.FC<{ group: ActivityGroup }> = ({ group }) => {
  const { entries: activity, toolCount } = group;
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
                minimal
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
  /** ISO 8601 timestamp of when the turn completed. */
  completedAt?: string;
}

/**
 * One assistant turn: the prose with each run of tool calls collapsed into a line where it
 * happened, and either the animated status (while streaming) or the duration and token figures
 * of the finished run.
 */
export const AssistantTurn: React.FC<AssistantTurnProps> = ({ stream, live = false, completedAt }) => {
  /* One parse per stream update; the turn, its status and its metrics all derive from it. */
  const wires = useMemo(() => (stream ? parseEventWires(stream) : []), [stream]);
  const events = useMemo(() => presentEventWires(wires), [wires]);
  const turn = useMemo(() => summarizeTurn(events), [events]);
  const derived = useMemo(() => deriveStatus(events), [events]);
  const status = useHeldStatus(derived);
  const metrics = useMemo(() => deriveStreamMetrics(wires), [wires]);

  return (
    <div className="chat-turn">
      {turn.segments.map((segment, index) =>
        segment.kind === "activity" ? (
          <ActivityDisclosure key={index} group={segment} />
        ) : segment.kind === "text" ? (
          <div key={index} className="chat-markdown-body">
            <BlockMarkdown content={segment.text} />
          </div>
        ) : (
          <div key={index} className="chat-turn-error" role="alert">
            {segment.message}
          </div>
        ),
      )}
      {live && !status.complete && (
        <div className="aov-shell chat-turn-status">
          <StatusLine
            statusText={status.text}
            startedAt={metrics.startedAt}
            tokens={metrics.tokens}
            tokensEstimated={metrics.tokensEstimated}
          />
        </div>
      )}
      {!live && (turn.result || completedAt) && <TurnMeta wire={turn.result} completedAt={completedAt} />}
    </div>
  );
};
