import { Pt } from '../../../sketch/hand';
import { FlowShape } from '../shapes';
import { EdgeStyle, FlowDirection, PlacedEdge, PlacedNode } from './types';
export interface RoutedEdge {
    /** The point run, ready for a `linearPath` or `curve` shape. */
    points: Pt[];
    /** Where the arrowhead goes, and the point it should aim back along. */
    tip: Pt;
    from: Pt;
    /** Where an edge label sits, if it has one. */
    label: Pt;
    index: number;
}
/**
 * Routes every edge.
 *
 * @param shapeOf the shape of each node, so the line can stop on its outline rather than its
 *   bounding box — the difference between an arrow touching a diamond and an arrow ending in
 *   the white space beside one.
 */
export declare function routeEdges(nodes: PlacedNode[], edges: PlacedEdge[], shapeOf: (id: string) => FlowShape, direction: FlowDirection, style: EdgeStyle): RoutedEdge[];
