import { WidgetBaseProps } from '../lib/types';
export interface ProgressProps extends WidgetBaseProps {
    /** 0 – 100. Ignored when `indeterminate`. */
    value?: number;
    /** Caption under the bar, e.g. `"3 of 8 uploaded"`. */
    goal?: string;
    color?: string;
    indeterminate?: boolean;
}
/**
 * A pencilled progress bar. Mirrors `Ivy.Progress`.
 *
 * @tags loading percentage
 * @example <Progress value={62} goal="Uploading" />
 */
export declare const Progress: ({ id, value, goal, color, width, height, aspectRatio, visible, indeterminate, density, className, style, }: ProgressProps) => import("react").JSX.Element;
export interface ProgressSegment {
    value: number;
    color?: string;
    label?: string;
}
export interface StackedProgressProps extends WidgetBaseProps {
    segments?: ProgressSegment[];
    barHeight?: number;
    showLabels?: boolean;
    rounded?: boolean;
    /** Index of the segment to emphasise. */
    selected?: number;
    onSelect?: (index: number, segment: ProgressSegment) => void;
}
/**
 * Several proportional segments in one bar. Mirrors `Ivy.StackedProgress`.
 *
 * @tags breakdown proportion
 * @example <StackedProgress segments={[{ value: 40, label: "Done" }, { value: 60, label: "Todo" }]} />
 */
export declare const StackedProgress: ({ id, segments, barHeight, showLabels, rounded, selected, width, height: rootHeight, aspectRatio, visible, density, className, style, onSelect, }: StackedProgressProps) => import("react").JSX.Element;
