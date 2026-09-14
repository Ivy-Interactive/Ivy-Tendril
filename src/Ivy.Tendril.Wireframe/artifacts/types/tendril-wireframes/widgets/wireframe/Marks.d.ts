import { WidgetBaseProps } from '../../lib/types';
import * as React from "react";
export interface WireframePlaceholderProps extends WidgetBaseProps {
    /** Label drawn across the middle, breaking the diagonals. */
    text?: string;
    color?: string;
}
/**
 * The crossed box that stands in for content nobody has drawn yet.
 * Mirrors `Ivy.WireframePlaceholder`.
 *
 * @tags annotation stub todo empty
 * @example <WireframePlaceholder text="Hero image" />
 * @example <WireframePlaceholder text="Chart goes here" color="Sky" width="20rem" />
 */
export declare const WireframePlaceholder: ({ id, text, color, density, width, height, aspectRatio, visible, className, style, }: WireframePlaceholderProps) => React.JSX.Element;
export interface WireframeRedXProps extends WidgetBaseProps {
    color?: string;
}
/**
 * A marker X struck over a region — this part is wrong, or cut.
 * Mirrors `Ivy.WireframeRedX`.
 *
 * @tags annotation reject cross-out
 * @example <WireframeRedX width="12rem" height="6rem" />
 */
export declare const WireframeRedX: ({ id, color, density, width, height, aspectRatio, visible, className, style, }: WireframeRedXProps) => React.JSX.Element;
export interface WireframeScratchOutProps extends WidgetBaseProps {
    color?: string;
}
/**
 * A scribbled-over region — struck through, but still legible underneath.
 * Mirrors `Ivy.WireframeScratchOut`.
 *
 * @tags annotation strike delete scribble
 * @example <WireframeScratchOut width="14rem" height="4rem" />
 */
export declare const WireframeScratchOut: ({ id, color, density, width, height, aspectRatio, visible, className, style, }: WireframeScratchOutProps) => React.JSX.Element;
