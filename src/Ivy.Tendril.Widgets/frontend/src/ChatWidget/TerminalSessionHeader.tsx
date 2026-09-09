import React, { useCallback } from "react";
import { ChatHeader } from "./ChatHeader";
import type { ChatJobDto, IvyEventHandler } from "./types";
import "./chat-widget.css";

export interface TerminalSessionHeaderProps {
  id: string;
  events?: string[];
  eventHandler?: IvyEventHandler;
  sessionId: string;
  title?: string;
  jobs?: ChatJobDto[];
  spawned?: boolean;
}

/**
 * Standalone chat header for a terminal-hosted session: always the black
 * `chat-header--terminal` variant, wired directly to session events without
 * ChatWidget's optimistic-rename bookkeeping.
 */
export const TerminalSessionHeader: React.FC<TerminalSessionHeaderProps> = ({
  id,
  events = [],
  eventHandler,
  sessionId,
  title = "New Chat",
  jobs = [],
  spawned = false,
}) => {
  const emit = useCallback(
    (eventName: string, ...args: unknown[]) => {
      if (eventHandler && events.includes(eventName)) {
        eventHandler(eventName, id, args);
      }
    },
    [eventHandler, events, id],
  );

  return (
    <div className="chat-header-host chat-header-host--terminal">
      <ChatHeader
        variant="terminal"
        title={title}
        editable
        jobs={jobs}
        spawned={spawned}
        onRename={(newTitle) => emit("OnRenameSession", [sessionId, newTitle])}
        onDelete={() => emit("OnDeleteSession", sessionId)}
        onNewChat={() => emit("OnCreateSession")}
        onOpenPlan={(planId) => emit("OnOpenPlan", planId)}
        onReviewJobs={() => emit("OnReviewJobs")}
      />
    </div>
  );
};
