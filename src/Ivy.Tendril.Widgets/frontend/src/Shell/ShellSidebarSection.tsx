import React, { useCallback, useEffect } from "react";
import { Plus, Search, SquareTerminal } from "lucide-react";
import { useShell } from "./ShellContext";
import {
  ShellSectionItemDto,
  ShellWidgetProps,
  isEditableTarget,
  isModKey,
  modKeyLabel,
} from "./types";
import { ShellTooltip } from "./ShellTooltip";
import { Badge } from "../ui/Badge";
import { Kbd } from "../ui/Kbd";
import "./shell.css";

const SEARCH_SHORTCUT_KEY = "K";

/** Maps a `ShellSectionItemDto.icon` name to its lucide component; unknown names render nothing. */
const sectionItemIcons: Record<string, React.FC<{ size?: number }>> = {
  Terminal: SquareTerminal,
};

interface ShellSidebarSectionProps extends ShellWidgetProps {
  title?: string;
  items?: ShellSectionItemDto[];
  selectedId?: string;
  searchable?: boolean;
  /** The search icon's tooltip and accessible name; the section defaults to plans. */
  searchLabel?: string;
  emptyText?: string;
  /** Shows a "+" button in the header (e.g. "New chat"); fires OnNew. */
  newLabel?: string;
}

/**
 * The contextual list under the nav: plans for Review/Drafts, recommendations,
 * etc. Published by the active app. In the collapsed rail the list shrinks to
 * narrow ID chips (the row tags, e.g. "#40") with the search button above.
 * Without a list (other apps, or an app whose list is empty) the header slot
 * holds a full-width Search button instead of the title, and Cmd/Ctrl+K opens
 * the search from anywhere in the shell.
 */
export const ShellSidebarSection: React.FC<ShellSidebarSectionProps> = ({
  id,
  events = [],
  eventHandler,
  title,
  items = [],
  selectedId,
  searchable = false,
  searchLabel = "Search plans",
  emptyText,
  newLabel,
}) => {
  const select = (itemId: string) => {
    if (events.includes("OnSelectItem")) eventHandler("OnSelectItem", id, [itemId]);
  };

  const openSearch = useCallback(() => {
    if (events.includes("OnSearch")) eventHandler("OnSearch", id, []);
  }, [events, eventHandler, id]);

  const createNew = useCallback(() => {
    if (events.includes("OnNew")) eventHandler("OnNew", id, []);
  }, [events, eventHandler, id]);

  useEffect(() => {
    if (!searchable) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (
        isModKey(e) &&
        !e.shiftKey &&
        !e.altKey &&
        e.key.toLowerCase() === SEARCH_SHORTCUT_KEY.toLowerCase() &&
        !isEditableTarget(e)
      ) {
        e.preventDefault();
        openSearch();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [searchable, openSearch]);

  const { collapsed } = useShell();
  const shortcutHint = `${modKeyLabel()}+${SEARCH_SHORTCUT_KEY}`;

  const hasHeader = !!title || searchable;
  const showSearchButton = searchable && (!title || (items.length === 0 && !newLabel));

  if (collapsed) {
    return (
      <div className="tsh-section tsh-section-rail">
        {newLabel && (
          <ShellTooltip content={newLabel} side="right">
            <button className="tsh-rail-new" onClick={createNew} aria-label={newLabel}>
              <Plus size={16} />
            </button>
          </ShellTooltip>
        )}
        {searchable && (
          <ShellTooltip content={searchLabel} shortcut={shortcutHint} side="right">
            <button className="tsh-rail-search" onClick={openSearch} aria-label={searchLabel}>
              <Search size={16} />
            </button>
          </ShellTooltip>
        )}
        <div className="tsh-rail-list">
          {items.map(
            (item) =>
              item.tag && (
                <ShellTooltip
                  key={item.id}
                  side="right"
                  className="tsh-rail-tooltip"
                  content={
                    <div>
                      <div className="tsh-rail-tooltip-title">{item.title}</div>
                      {item.badges && item.badges.length > 0 && (
                        <div className="tsh-rail-tooltip-badges">
                          {item.badges.map((badge, i) => (
                            <Badge key={i} kind={badge.kind}>
                              {badge.label}
                            </Badge>
                          ))}
                        </div>
                      )}
                    </div>
                  }
                >
                  <button
                    className="tsh-rail-item"
                    data-selected={item.id === selectedId}
                    onClick={() => select(item.id)}
                    aria-label={item.title}
                  >
                    <span className="tsh-rail-item-text">{item.tag}</span>
                  </button>
                </ShellTooltip>
              ),
          )}
        </div>
      </div>
    );
  }

  return (
    <div className="tsh-section" data-headerless={!hasHeader}>
      {hasHeader && showSearchButton && (
        <div className="tsh-section-header" data-search-button="true">
          <ShellTooltip content={searchLabel} shortcut={shortcutHint} side="right">
            <button
              className="tsh-section-search-button"
              onClick={openSearch}
              aria-label={searchLabel}
            >
              <span className="tsh-section-search-button-main">
                <Search size={16} />
                <span className="tsh-section-search-button-label">Search</span>
              </span>
              <Kbd keys={[modKeyLabel(), SEARCH_SHORTCUT_KEY]} variant="bare" className="tsh-kbd" />
            </button>
          </ShellTooltip>
        </div>
      )}
      {hasHeader && !showSearchButton && (
        <div className="tsh-section-header">
          <span className="tsh-section-title">{title}</span>
          <span className="tsh-section-header-actions">
            {newLabel && (
              <ShellTooltip content={newLabel} side="right">
                <button className="tsh-section-new" onClick={createNew} aria-label={newLabel}>
                  <Plus size={16} />
                </button>
              </ShellTooltip>
            )}
            {searchable && (
              <ShellTooltip content={searchLabel} shortcut={shortcutHint} side="right">
                <button className="tsh-section-search" onClick={openSearch} aria-label={searchLabel}>
                  <Search size={16} />
                </button>
              </ShellTooltip>
            )}
          </span>
        </div>
      )}
      <div className="tsh-section-list">
        {items.length === 0 && emptyText && <div className="tsh-section-empty">{emptyText}</div>}
        {items.map((item) => {
          const ItemIcon = item.icon ? sectionItemIcons[item.icon] : undefined;
          return (
            <button
              key={item.id}
              className="tsh-section-item"
              data-selected={item.id === selectedId}
              onClick={() => select(item.id)}
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
              {item.badges && item.badges.length > 0 && (
                <span className="tsh-section-item-badges">
                  {item.badges.map((badge, i) => (
                    <Badge key={i} kind={badge.kind}>
                      {badge.label}
                    </Badge>
                  ))}
                </span>
              )}
            </button>
          );
        })}
      </div>
    </div>
  );
};
