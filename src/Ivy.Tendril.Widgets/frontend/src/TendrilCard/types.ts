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

export interface TendrilCardProps {
  id: string;
  width?: string;
  height?: string;
  events?: string[];
  eventHandler: IvyEventHandler;
  title: string;
  badge?: string;
  badgeIcon?: string;
  assignee?: string;
  assigneeColor?: string;
  footer?: string;
  menuItems?: TendrilCardMenuItem[];
}
