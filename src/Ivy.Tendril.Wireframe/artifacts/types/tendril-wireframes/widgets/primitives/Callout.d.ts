import { WidgetBaseProps } from '../../lib/types';
import * as React from "react";
export type CalloutVariant = "Info" | "Success" | "Warning" | "Error" | "Destructive";
export interface CalloutProps extends WidgetBaseProps {
    title?: string;
    children?: React.ReactNode;
    variant?: CalloutVariant;
    icon?: string;
    onClick?: React.MouseEventHandler<HTMLElement>;
}
/**
 * A boxed aside with an icon, in one of five tones. Mirrors `Ivy.Callout`.
 *
 * @tags alert notice banner
 * @example <Callout variant="Warning" title="Careful">This cannot be undone.</Callout>
 */
export declare const Callout: ({ id, title, children, variant, density, width, height, aspectRatio, visible, icon, className, style, onClick, ...rest }: CalloutProps) => React.JSX.Element;
