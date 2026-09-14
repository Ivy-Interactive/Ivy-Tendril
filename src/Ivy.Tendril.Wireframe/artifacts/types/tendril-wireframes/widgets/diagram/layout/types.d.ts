/**
 * The vocabulary the layout speaks.
 *
 * Deliberately separate from the component's props: the layout takes measured pixel sizes
 * and returns pixel positions, and knows nothing about React, `Sizing` units or shapes. That
 * is what makes it testable without a DOM, and what would make a different layout engine a
 * drop-in replacement.
 */
/** Which way the flow runs. Ranks advance along this axis. */
export type FlowDirection = "Down" | "Up" | "Right" | "Left";
/** How an edge gets from one node to the next. */
export type EdgeStyle = "Elbow" | "Curved" | "Straight";
/** A node as the layout sees it: an id and a measured box. */
export interface LayoutNode {
    id: string;
    width: number;
    height: number;
    /** Pins the node, in diagram pixels, and takes it out of the automatic flow. */
    x?: number;
    y?: number;
}
export interface LayoutEdge {
    from: string;
    to: string;
    /** Measured size of the edge label, if it has one, so routing can keep room for it. */
    labelWidth?: number;
    labelHeight?: number;
}
export interface LayoutOptions {
    direction: FlowDirection;
    /** Gap between neighbours within a rank. */
    nodeGap: number;
    /** Gap between one rank and the next. */
    rankGap: number;
}
/** A node with a place. */
export interface PlacedNode extends LayoutNode {
    x: number;
    y: number;
    rank: number;
    /** Position within the rank, after crossing reduction. */
    order: number;
}
export interface PlacedEdge extends LayoutEdge {
    /** Corner points from source to target, before anchoring to the node outlines. */
    waypoints: Array<[number, number]>;
    /** True when the edge ran against the flow and was reversed to rank the graph. */
    reversed: boolean;
}
export interface Placed {
    nodes: PlacedNode[];
    edges: PlacedEdge[];
    width: number;
    height: number;
}
