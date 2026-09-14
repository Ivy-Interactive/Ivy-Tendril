import { WidgetBaseProps } from '../../lib/types';
import * as React from "react";
/**
 * Which way the brace's centre nub points. This follows the wireframing
 * convention the widget is modelled on: the name is the direction of the nub,
 * not of the span. A `Horizontal` brace is the familiar `{` — it spans
 * downwards with the nub pointing left.
 */
export type CurlyBraceVariant = "Horizontal" | "Vertical";
export interface WireframeCurlyBraceProps extends WidgetBaseProps {
    /** Direction the centre nub points, not the direction of the span. */
    variant?: CurlyBraceVariant;
    color?: string;
}
/**
 * A brace gathering a run of the sketch together, to be labelled as one thing.
 * Mirrors `Ivy.WireframeCurlyBrace`.
 *
 * @tags annotation group span bracket
 * @example <WireframeCurlyBrace height="12rem" />
 * @example <WireframeCurlyBrace variant="Vertical" width="16rem" />
 */
export declare const WireframeCurlyBrace: ({ id, variant, color, density, width, height, aspectRatio, visible, className, style, }: WireframeCurlyBraceProps) => React.JSX.Element;
