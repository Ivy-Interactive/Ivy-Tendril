import React, { useCallback, useEffect } from "react";
import { Plus, Search } from "lucide-react";
import { useShell } from "./ShellContext";
import {
  ShellSectionItemDto,
  ShellWidgetProps,
  isEditableTarget,
  isModKey,
  modKeyLabel,
} from "./types";
import { ShellSectionItems } from "./ShellSectionItems";
import { ShellRailList } from "./ShellRailList";
import { ShellTooltip } from "./ShellTooltip";
import "./shell.css";

const SEARCH_SHORTCUT_KEY = "K";

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
  collapsible?: boolean;
  /** Puts the item count on the collapsed rail's list button; chat lists opt in. */
  showCount?: boolean;
  /** Keeps item badges in the collapsed rail's flyout; plan lists drop them. */
  collapsedBadges?: boolean;
}

/**
 * The contextual list under the nav: plans for Review/Drafts, recommendations,
 * etc. Published by the active app. In the collapsed rail the whole list folds
 * into a single button that floats it back over the content (see ShellRailList),
 * with the search and new buttons stacked above it.
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
  collapsible = true,
  showCount = false,
  collapsedBadges = false,
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

  if (collapsible && collapsed) {
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
        {items.length > 0 && (
          <ShellRailList
            title={title}
            items={items}
            selectedId={selectedId}
            showCount={showCount}
            showBadges={collapsedBadges}
            onSelect={select}
          />
        )}
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
              <span className="tsh-row">
                <span className="tsh-section-search-button-main">
                  <Search size={16} />
                  <span className="tsh-section-search-button-label">Search</span>
                </span>
                <span className="tsh-kbd">
                  <span>{modKeyLabel()}</span>
                  <span>{SEARCH_SHORTCUT_KEY}</span>
                </span>
              </span>
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
      <ShellSectionItems
        items={items}
        selectedId={selectedId}
        emptyText={emptyText}
        onSelect={select}
      />
    </div>
  );
};
