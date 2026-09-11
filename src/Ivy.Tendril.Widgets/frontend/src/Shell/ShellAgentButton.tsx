import React, { useCallback, useEffect } from "react";
import { Pin } from "lucide-react";
import { BrandIcon } from "./brandIcons";
import { useShell } from "./ShellContext";
import { ShellSectionItemDto, ShellWidgetProps, isEditableTarget, isModKey } from "./types";
import { ShellRailFlyout, RailFlyoutTrigger } from "./ShellRailFlyout";
import { ShellTooltip } from "./ShellTooltip";
import { Badge } from "../ui/Badge";
import { Kbd } from "../ui/Kbd";
import { NEW_CHAT_SHORTCUT_KEY, modAltKeys } from "../ui/shortcuts";
import "./shell.css";

const NEW_CHAT_LABEL = "New Chat";

interface ShellAgentButtonProps extends ShellWidgetProps {
  label?: string;
  icon?: string;
  shortcutKey?: string;
  isActive?: boolean;
  items?: ShellSectionItemDto[];
  selectedId?: string;
  listTitle?: string;
}

/**
 * The coding-agent row: clicking it opens the latest agent session, and
 * Cmd+Opt+shortcutKey (macOS) / Ctrl+Alt+shortcutKey (Windows/Linux) starts a
 * new one. The shortcut is ignored while typing. On the collapsed rail, while
 * the chat list is published, hovering the row floats that list beside it (the
 * icon turns into a pin) and clicking pins the list open.
 */
export const ShellAgentButton: React.FC<ShellAgentButtonProps> = ({
  id,
  events = [],
  eventHandler,
  label = "Agent",
  icon,
  shortcutKey = NEW_CHAT_SHORTCUT_KEY,
  isActive = false,
  items,
  selectedId,
  listTitle,
}) => {
  const { collapsed } = useShell();

  const fireOpen = useCallback(() => {
    if (events.includes("OnOpen")) eventHandler("OnOpen", id, []);
  }, [events, eventHandler, id]);

  const fireNewChat = useCallback(() => {
    if (events.includes("OnNewChat")) eventHandler("OnNewChat", id, []);
  }, [events, eventHandler, id]);

  const fireSelect = useCallback(
    (itemId: string) => {
      if (events.includes("OnSelectItem")) eventHandler("OnSelectItem", id, [itemId]);
    },
    [events, eventHandler, id],
  );

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

  const hintKeys = modAltKeys(shortcutKey);
  const hasList = collapsed && !!items?.length;
  const count = items && items.length > 99 ? "99" : String(items?.length ?? 0);

  const renderButton = (trigger?: RailFlyoutTrigger) => (
    <button
      ref={trigger?.ref}
      className="tsh-agent"
      data-active={isActive}
      data-has-list={!!trigger}
      data-open={trigger?.open}
      data-pinned={trigger?.pinned}
      aria-expanded={trigger ? trigger.open : undefined}
      onPointerEnter={trigger?.onPointerEnter}
      onPointerLeave={trigger?.onPointerLeave}
      onClick={trigger ? trigger.onClick : fireOpen}
      aria-label={label}
    >
      <span className="tsh-row">
        <span className="tsh-agent-brand">
          <span className="tsh-agent-icon">
            <BrandIcon name={icon} size={16} />
            {trigger && (
              <span className="tsh-agent-pin" aria-hidden="true">
                <Pin size={16} />
              </span>
            )}
          </span>
          <span className="tsh-agent-label">{label}</span>
        </span>
        <span className="tsh-agent-actions">
          <Kbd keys={hintKeys} variant="bare" className="tsh-kbd" />
        </span>
      </span>
      {trigger && (
        <Badge numeric className="tsh-nav-badge tsh-agent-count">
          {count}
        </Badge>
      )}
    </button>
  );

  return (
    <div className="tsh-agent-wrap">
      {hasList ? (
        <ShellRailFlyout
          title={listTitle}
          items={items}
          selectedId={selectedId}
          emptyText="No chats yet"
          onSelect={fireSelect}
          newLabel="New chat"
          onNew={fireNewChat}
        >
          {renderButton}
        </ShellRailFlyout>
      ) : (
        <ShellTooltip content={NEW_CHAT_LABEL} shortcut={hintKeys} side="right">
          {renderButton()}
        </ShellTooltip>
      )}
    </div>
  );
};
