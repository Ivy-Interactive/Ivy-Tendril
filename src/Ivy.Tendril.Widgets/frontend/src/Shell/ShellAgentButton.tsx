import React, { useCallback, useEffect } from "react";
import { BrandIcon } from "./brandIcons";
import { ShellWidgetProps, isEditableTarget, isModKey, modKeyLabel } from "./types";
import "./shell.css";

interface ShellAgentButtonProps extends ShellWidgetProps {
  label?: string;
  icon?: string;
  newChatLabel?: string;
  shortcutKey?: string;
}

/**
 * The coding-agent row: clicking it opens the latest agent session, the
 * "New chat" action (or Cmd/Ctrl+shortcutKey) starts a new one. The shortcut
 * is ignored while typing so it never fights select-all in inputs.
 */
export const ShellAgentButton: React.FC<ShellAgentButtonProps> = ({
  id,
  events = [],
  eventHandler,
  label = "Agent",
  icon,
  newChatLabel = "New chat",
  shortcutKey = "A",
}) => {
  const fireOpen = useCallback(() => {
    if (events.includes("OnOpen")) eventHandler("OnOpen", id, []);
  }, [events, eventHandler, id]);

  const fireNewChat = useCallback(() => {
    if (events.includes("OnNewChat")) eventHandler("OnNewChat", id, []);
  }, [events, eventHandler, id]);

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (
        isModKey(e) &&
        !e.shiftKey &&
        !e.altKey &&
        e.key.toLowerCase() === shortcutKey.toLowerCase() &&
        !isEditableTarget(e)
      ) {
        e.preventDefault();
        fireNewChat();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [fireNewChat, shortcutKey]);

  return (
    <div className="tsh-agent-wrap">
      <button className="tsh-agent" onClick={fireOpen} title={label}>
        <span className="tsh-agent-brand">
          <span className="tsh-agent-icon">
            <BrandIcon name={icon} size={16} />
          </span>
          <span className="tsh-agent-label">{label}</span>
        </span>
        <span className="tsh-agent-actions">
          <span
            className="tsh-agent-newchat"
            role="button"
            tabIndex={-1}
            onClick={(e) => {
              e.stopPropagation();
              fireNewChat();
            }}
          >
            {newChatLabel}
          </span>
          <span className="tsh-kbd">
            <span>{modKeyLabel()}</span>
            <span>{shortcutKey}</span>
          </span>
        </span>
      </button>
    </div>
  );
};
