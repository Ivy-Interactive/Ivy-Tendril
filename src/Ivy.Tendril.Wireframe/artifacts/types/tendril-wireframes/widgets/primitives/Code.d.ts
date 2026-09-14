import { TextAlignment, WidgetBaseProps } from '../../lib/types';
import * as React from "react";
export interface CodeBlockProps extends WidgetBaseProps {
    content: string;
    language?: string;
    showCopyButton?: boolean;
    showLineNumbers?: boolean;
    startingLineNumber?: number;
    showBorder?: boolean;
    wrapLines?: boolean;
}
/**
 * Monospaced source listing on ruled paper. Mirrors `Ivy.CodeBlock`.
 *
 * @tags code syntax listing
 * @example <CodeBlock language="ts" content="export const x = 1;" />
 */
export declare const CodeBlock: ({ id, content, language, showCopyButton, showLineNumbers, startingLineNumber, showBorder, wrapLines, width, height, aspectRatio, visible, density, className, style, }: CodeBlockProps) => React.JSX.Element;
export interface JsonProps extends WidgetBaseProps {
    content: string;
    expanded?: number | null;
}
/** Pretty-printed JSON. Mirrors `Ivy.Json`. */
export declare const Json: ({ id, content, width, height, aspectRatio, visible, density, className, style }: JsonProps) => React.JSX.Element;
export interface XmlProps extends WidgetBaseProps {
    content: string;
    expanded?: number | null;
}
/** Indented XML. Mirrors `Ivy.Xml`. */
export declare const Xml: ({ id, content, width, height, aspectRatio, visible, density, className, style }: XmlProps) => React.JSX.Element;
export interface TerminalLine {
    content: string;
    isCommand?: boolean;
    prompt?: string;
}
export interface TerminalProps extends WidgetBaseProps {
    lines?: TerminalLine[];
    title?: string;
    showHeader?: boolean;
    showCopyButton?: boolean;
}
/** A console transcript. Mirrors `Ivy.Terminal`. */
export declare const Terminal: ({ id, lines, title, showHeader, showCopyButton, width, height, aspectRatio, visible, className, style, }: TerminalProps) => React.JSX.Element;
export interface MarkdownProps extends WidgetBaseProps {
    content: string;
    textAlignment?: TextAlignment;
    /** Widens the measure and adds article spacing, as Ivy's docs pages do. */
    article?: boolean;
    /** Allows `file://` image sources, which are stripped by default. */
    dangerouslyAllowLocalFiles?: boolean;
}
/**
 * A deliberately small Markdown renderer — headings, lists, quotes, fences and
 * inline emphasis, which is all a wireframe needs. Mirrors `Ivy.Markdown`.
 *
 * @tags prose rich-text formatted
 * @example <Markdown content="## Title\n\nSome **bold** copy." />
 */
export declare const Markdown: ({ id, content, density, textAlignment, article, dangerouslyAllowLocalFiles, width, height, aspectRatio, visible, className, style, }: MarkdownProps) => React.JSX.Element;
