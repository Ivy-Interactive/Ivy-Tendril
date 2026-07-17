export type IvyEventHandler = (
  eventName: string,
  widgetId: string,
  args: unknown[]
) => void;

export interface TendrilCardMenuItem {
  tag: string;
  label: string;
  icon?: string;
  destructive?: boolean;
}

export interface TendrilCardMeta {
  icon: string;
  label: string;
  tag?: string;
}

export interface TendrilCardProps {
  id: string;
  width?: string;
  height?: string;
  events?: string[];
  eventHandler: IvyEventHandler;
  title: string;
  /** Highlights the card as selected (info-tinted bg + info border). */
  selected?: boolean;
  icon?: string;
  iconSpin?: boolean;
  project?: string;
  projectColor?: string;
  status?: string;
  statusIcon?: string;
  meta?: TendrilCardMeta[];
  /**
   * UTC ISO timestamp of when the card's job started. When set, a live
   * elapsed-time meta item ticks every second on the client (rendered before
   * the other trailing meta items).
   */
  timerStartedAt?: string;
  menuItems?: TendrilCardMenuItem[];
}
