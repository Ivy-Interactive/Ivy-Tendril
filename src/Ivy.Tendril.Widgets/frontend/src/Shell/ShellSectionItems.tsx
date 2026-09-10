import React from "react";
import { MessageCircle, SquareTerminal } from "lucide-react";
import { ShellSectionItemDto } from "./types";
import "./shell.css";

/** Maps a `ShellSectionItemDto.icon` name to its lucide component; unknown names render nothing. */
const sectionItemIcons: Record<string, React.FC<{ size?: number }>> = {
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
            <span className="tsh-section-item-title">{item.title}</span>
            {item.tag && <span className="tsh-section-item-tag">{item.tag}</span>}
          </span>
          {badges && badges.length > 0 && (
            <span className="tsh-section-item-badges">
              {badges.map((badge, i) => (
                <span key={i} className="tsh-badge" data-kind={badge.kind}>
                  {badge.label}
                </span>
              ))}
            </span>
          )}
        </button>
      );
    })}
  </div>
);
