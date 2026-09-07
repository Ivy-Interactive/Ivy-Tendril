import type { IvyEventHandler } from "../TendrilProcessViewer/types";

export interface DashboardKpiDto {
  label: string;
  value: string;
  delta?: string | null;
  direction?: "up" | "down" | null;
  hint?: string | null;
}

export interface DashboardMonthValueDto {
  label: string;
  value: number;
}

export interface DashboardActivityDayDto {
  date: string;
  count: number;
}

export interface DashboardActivityMonthDto {
  label: string;
  weeks: number[];
  days?: DashboardActivityDayDto[];
}

export interface DashboardJobDto {
  id: string;
  planId: string;
  title: string;
  /** Lowercased job status; "running" animates the row's spinner. */
  status: string;
}

export interface DashboardTrendDto {
  /** One `yyyy-MM-dd` per plotted day, ascending and contiguous. Labels are derived from these. */
  dates: string[];
  cost: number[];
  plans: number[];
  prevCost: (number | null)[];
  prevPlans: (number | null)[];
  /** 7-day trailing mean aligned to `dates`; null where the window reaches past the earliest record. */
  rollingCost: (number | null)[];
  rollingPlans: (number | null)[];
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
  trendWeekly?: DashboardTrendDto | null;
  pullRequests?: DashboardMonthValueDto[];
  activity?: DashboardActivityMonthDto[];
  jobs?: DashboardJobDto[];
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

export interface DashboardActivityMetrics {
  totalPrs: number;
  activeWeeks: number;
}

export const computeActivityMetrics = (
  months: DashboardActivityMonthDto[],
): DashboardActivityMetrics => {
  const totalPrs = months.reduce(
    (acc, m) => acc + (m.weeks ?? []).reduce((wAcc, c) => wAcc + c, 0),
    0,
  );
  const activeWeeks = months.reduce(
    (acc, m) => acc + (m.weeks ?? []).filter((c) => c > 0).length,
    0,
  );
  return { totalPrs, activeWeeks };
};

/** Arithmetic mean of non-empty number arrays, returning null when empty. */
export const computeAverage = (values: number[]): number | null => {
  if (!values || values.length === 0) return null;
  const sum = values.reduce((acc, val) => acc + val, 0);
  return sum / values.length;
};

export const ROLLING_WINDOW_DAYS = 7;

/**
 * Trailing mean of each entry and the `window - 1` before it, null for the leading entries that have
 * no full window. Only a fallback: the server sends a rolling series that also sees the days before
 * the displayed range, and this one cannot.
 */
export const computeRollingAverage = (
  values: number[],
  window = ROLLING_WINDOW_DAYS,
): (number | null)[] =>
  values.map((_, index) =>
    index < window - 1
      ? null
      : values
          .slice(index - window + 1, index + 1)
          .reduce((acc, value) => acc + value, 0) / window,
  );

const MONTH_NAMES = [
  "Jan", "Feb", "Mar", "Apr", "May", "Jun",
  "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
];

const DAY_NAMES = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

/**
 * Splits `yyyy-MM-dd` by hand rather than through `new Date(iso)`: the string form parses as UTC
 * midnight and reports the previous day in every negative-offset time zone.
 */
const parseIsoDate = (iso: string): { year: number; month: number; day: number } | null => {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  if (!parts) return null;
  const month = Number(parts[2]);
  if (month < 1 || month > 12) return null;
  return { year: Number(parts[1]), month, day: Number(parts[3]) };
};

/** Axis tick for a day: "Sep 7", or "Sep 7 '25" when the year has to be spelled out. */
export const formatAxisDate = (iso: string, includeYear = false): string => {
  const parsed = parseIsoDate(iso);
  if (!parsed) return iso;
  const base = `${MONTH_NAMES[parsed.month - 1]} ${parsed.day}`;
  return includeYear ? `${base} '${String(parsed.year).slice(-2)}` : base;
};

/** Tooltip title for a day: "Sun, Sep 7, 2026". */
export const formatTooltipDate = (iso: string): string => {
  const parsed = parseIsoDate(iso);
  if (!parsed) return iso;
  // Local Date constructor, for the weekday only; the parts above are already parsed.
  const weekday = DAY_NAMES[new Date(parsed.year, parsed.month - 1, parsed.day).getDay()];
  return `${weekday}, ${MONTH_NAMES[parsed.month - 1]} ${parsed.day}, ${parsed.year}`;
};
