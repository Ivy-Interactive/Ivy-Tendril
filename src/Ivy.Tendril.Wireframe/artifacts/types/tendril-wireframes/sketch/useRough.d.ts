import { Options as RoughOptions } from 'roughjs/bin/core';
export type { RoughOptions };
export interface RoughPath {
    d: string;
    stroke: string;
    strokeWidth: number;
    fill: string;
    /**
     * rough.js applies `strokeLineDash` when it renders to the DOM itself, and
     * drops it on the way through `toPaths`. Since the sketch layer renders the
     * paths declaratively, the dash is re-attached here for callers to spread
     * onto the element as `stroke-dasharray`.
     */
    strokeLineDash?: number[];
}
/**
 * The geometry `RoughShape` and `SketchFrame` can draw. `kind` picks the shape and decides
 * which of the other fields apply.
 *
 * All coordinates are in the parent SVG's user units, origin top-left. The library draws in
 * pixel space rather than a scaled `viewBox`, so one unit is one CSS pixel.
 *
 * `linearPath` is an open run of points, `polygon` closes it, `curve` smooths through it,
 * and `path` takes any SVG `d` string for anything the named kinds cannot express.
 */
export type SketchShape = {
    kind: "rectangle";
    x?: number;
    y?: number;
    width: number;
    height: number;
} | {
    kind: "ellipse";
    cx: number;
    cy: number;
    width: number;
    height: number;
} | {
    kind: "circle";
    cx: number;
    cy: number;
    diameter: number;
} | {
    kind: "line";
    x1: number;
    y1: number;
    x2: number;
    y2: number;
} | {
    kind: "linearPath";
    points: [number, number][];
} | {
    kind: "polygon";
    points: [number, number][];
} | {
    kind: "curve";
    points: [number, number][];
} | {
    kind: "arc";
    x: number;
    y: number;
    width: number;
    height: number;
    start: number;
    stop: number;
    closed?: boolean;
} | {
    kind: "path";
    d: string;
};
/**
 * Converts a shape into plain SVG path descriptors so callers can render them
 * declaratively instead of letting rough.js mutate the DOM.
 */
export declare function roughPaths(shape: SketchShape, options: RoughOptions): RoughPath[];
export declare function useRoughPaths(shape: SketchShape | null, options: RoughOptions): RoughPath[];
/** Tracks an element's border box so the sketch layer can be redrawn to fit. */
export declare function useMeasuredSize<T extends HTMLElement>(): {
    width: number;
    height: number;
    ref: import('react').RefObject<T | null>;
};
