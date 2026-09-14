import { Overflow, TextAlignment, WidgetBaseProps } from '../../lib/types';
import * as React from "react";
export type TextBlockVariant = "Literal" | "H1" | "H2" | "H3" | "H4" | "H5" | "H6" | "P" | "Inline" | "Block" | "Blockquote" | "Monospaced" | "Lead" | "Muted" | "Danger" | "Warning" | "Success" | "Label" | "Strong" | "Display";
export interface TextBlockProps extends WidgetBaseProps {
    content?: string;
    variant?: TextBlockVariant;
    strikeThrough?: boolean;
    color?: string;
    noWrap?: boolean;
    overflow?: Overflow;
    bold?: boolean;
    italic?: boolean;
    muted?: boolean;
    textAlignment?: TextAlignment;
    anchor?: string;
    children?: React.ReactNode;
}
/**
 * Every piece of static text in the wireframe. Mirrors `Ivy.TextBlock`.
 *
 * @tags text heading paragraph copy
 * @example <TextBlock variant="H2" content="Section title" />
 */
export declare const TextBlock: ({ id, content, variant, width, height, aspectRatio, visible, strikeThrough, color, noWrap, overflow, bold, italic, muted, density, textAlignment, anchor, className, style, children, ...rest }: TextBlockProps) => React.JSX.Element;
