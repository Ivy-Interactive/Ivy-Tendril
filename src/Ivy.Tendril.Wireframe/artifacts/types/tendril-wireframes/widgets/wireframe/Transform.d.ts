import { WidgetBaseProps } from '../../lib/types';
import * as React from "react";
/** The anchor a transform pivots and scales around. */
export type TransformOrigin = "Center" | "TopLeft" | "Top" | "TopRight" | "Left" | "Right" | "BottomLeft" | "Bottom" | "BottomRight";
export interface WireframeTransformProps extends WidgetBaseProps {
    /** Clockwise rotation in degrees. */
    rotate?: number;
    /** Uniform scale factor. 1 is unchanged. */
    scale?: number;
    /** Horizontal scale, overriding `scale` on that axis. */
    scaleX?: number;
    /** Vertical scale, overriding `scale` on that axis. */
    scaleY?: number;
    /** Horizontal skew in degrees. */
    skewX?: number;
    /** Vertical skew in degrees. */
    skewY?: number;
    /** Horizontal nudge in pixels. */
    offsetX?: number;
    /** Vertical nudge in pixels. */
    offsetY?: number;
    flipHorizontal?: boolean;
    flipVertical?: boolean;
    origin?: TransformOrigin;
    /**
     * Shrinks the widget's own layout box to the transformed bounds, so a scaled
     * or rotated child takes up the room it visually occupies instead of the room
     * it started with. Off by default, which is the plain CSS behaviour. When on,
     * `origin` no longer affects the result — the bounds are recentred either way.
     */
    fit?: boolean;
    /** Fades the children. 1 is opaque. */
    opacity?: number;
    children?: React.ReactNode;
}
/**
 * Renders its children under a transform: rotated, scaled, skewed, flipped or
 * nudged. The transform is visual only — children keep the layout box they
 * started with, so a rotated child does not push its neighbours around. Turn
 * `fit` on when the transformed bounds should take up room.
 * Mirrors `Ivy.WireframeTransform`.
 *
 * @tags rotate scale skew flip tilt
 * @slot children The subtree the transform applies to
 * @example <WireframeTransform rotate={-2}><Card title="Pinned up crooked" /></WireframeTransform>
 * @example <WireframeTransform scale={0.5} fit><WireframeMockup variant="Mobile" /></WireframeTransform>
 */
export declare const WireframeTransform: ({ id, rotate, scale, scaleX, scaleY, skewX, skewY, offsetX, offsetY, flipHorizontal, flipVertical, origin, fit, opacity, children, width, height, aspectRatio, visible, className, style, }: WireframeTransformProps) => React.JSX.Element;
