import React from "react";
import {
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  Copy,
  Ellipsis,
  ExternalLink,
  Eye,
  LucideIcon,
  MessageSquarePlus,
  Paperclip,
  Pencil,
  Play,
  Plus,
  RefreshCw,
  Search,
  Settings,
  Square,
  Trash2,
  X,
} from "lucide-react";
import type { IvyEventHandler } from "../TendrilProcessViewer/types";
import { Badge, BadgeKind, CountBadge, DotTone, StatusDot } from "./Badge";
import { IconButton, IconButtonSize, IconButtonVariant } from "./IconButton";
import { Kbd, KbdVariant } from "./Kbd";
import { StatusLine } from "./StatusLine";
import { Tooltip, TooltipSide } from "./Tooltip";
import "./ui.css";

/* The Ivy props every external widget receives. */
interface WidgetProps {
  id: string;
  events?: string[];
  eventHandler?: IvyEventHandler;
}

/* C# enums arrive as their PascalCase member names; the primitives speak lowercase. */
const lower = <T extends string>(value: string | undefined, fallback: T): T =>
  (value ? (value.toLowerCase() as T) : fallback);

/* Only these icons are bundled for TendrilIconButton; an unknown name renders the slot child
   (or nothing), so a caller that needs another icon passes it as the widget's content. */
const buttonIcons: Record<string, LucideIcon> = {
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  Copy,
  Ellipsis,
  ExternalLink,
  Eye,
  MessageSquarePlus,
  Paperclip,
  Pencil,
  Play,
  Plus,
  RefreshCw,
  Search,
  Settings,
  Square,
  Trash2,
  X,
};

interface TendrilTooltipProps extends WidgetProps {
  content?: string;
  shortcut?: string[];
  side?: string;
  enabled?: boolean;
  delayDuration?: number;
  children?: React.ReactNode;
}

/** Wraps its content in the shared tooltip. */
export const TendrilTooltip: React.FC<TendrilTooltipProps> = ({
  content,
  shortcut,
  side,
  enabled = true,
  delayDuration = 500,
  children,
}) => (
  <Tooltip
    content={content}
    shortcut={shortcut && shortcut.length > 0 ? shortcut : undefined}
    side={lower<TooltipSide>(side, "top")}
    enabled={enabled}
    delayDuration={delayDuration}
    asChild={false}
  >
    <span style={{ display: "inline-flex" }}>{children}</span>
  </Tooltip>
);

interface TendrilKbdProps extends WidgetProps {
  keys?: string[];
  variant?: string;
}

/** A keyboard shortcut hint. */
export const TendrilKbd: React.FC<TendrilKbdProps> = ({ keys = [], variant }) =>
  keys.length > 0 ? <Kbd keys={keys} variant={lower<KbdVariant>(variant, "boxed")} /> : null;

interface TendrilBadgeProps extends WidgetProps {
  label?: string;
  count?: number | null;
  max?: number;
  kind?: string;
  dot?: boolean;
  dotTone?: string;
  pulse?: boolean;
}

/** A label badge, a count badge or a status dot — the app's notification indicators. */
export const TendrilBadge: React.FC<TendrilBadgeProps> = ({
  label,
  count,
  max = 99,
  kind,
  dot = false,
  dotTone,
  pulse = false,
}) => {
  const badgeKind = lower<BadgeKind>(kind, "neutral");
  if (dot) return <StatusDot tone={lower<DotTone>(dotTone, "neutral")} pulse={pulse} label={label} />;
  if (count != null) return <CountBadge count={count} max={max} kind={badgeKind} label={label} />;
  return label ? <Badge kind={badgeKind}>{label}</Badge> : null;
};

interface TendrilIconButtonProps extends WidgetProps {
  icon?: string;
  label?: string;
  tooltip?: string;
  shortcut?: string[];
  tooltipSide?: string;
  size?: string;
  variant?: string;
  disabled?: boolean;
  active?: boolean;
  children?: React.ReactNode;
}

/** A square icon button with the shared hover surface and tooltip. */
export const TendrilIconButton: React.FC<TendrilIconButtonProps> = ({
  id,
  events = [],
  eventHandler,
  icon,
  label = "",
  tooltip,
  shortcut,
  tooltipSide,
  size,
  variant,
  disabled = false,
  active = false,
  children,
}) => {
  const Icon = icon ? buttonIcons[icon] : undefined;
  return (
    <IconButton
      label={label}
      tooltip={tooltip ?? (label || false)}
      shortcut={shortcut && shortcut.length > 0 ? shortcut : undefined}
      tooltipSide={lower<TooltipSide>(tooltipSide, "top")}
      size={lower<IconButtonSize>(size, "lg")}
      variant={lower<IconButtonVariant>(variant, "ghost")}
      disabled={disabled}
      active={active}
      onClick={() => {
        if (events.includes("OnClick") && eventHandler) eventHandler("OnClick", id, []);
      }}
    >
      {Icon ? <Icon size={16} /> : children}
    </IconButton>
  );
};

interface TendrilStatusLineProps extends WidgetProps {
  statusText?: string;
  isComplete?: boolean;
  showIcon?: boolean;
  startedAt?: string | null;
  elapsedMs?: number | null;
  tokens?: number | null;
  tokensEstimated?: boolean;
}

/** The agent status line: live elapsed time, token count and status message. */
export const TendrilStatusLine: React.FC<TendrilStatusLineProps> = ({
  statusText = "Working…",
  isComplete = false,
  showIcon = true,
  startedAt,
  elapsedMs,
  tokens,
  tokensEstimated = false,
}) => (
  <StatusLine
    statusText={statusText}
    isComplete={isComplete}
    showIcon={showIcon}
    startedAt={startedAt}
    elapsedMs={elapsedMs}
    tokens={tokens}
    tokensEstimated={tokensEstimated}
  />
);
