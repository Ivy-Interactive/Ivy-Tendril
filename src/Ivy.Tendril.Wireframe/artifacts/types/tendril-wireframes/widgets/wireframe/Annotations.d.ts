import { WidgetBaseProps } from '../../lib/types';
import * as React from "react";
export interface WireframeNoteProps extends WidgetBaseProps {
    text?: string;
    color?: string;
    children?: React.ReactNode;
}
/**
 * A sticky note pinned to the wireframe. Mirrors `Ivy.WireframeNote`.
 *
 * @tags annotation sticky comment
 * @example <WireframeNote text="Copy still to be written." />
 */
export declare const WireframeNote: ({ id, text, color, width, height, aspectRatio, visible, children, className, style, }: WireframeNoteProps) => React.JSX.Element;
export interface WireframeCalloutProps extends WidgetBaseProps {
    /** Short marker text, e.g. `"1"` or `"A"`. */
    label?: string;
    color?: string;
    /** Draws a leader line pointing this many pixels to the right. */
    leader?: number;
    children?: React.ReactNode;
}
/**
 * A numbered marker for annotating a wireframe. Mirrors `Ivy.WireframeCallout`.
 *
 * @tags annotation marker numbered
 * @example <WireframeCallout label="1" leader={60}>Primary action</WireframeCallout>
 */
export declare const WireframeCallout: ({ id, label, color, leader, children, width, height, aspectRatio, visible, className, style, }: WireframeCalloutProps) => React.JSX.Element;
