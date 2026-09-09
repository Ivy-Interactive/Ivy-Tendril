import React from "react";
import "./ui.css";

export type BadgeKind = "neutral" | "project" | "success" | "warning" | "danger";

export interface BadgeProps {
  kind?: BadgeKind;
  children: React.ReactNode;
  /** Numeric styling: a fixed minimum width and tabular figures, so a ticking count is steady. */
  numeric?: boolean;
  className?: string;
  /** Native title, for a badge whose label is abbreviated. */
  title?: string;
  "aria-label"?: string;
}

/** A small label chip: the bundle's one badge shape, tinted by kind. */
export const Badge: React.FC<BadgeProps> = ({
  kind = "neutral",
  children,
  numeric = false,
  className = "",
  title,
  "aria-label": ariaLabel,
}) => (
  <span
    className={`tui-badge ${numeric ? "tui-badge--count" : ""} ${className}`.replace(/\s+/g, " ").trim()}
    data-kind={kind}
    title={title}
    aria-label={ariaLabel}
  >
    {children}
  </span>
);

export interface CountBadgeProps {
  count: number;
  /** Counts above this render as "99+". */
  max?: number;
  kind?: BadgeKind;
  className?: string;
  /** Accessible name, e.g. "3 unread plans". Defaults to the number alone. */
  label?: string;
}

/** Formats a count for a notification badge: 100 with max 99 becomes "99+". */
export const formatCount = (count: number, max = 99): string =>
  count > max ? `${max}+` : `${count}`;

/**
 * The bundle's one notification/count indicator. Renders nothing for a non-positive count, so
 * a caller can hand it a raw number.
 */
export const CountBadge: React.FC<CountBadgeProps> = ({
  count,
  max = 99,
  kind = "neutral",
  className = "",
  label,
}) => {
  if (!Number.isFinite(count) || count <= 0) return null;
  const text = formatCount(count, max);
  return (
    <Badge numeric kind={kind} className={className} aria-label={label ?? text}>
      {text}
    </Badge>
  );
};

export type DotTone = "neutral" | "info" | "success" | "warning" | "danger";

export interface StatusDotProps {
  tone?: DotTone;
  /** Pulses while something is in flight. */
  pulse?: boolean;
  className?: string;
  label?: string;
}

/** A status dot — the small round sibling of the count badge. */
export const StatusDot: React.FC<StatusDotProps> = ({
  tone = "neutral",
  pulse = false,
  className = "",
  label,
}) => (
  <span
    className={`tui-dot ${className}`.trim()}
    data-tone={tone}
    data-pulse={pulse}
    role={label ? "img" : undefined}
    aria-label={label}
    aria-hidden={label ? undefined : true}
  />
);
