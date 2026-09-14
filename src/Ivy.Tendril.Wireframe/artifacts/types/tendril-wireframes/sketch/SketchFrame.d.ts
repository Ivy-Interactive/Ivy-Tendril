import * as React from "react";
export type SketchFillStyle = "hachure" | "solid" | "zigzag" | "cross-hatch" | "dots" | "dashed" | "zigzag-line";
export type SketchOutline = "solid" | "dashed" | "dotted" | "none";
export type SketchCorner = "sharp" | "rounded" | "pill" | "ellipse";
export interface SketchLayerProps {
    corner?: SketchCorner;
    outline?: SketchOutline;
    stroke?: string;
    strokeWidth?: number;
    fill?: string;
    fillStyle?: SketchFillStyle;
    fillWeight?: number;
    hachureAngle?: number;
    roughness?: number;
    bowing?: number;
    /** Stable seed so a widget redraws with the same wobble on every render. */
    seed?: string | number;
    /** Draw only some of the four edges — for table cells, headers, dividers. */
    sides?: Array<"top" | "right" | "bottom" | "left">;
    /** Trace the border twice, the way a marker doubles back over a box. */
    doubleStroke?: boolean;
    opacity?: number;
}
export interface SketchLayerRenderProps extends SketchLayerProps {
    width: number;
    height: number;
    className?: string;
}
/**
 * The pencil layer: absolutely positioned, behind content, never clickable.
 *
 * @internal
 */
export declare const SketchLayer: ({ width, height, corner, outline, stroke, strokeWidth, fill, fillStyle, fillWeight, hachureAngle, roughness, bowing, seed, sides, doubleStroke, opacity, className, }: SketchLayerRenderProps) => React.JSX.Element | null;
export interface SketchFrameProps extends SketchLayerProps, Omit<React.AllHTMLAttributes<HTMLElement>, "color" | "children" | "width" | "height" | "size" | "as"> {
    as?: React.ElementType;
    /** Element used for the content layer. Defaults to a `<span>`. */
    contentAs?: React.ElementType;
    contentClassName?: string;
    children?: React.ReactNode;
}
/**
 * A box whose border (and optional fill) is drawn with rough.js. Every other
 * widget in the library is built on top of this.
 */
export declare const SketchFrame: React.ForwardRefExoticComponent<SketchFrameProps & React.RefAttributes<HTMLElement>>;
