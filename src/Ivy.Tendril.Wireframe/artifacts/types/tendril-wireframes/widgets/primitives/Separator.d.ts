import { TextAlignment, WidgetBaseProps } from '../../lib/types';
export interface SeparatorProps extends WidgetBaseProps {
    orientation?: "Horizontal" | "Vertical";
    text?: string;
    textAlign?: TextAlignment;
}
/**
 * A hand-drawn rule, optionally with a label. Mirrors `Ivy.Separator`.
 *
 * @tags divider rule spacing
 * @example <Separator text="Or" />
 */
export declare const Separator: ({ id, orientation, text, textAlign, width, height, aspectRatio, visible, className, style, }: SeparatorProps) => import("react").JSX.Element;
