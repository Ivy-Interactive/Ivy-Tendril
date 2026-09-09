import type { IvyEventHandler } from "../TendrilProcessViewer/types";

export type { IvyEventHandler };

export interface PlanActionDto {
  tag: string;
  label: string;
  /** The C# `Icons` enum name, resolved by `ActionIcon`. */
  icon?: string;
  /** Bound while nothing editable has focus: `E`, `Backspace`, `Escape`, `Ctrl+Enter`. */
  shortcut?: string;
  active?: boolean;
  disabled?: boolean;
  badge?: string;
  loading?: boolean;
  danger?: boolean;
  /** Puts the caret in the chat composer when fired, so "Update" starts a conversation. */
  focusChat?: boolean;
}

export interface PlanTabDto {
  id: string;
  label: string;
  badge?: string;
}

export interface PlanWorkspaceSlots {
  Content?: React.ReactNode[];
  Chat?: React.ReactNode[];
  Verifications?: React.ReactNode[];
  Questions?: React.ReactNode[];
  Toolbar?: React.ReactNode[];
}

export interface PlanWorkspaceProps {
  id: string;
  planId?: string;
  title?: string;
  meta?: string;
  sourceUrl?: string;
  sourceLabel?: string;
  persona?: string;
  personaInitials?: string;
  actions?: PlanActionDto[];
  menuItems?: PlanActionDto[];
  primary?: PlanActionDto | null;
  secondary?: PlanActionDto[];
  tabs?: PlanTabDto[];
  selectedTab?: string | null;
  chatWidth?: number;
  verificationsLabel?: string;
  questionsLabel?: string;
  events?: string[];
  eventHandler?: IvyEventHandler;
  slots?: PlanWorkspaceSlots;
}

export const hasNodes = (nodes?: React.ReactNode[]): boolean =>
  Array.isArray(nodes) && nodes.some((node) => node !== null && node !== undefined && node !== false);
