import { BaseChartProps, ChartData, FunnelSeries, GaugePointer, GaugeThreshold, PieSeries, RadarIndicator, RadarSeries } from './types';
import * as React from "react";
declare const polar: (cx: number, cy: number, radius: number, angle: number) => [number, number];
/** Donut/pie wedge as an SVG path, so rough.js can scribble over it. */
declare function wedgePath(cx: number, cy: number, outer: number, inner: number, start: number, end: number): string;
export interface PieChartProps extends BaseChartProps {
    data?: ChartData[];
    pies?: PieSeries[];
    /** Centre label for a donut, e.g. total revenue. */
    total?: {
        formattedValue: string;
        label: string;
    };
}
/**
 * Mirrors `Ivy.PieChart`.
 *
 * @tags chart proportion share donut
 * @example <PieChart data={rows} pies={[{ dataKey: "value", nameKey: "name" }]} />
 */
export declare const PieChart: ({ id, data, pies, total, colorScheme, legend, width, height, aspectRatio, visible, density, className, style, }: PieChartProps) => React.JSX.Element;
export interface RadarChartProps extends BaseChartProps {
    data?: ChartData[];
    radars?: RadarSeries[];
    indicators?: RadarIndicator[];
    shape?: "Polygon" | "Circle";
    splitLine?: boolean;
    /** Shades alternate rings, the way Ivy's `SplitArea` does. */
    splitArea?: boolean;
    axisLine?: boolean;
    radius?: string | number;
    startAngle?: number;
    /** Centre of the web, as a percentage of the plot or a pixel value. */
    cx?: string | number;
    cy?: string | number;
}
/** Mirrors `Ivy.RadarChart`. */
export declare const RadarChart: ({ id, data, radars, indicators, shape, splitLine, splitArea, axisLine, radius, cx: cxProp, cy: cyProp, startAngle, colorScheme, legend, width, height, aspectRatio, visible, density, className, style, }: RadarChartProps) => React.JSX.Element;
export interface FunnelChartProps extends BaseChartProps {
    data?: ChartData[];
    funnels?: FunnelSeries[];
    sort?: "Descending" | "Ascending" | "None";
    orientation?: "Vertical" | "Horizontal";
    gap?: number;
}
/** Mirrors `Ivy.FunnelChart`. */
export declare const FunnelChart: ({ id, data, funnels, sort, gap, colorScheme, legend, width, height, aspectRatio, visible, density, className, style, }: FunnelChartProps) => React.JSX.Element;
export interface GaugeChartProps extends BaseChartProps {
    value?: number;
    min?: number;
    max?: number;
    label?: string;
    /** Degrees, measured the way Ivy does: 225 down to -45 by default. */
    startAngle?: number;
    endAngle?: number;
    thresholds?: GaugeThreshold[];
    pointer?: GaugePointer;
    animated?: boolean;
}
/**
 * Mirrors `Ivy.GaugeChart`.
 *
 * @tags chart dial meter single-value
 * @example <GaugeChart value={72} label="Capacity" />
 */
export declare const GaugeChart: ({ id, value, min, max, label, startAngle, endAngle, thresholds, pointer, colorScheme, width, height, aspectRatio, visible, density, className, style, }: GaugeChartProps) => React.JSX.Element;
export { polar, wedgePath };
