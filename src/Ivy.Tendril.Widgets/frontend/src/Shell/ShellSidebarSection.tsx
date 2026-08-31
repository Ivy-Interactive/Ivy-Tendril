import React from "react";
import { Search } from "lucide-react";
import { useShell } from "./ShellContext";
import { ShellSectionItemDto, ShellWidgetProps } from "./types";
import "./shell.css";

/** The icon-only rail width from shell.css; the measured rail never goes below it. */
const RAIL_MIN_WIDTH = 56;

/** Upper bound on the measured rail, so one very long ID cannot dominate the shell. */
const RAIL_MAX_WIDTH = 160;

interface ShellSidebarSectionProps extends ShellWidgetProps {
  title?: string;
  items?: ShellSectionItemDto[];
  selectedId?: string;
  searchable?: boolean;
  emptyText?: string;
}

/**
 * Widens the collapsed rail so the longest plan ID fits instead of being
 * ellipsised. The chips are measured off-screen in the rail's own font, and the
 * result is published as --tsh-rail-width on .tsh-root, which .tsh-sidebar
 * already consumes. Falls back to the CSS default when there is nothing to
 * measure, and the property is cleared on unmount so a section without tags
 * cannot leave the rail stretched.
 */
const useRailWidth = (tags: string[], enabled: boolean) => {
  const ruler = React.useRef<HTMLDivElement | null>(null);
  // Remembered separately from the ruler: the ruler unmounts when the shell
  // expands, and the cleanup still needs the root to reset the property.
  const rootRef = React.useRef<HTMLElement | null>(null);
  const key = tags.join("\u0000");

  React.useLayoutEffect(() => {
    const root = (ruler.current?.closest(".tsh-root") as HTMLElement | null) ?? rootRef.current;
    if (!root) return;
    rootRef.current = root;

    const reset = () => {
      root.style.removeProperty("--tsh-rail-width");
    };
    if (!enabled || !ruler.current) {
      reset();
      return;
    }

    const chips = Array.from(ruler.current.children) as HTMLElement[];
    const widest = chips.reduce((max, chip) => Math.max(max, chip.getBoundingClientRect().width), 0);
    if (widest === 0) {
      reset();
      return;
    }

    // Chip padding (2px each side) plus the sidebar's 8px collapsed padding,
    // clamped so a pathologically long ID cannot swallow the content area — a
    // chip past the cap still ellipsises and the hover flyout shows it in full.
    const width = Math.min(
      RAIL_MAX_WIDTH,
      Math.max(RAIL_MIN_WIDTH, Math.ceil(widest) + 4 + 16)
    );
    root.style.setProperty("--tsh-rail-width", `${width}px`);

    return reset;
  }, [key, enabled]);

  return ruler;
};

/**
 * The contextual list under the nav — plans for Review/Drafts, recommendations,
 * etc. Published by the active app. In the collapsed rail the list shrinks to
 * narrow ID chips (the row tags, e.g. "#40") with the search button above. IDs
 * can be long, so the rail widens to fit the widest chip (see useRailWidth).
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

  const { collapsed } = useShell();

  // Rail chips get a hover flyout (title + badges) since the chip itself only
  // shows the ID. Fixed-position and portaled so the rail cannot clip it.
  const [flyout, setFlyout] = React.useState<{
    item: ShellSectionItemDto;
    top: number;
    left: number;
  } | null>(null);

  const hasHeader = !!title || searchable;

  const tags = items.map((item) => item.tag).filter((tag): tag is string => !!tag);
  const ruler = useRailWidth(tags, collapsed);

  if (collapsed) {
    return (
      <div className="tsh-section tsh-section-rail">
        {/* Off-screen copies of the chips, measured to size the rail. */}
        <div className="tsh-rail-ruler" aria-hidden="true" ref={ruler}>
          {tags.map((tag, i) => (
            <span key={i} className="tsh-rail-item">
              {tag}
            </span>
          ))}
        </div>
        {searchable && (
          <button
            className="tsh-rail-search"
            onClick={openSearch}
            aria-label="Search plans"
            title="Search plans"
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
                  {item.tag}
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
      {hasHeader && (
        <div className="tsh-section-header">
          <span className="tsh-section-title">{title}</span>
          {searchable && (
            <button
              className="tsh-section-search"
              onClick={openSearch}
              aria-label="Search plans"
              title="Search plans"
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
