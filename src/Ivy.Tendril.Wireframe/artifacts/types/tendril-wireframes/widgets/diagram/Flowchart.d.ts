import { Sizing, WidgetBaseProps } from '../../lib/types';
import { ColorScheme } from '../../sketch/colors';
import { ArrowHeads } from '../wireframe/Arrow';
import { FlowShape } from './shapes';
import { EdgeStyle, FlowDirection } from './layout/types';
import * as React from "react";
export type { FlowShape } from './shapes';
export type { EdgeStyle, FlowDirection } from './layout/types';
/** A box in the chart. */
export interface FlowchartNode {
    /** Referenced by edges. Must be unique. */
    id: string;
    /** Text inside the box. Falls back to the id, so a bare `{ id: "Start" }` still reads. */
    label?: string;
    /**
     * Which flowchart symbol to draw.
     * @default "Process"
     */
    shape?: FlowShape;
    /** A lucide icon name, drawn before the label. */
    icon?: string;
    /** Ivy colour name or CSS colour for the outline and wash. */
    color?: string;
    /** Fixes the box size instead of measuring the label. */
    width?: Sizing;
    height?: Sizing;
    /** Pins the node at this position and takes it out of the automatic layout. */
    x?: Sizing;
    y?: Sizing;
}
/** A connection between two boxes. */
export interface FlowchartEdge {
    /** Id of the node the arrow leaves. */
    from: string;
    /** Id of the node the arrow points at. */
    to: string;
    /** Text on the line — the branch condition, usually. */
    label?: string;
    /** Draws the line dashed, for a weak or optional step. */
    dashed?: boolean;
    /**
     * Which end carries an arrowhead.
     * @default "End"
     */
    heads?: ArrowHeads;
    /** Ivy colour name or CSS colour for the line. */
    color?: string;
}
export interface FlowchartProps extends WidgetBaseProps {
    /** The boxes. Ignored when `chart` is given. */
    nodes?: FlowchartNode[];
    /** The arrows. Ignored when `chart` is given. */
    edges?: FlowchartEdge[];
    /**
     * The chart in text form, as a subset of Mermaid's flowchart syntax:
     * `Start([Start]) --> Check{Valid?}` and `Check -- yes --> Save[Save]`.
     * Brackets pick the shape, arrows pick the edge. Takes precedence over `nodes`.
     */
    chart?: string;
    /**
     * Which way the flow runs.
     *
     * Horizontal by default. A wireframe viewport is wide and short — 1440x860 is the usual
     * one — so a chart that runs downward leaves the width empty and falls off the bottom,
     * while the same chart running rightward fits on screen.
     *
     * @default "Right"
     */
    direction?: FlowDirection;
    /**
     * How the arrows get between boxes.
     * @default "Elbow"
     */
    edgeStyle?: EdgeStyle;
    /** Gap between neighbours in the same rank. */
    nodeGap?: number;
    /** Gap between one rank and the next. */
    rankGap?: number;
    /**
     * Colours the nodes in sequence rather than leaving them all ink.
     * @default "Default"
     */
    colorScheme?: ColorScheme;
    onNodeClick?: (id: string) => void;
}
/**
 * A flowchart, laid out automatically and drawn in the sketch hand.
 * Mirrors no single Ivy widget; it is the diagram the wireframe set was missing.
 *
 * Give it `nodes` and `edges`, or write the whole chart in `chart` using a subset of
 * Mermaid's syntax. Boxes size themselves to their labels, ranks fall out of the arrows,
 * and long edges route around whatever is in the way.
 *
 * @category Diagrams
 * @tags flowchart diagram graph process decision workflow
 * @example <Flowchart chart="Start([Start]) --> Check{Valid?}" />
 * @example <Flowchart chart={`
 *   Start([Start]) --> Check{Valid?}
 *   Check -- yes --> Save[Save record]
 *   Check -- no --> Start
 * `} />
 * @example <Flowchart direction="Down" nodes={[{ id: "a", label: "Draft" }, { id: "b", label: "Review" }]} edges={[{ from: "a", to: "b" }]} />
 */
export declare const Flowchart: ({ id, nodes, edges, chart, direction, edgeStyle, nodeGap, rankGap, colorScheme, density, width, height, aspectRatio, visible, className, style, onNodeClick, ...rest }: FlowchartProps) => React.JSX.Element;
