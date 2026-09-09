import React, { useEffect, useRef, useState } from "react";
import { CircleCheck, LoaderCircle } from "lucide-react";
import { Tooltip } from "./Tooltip";
import "./ui.css";

export interface StatusLineProps {
  /** What the agent is doing, e.g. "Waiting for Claude…". */
  statusText: string;
  isComplete?: boolean;
  showIcon?: boolean;
  /**
   * When the run started (ISO timestamp or epoch ms). While the run is live the elapsed time
   * ticks once a second; `elapsedMs` freezes it for a finished run.
   */
  startedAt?: string | number | null;
  elapsedMs?: number | null;
  /** Tokens consumed so far; hidden when null. */
  tokens?: number | null;
  /** Marks the figure as derived from the stream rather than reported, rendering "~16.8k". */
  tokensEstimated?: boolean;
  className?: string;
}

/** "45s", "12m 22s", "1h 3m" — the shape Claude Code's own status line uses. */
export const formatElapsed = (ms: number): string => {
  const total = Math.max(0, Math.floor(ms / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m ${seconds}s`;
  return `${seconds}s`;
};

/** "842", "16.8k", "1.2M". */
export const formatTokenCount = (count: number): string => {
  const value = Math.max(0, Math.round(count));
  if (value < 1000) return `${value}`;
  const trim = (n: number) => n.toFixed(1).replace(/\.0$/, "");
  if (value < 1_000_000) return `${trim(value / 1000)}k`;
  return `${trim(value / 1_000_000)}M`;
};

const toMillis = (value: string | number | null | undefined): number | null => {
  if (value == null) return null;
  const ms = typeof value === "number" ? value : Date.parse(value);
  return Number.isFinite(ms) ? ms : null;
};

/**
 * The bundle's one agent status line: a spinner, the live elapsed time and token count, and
 * the status message — `12m 22s · 16.8k tokens · Waiting for Claude…`. It is what every
 * surface that shows a thinking agent renders, so the chat and the agent viewer stay in step.
 */
export const StatusLine: React.FC<StatusLineProps> = ({
  statusText,
  isComplete = false,
  showIcon = true,
  startedAt,
  elapsedMs,
  tokens,
  tokensEstimated = false,
  className = "",
}) => {
  const startMs = toMillis(startedAt);
  const ticking = startMs != null && elapsedMs == null && !isComplete;
  const [now, setNow] = useState(() => Date.now());

  /* A stream's timestamps come from the agent's machine, `now` from this browser. A server
     running ahead of us would pin the timer at "0s" for the first seconds of every run, so the
     run is anchored on whichever is earlier: its reported start, or when we first saw it. */
  const seen = useRef<{ key: number | null; at: number }>({ key: null, at: 0 });
  if (seen.current.key !== startMs) seen.current = { key: startMs, at: Date.now() };
  const anchor = startMs == null ? null : Math.min(startMs, seen.current.at);

  useEffect(() => {
    if (!ticking) return;
    setNow(Date.now());
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, [ticking, startMs]);

  const elapsed = elapsedMs != null ? elapsedMs : anchor != null ? Math.max(0, now - anchor) : null;

  const parts: React.ReactNode[] = [];
  if (elapsed != null) {
    parts.push(
      <span key="elapsed" className="tui-status-elapsed">
        {formatElapsed(elapsed)}
      </span>,
    );
  }
  if (tokens != null && tokens > 0) {
    const text = `${tokensEstimated ? "~" : ""}${formatTokenCount(tokens)} tokens`;
    parts.push(
      tokensEstimated ? (
        <Tooltip key="tokens" content="Estimated from the stream so far">
          <span className="tui-status-tokens">{text}</span>
        </Tooltip>
      ) : (
        <span key="tokens" className="tui-status-tokens">
          {text}
        </span>
      ),
    );
  }

  return (
    <div
      className={`tui-status ${className}`.trim()}
      data-complete={isComplete}
      role="status"
      aria-live="polite"
    >
      {showIcon && (
        <span className="tui-status-icon" data-spin={!isComplete} aria-hidden="true">
          {isComplete ? <CircleCheck size={14} /> : <LoaderCircle size={14} />}
        </span>
      )}
      {/* The elapsed time re-renders every second; inside the live region it would be read out
          again every second, so only the message itself is announced. */}
      {parts.length > 0 && (
        <span className="tui-status-metrics" aria-hidden="true">
          {parts.map((part, index) => (
            <React.Fragment key={index}>
              {part}
              <span className="tui-status-sep" aria-hidden="true">
                ·
              </span>
            </React.Fragment>
          ))}
        </span>
      )}
      <span className="tui-status-text">{statusText}</span>
    </div>
  );
};
