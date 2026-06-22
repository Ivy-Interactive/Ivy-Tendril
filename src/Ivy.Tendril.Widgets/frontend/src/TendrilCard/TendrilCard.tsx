import React from "react";
import { icons, ScanLine, type LucideProps } from "lucide-react";
import { TendrilCardProps } from "./types";
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
        {assignee && (
          <span
            className="tc-avatar"
            style={{ backgroundColor: avatarColor }}
            title={assignee}
          >
            {assignee}
          </span>
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
