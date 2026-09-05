import React, { useCallback, useEffect } from "react";
import { BrandIcon } from "./brandIcons";
import { ShellWidgetProps, isEditableTarget, isModKey, isMac } from "./types";
import "./shell.css";

interface ShellAgentButtonProps extends ShellWidgetProps {
  label?: string;
  icon?: string;
  shortcutKey?: string;
  isActive?: boolean;
}

/**
 * The coding-agent row: clicking it opens the latest agent session, and
 * Cmd+Opt+shortcutKey (macOS) / Ctrl+Alt+shortcutKey (Windows/Linux) starts a
 * new one. The shortcut is ignored while typing.
 */
export const ShellAgentButton: React.FC<ShellAgentButtonProps> = ({
  id,
  events = [],
  eventHandler,
  label = "Agent",
  icon,
  shortcutKey = "A",
  isActive = false,
}) => {
  const fireOpen = useCallback(() => {
    if (events.includes("OnOpen")) eventHandler("OnOpen", id, []);
  }, [events, eventHandler, id]);

  const fireNewChat = useCallback(() => {
    if (events.includes("OnNewChat")) eventHandler("OnNewChat", id, []);
  }, [events, eventHandler, id]);

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      // shortcutKey is a single letter, so the Key prefix composes correctly
      if (
        isModKey(e) &&
        e.altKey &&
        !e.shiftKey &&
        (e.code === `Key${shortcutKey.toUpperCase()}` ||
          e.key.toLowerCase() === shortcutKey.toLowerCase()) &&
        !isEditableTarget(e)
      ) {
        e.preventDefault();
        fireNewChat();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [fireNewChat, shortcutKey]);

  const hintKeys = isMac() ? ["⌘", "⌥", shortcutKey] : ["Ctrl", "Alt", shortcutKey];

  return (
    <div className="tsh-agent-wrap">
      <button
        className="tsh-agent"
        data-active={isActive}
        onClick={fireOpen}
        title={`${label} (${hintKeys.join("+")})`}
      >
        <span className="tsh-agent-brand">
          <span className="tsh-agent-icon">
            <BrandIcon name={icon} size={16} />
          </span>
          <span className="tsh-agent-label">{label}</span>
        </span>
        <span className="tsh-agent-actions">
          <span className="tsh-kbd">
            {hintKeys.map((k) => (
              <span key={k}>{k}</span>
            ))}
          </span>
        </span>
      </button>
    </div>
  );
};
