export interface JobSubItem {
  id: string;
  name: string;
  count?: number;
}

export interface TendrilSidebarProps {
  id: string;
  version?: string;
  agentName?: string;
  agentShortcut?: string;
  newPlanShortcut?: string;
  activeItem?: string;
  draftCount?: number;
  reviewCount?: number;
  recommendationsCount?: number;
  jobCount?: number;
  jobs?: JobSubItem[];
  pullRequestCount?: number;
  iceboxCount?: number;
  helpRequestCount?: number;
  collapsed?: boolean;
  eventHandler?: (eventName: string, widgetId: string, args: unknown[]) => void;
}
