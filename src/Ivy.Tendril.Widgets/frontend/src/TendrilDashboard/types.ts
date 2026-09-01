import type { IvyEventHandler } from "../TendrilProcessViewer/types";

export interface DashboardKpiDto {
  label: string;
  value: string;
  delta?: string | null;
  direction?: "up" | "down" | null;
}

export interface DashboardMonthValueDto {
  label: string;
  value: number;
}

export interface DashboardActivityMonthDto {
  label: string;
  weeks: number[];
}

export interface DashboardTrendDto {
  months: string[];
  cost: number[];
  plans: number[];
}

export interface TendrilDashboardProps {
  id: string;
  width?: string;
  height?: string;
  events?: string[];
  eventHandler: IvyEventHandler;
  dateText?: string;
  greeting?: string;
  headline?: string;
  draftCount?: number;
  inProgressCount?: number;
  reviewCount?: number;
  completedCount?: number;
  failedCount?: number;
  kpis?: DashboardKpiDto[];
  trend?: DashboardTrendDto | null;
  pullRequests?: DashboardMonthValueDto[];
  activity?: DashboardActivityMonthDto[];
  slots?: {
    ProcessViewer?: React.ReactNode;
    UpdateNotice?: React.ReactNode;
    TunnelQr?: React.ReactNode;
    TunnelMenu?: React.ReactNode;
  };
}

/** Slots arrive as arrays of rendered nodes; an omitted slot is undefined. */
export const hasSlotContent = (slot?: React.ReactNode): boolean =>
  slot != null && (!Array.isArray(slot) || slot.length > 0);

/** Grey ramp step (1-4) for an intensity relative to the range maximum. */
export const rampLevel = (value: number, max: number): number => {
  if (value <= 0 || max <= 0) return 0;
  const ratio = value / max;
  if (ratio <= 0.25) return 1;
  if (ratio <= 0.5) return 2;
  if (ratio <= 0.8) return 3;
  return 4;
};

/** "Nice" rounded axis ticks from 0 up to at least max, e.g. [0, 10, 20, 30]. */
export const niceTicks = (max: number, count = 3): number[] => {
  if (max <= 0) return [0, 1];
  const rawStep = max / count;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rawStep)));
  const residual = rawStep / magnitude;
  const step = (residual > 5 ? 10 : residual > 2 ? 5 : residual > 1 ? 2 : 1) * magnitude;
  const ticks: number[] = [];
  for (let v = 0; v < max + step; v += step) ticks.push(Math.round(v * 1000) / 1000);
  return ticks;
};

export const formatCurrencyTick = (value: number): string => {
  if (value >= 1000) return `$${Math.round(value / 1000)}K`;
  return value === 0 ? "0" : `$${Math.round(value)}`;
};

export const formatCountTick = (value: number): string => {
  if (value >= 1000) return `${Math.round(value / 1000)}K`;
  return String(Math.round(value));
};
