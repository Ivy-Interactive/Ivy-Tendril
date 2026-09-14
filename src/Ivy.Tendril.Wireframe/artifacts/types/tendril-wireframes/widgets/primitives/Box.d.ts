import { Align, BorderRadius, BorderStyle, HoverEffect, Sizing, WidgetBaseProps } from '../../lib/types';
import { SketchCorner, SketchOutline } from '../../sketch/SketchFrame';
import * as React from "react";
export declare const HOVER_CLASS: Record<HoverEffect, string>;
export declare const cornerFor: (radius?: BorderRadius) => SketchCorner;
export declare const outlineFor: (style?: BorderStyle) => SketchOutline;
export interface BoxProps extends WidgetBaseProps {
    children?: React.ReactNode;
    background?: string;
    borderRadius?: BorderRadius;
    borderThickness?: Sizing;
    borderStyle?: BorderStyle;
    borderColor?: string;
    padding?: Sizing;
    margin?: Sizing;
    contentAlign?: Align;
    opacity?: number;
    borderOpacity?: number;
    hoverVariant?: HoverEffect;
    onClick?: React.MouseEventHandler<HTMLElement>;
}
/** A sketched container with a border, fill and alignment. Mirrors `Ivy.Box`. */
export declare const Box: ({ id, children, background, borderRadius, borderThickness, borderStyle, borderColor, padding, margin, width, height, aspectRatio, visible, contentAlign, opacity, borderOpacity, hoverVariant, className, style, onClick, ...rest }: BoxProps) => React.JSX.Element;
