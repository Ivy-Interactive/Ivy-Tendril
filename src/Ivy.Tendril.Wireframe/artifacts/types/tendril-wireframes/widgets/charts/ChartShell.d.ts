import { WidgetBaseProps } from '../../lib/types';
import { ColorScheme } from '../../sketch/colors';
import { CartesianGridProps, ChartData, LegendProps, XAxisProps, YAxisProps } from './types';
import * as React from "react";
export interface SeriesLegendEntry {
    name: string;
    color: string;
}
export declare const LEGEND_ALIGN: Record<string, string>;
/**
 * The series key drawn under (or over) a chart's plot area.
 *
 * @internal
 */
export declare const ChartLegend: ({ entries, legend, }: {
    entries: SeriesLegendEntry[];
    legend?: LegendProps;
}) => React.JSX.Element | null;
export interface ChartShellProps extends WidgetBaseProps {
    legend?: LegendProps;
    legendEntries?: SeriesLegendEntry[];
    /** Receives the plot area in pixels once it has been measured. */
    children: (size: {
        width: number;
        height: number;
    }) => React.ReactNode;
}
/**
 * Paper, border, legend and a measured drawing area for every chart.
 *
 * @internal
 */
export declare const ChartShell: ({ id, width, height, aspectRatio, visible, className, style, legend, legendEntries, children, ...rest }: ChartShellProps) => React.JSX.Element;
export interface Scales {
    plot: {
        left: number;
        top: number;
        width: number;
        height: number;
    };
    xOf: (index: number) => number;
    yOf: (value: number) => number;
    bandWidth: number;
    yMin: number;
    yMax: number;
    ticks: number[];
}
export declare function buildScales(width: number, height: number, data: ChartData[], values: number[], options?: {
    tickCount?: number;
    padLeft?: number;
    padBottom?: number;
    zeroBased?: boolean;
}): Scales;
export declare const formatTick: (value: number) => string;
export interface AxesProps {
    scales: Scales;
    data: ChartData[];
    categoryKey?: string;
    grid?: CartesianGridProps;
    xAxis?: XAxisProps;
    yAxis?: YAxisProps;
    seed: string;
}
/**
 * Hand-drawn axes, ticks and grid lines shared by the cartesian charts.
 *
 * @internal
 */
export declare const Axes: ({ scales, data, categoryKey, grid, xAxis, yAxis, seed }: AxesProps) => React.JSX.Element;
export declare const legendEntriesFrom: <T extends {
    dataKey: string;
    name?: string | null;
}>(series: T[], scheme: ColorScheme | undefined, colorOf: (item: T, index: number) => string | null | undefined) => SeriesLegendEntry[];
