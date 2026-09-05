import React from "react";
import { Plus, SquareTerminal, X } from "lucide-react";
import { ShellTabDto, ShellWidgetProps } from "./types";
import "./shell.css";

interface ShellTabsProps extends ShellWidgetProps {
  tabs?: ShellTabDto[];
  selectedId?: string;
}

/** The agent-session tab strip at the bottom of the content area. */
export const ShellTabs: React.FC<ShellTabsProps> = ({
  id,
  events = [],
  eventHandler,
  tabs = [],
  selectedId,
}) => {
  const fire = (eventName: string, args: unknown[] = []) => {
    if (events.includes(eventName)) eventHandler(eventName, id, args);
  };

  // With no sessions open the strip would be an empty band with a lone "+",
  // so it stays hidden until the first tab exists (new sessions start from the
  // sidebar agent row instead).
  if (tabs.length === 0) return null;

  return (
    <div className="tsh-tabs">
      {tabs.map((tab) => (
        <div
          key={tab.id}
          className="tsh-tab"
          data-active={tab.id === selectedId}
          role="tab"
          aria-selected={tab.id === selectedId}
          tabIndex={0}
          onClick={() => fire("OnSelect", [tab.id])}
          onKeyDown={(e) => {
            if (e.key === "Enter" || e.key === " ") fire("OnSelect", [tab.id]);
          }}
          onAuxClick={(e) => {
            if (e.button === 1) fire("OnClose", [tab.id]);
          }}
        >
          <span className="tsh-tab-main">
            <SquareTerminal size={16} className="tsh-tab-icon" />
            <span className="tsh-tab-label">{tab.title}</span>
          </span>
          <button
            className="tsh-tab-close"
            aria-label={`Close ${tab.title}`}
            onClick={(e) => {
              e.stopPropagation();
              fire("OnClose", [tab.id]);
            }}
          >
            <X size={16} />
          </button>
        </div>
      ))}
      <button className="tsh-tab-new" aria-label="New agent session" onClick={() => fire("OnNew")}>
        <Plus size={16} />
      </button>
    </div>
  );
};
