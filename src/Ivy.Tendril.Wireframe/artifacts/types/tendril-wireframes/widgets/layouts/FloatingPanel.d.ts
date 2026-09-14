import { Align, Sizing, Thickness, WidgetBaseProps } from '../../lib/types';
import * as React from "react";
export interface FloatingPanelProps extends WidgetBaseProps {
    /** Which corner or edge the panel sits against. */
    alignSelf?: Align;
    /** Nudges the panel off that anchor, per edge. */
    offset?: Sizing | Thickness;
    /**
     * Floats within the nearest positioned ancestor instead of the viewport.
     * Give that ancestor `relative` for it to have anything to hold on to.
     */
    contained?: boolean;
    children?: React.ReactNode;
}
/**
 * A panel floating above everything else — a tool palette, a floating action
 * button, a status pill. Mirrors `Ivy.FloatingPanel`.
 *
 * @category Layouts
 * @ivy Ivy.FloatingPanel
 * @tags overlay palette fab sticky
 * @slot children The single child the panel floats
 * @example <FloatingPanel><Button title="New" icon="Plus" /></FloatingPanel>
 * @example <FloatingPanel alignSelf="TopCenter" offset={{ top: 8 }}>Saving…</FloatingPanel>
 */
export declare const FloatingPanel: ({ id, alignSelf, offset, contained, children, width, height, aspectRatio, visible, className, style, }: FloatingPanelProps) => React.JSX.Element;
