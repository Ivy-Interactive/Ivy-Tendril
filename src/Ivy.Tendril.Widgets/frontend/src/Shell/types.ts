export type { IvyEventHandler } from "../TendrilProcessViewer/types";
import type { IvyEventHandler } from "../TendrilProcessViewer/types";
import { isMac } from "../ui/shortcuts";

export interface ShellWidgetProps {
  id: string;
  events?: string[];
  eventHandler: IvyEventHandler;
}

export interface ShellNavItemDto {
  id: string;
  label: string;
  icon?: string;
  badge?: string;
  isActive?: boolean;
}

export interface ShellBadgeDto {
  label: string;
  kind: "project" | "success" | "warning" | "neutral" | "color";
  /** An Ivy color name the host assigned (e.g. "Purple"); it recolors the badge and takes precedence over `kind`. */
  color?: string;
}

export type ShellItemState = "working" | "completed";

export interface ShellSectionItemDto {
  id: string;
  title: string;
  tag?: string;
  badges?: ShellBadgeDto[];
  /** A lucide icon name (see ShellSidebarSection's icon map), rendered left of the title. */
  icon?: string;
  /** A small icon left of the title telling the row's state, e.g. a chat still being answered. */
  state?: ShellItemState;
}

export interface ShellTabDto {
  id: string;
  title: string;
  /** The page tab cannot be closed: it is how the user gets back to the page behind the sessions. */
  closable?: boolean;
  /** Lucide icon name; defaults to the terminal glyph used by session tabs. */
  icon?: string;
}

export { isMac, modKeyLabel } from "../ui/shortcuts";

/** True when the keydown's modifier matches the platform's command key. */
export const isModKey = (e: KeyboardEvent): boolean => (isMac() ? e.metaKey : e.ctrlKey);

export const isEditableTarget = (e: KeyboardEvent): boolean => {
  const t = e.target as HTMLElement | null;
  if (!t) return false;
  return (
    t.isContentEditable ||
    t.tagName === "INPUT" ||
    t.tagName === "TEXTAREA" ||
    t.tagName === "SELECT" ||
    !!t.closest(".xterm")
  );
};
