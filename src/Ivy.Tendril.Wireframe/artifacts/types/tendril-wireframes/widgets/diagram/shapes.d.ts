import { Pt } from '../../sketch/hand';
/**
 * The standard flowchart vocabulary, named the way the diagrams themselves are read rather
 * than by geometry — a `Decision` rather than a diamond, so swapping its drawn shape later
 * does not rename the API.
 */
export type FlowShape = 
/** A step. The default, and most of any chart. */
"Process"
/** A branch. Its outgoing edges are the ones worth labelling. */
 | "Decision"
/** A start or an end. */
 | "Terminator"
/** Data in or out. */
 | "InputOutput"
/** A setup step, before the work proper. */
 | "Preparation"
/** A step a person does. */
 | "Manual"
/** Something printed or produced. */
 | "Document"
/** Stored state. */
 | "Database"
/** A join to somewhere else on the page. */
 | "Connector"
/** An aside. Not part of the flow; usually the target of a dashed edge. */
 | "Note";
/** A node's box in diagram pixels. */
export interface Rect {
    x: number;
    y: number;
    width: number;
    height: number;
}
/**
 * How much bigger than its label a node has to be. A decision has to grow a lot, because a
 * diamond only offers its full width on one line through the middle.
 */
export declare const SHAPE_PADDING: Record<FlowShape, {
    x: number;
    y: number;
}>;
/**
 * The outline of a shape, clockwise from its top-left, in absolute diagram coordinates.
 *
 * Everything is a polygon, including the round shapes — a terminator's ends and a database's
 * lip are sampled arcs. rough.js is going to redraw the whole thing with a wobble anyway, so
 * the extra vertices cost nothing visually and buy one intersection routine instead of one
 * per shape.
 */
export declare function shapeOutline(shape: FlowShape, rect: Rect): Pt[];
/**
 * The rectangle a node's label may occupy, given its box. Keeps text off the slanted edges
 * and out of a diamond's points.
 */
export declare function labelBox(shape: FlowShape, rect: Rect): Rect;
/**
 * Where an edge heading for `towards` should leave a node: the point where the ray from the
 * node's centre crosses its outline.
 *
 * Falls back to the centre when the target is inside the shape, which only happens for
 * overlapping nodes — an edge drawn centre-to-centre is ugly but visible, and visible is
 * what a wireframe needs from a degenerate case.
 */
export declare function anchorOn(outline: Pt[], centre: Pt, towards: Pt): Pt;
/** The centre of a rect, as a point. */
export declare const centreOf: (rect: Rect) => Pt;
