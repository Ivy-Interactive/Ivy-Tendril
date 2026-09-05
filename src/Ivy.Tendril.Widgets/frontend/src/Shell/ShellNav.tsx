import React, { useCallback, useRef, useState } from "react";
import {
  Activity,
  ChartBar,
  Feather,
  GitPullRequest,
  Lightbulb,
  LucideIcon,
  MessageSquare,
  Snowflake,
  ThumbsUp,
  Trash2,
} from "lucide-react";
import { useShell } from "./ShellContext";
import { ShellNavItemDto, ShellWidgetProps } from "./types";
import "./shell.css";

interface ShellNavProps extends ShellWidgetProps {
  items?: ShellNavItemDto[];
  showDivider?: boolean;
}

/* The nav renders the app menu, so only icons used by [App] attributes are
   bundled; unknown names fall back to the label's initial. */
const navIcons: Record<string, LucideIcon> = {
  ChartBar,
  Feather,
  ThumbsUp,
  Lightbulb,
  Activity,
  GitPullRequest,
  Snowflake,
  MessageSquare,
  Trash2,
};

const NavIcon: React.FC<{ icon?: string; label: string }> = ({ icon, label }) => {
  const Icon = icon ? navIcons[icon] : undefined;
  if (Icon) return <Icon size={16} />;
  return <span style={{ fontSize: 12, fontWeight: 500 }}>{label.charAt(0).toUpperCase()}</span>;
};

/** One nav row: the nav can never be dragged smaller than this. */
const MIN_NAV_HEIGHT = 38;
/** The plan list below the divider always keeps at least this much room. */
const MIN_LIST_HEIGHT = 96;
export const NAV_HEIGHT_STORAGE_KEY = "tendril.shell.navHeight";

const readStoredHeight = (): number | null => {
  try {
    const raw = window.localStorage.getItem(NAV_HEIGHT_STORAGE_KEY);
    if (raw == null) return null;
    const value = Number(raw);
    return Number.isFinite(value) && value > 0 ? value : null;
  } catch {
    return null;
  }
};

const writeStoredHeight = (height: number | null) => {
  try {
    if (height == null) window.localStorage.removeItem(NAV_HEIGHT_STORAGE_KEY);
    else window.localStorage.setItem(NAV_HEIGHT_STORAGE_KEY, String(Math.round(height)));
  } catch {
    /* storage unavailable (private mode, sandboxed host) — the size just doesn't persist */
  }
};

/**
 * The app menu. When a sidebar section (plan list) follows it, the divider
 * between the two doubles as a drag handle: dragging it up caps the nav's
 * height (the items scroll) and hands the room to the list, dragging it down
 * gives the nav its rows back. Double-click resets to the natural height. The
 * chosen height is client-side state, remembered in localStorage.
 */
export const ShellNav: React.FC<ShellNavProps> = ({
  id,
  events = [],
  eventHandler,
  items = [],
  showDivider = false,
}) => {
  const { collapsed } = useShell();
  const itemsRef = useRef<HTMLDivElement>(null);
  const [navHeight, setNavHeight] = useState<number | null>(readStoredHeight);
  const [dragging, setDragging] = useState(false);
  const dragRef = useRef<{ startY: number; startHeight: number; max: number } | null>(null);

  const select = (itemId: string) => {
    if (events.includes("OnSelect")) eventHandler("OnSelect", id, [itemId]);
  };

  const resizable = showDivider && !collapsed;

  const onDividerPointerDown = useCallback(
    (e: React.PointerEvent<HTMLDivElement>) => {
      const list = itemsRef.current;
      if (!resizable || e.button !== 0 || !list) return;
      e.preventDefault();
      const body = list.closest(".tsh-sidebar-body") ?? list.parentElement;
      const listRect = list.getBoundingClientRect();
      const dividerRect = e.currentTarget.getBoundingClientRect();
      // Room below the divider is everything left in the sidebar body minus the
      // minimum the plan list keeps; the nav also never grows past its content.
      const bodyBottom = body ? body.getBoundingClientRect().bottom : Infinity;
      const available = bodyBottom - listRect.top - dividerRect.height - MIN_LIST_HEIGHT;
      const max = Math.max(MIN_NAV_HEIGHT, Math.min(list.scrollHeight, available));
      dragRef.current = { startY: e.clientY, startHeight: listRect.height, max };
      e.currentTarget.setPointerCapture?.(e.pointerId);
      setDragging(true);
    },
    [resizable]
  );

  const onDividerPointerMove = useCallback((e: React.PointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current;
    if (!drag) return;
    const next = drag.startHeight + (e.clientY - drag.startY);
    setNavHeight(Math.round(Math.min(drag.max, Math.max(MIN_NAV_HEIGHT, next))));
  }, []);

  const onDividerPointerUp = useCallback((e: React.PointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current;
    if (!drag) return;
    dragRef.current = null;
    setDragging(false);
    e.currentTarget.releasePointerCapture?.(e.pointerId);
    setNavHeight((height) => {
      // Dragged all the way back to the content height: forget the cap so the
      // nav keeps sizing to its rows if items are added later.
      const list = itemsRef.current;
      const settled = height != null && list && height >= list.scrollHeight - 1 ? null : height;
      writeStoredHeight(settled);
      return settled;
    });
  }, []);

  const onDividerDoubleClick = useCallback(() => {
    if (!resizable) return;
    setNavHeight(null);
    writeStoredHeight(null);
  }, [resizable]);

  return (
    <nav className="tsh-nav" data-dragging={dragging}>
      <div
        className="tsh-nav-items"
        ref={itemsRef}
        style={showDivider && navHeight != null ? { maxHeight: navHeight } : undefined}
      >
        {items.map((item) => (
          <button
            key={item.id}
            className="tsh-nav-item"
            data-active={item.isActive === true}
            data-menu-item={item.id}
            onClick={() => select(item.id)}
            title={item.label}
          >
            <span className="tsh-nav-item-main">
              <span className="tsh-nav-icon">
                <NavIcon icon={item.icon} label={item.label} />
              </span>
              <span className="tsh-nav-label">{item.label}</span>
            </span>
            {item.badge && (
              <span className="tsh-nav-badge">
                {/* The rail fits two digits beside the icon; larger counts cap at 99. */}
                {collapsed && item.badge.length > 2 ? "99" : item.badge}
              </span>
            )}
          </button>
        ))}
      </div>
      {showDivider && (
        <div
          className="tsh-nav-divider"
          role="separator"
          aria-orientation="horizontal"
          aria-label="Resize navigation"
          data-resizable={resizable}
          data-dragging={dragging}
          onPointerDown={onDividerPointerDown}
          onPointerMove={onDividerPointerMove}
          onPointerUp={onDividerPointerUp}
          onPointerCancel={onDividerPointerUp}
          onDoubleClick={onDividerDoubleClick}
        />
      )}
    </nav>
  );
};
