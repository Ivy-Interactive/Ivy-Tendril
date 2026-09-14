import { WidgetBaseProps } from '../../lib/types';
import * as React from "react";
export type ArrowHeads = "None" | "Start" | "End" | "Both";
export type ArrowBend = "None" | "Left" | "Right";
export type ArrowDirection = "Right" | "Left" | "Up" | "Down" | "UpLeft" | "UpRight" | "DownLeft" | "DownRight";
export interface WireframeArrowProps extends WidgetBaseProps {
    color?: string;
    /** Which way the shaft runs across the box. */
    direction?: ArrowDirection;
    /** Which end (or both) gets a head. */
    heads?: ArrowHeads;
    /** Bows the shaft off the straight line, to either side. */
    bend?: ArrowBend;
    dashed?: boolean;
}
/**
 * A drawn-on arrow, for pointing one part of a sketch at another.
 * Mirrors `Ivy.WireframeArrow`.
 *
 * @tags annotation pointer connector leader
 * @example <WireframeArrow direction="Right" />
 * @example <WireframeArrow direction="DownRight" bend="Left" heads="Both" dashed />
 */
export declare const WireframeArrow: ({ id, color, direction, heads, bend, dashed, density, width, height, aspectRatio, visible, className, style, }: WireframeArrowProps) => React.JSX.Element;
