import React from "react";
import { Search } from "lucide-react";
import { ShellSectionItemDto, ShellWidgetProps } from "./types";
import "./shell.css";

interface ShellSidebarSectionProps extends ShellWidgetProps {
  title?: string;
  items?: ShellSectionItemDto[];
  selectedId?: string;
  searchable?: boolean;
  emptyText?: string;
}

/**
 * The contextual list under the nav — plans for Review/Drafts, recommendations,
 * etc. Published by the active app; hidden entirely in the collapsed rail
 * (via CSS) since there is no icon-only representation of a plan list.
 */
export const ShellSidebarSection: React.FC<ShellSidebarSectionProps> = ({
  id,
  events = [],
  eventHandler,
  title,
  items = [],
  selectedId,
  searchable = false,
  emptyText,
}) => {
  const select = (itemId: string) => {
    if (events.includes("OnSelectItem")) eventHandler("OnSelectItem", id, [itemId]);
  };

  const openSearch = () => {
    if (events.includes("OnSearch")) eventHandler("OnSearch", id, []);
  };

  const hasHeader = !!title || searchable;

  return (
    <div className="tsh-section" data-headerless={!hasHeader}>
      {hasHeader && (
        <div className="tsh-section-header">
          <span className="tsh-section-title">{title}</span>
          {searchable && (
            <button className="tsh-section-search" onClick={openSearch} aria-label="Search plans">
              <Search size={16} />
            </button>
          )}
        </div>
      )}
      <div className="tsh-section-list">
        {items.length === 0 && emptyText && <div className="tsh-section-empty">{emptyText}</div>}
        {items.map((item) => (
          <button
            key={item.id}
            className="tsh-section-item"
            data-selected={item.id === selectedId}
            onClick={() => select(item.id)}
          >
            <span className="tsh-section-item-top">
              <span className="tsh-section-item-title">{item.title}</span>
              {item.tag && <span className="tsh-section-item-tag">{item.tag}</span>}
            </span>
            {item.badges && item.badges.length > 0 && (
              <span className="tsh-section-item-badges">
                {item.badges.map((badge, i) => (
                  <span key={i} className="tsh-badge" data-kind={badge.kind}>
                    {badge.label}
                  </span>
                ))}
              </span>
            )}
          </button>
        ))}
      </div>
    </div>
  );
};
