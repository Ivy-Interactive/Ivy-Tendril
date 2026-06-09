export type IvyEventHandler = (
  eventName: string,
  widgetId: string,
  args: unknown[]
) => void;

export interface TendrilProcessProps {
  id: string;
  width?: string;
  height?: string;
  events?: string[];
  eventHandler: IvyEventHandler;
  draftCount?: number;
  reviewCount?: number;
  creatingPlansCount?: number;
  updatingPlansCount?: number;
  executingPlansCount?: number;
  retryingPlansCount?: number;
  creatingPrCount?: number;
}
