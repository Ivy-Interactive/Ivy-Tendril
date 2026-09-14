import { WidgetBaseProps } from '../lib/types';
import * as React from "react";
export type BadgeVariant = "Primary" | "Destructive" | "Outline" | "Secondary" | "Success" | "Warning" | "Info";
export interface BadgeProps extends WidgetBaseProps {
    title?: string;
    icon?: string;
    iconPosition?: "Left" | "Right";
    variant?: BadgeVariant;
    color?: string;
    children?: React.ReactNode;
    onClick?: React.MouseEventHandler<HTMLElement>;
}
/**
 * A small status pill. Mirrors `Ivy.Badge`.
 *
 * @tags status label chip pill
 * @example <Badge title="Active" variant="Success" />
 */
export declare const Badge: ({ id, title, icon, iconPosition, variant, color, density, width, height, aspectRatio, visible, children, className, style, onClick, ...rest }: BadgeProps) => React.JSX.Element;
