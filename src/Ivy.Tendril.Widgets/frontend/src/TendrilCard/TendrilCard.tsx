import React from "react";
import { createPortal } from "react-dom";
import { icons, MoreHorizontal, ScanLine, Timer, type LucideProps } from "lucide-react";
import {
  TendrilCardProps,
  TendrilCardMenuItem,
  TendrilCardMeta,
  IvyEventHandler,
} from "./types";
import { getWidth, getHeight } from "../styles";
import "./tendril-card.css";

const PROJECT_PALETTE = [
  "#6366f1",
  "#0ea5e9",
  "#14b8a6",
  "#22a06b",
  "#f97316",
  "#e11d8f",
  "#8b5cf6",
  "#ca8a04",
];

/** Deterministically pick a project pill color from the project name. */
function colorForProject(project: string): string {
  let hash = 0;
  for (let i = 0; i < project.length; i++) {
    hash = (hash * 31 + project.charCodeAt(i)) | 0;
  }
  return PROJECT_PALETTE[Math.abs(hash) % PROJECT_PALETTE.length];
}

/** Resolve a Lucide icon component by its PascalCase name, falling back to ScanLine. */
function resolveIcon(name?: string): React.ComponentType<LucideProps> {
  if (!name) return ScanLine;
  const lookup = icons as Record<string, React.ComponentType<LucideProps>>;
  return lookup[name] ?? ScanLine;
}

interface CardMenuProps {
  widgetId: string;
  items: TendrilCardMenuItem[];
  eventHandler: IvyEventHandler;
}

/**
 * "…" dropdown menu in the card's top-right corner. The menu list is rendered
 * in a document.body portal with a fixed position so it isn't clipped by
 * scroll containers around the card.
 */
const CardMenu: React.FC<CardMenuProps> = ({ widgetId, items, eventHandler }) => {
  const [open, setOpen] = React.useState(false);
  const [position, setPosition] = React.useState({ top: 0, right: 0 });
  const triggerRef = React.useRef<HTMLButtonElement>(null);
  const menuRef = React.useRef<HTMLDivElement>(null);

  const close = React.useCallback(() => setOpen(false), []);

  const toggle = (e: React.MouseEvent) => {
    e.stopPropagation();
    if (!open && triggerRef.current) {
      const rect = triggerRef.current.getBoundingClientRect();
      setPosition({
        top: rect.bottom + 4,
        right: window.innerWidth - rect.right,
      });
    }
    setOpen((v) => !v);
  };

  React.useEffect(() => {
    if (!open) return;

    const onPointerDown = (e: PointerEvent) => {
      const target = e.target as Node;
      if (menuRef.current?.contains(target) || triggerRef.current?.contains(target)) {
        return;
      }
      close();
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") close();
    };
    const onScrollOrResize = () => close();

    document.addEventListener("pointerdown", onPointerDown, true);
    document.addEventListener("keydown", onKeyDown);
    window.addEventListener("scroll", onScrollOrResize, true);
    window.addEventListener("resize", onScrollOrResize);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown, true);
      document.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("scroll", onScrollOrResize, true);
      window.removeEventListener("resize", onScrollOrResize);
    };
  }, [open, close]);

  const select = (e: React.MouseEvent, item: TendrilCardMenuItem) => {
    e.stopPropagation();
    close();
    eventHandler("OnMenuSelect", widgetId, [item.tag]);
  };

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className="tc-menu-trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label="Card actions"
        onClick={toggle}
        onKeyDown={(e) => e.stopPropagation()}
      >
        <MoreHorizontal size={18} />
      </button>
      {open &&
        createPortal(
          <div
            ref={menuRef}
            role="menu"
            className="tc-menu"
            style={{ top: position.top, right: position.right }}
            onClick={(e) => e.stopPropagation()}
          >
            {items.map((item) => {
              const ItemIcon = item.icon ? resolveIcon(item.icon) : null;
              return (
                <button
                  key={item.tag}
                  type="button"
                  role="menuitem"
                  className={`tc-menu-item${item.destructive ? " tc-menu-item-destructive" : ""}`}
                  onClick={(e) => select(e, item)}
                >
                  {ItemIcon && <ItemIcon className="tc-menu-item-icon" size={14} />}
                  <span>{item.label}</span>
                </button>
              );
            })}
          </div>,
          document.body
        )}
    </>
  );
};

interface MetaItemProps {
  meta: TendrilCardMeta;
  onClick?: (tag: string) => void;
}

