import { EdgeStyle, FlowDirection, LayoutEdge, LayoutNode, LayoutOptions, Placed } from './types';
/**
 * Runs the whole pipeline.
 *
 * @param nodes measured boxes, in pixels
 * @param edges connections between them
 */
export declare function layered(nodes: LayoutNode[], edges: LayoutEdge[], options: LayoutOptions): Placed;
/** Re-exported so callers need only one import. */
export type { EdgeStyle, FlowDirection, LayoutOptions, Placed };
