import React, { useCallback, useEffect, useMemo, useState } from "react";
import Markdown from "react-markdown";
import rehypeHighlight from "rehype-highlight";
import remarkGfm from "remark-gfm";
import "./agent-output-view.css";
import type { EventHandler, PresentationEvent } from "./types";
import { getHeight, getWidth } from "./styles";
import { useAutoScroll } from "./use-auto-scroll";
import { parseEventWireStream } from "./parse-events";
import { deriveStatus } from "./status";
import { AnimatedStatus } from "./animated-status";
import { ToolUseCard } from "./tool-use-card";
import { ResultSummary } from "./result-summary";

function buildSuppressIndices(events: PresentationEvent[]): Set<number> {
  const indices = new Set<number>();
  for (let i = 0; i < events.length - 1; i++) {
    const cur = events[i];
    const next = events[i + 1];
    if (
      cur.kind === "assistant-text" &&
      next.kind === "result" &&
      next.wire.response?.trim() === cur.text.trim()
    ) {
      indices.add(i);
    }
  }
  return indices;
}

type StreamSubscriber = (streamId: string, onData: (data: unknown) => void) => () => void;

interface AgentOutputViewProps {
  id: string;
  width?: string;
  height?: string;
  onIvyEvent: EventHandler;
  events?: string[];
  jsonStream?: string;
  stream?: { id: string };
  subscribeToStream?: StreamSubscriber;
  autoScroll?: boolean;
  showThinking?: boolean;
  showSystemEvents?: boolean;
  showStatusLabel?: boolean;
  statusLabelOverride?: string;
  resetToken?: number;
}

export const AgentOutputView: React.FC<AgentOutputViewProps> = ({
  id,
  width,
  height,
  onIvyEvent,
  events: enabledEvents = [],
  jsonStream,
  stream,
  subscribeToStream,
  autoScroll = true,
  showThinking = false,
  showSystemEvents = false,
  showStatusLabel = true,
  statusLabelOverride,
  resetToken = 0,
}) => {
  const [streamedLines, setStreamedLines] = useState<string[]>([]);

  useEffect(() => {
    setStreamedLines([]);
  }, [resetToken, jsonStream]);

  useEffect(() => {
    if (!stream?.id || !subscribeToStream) return;
    const unsubscribe = subscribeToStream(stream.id, (data) => {
      if (typeof data === "string") {
        setStreamedLines((prev) => [...prev, data]);
      }
    });
    return unsubscribe;
  }, [stream?.id, subscribeToStream]);

  const combinedStream = useMemo(() => {
    const parts: string[] = [];
    if (jsonStream) parts.push(jsonStream);
    if (streamedLines.length > 0) parts.push(streamedLines.join("\n"));
    return parts.join("\n");
  }, [jsonStream, streamedLines]);

  const parsedEvents = useMemo<PresentationEvent[]>(
    () => parseEventWireStream(combinedStream),
    [combinedStream],
  );

  const derived = useMemo(() => deriveStatus(parsedEvents), [parsedEvents]);
  const statusText = statusLabelOverride ?? derived.text;
  const isComplete = derived.complete;

  const { scrollRef, disableAutoScroll } = useAutoScroll({
    content: parsedEvents,
    enabled: autoScroll,
    smooth: false,
  });

  const handleComplete = useCallback(
    (resultJson: string) => {
      if (enabledEvents.includes("OnComplete")) {
        onIvyEvent("OnComplete", id, [resultJson]);
      }
    },
    [enabledEvents, onIvyEvent, id],
  );

  useEffect(() => {
    const last = parsedEvents[parsedEvents.length - 1];
    if (last && last.kind === "result") {
      handleComplete(JSON.stringify(last.wire));
    }
  }, [parsedEvents, handleComplete]);

  const shellStyle: React.CSSProperties = {
    boxSizing: "border-box",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    ...getWidth(width),
    ...getHeight(height),
  };

  const suppressIndices = useMemo(
    () => buildSuppressIndices(parsedEvents),
    [parsedEvents],
  );

  return (
    <div style={shellStyle} className="aov-shell">
      <div
        ref={scrollRef}
        className="aov-body"
        onWheel={autoScroll ? disableAutoScroll : undefined}
        onTouchMove={autoScroll ? disableAutoScroll : undefined}
      >
        {parsedEvents.map((event, idx) => {
          if (suppressIndices.has(idx)) return null;
          switch (event.kind) {
            case "tool-use":
              return (
                <ToolUseCard
                  key={idx}
                  tool={{
                    name: event.tool.name,
                    input: event.tool.input,
                    result: event.tool.result,
                    isError: event.tool.isError,
                  }}
                />
              );
            case "system":
              if (!showSystemEvents) return null;
              return (
                <div key={idx} className="aov-system">
                  session: {event.sessionId ?? "init"}
                  {event.model ? ` (${event.model})` : ""}
                </div>
              );
            case "thinking":
              if (!showThinking) return null;
              return (
                <div key={idx} className="aov-thinking">
                  {event.text}
                </div>
              );
            case "assistant-text":
              return (
                <div key={idx} className="aov-markdown aov-assistant">
                  <Markdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeHighlight]}>
                    {event.text}
                  </Markdown>
                </div>
              );
            case "result":
              return <ResultSummary key={idx} wire={event.wire} />;
            case "error":
              return (
                <div key={idx} className="aov-result error">
                  <div className="aov-result-header">
                    <span className="aov-result-title">✗ Error</span>
                  </div>
                  <div className="aov-result-body">{event.message}</div>
                </div>
              );
            default:
              return null;
          }
        })}
        {showStatusLabel && (
          <div className="aov-status-row">
            <AnimatedStatus statusText={statusText} isComplete={isComplete} />
          </div>
        )}
      </div>
    </div>
  );
};