const MetaItem: React.FC<MetaItemProps> = ({ meta, onClick }) => {
  const Icon = resolveIcon(meta.icon);
  const clickable = !!meta.tag && !!onClick;

  const body = (
    <>
      <Icon className="tc-meta-icon" size={14} />
      <span className="tc-meta-label">{meta.label}</span>
    </>
  );

  if (clickable) {
    return (
      <button
        type="button"
        className="tc-meta-item tc-meta-item-clickable"
        onClick={(e) => {
          e.stopPropagation();
          onClick!(meta.tag!);
        }}
        onKeyDown={(e) => e.stopPropagation()}
      >
        {body}
      </button>
    );
  }

  return <span className="tc-meta-item">{body}</span>;
};

/** Mirrors JobsApp.FormatTimeSpan: "2h 05m", "5m 20s" or "45s". */
function formatElapsed(totalSeconds: number): string {
  const clamped = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(clamped / 3600);
  const minutes = Math.floor((clamped % 3600) / 60);
  const seconds = clamped % 60;
  if (hours >= 1) return `${hours}h ${String(minutes).padStart(2, "0")}m`;
  if (minutes === 0) return `${seconds}s`;
  return `${minutes}m ${String(seconds).padStart(2, "0")}s`;
}

/**
 * Elapsed-time meta item that ticks locally every second, so running jobs show
 * a live timer without waiting for a server refresh.
 */
const LiveTimer: React.FC<{ startedAt: string }> = ({ startedAt }) => {
  const startMs = React.useMemo(() => Date.parse(startedAt), [startedAt]);
  const [now, setNow] = React.useState(() => Date.now());

  React.useEffect(() => {
    const interval = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(interval);
  }, []);

  if (Number.isNaN(startMs)) return null;

  return (
    <span className="tc-meta-item">
      <Timer className="tc-meta-icon" size={14} />
      <span className="tc-meta-label">{formatElapsed((now - startMs) / 1000)}</span>
    </span>
  );
};

export const TendrilCard: React.FC<TendrilCardProps> = ({
  id,
  width = "full",
  height,
  events = [],
  eventHandler,
  title,
  selected = false,
  icon,
  iconSpin = false,
  project,
  projectColor,
  status,
  statusIcon,
  meta,
  timerStartedAt,
  menuItems,
}) => {
  const style: React.CSSProperties = {
    ...getWidth(width),
    ...getHeight(height),
  };

  const clickable = events.includes("OnClick");
  const handleClick = () => {
    if (clickable) {
      eventHandler("OnClick", id, [title]);
    }
  };

  const TileIcon = resolveIcon(icon);
  const StatusIcon = statusIcon ? resolveIcon(statusIcon) : null;
  const pillColor = project ? projectColor || colorForProject(project) : undefined;
  const hasMenu = (menuItems?.length ?? 0) > 0 && events.includes("OnMenuSelect");

  const onMetaClick = events.includes("OnMetaClick")
    ? (tag: string) => eventHandler("OnMetaClick", id, [tag])
    : undefined;

  // First meta item sits on the left (typically the plan id); the rest are
  // grouped on the right (time, tokens), matching the reference footer layout.
  const [leadMeta, ...trailMeta] = meta ?? [];

  return (
    <div
      className={`tc-card${clickable ? " tc-card-clickable" : ""}${selected ? " tc-card-selected" : ""}`}
      style={style}
      onClick={clickable ? handleClick : undefined}
      role={clickable ? "button" : undefined}
      tabIndex={clickable ? 0 : undefined}
      onKeyDown={
        clickable
          ? (e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                handleClick();
              }
            }
          : undefined
      }
    >
      <div className="tc-top">
        {icon && (
          <span className="tc-icon-tile">
            <TileIcon className={iconSpin ? "tc-icon-spin" : undefined} size={15} />
          </span>
        )}
        {project && (
          <span
            className="tc-pill"
            style={{ "--tc-pill-color": pillColor } as React.CSSProperties}
          >
            {project}
          </span>
        )}
        <span className="tc-top-spacer" />
        {hasMenu && <CardMenu widgetId={id} items={menuItems!} eventHandler={eventHandler} />}
      </div>

      <p className="tc-title">{title}</p>

      {status && (
        <div className="tc-status">
          {StatusIcon && <StatusIcon className="tc-status-icon" size={14} />}
          <span className="tc-status-text">{status}</span>
        </div>
      )}

      {((meta && meta.length > 0) || timerStartedAt) && (
        <div className="tc-meta">
          {leadMeta && <MetaItem meta={leadMeta} onClick={onMetaClick} />}
          {(timerStartedAt || trailMeta.length > 0) && (
            <div className="tc-meta-trail">
              {timerStartedAt && <LiveTimer startedAt={timerStartedAt} />}
              {trailMeta.map((m, i) => (
                <MetaItem key={`${m.icon}-${i}`} meta={m} onClick={onMetaClick} />
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
};
