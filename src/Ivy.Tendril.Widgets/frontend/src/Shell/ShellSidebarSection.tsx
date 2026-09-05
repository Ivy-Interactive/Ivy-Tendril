import React, { useCallback, useEffect } from "react";
import { Search } from "lucide-react";
import { useShell } from "./ShellContext";
import { ShellSectionItemDto, ShellWidgetProps, isEditableTarget, isModKey, modKeyLabel } from "./types";
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
 * The contextual list under the nav — plans for Review/Drafts, recommendations,
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
  const searchTitle = `Search plans (${shortcutHint})`;

  // Rail chips get a hover flyout (title + badges) since the chip itself only
  // shows the ID. Fixed-position and portaled so the rail cannot clip it.
  const [flyout, setFlyout] = React.useState<{
    item: ShellSectionItemDto;
    top: number;
    left: number;
  } | null>(null);

  const hasHeader = !!title || searchable;
  const showSearchButton = searchable && (!title || items.length === 0);

  if (collapsed) {
    return (
      <div className="tsh-section tsh-section-rail">
        {searchable && (
          <button
            className="tsh-rail-search"
            onClick={openSearch}
            aria-label="Search plans"
            title={searchTitle}
          >
            <Search size={16} />
          </button>
        )}
        <div className="tsh-rail-list" onScroll={() => setFlyout(null)}>
          {items.map(
            (item) =>
              item.tag && (
                <button
                  key={item.id}
                  className="tsh-rail-item"
                  data-selected={item.id === selectedId}
                  onClick={() => select(item.id)}
                  onMouseEnter={(e) => {
                    const r = e.currentTarget.getBoundingClientRect();
                    setFlyout({ item, top: r.top + r.height / 2, left: r.right + 10 });
                  }}
                  onMouseLeave={() => setFlyout(null)}
                >
                  <span className="tsh-rail-item-text">{item.tag}</span>
                </button>
              )
          )}
        </div>
        {flyout && (
          <div
            className="tsh-rail-tooltip"
            style={{ top: flyout.top, left: flyout.left }}
          >
            <div className="tsh-rail-tooltip-title">{flyout.item.title}</div>
            {flyout.item.badges && flyout.item.badges.length > 0 && (
              <div className="tsh-rail-tooltip-badges">
                {flyout.item.badges.map((badge, i) => (
                  <span key={i} className="tsh-badge" data-kind={badge.kind}>
                    {badge.label}
                  </span>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
    );
  }

  return (
    <div className="tsh-section" data-headerless={!hasHeader}>
      {hasHeader && showSearchButton && (
        <div className="tsh-section-header" data-search-button="true">
          <button
            className="tsh-section-search-button"
            onClick={openSearch}
            aria-label="Search plans"
            title={searchTitle}
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
        </div>
      )}
      {hasHeader && !showSearchButton && (
        <div className="tsh-section-header">
          <span className="tsh-section-title">{title}</span>
          {searchable && (
            <button
              className="tsh-section-search"
              onClick={openSearch}
              aria-label="Search plans"
              title={searchTitle}
            >
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
