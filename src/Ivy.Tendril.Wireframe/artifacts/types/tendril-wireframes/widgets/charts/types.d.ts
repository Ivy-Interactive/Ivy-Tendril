import { WidgetBaseProps } from '../../lib/types';
import { ColorScheme } from '../../sketch/colors';
export type { ColorScheme };
export interface ChartData {
    [key: string]: string | number;
}
export interface CartesianGridProps {
    horizontal?: boolean;
    vertical?: boolean;
    stroke?: string | null;
    strokeDashArray?: string | null;
    fill?: string | null;
    fillOpacity?: number | null;
}
export interface LegendProps {
    align?: "Left" | "Center" | "Right";
    iconSize?: number;
    iconType?: string | null;
    layout?: "Horizontal" | "Vertical";
    verticalAlign?: "Top" | "Middle" | "Bottom";
}
export type PieLegendProps = LegendProps;
export interface ToolTipProps {
    animated?: boolean;
    valueFormat?: string | null;
    valueFormatType?: "Auto" | "Number" | "Date" | null;
}
export interface ToolboxProps {
    enabled?: boolean;
    orientation?: "Horizontal" | "Vertical";
    align?: "Left" | "Center" | "Right";
    verticalAlign?: "Top" | "Middle" | "Bottom";
    saveAsImage?: boolean;
    restore?: boolean;
    dataView?: boolean;
    magicType?: boolean;
}
export interface AxisProps {
    dataKey?: string;
    allowDecimals?: boolean;
    angle?: number;
    axisLine?: boolean;
    domainMin?: number | string;
    domainMax?: number | string;
    hide?: boolean;
    label?: string | null;
    name?: string | null;
    reversed?: boolean;
    scale?: "Auto" | "Linear" | "Log" | "Time" | "Ordinal";
    tickCount?: number;
    tickLine?: boolean;
    tickSize?: number;
    type?: "Category" | "Number" | "Time";
    unit?: string | null;
    hideTickLabels?: boolean;
    width?: number;
    height?: number;
}
export interface XAxisProps extends AxisProps {
    orientation?: "Top" | "Bottom";
}
export interface YAxisProps extends AxisProps {
    orientation?: "Left" | "Right";
}
export interface ZAxisProps {
    dataKey?: string;
    rangeMin?: number;
    rangeMax?: number;
    unit?: string | null;
    name?: string | null;
}
export interface ReferenceDot {
    x: number;
    y: number;
    label?: string;
}
export interface MarkLine {
    x?: number | string;
    y?: number | string;
    label?: string;
    stroke?: string;
    strokeDashArray?: string;
}
export interface MarkArea {
    x1?: number | string;
    x2?: number | string;
    y1?: number | string;
    y2?: number | string;
    label?: string;
    fill?: string;
}
/** Props shared by every chart in the library. */
export interface BaseChartProps extends WidgetBaseProps {
    colorScheme?: ColorScheme;
    tooltip?: ToolTipProps;
    legend?: LegendProps;
    toolbox?: ToolboxProps;
}
export type StackOffset = "None" | "Expand" | "Wiggle" | "Silhouette";
export interface CartesianChartProps extends BaseChartProps {
    data?: ChartData[];
    cartesianGrid?: CartesianGridProps;
    xAxis?: XAxisProps[];
    yAxis?: YAxisProps[];
    referenceLines?: MarkLine[];
    referenceAreas?: MarkArea[];
    referenceDots?: ReferenceDot[];
    layout?: "Horizontal" | "Vertical";
}
export interface LineSeries {
    dataKey: string;
    name?: string;
    stroke?: string | null;
    strokeWidth?: number;
    strokeDashArray?: string | null;
    curveType?: "Linear" | "Monotone" | "Step";
    connectNulls?: boolean;
    stackId?: string | number;
    unit?: string | null;
    animated?: boolean;
    label?: string | null;
    legendType?: string;
}
export interface BarSeries {
    dataKey: string;
    name?: string;
    fill?: string | null;
    fillOpacity?: number | null;
    stroke?: string | null;
    strokeWidth?: number;
    stackId?: string | number;
    radius?: number[];
    unit?: string | null;
    animated?: boolean;
    labelLists?: string[];
    legendType?: string;
}
export type ScatterShape = "Circle" | "Square" | "Cross" | "Diamond" | "Star" | "Triangle" | "Wye";
export interface ScatterSeries {
    dataKey: string;
    name: string;
    fill?: string | null;
    fillOpacity?: number | null;
    stroke?: string | null;
    strokeWidth?: number;
    shape?: ScatterShape;
    line?: boolean;
    lineType?: "Joint" | "Fitting";
    unit?: string | null;
    animated?: boolean;
}
export interface PieSeries {
    dataKey: string;
    nameKey: string;
    innerRadius?: string | number;
    outerRadius?: string | number;
    startAngle?: number;
    endAngle?: number;
    fill?: string | null;
    fillOpacity?: number | null;
    stroke?: string | null;
    strokeWidth?: number;
    animated?: boolean;
    labelLists?: string[];
    legendType?: string;
}
export interface RadarSeries {
    dataKey: string;
    name?: string | null;
    filled?: boolean;
    fill?: string | null;
    stroke?: string | null;
    strokeWidth?: number;
    strokeDashArray?: string | null;
    showSymbol?: boolean;
    legendType?: string;
    labelLists?: string[];
}
export interface RadarIndicator {
    name: string;
    max?: number;
    min?: number;
}
export interface FunnelSeries {
    dataKey: string;
    nameKey: string;
    fill?: string | null;
    fillOpacity?: number | null;
    stroke?: string | null;
    strokeWidth?: number;
    minSize?: string;
    maxSize?: string;
    animated?: boolean;
    legendType?: string;
}
export interface FlowNode {
    name: string;
}
export interface FlowLink {
    source: number;
    target: number;
    value: number;
}
export interface FlowData {
    nodes: FlowNode[];
    links: FlowLink[];
}
export type SankeyData = FlowData;
export type ChordData = FlowData;
export type GaugePointerStyle = "Line" | "Arrow" | "Rounded";
export interface GaugeThreshold {
    value: number;
    color: string;
}
export interface GaugePointer {
    style?: GaugePointerStyle;
    width?: number;
    /** Percentage of the radius, e.g. `"60%"`. */
    length?: string;
}
