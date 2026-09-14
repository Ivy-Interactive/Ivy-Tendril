import { BaseChartProps, ChordData, SankeyData } from './types';
export type SankeyAlign = "Justify" | "Left";
export interface SankeyChartProps extends BaseChartProps {
    data?: SankeyData | null;
    nodeWidth?: number;
    nodeGap?: number;
    curvature?: number;
    nodeAlign?: SankeyAlign;
    layoutIterations?: number;
}
/** Mirrors `Ivy.SankeyChart`. */
export declare const SankeyChart: ({ id, data, nodeWidth, nodeGap, curvature, nodeAlign, colorScheme, legend, width, height, aspectRatio, visible, density, className, style, }: SankeyChartProps) => import("react").JSX.Element;
export interface ChordChartProps extends BaseChartProps {
    data?: ChordData | null;
    sort?: boolean;
    sortSubGroups?: boolean;
    padAngle?: number;
}
/** Mirrors `Ivy.ChordChart`. */
export declare const ChordChart: ({ id, data, padAngle, colorScheme, legend, width, height, aspectRatio, visible, density, className, style, }: ChordChartProps) => import("react").JSX.Element;
