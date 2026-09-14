import { Orientation, Sizing, WidgetBaseProps } from '../../lib/types';
import * as React from "react";
export interface ResizablePanelProps extends WidgetBaseProps {
    /**
     * Share of the group this panel starts at, as a percentage or a `"1/3"`
     * fraction. Panels without one split whatever is left evenly.
     */
    defaultSize?: Sizing;
    children?: React.ReactNode;
}
/**
 * One panel inside a `ResizablePanelGroup`. Mirrors `Ivy.ResizablePanel`.
 *
 * The group reads `defaultSize` off the element and owns the sizing from then
 * on, so a panel only ever renders its own content.
 *
 * @category Layouts
 * @ivy Ivy.ResizablePanel
 * @tags panel split pane
 * @example <ResizablePanel defaultSize={30}>Navigator</ResizablePanel>
 */
export declare const ResizablePanel: ({ children, className, style }: ResizablePanelProps) => React.JSX.Element;
export interface ResizablePanelGroupProps extends WidgetBaseProps {
    direction?: Orientation;
    /** Draws the grab handle on each divider. */
    showHandle?: boolean;
    /** `ResizablePanel` elements. Anything else is ignored. */
    children?: React.ReactNode;
    /** Fires with the new percentage split once a drag settles. */
    onResize?: (sizes: number[]) => void;
}
/**
 * Panels that can be dragged to resize against each other.
 * Mirrors `Ivy.ResizablePanelGroup`.
 *
 * @category Layouts
 * @ivy Ivy.ResizablePanelGroup
 * @tags split pane resize columns
 * @slot children The `ResizablePanel` elements to lay out
 * @example <ResizablePanelGroup><ResizablePanel defaultSize={30}>Tree</ResizablePanel><ResizablePanel>Editor</ResizablePanel></ResizablePanelGroup>
 */
export declare const ResizablePanelGroup: ({ id, direction, showHandle, children, width, height, aspectRatio, visible, className, style, onResize, }: ResizablePanelGroupProps) => React.JSX.Element;
