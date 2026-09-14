import { FlowShape } from '../shapes';
export interface ParsedNode {
    id: string;
    label?: string;
    shape?: FlowShape;
}
export interface ParsedEdge {
    from: string;
    to: string;
    label?: string;
    dashed?: boolean;
    headless?: boolean;
}
export interface ParseProblem {
    /** 1-based, so it matches what an editor shows. */
    line: number;
    text: string;
    message: string;
}
export interface ParsedChart {
    nodes: ParsedNode[];
    edges: ParsedEdge[];
    problems: ParseProblem[];
}
/**
 * Parses the text form.
 *
 * Blank lines and `%%` comments are skipped. A `direction TD` line is accepted and ignored —
 * the prop is the one that decides, so that a chart pasted from elsewhere does not silently
 * override it.
 */
export declare function parseChart(source: string): ParsedChart;
