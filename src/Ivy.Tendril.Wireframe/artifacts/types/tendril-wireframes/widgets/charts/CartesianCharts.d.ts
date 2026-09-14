import { BarSeries, CartesianChartProps, LineSeries, ScatterSeries, StackOffset, ZAxisProps } from './types';
export interface LineChartProps extends CartesianChartProps {
    lines?: LineSeries[];
}
/**
 * Mirrors `Ivy.LineChart`.
 *
 * @tags chart trend series time
 * @example <LineChart data={rows} xAxis={[{ dataKey: "month" }]} lines={[{ dataKey: "revenue" }]} />
 */
export declare const LineChart: ({ id, data, lines, cartesianGrid, xAxis, yAxis, colorScheme, legend, width, height, aspectRatio, visible, density, className, style, referenceLines, referenceDots, }: LineChartProps) => import("react").JSX.Element;
export interface AreaChartProps extends CartesianChartProps {
    areas?: LineSeries[];
    /** `Expand` normalises each stack to 100%. Mirrors Ivy's `StackOffset`. */
    stackOffset?: StackOffset;
}
/** Mirrors `Ivy.AreaChart`. */
export declare const AreaChart: ({ id, data, areas, stackOffset, cartesianGrid, xAxis, yAxis, colorScheme, legend, width, height, aspectRatio, visible, density, className, style, }: AreaChartProps) => import("react").JSX.Element;
export interface BarChartProps extends CartesianChartProps {
    bars?: BarSeries[];
    barGap?: number;
    barCategoryGap?: number | string;
    maxBarSize?: number;
    reverseStackOrder?: boolean;
    /** `Expand` normalises each stack to 100%. Mirrors Ivy's `StackOffset`. */
    stackOffset?: StackOffset;
}
/**
 * Mirrors `Ivy.BarChart`, including grouped, stacked and horizontal layouts.
 *
 * @tags chart comparison categories
 * @example <BarChart data={rows} xAxis={[{ dataKey: "month" }]} bars={[{ dataKey: "sales" }]} />
 */
export declare const BarChart: ({ id, data, bars, cartesianGrid, xAxis, yAxis, colorScheme, legend, barGap, maxBarSize, stackOffset, layout, width, height, aspectRatio, visible, density, className, style, }: BarChartProps) => import("react").JSX.Element;
export interface ScatterChartProps extends CartesianChartProps {
    scatters?: ScatterSeries[];
    zAxis?: ZAxisProps | null;
}
/** Mirrors `Ivy.ScatterChart`. */
export declare const ScatterChart: ({ id, data, scatters, zAxis, cartesianGrid, xAxis, yAxis, colorScheme, legend, width, height, aspectRatio, visible, density, className, style, }: ScatterChartProps) => import("react").JSX.Element;
