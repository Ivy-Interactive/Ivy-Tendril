import React, { useCallback, useEffect } from "react";
import { Search } from "lucide-react";
import { useShell } from "./ShellContext";
import {
  ShellSectionItemDto,
  ShellWidgetProps,
  isEditableTarget,
  isModKey,
  modKeyLabel,
} from "./types";
import { ShellTooltip } from "./ShellTooltip";
import "./shell.css";

const SEARCH_SHORTCUT_KEY = "K";

interface ShellSidebarSectionProps extends ShellWidgetProps {
  title?: string;
  items?: ShellSectionItemDto[];
  selectedId?: string;
  searchable?: boolean;
  emptyText?: string;
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
  emptyText,
}) => {
  const select = (itemId: string) => {
    if (events.includes("OnSelectItem")) eventHandler("OnSelectItem", id, [itemId]);
  };

  const openSearch = useCallback(() => {
    if (events.includes("OnSearch")) eventHandler("OnSearch", id, []);
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
  const showSearchButton = searchable && (!title || items.length === 0);

  if (collapsed) {
    return (
      <div className="tsh-section tsh-section-rail">
        {searchable && (
          <ShellTooltip content="Search plans" shortcut={shortcutHint} side="right">
            <button className="tsh-rail-search" onClick={openSearch} aria-label="Search plans">
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
                            <span key={i} className="tsh-badge" data-kind={badge.kind}>
                              {badge.label}
                            </span>
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
          <ShellTooltip content="Search plans" shortcut={shortcutHint} side="right">
            <button
              className="tsh-section-search-button"
              onClick={openSearch}
              aria-label="Search plans"
            >
              <span className="tsh-section-search-button-main">
                <Search size={16} />
                <span className="tsh-section-search-button-label">Search</span>
              </span>
              <span className="tsh-kbd">
                <span>{modKeyLabel()}</span>
                <span>{SEARCH_SHORTCUT_KEY}</span>
              </span>
            </button>
          </ShellTooltip>
        </div>
      )}
      {hasHeader && !showSearchButton && (
        <div className="tsh-section-header">
          <span className="tsh-section-title">{title}</span>
          {searchable && (
            <ShellTooltip content="Search plans" shortcut={shortcutHint} side="right">
              <button className="tsh-section-search" onClick={openSearch} aria-label="Search plans">
                <Search size={16} />
              </button>
            </ShellTooltip>
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
