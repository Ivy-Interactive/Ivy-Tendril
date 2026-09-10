import React from "react";
import { CircleCheck, LoaderCircle, MessageCircle, SquareTerminal } from "lucide-react";
import { ShellItemState, ShellSectionItemDto } from "./types";
import { Badge } from "../ui/Badge";
import "./shell.css";

const STATE_LABELS: Record<ShellItemState, string> = { working: "Working", completed: "Completed" };

const ItemStateIcon: React.FC<{ state: ShellItemState }> = ({ state }) => (
  <span className="tsh-section-item-state" data-state={state} role="img" aria-label={STATE_LABELS[state]}>
    {state === "working" ? <LoaderCircle size={12} className="tsh-spin" /> : <CircleCheck size={12} />}
  </span>
);

/** Maps a `ShellSectionItemDto.icon` name to its lucide component; unknown names render nothing. */
export const sectionItemIcons: Record<string, React.FC<{ size?: number }>> = {
  Terminal: SquareTerminal,
  MessageCircle: MessageCircle,
};

interface ShellSectionItemsProps {
  items: ShellSectionItemDto[];
  selectedId?: string;
  showBadges?: boolean;
  emptyText?: string;
  className?: string;
  onSelect: (itemId: string) => void;
}

/** The plan/chat rows, shared by the expanded sidebar list and the rail's flyout menu. */
export const ShellSectionItems: React.FC<ShellSectionItemsProps> = ({
  items,
  selectedId,
  showBadges = true,
  emptyText,
  className = "",
  onSelect,
}) => (
  <div className={`tsh-section-list ${className}`.trim()}>
    {items.length === 0 && emptyText && <div className="tsh-section-empty">{emptyText}</div>}
    {items.map((item) => {
      const ItemIcon = item.icon ? sectionItemIcons[item.icon] : undefined;
      const badges = showBadges ? item.badges : undefined;
      return (
        <button
          key={item.id}
          className="tsh-section-item"
          data-selected={item.id === selectedId}
          onClick={() => onSelect(item.id)}
        >
          <span className="tsh-section-item-top">
            {ItemIcon && (
              <span className="tsh-section-item-icon">
                <ItemIcon size={14} />
              </span>
            )}
            {item.state && <ItemStateIcon state={item.state} />}
            <span className="tsh-section-item-title">{item.title}</span>
            {item.tag && <span className="tsh-section-item-tag">{item.tag}</span>}
          </span>
          {badges && badges.length > 0 && (
            <span className="tsh-section-item-badges">
              {badges.map((badge, i) => (
                <Badge key={i} kind={badge.kind} color={badge.color}>
                  {badge.label}
                </Badge>
              ))}
            </span>
          )}
        </button>
      );
    })}
  </div>
);
