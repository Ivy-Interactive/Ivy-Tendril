import React, { useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { computeRollingAverage, formatAxisDate, formatTooltipDate, niceTicks } from "./types";

interface TrendChartProps {
  /** One `yyyy-MM-dd` per point, ascending and contiguous. Axis ticks and tooltips come from these. */
  dates: string[];
  values: number[];
  currentName: string;
  formatTick: (value: number) => string;
  formatValue: (value: number) => string;
  /**
   * 7-day trailing mean aligned to `dates`, null where history runs out. Omitted only by a payload
   * that predates the series, which falls back to a mean of the displayed values.
   */
  rolling?: (number | null)[];
}

interface Point {
  x: number;
  y: number;
}

/** Contiguous runs of non-null points, so a gap in history breaks the line instead of bridging it. */
const splitSegments = (points: (Point | null)[]): Point[][] => {
  const segments: Point[][] = [];
  let current: Point[] = [];
  for (const point of points) {
    if (point == null) {
      if (current.length > 0) segments.push(current);
      current = [];
    } else {
      current.push(point);
    }
  }
  if (current.length > 0) segments.push(current);
  return segments;
};

/** Catmull-Rom spline through the points, as an SVG cubic-bezier path. */
const smoothPath = (points: Point[], maxY?: number): string => {
  if (points.length === 0) return "";
  if (points.length === 1) return `M ${points[0].x} ${points[0].y}`;
  let d = `M ${points[0].x} ${points[0].y}`;
  for (let i = 0; i < points.length - 1; i++) {
    const p0 = points[i - 1] ?? points[i];
    const p1 = points[i];
    const p2 = points[i + 1];
    const p3 = points[i + 2] ?? p2;
    let c1x = p1.x + (p2.x - p0.x) / 6;
    let c1y = p1.y + (p2.y - p0.y) / 6;
    let c2x = p2.x - (p3.x - p1.x) / 6;
    let c2y = p2.y - (p3.y - p1.y) / 6;
    // Enforce monotonicity on x to prevent any backward loops
    c1x = Math.max(p1.x, Math.min(p2.x, c1x));
    c2x = Math.max(p1.x, Math.min(p2.x, c2x));
    if (maxY != null) {
      c1y = Math.min(c1y, maxY);
      c2y = Math.min(c2y, maxY);
    }
    d += ` C ${c1x} ${c1y}, ${c2x} ${c2y}, ${p2.x} ${p2.y}`;
  }
  return d;
};

const DEFAULT_HEIGHT = 236;
const PAD_TOP = 10;
const PAD_BOTTOM = 26;
const PAD_RIGHT = 8;
const Y_LABEL_WIDTH = 44;
/* Keeps zero-valued points slightly above the x-axis labels. */
const ZERO_LIFT = 6;

export const TrendChart: React.FC<TrendChartProps> = ({
  dates,
  values,
  currentName,
  formatTick,
  formatValue,
  rolling,
}) => {
  const wrapRef = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ width: 0, height: DEFAULT_HEIGHT });
  const [hoverIndex, setHoverIndex] = useState<number | null>(null);
  const gradientId = useId();

  useLayoutEffect(() => {
    const el = wrapRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    setSize({
      width: rect.width,
      height: Math.max(DEFAULT_HEIGHT, rect.height),
    });
    const observer = new ResizeObserver((entries) => {
      const entry = entries[0];
      if (entry) {
        setSize({
          width: entry.contentRect.width,
          height: Math.max(DEFAULT_HEIGHT, entry.contentRect.height),
        });
      }
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const { width, height } = size;
  const n = dates.length;
  const plotLeft = Y_LABEL_WIDTH;
  const plotWidth = Math.max(0, width - Y_LABEL_WIDTH - PAD_RIGHT);
  const plotBottom = height - PAD_BOTTOM;
  const zeroY = plotBottom - ZERO_LIFT;
  const plotHeight = zeroY - PAD_TOP;

  const rollingSeries = useMemo(
    () => (rolling != null && rolling.length > 0 ? rolling : computeRollingAverage(values)),
    [rolling, values],
  );

  const { ticks, points, rollingSegments } = useMemo(() => {
    // The rolling series counts towards the scale: its first points average days from before the
    // displayed range, which can sit above everything on screen.
    const rollingValues = rollingSeries.filter((v): v is number => v != null);
    const maxValue = Math.max(1, ...values, ...rollingValues);
    const tickValues = niceTicks(maxValue, 4);
    const scaleMax = tickValues[tickValues.length - 1];
    const toPoint = (value: number, index: number): Point => ({
      x: plotLeft + (n <= 1 ? plotWidth / 2 : (index / (n - 1)) * plotWidth),
      y: zeroY - (value / scaleMax) * plotHeight,
    });
    return {
      ticks: tickValues,
      points: values.map(toPoint),
      rollingSegments: splitSegments(
        rollingSeries.map((value, index) => (value != null ? toPoint(value, index) : null)),
      ),
    };
  }, [values, rollingSeries, n, plotLeft, plotWidth, zeroY, plotHeight]);

  const scaleTop = ticks[ticks.length - 1];

  // The year is only worth the axis space when the range straddles one, and then only on the points
  // that are not in the range's own end year.
  const endYear = n > 0 ? dates[n - 1].slice(0, 4) : "";
  const spansYears = n > 0 && dates[0].slice(0, 4) !== endYear;

  const areaPath = useMemo(() => {
    if (points.length < 2) return "";
    const line = smoothPath(points, zeroY);
    const last = points[points.length - 1];
    const first = points[0];
    return `${line} L ${last.x} ${zeroY} L ${first.x} ${zeroY} Z`;
  }, [points, zeroY]);

  const onMove = (e: React.MouseEvent<SVGSVGElement>) => {
    if (n === 0 || plotWidth <= 0) return;
    const rect = e.currentTarget.getBoundingClientRect();
    const x = e.clientX - rect.left - plotLeft;
    const index = Math.round((x / plotWidth) * (n - 1));
    setHoverIndex(Math.max(0, Math.min(n - 1, index)));
  };

  const hover =
    hoverIndex != null && values[hoverIndex] != null
      ? {
          index: hoverIndex,
          x: plotLeft + (n <= 1 ? plotWidth / 2 : (hoverIndex / (n - 1)) * plotWidth),
          value: values[hoverIndex],
          rollingValue: rollingSeries[hoverIndex] ?? null,
        }
      : null;

  return (
    <div className="tdb-chart-wrap" ref={wrapRef}>
      {width > 0 && (
        <svg
          height={height}
          viewBox={`0 0 ${width} ${height}`}
          onMouseMove={onMove}
          onMouseLeave={() => setHoverIndex(null)}
        >
          <defs>
            <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="currentColor" stopOpacity="0.09" />
              <stop offset="100%" stopColor="currentColor" stopOpacity="0" />
            </linearGradient>
          </defs>
          {ticks.map((tick) => {
            const y = zeroY - (tick / scaleTop) * plotHeight;
            return (
              <text key={tick} className="tdb-axis-text" x={0} y={y + 4}>
                {tick === 0 ? "0" : formatTick(tick)}
              </text>
            );
          })}
          {dates.map((date, i) => {
            const maxVisible = Math.max(4, Math.floor(plotWidth / 60));
            const step = n > maxVisible ? Math.ceil((n - 1) / maxVisible) : 1;
            const isVisible = i === 0 || i === n - 1 || i % step === 0;
            if (!isVisible) return null;
            return (
              <text
                key={date + i}
                className="tdb-axis-text"
                x={plotLeft + (n <= 1 ? plotWidth / 2 : (i / (n - 1)) * plotWidth)}
                y={height - 6}
                textAnchor="middle"
              >
                {formatAxisDate(date, spansYears && date.slice(0, 4) !== endYear)}
              </text>
            );
          })}
          {areaPath && <path d={areaPath} fill={`url(#${gradientId})`} style={{ color: "var(--tdb-fg)" }} />}
          {rollingSegments.map((segment, i) => (
            <path
              key={`rolling-${i}`}
              className="tdb-trend-avg-curve"
              d={smoothPath(segment, zeroY)}
            />
          ))}
          {points.length > 1 && <path className="tdb-trend-line" d={smoothPath(points, zeroY)} />}
          {hover && (
            <g>
              <line
                x1={hover.x}
                x2={hover.x}
                y1={PAD_TOP}
                y2={zeroY}
                stroke="var(--tdb-divider)"
                strokeDasharray="3 3"
              />
              <circle cx={hover.x} cy={points[hover.index].y} r={3.5} fill="var(--tdb-fg)" />
            </g>
          )}
        </svg>
      )}
      {hover && (
        <div className="tdb-chart-tooltip" style={{ left: hover.x, top: PAD_TOP + 12 }}>
          <div className="tdb-chart-tooltip-title">{formatTooltipDate(dates[hover.index])}</div>
          <div className="tdb-chart-tooltip-row">
            <span className="tdb-legend-dot" />
            {currentName}: {formatValue(hover.value)}
          </div>
          {hover.rollingValue != null && (
            <div className="tdb-chart-tooltip-row">
              <span className="tdb-legend-line-avg" />
              7-day average: {formatValue(hover.rollingValue)}
            </div>
          )}
        </div>
      )}
    </div>
  );
};
