export type IvyEventHandler = (
  eventName: string,
  widgetId: string,
  args: unknown[]
) => void;

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
}
