import React, { useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { niceTicks } from "./types";

interface TrendChartProps {
  labels: string[];
  values: number[];
  previous?: (number | null)[];
  currentName: string;
  previousName: string;
  formatTick: (value: number) => string;
  formatValue: (value: number) => string;
}

interface Point {
  x: number;
  y: number;
}

/** Catmull-Rom spline through the points, as an SVG cubic-bezier path. */
const smoothPath = (points: Point[]): string => {
  if (points.length === 0) return "";
  if (points.length === 1) return `M ${points[0].x} ${points[0].y}`;
  let d = `M ${points[0].x} ${points[0].y}`;
  for (let i = 0; i < points.length - 1; i++) {
    const p0 = points[i - 1] ?? points[i];
    const p1 = points[i];
    const p2 = points[i + 1];
    const p3 = points[i + 2] ?? p2;
    const c1x = p1.x + (p2.x - p0.x) / 6;
    const c1y = p1.y + (p2.y - p0.y) / 6;
    const c2x = p2.x - (p3.x - p1.x) / 6;
    const c2y = p2.y - (p3.y - p1.y) / 6;
    d += ` C ${c1x} ${c1y}, ${c2x} ${c2y}, ${p2.x} ${p2.y}`;
  }
  return d;
};

const HEIGHT = 236;
const PAD_TOP = 10;
const PAD_BOTTOM = 26;
const PAD_RIGHT = 8;
const Y_LABEL_WIDTH = 44;
/* Keeps zero-valued points slightly above the x-axis labels. */
const ZERO_LIFT = 6;

export const TrendChart: React.FC<TrendChartProps> = ({
  labels,
  values,
  previous = [],
  currentName,
  previousName,
  formatTick,
  formatValue,
}) => {
  const wrapRef = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  const [hoverIndex, setHoverIndex] = useState<number | null>(null);
  const gradientId = useId();

  useLayoutEffect(() => {
    const el = wrapRef.current;
    if (!el) return;
    setWidth(el.getBoundingClientRect().width);
    const observer = new ResizeObserver((entries) => {
      setWidth(entries[0].contentRect.width);
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const n = labels.length;
  const plotLeft = Y_LABEL_WIDTH;
  const plotWidth = Math.max(0, width - Y_LABEL_WIDTH - PAD_RIGHT);
  const plotBottom = HEIGHT - PAD_BOTTOM;
  const zeroY = plotBottom - ZERO_LIFT;
  const plotHeight = zeroY - PAD_TOP;

  const { ticks, points, previousPoints } = useMemo(() => {
    const previousValues = previous.filter((v): v is number => v != null);
    const maxValue = Math.max(1, ...values, ...previousValues);
    const tickValues = niceTicks(maxValue, 4);
    const scaleMax = tickValues[tickValues.length - 1];
    const toPoint = (value: number, index: number): Point => ({
      x: plotLeft + (n <= 1 ? plotWidth / 2 : (index / (n - 1)) * plotWidth),
      y: zeroY - (value / scaleMax) * plotHeight,
    });
    return {
      ticks: tickValues,
      points: values.map(toPoint),
      previousPoints: previous
        .map((value, index) => (value != null ? toPoint(value, index) : null))
        .filter((p): p is Point => p != null),
    };
  }, [values, previous, n, plotLeft, plotWidth, zeroY, plotHeight]);

  const scaleTop = ticks[ticks.length - 1];

  const areaPath = useMemo(() => {
    if (points.length < 2) return "";
    const line = smoothPath(points);
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
          previousValue: previous[hoverIndex] ?? null,
          previousY:
            previous[hoverIndex] != null
              ? zeroY - (previous[hoverIndex]! / scaleTop) * plotHeight
              : null,
        }
      : null;

  return (
    <div className="tdb-chart-wrap" ref={wrapRef}>
      {width > 0 && (
        <svg
          height={HEIGHT}
          viewBox={`0 0 ${width} ${HEIGHT}`}
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
          {labels.map((label, i) => (
            <text
              key={label + i}
              className="tdb-axis-text"
              x={plotLeft + (n <= 1 ? plotWidth / 2 : (i / (n - 1)) * plotWidth)}
              y={HEIGHT - 6}
              textAnchor="middle"
            >
              {label}
            </text>
          ))}
          {areaPath && <path d={areaPath} fill={`url(#${gradientId})`} style={{ color: "var(--tdb-fg)" }} />}
          {previousPoints.length > 1 && <path className="tdb-trend-compare" d={smoothPath(previousPoints)} />}
          {points.length > 1 && <path className="tdb-trend-line" d={smoothPath(points)} />}
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
              {hover.previousY != null && (
                <circle cx={hover.x} cy={hover.previousY} r={3.5} fill="var(--tdb-compare)" />
              )}
            </g>
          )}
        </svg>
      )}
      {hover && (
        <div className="tdb-chart-tooltip" style={{ left: hover.x, top: PAD_TOP + 12 }}>
          <div className="tdb-chart-tooltip-title">{labels[hover.index]}</div>
          <div className="tdb-chart-tooltip-row">
            <span className="tdb-legend-dot" />
            {currentName}: {formatValue(hover.value)}
          </div>
          {hover.previousValue != null && (
            <div className="tdb-chart-tooltip-row">
              <span className="tdb-legend-dash" />
              {previousName}: {formatValue(hover.previousValue)}
            </div>
          )}
        </div>
      )}
    </div>
  );
};
