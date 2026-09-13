import React from "react";
import {
  Activity,
  ChartBar,
  Feather,
  File,
  FileText,
  GitPullRequest,
  Inbox,
  Info,
  Lightbulb,
  LucideIcon,
  MessageSquare,
  Plus,
  Rocket,
  Settings as SettingsIcon,
  Snowflake,
  SquareTerminal,
  ThumbsUp,
  X,
} from "lucide-react";
import { ShellTabDto, ShellWidgetProps } from "./types";
import "./shell.css";

interface ShellTabsProps extends ShellWidgetProps {
  tabs?: ShellTabDto[];
  selectedId?: string;
}

/* Only the page tab carries an icon name - the icon of the app it reveals - so this
   covers the [App] icons that can appear as a page. An unmapped name falls back to the
   neutral page glyph rather than the terminal one, which would misread the tab as a
   session; session tabs pass no name at all and always get the terminal glyph. */
const tabIcons: Record<string, LucideIcon> = {
  Activity,
  ChartBar,
  Feather,
  FileText,
  GitPullRequest,
  Inbox,
  Info,
  Lightbulb,
  MessageSquare,
  Rocket,
  Settings: SettingsIcon,
  Snowflake,
  ThumbsUp,
};

/** The session tab strip at the bottom of the content area, led by the page tab. */
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

  // With no sessions open the strip would be the page tab alone beside a "+", so it
  // stays hidden until a session exists (new sessions start from the sidebar agent row).
  // TendrilShell's `hasTabs` gates the surrounding row on the same condition, counting the
  // session tabs before the page tab is prepended - keep the two in step.
  if (tabs.every((tab) => tab.closable === false)) return null;

  return (
    <div className="tsh-tabs">
      {tabs.map((tab) => {
        const closable = tab.closable !== false;
        const TabIcon =
          (tab.icon ? tabIcons[tab.icon] : undefined) ?? (closable ? SquareTerminal : File);
        return (
          <div
            key={tab.id}
            className="tsh-tab"
            data-active={tab.id === selectedId}
            data-closable={closable}
            role="tab"
            aria-selected={tab.id === selectedId}
            tabIndex={0}
            onClick={() => fire("OnSelect", [tab.id])}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === " ") fire("OnSelect", [tab.id]);
            }}
            onAuxClick={(e) => {
              if (e.button === 1 && closable) fire("OnClose", [tab.id]);
            }}
          >
            <span className="tsh-tab-main">
              <TabIcon size={16} className="tsh-tab-icon" />
              <span className="tsh-tab-label">{tab.title}</span>
            </span>
            {closable && (
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
            )}
          </div>
        );
      })}
      <button className="tsh-tab-new" aria-label="New agent session" onClick={() => fire("OnNew")}>
        <Plus size={16} />
      </button>
    </div>
  );
};
