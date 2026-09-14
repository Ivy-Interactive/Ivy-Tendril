import { RoughOptions, SketchShape } from './useRough';
import * as React from "react";
export interface RoughShapeProps {
    shape: SketchShape;
    stroke?: string | "none";
    strokeWidth?: number;
    fill?: string;
    fillStyle?: RoughOptions["fillStyle"];
    fillWeight?: number;
    hachureAngle?: number;
    hachureGap?: number;
    roughness?: number;
    bowing?: number;
    seed?: string | number;
    opacity?: number;
    strokeLineDash?: number[];
    className?: string;
    onClick?: React.MouseEventHandler<SVGGElement>;
    onMouseEnter?: React.MouseEventHandler<SVGGElement>;
    onMouseLeave?: React.MouseEventHandler<SVGGElement>;
}
/**
 * Draws a rough.js shape inside an existing `<svg>`.
 *
 * **You supply the `<svg>`.** This renders a bare `<g>` of paths, so it has to sit inside an
 * SVG element you have positioned yourself — usually `absolute inset-0` over a measured box,
 * which is how every chart and wireframe mark in this library uses it. It is not a
 * standalone widget; for a drawn *border* around content, reach for `SketchFrame` instead.
 *
 * **Coordinates are the parent SVG's user units**, with the origin at its top-left. The
 * library draws in pixel space rather than a scaled `viewBox`, so one unit is one CSS pixel
 * and stroke weight stays constant whatever the size.
 *
 * @example
 * // A triangle over a measured box.
 * const { ref, width, height } = useMeasuredSize<HTMLDivElement>();
 * <div ref={ref} className="relative h-40 w-full">
 *   {width > 0 && (
 *     <svg className="absolute inset-0 h-full w-full overflow-visible">
 *       <RoughShape
 *         shape={{ kind: "polygon", points: [[10, height - 10], [width / 2, 10], [width - 10, height - 10]] }}
 *         seed="peak"
 *         fill="paper-sunken"
 *         fillStyle="hachure"
 *       />
 *     </svg>
 *   )}
 * </div>
 * @example <RoughShape shape={{ kind: "line", x1: 0, y1: 0, x2: 120, y2: 40 }} seed="rule" />
 * @example <RoughShape shape={{ kind: "path", d: "M 0 0 C 40 60, 80 -20, 120 30" }} seed="swoop" />
 */
export declare const RoughShape: ({ shape, stroke, strokeWidth, fill, fillStyle, fillWeight, hachureAngle, hachureGap, roughness, bowing, seed, opacity, strokeLineDash, className, ...handlers }: RoughShapeProps) => React.JSX.Element;
