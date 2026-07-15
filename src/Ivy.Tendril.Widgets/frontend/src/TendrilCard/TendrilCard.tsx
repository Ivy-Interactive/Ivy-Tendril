import React from "react";
import { createPortal } from "react-dom";
import { icons, ChevronDown, ScanLine, type LucideProps } from "lucide-react";
import { TendrilCardProps, TendrilCardMenuItem, IvyEventHandler } from "./types";
import { getWidth, getHeight } from "../styles";
import "./tendril-card.css";

const AVATAR_PALETTE = [
  "#e11d8f",
  "#f97316",
  "#14b8a6",
  "#0ea5e9",
  "#8b5cf6",
  "#ec4899",
  "#22c55e",
  "#eab308",
];

/** Deterministically pick an avatar background color from the initials. */
function colorForInitials(initials: string): string {
  let hash = 0;
  for (let i = 0; i < initials.length; i++) {
    hash = (hash * 31 + initials.charCodeAt(i)) | 0;
  }
  return AVATAR_PALETTE[Math.abs(hash) % AVATAR_PALETTE.length];
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
  assignee?: string;
  avatarColor?: string;
}

/**
 * Dropdown menu shown in the card's top-right corner (in place of the plain
 * assignee avatar). The menu list is rendered in a document.body portal with a
 * fixed position so it isn't clipped by scroll containers around the card.
 */
const CardMenu: React.FC<CardMenuProps> = ({
  widgetId,
  items,
  eventHandler,
  assignee,
  avatarColor,
}) => {
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
        {assignee ? (
          <span
            className="tc-avatar"
            style={{ backgroundColor: avatarColor }}
            title={assignee}
          >
            {assignee}
          </span>
        ) : null}
        <ChevronDown className="tc-menu-chevron" size={14} />
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

export const TendrilCard: React.FC<TendrilCardProps> = ({
  id,
  width = "full",
  height,
  events = [],
  eventHandler,
  title,
  badge,
  badgeIcon = "ScanLine",
  assignee,
  assigneeColor,
  footer,
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

  const BadgeIcon = resolveIcon(badgeIcon);
  const avatarColor = assignee
    ? assigneeColor || colorForInitials(assignee)
    : undefined;
  const hasMenu = (menuItems?.length ?? 0) > 0 && events.includes("OnMenuSelect");

  return (
    <div
      className={`tc-card${clickable ? " tc-card-clickable" : ""}`}
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
        {badge && (
          <span className="tc-badge">
            {badgeIcon && <BadgeIcon className="tc-badge-icon" size={13} />}
            <span className="tc-badge-label">{badge}</span>
          </span>
        )}
        {hasMenu ? (
          <CardMenu
            widgetId={id}
            items={menuItems!}
            eventHandler={eventHandler}
            assignee={assignee}
            avatarColor={avatarColor}
          />
        ) : (
          assignee && (
            <span
              className="tc-avatar"
              style={{ backgroundColor: avatarColor }}
              title={assignee}
            >
              {assignee}
            </span>
          )
        )}
      </div>

      <p className="tc-title">{title}</p>

      {footer && (
        <div className="tc-footer">
          <span className="tc-footer-dot" />
          <span className="tc-footer-text">{footer}</span>
        </div>
      )}
    </div>
  );
};
