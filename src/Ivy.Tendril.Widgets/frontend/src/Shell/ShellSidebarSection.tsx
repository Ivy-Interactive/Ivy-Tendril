import React from "react";
import { GitPullRequest, LucideIcon, Rocket, Search } from "lucide-react";
import { useShell } from "./ShellContext";
import { ShellSectionItemDto, ShellWidgetProps } from "./types";
import "./shell.css";

interface ShellSidebarSectionProps extends ShellWidgetProps {
  title?: string;
  items?: ShellSectionItemDto[];
  selectedId?: string;
  searchable?: boolean;
  emptyText?: string;
}

const actionIcons: Record<string, LucideIcon> = {
  Rocket,
  GitPullRequest,
};

const FLYOUT_CLOSE_DELAY_MS = 150;
const FLYOUT_VIEWPORT_MARGIN_PX = 8;

/**
 * The contextual list under the nav — plans for Review/Drafts, recommendations,
 * etc. Published by the active app. In the collapsed rail the list shrinks to
 * narrow ID chips (the row tags, e.g. "#40") with the search button above.
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

  // Rail chips get a hover flyout (title, badges, actions) since the chip itself
  // only shows the ID. Fixed-position so the rail cannot clip it.
  const [flyout, setFlyout] = React.useState<{
    item: ShellSectionItemDto;
    top: number;
    left: number;
  } | null>(null);
  const flyoutRef = React.useRef<HTMLDivElement | null>(null);
  const closeTimer = React.useRef<number | null>(null);

  const cancelClose = () => {
    if (closeTimer.current !== null) {
      window.clearTimeout(closeTimer.current);
      closeTimer.current = null;
    }
  };

  const scheduleClose = () => {
    cancelClose();
    closeTimer.current = window.setTimeout(() => {
      closeTimer.current = null;
      setFlyout(null);
    }, FLYOUT_CLOSE_DELAY_MS);
  };

  const closeFlyout = () => {
    cancelClose();
    setFlyout(null);
  };

  React.useEffect(() => cancelClose, []);

  React.useLayoutEffect(() => {
    const el = flyoutRef.current;
    if (!el || !flyout) return;
    el.style.setProperty("--tsh-flyout-shift", "0px");
    const rect = el.getBoundingClientRect();
    const overflowBottom = rect.bottom - (window.innerHeight - FLYOUT_VIEWPORT_MARGIN_PX);
    const overflowTop = FLYOUT_VIEWPORT_MARGIN_PX - rect.top;
    let shift = 0;
    if (overflowBottom > 0) shift = -overflowBottom;
    else if (overflowTop > 0) shift = overflowTop;
    if (shift !== 0) el.style.setProperty("--tsh-flyout-shift", `${shift}px`);
  }, [flyout]);

  const runAction = (itemId: string, actionId: string) => {
    closeFlyout();
    if (events.includes("OnItemAction")) eventHandler("OnItemAction", id, [{ itemId, actionId }]);
  };

  const hasHeader = !!title || searchable;

  if (collapsed) {
    return (
      <div className="tsh-section tsh-section-rail">
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
        <div className="tsh-rail-list" onScroll={closeFlyout}>
          {items.map(
            (item) =>
              item.tag && (
                <button
                  key={item.id}
                  className="tsh-rail-item"
                  data-selected={item.id === selectedId}
                  onClick={() => select(item.id)}
                  onMouseEnter={(e) => {
                    cancelClose();
                    const r = e.currentTarget.getBoundingClientRect();
                    setFlyout({ item, top: r.top + r.height / 2, left: r.right + 10 });
                  }}
                  onMouseLeave={scheduleClose}
                >
                  <span className="tsh-rail-item-text">{item.tag}</span>
                </button>
              )
          )}
        </div>
        {flyout && (
          <div
            ref={flyoutRef}
            className="tsh-rail-tooltip"
            role="tooltip"
            style={{ top: flyout.top, left: flyout.left }}
            onMouseEnter={cancelClose}
            onMouseLeave={scheduleClose}
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
            {flyout.item.actions && flyout.item.actions.length > 0 && (
              <div className="tsh-rail-tooltip-actions">
                {flyout.item.actions.map((action) => {
                  const Icon = action.icon ? actionIcons[action.icon] : undefined;
                  return (
                    <button
                      key={action.id}
                      type="button"
                      className="tsh-rail-tooltip-btn"
                      data-primary={!!action.primary}
                      onClick={() => runAction(flyout.item.id, action.id)}
                    >
                      {Icon && <Icon size={14} />}
                      <span>{action.label}</span>
                    </button>
                  );
                })}
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
