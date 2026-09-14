import { WidgetBaseProps } from '../../lib/types';
import * as React from "react";
export interface IconProps extends WidgetBaseProps {
    name: string;
    color?: string;
}
/** A single lucide glyph sized by density. Mirrors `Ivy.Icon`. */
export declare const Icon: ({ id, name, color, width, height, aspectRatio, visible, density, className, style, }: IconProps) => React.JSX.Element;
export interface AvatarProps extends WidgetBaseProps {
    image?: string;
    fallback?: string;
    color?: string;
}
/**
 * A circled portrait or set of initials. Mirrors `Ivy.Avatar`.
 *
 * @tags user portrait initials
 * @example <Avatar fallback="AL" color="Blue" />
 */
export declare const Avatar: ({ id, image, fallback, color, density, width, height, aspectRatio, visible, className, style, }: AvatarProps) => React.JSX.Element;
export interface KbdProps extends WidgetBaseProps {
    content?: string;
    ghost?: boolean;
    children?: React.ReactNode;
}
/** A key cap. Mirrors `Ivy.Kbd`. */
export declare const Kbd: ({ id, content, ghost, density, width, height, aspectRatio, visible, children, className, style, }: KbdProps) => React.JSX.Element;
export interface StepperItem {
    symbol?: string;
    icon?: string;
    label?: string;
    description?: string;
    loading?: boolean;
}
export interface StepperProps extends WidgetBaseProps {
    items?: StepperItem[];
    selectedIndex?: number;
    allowSelectForward?: boolean;
    disabled?: boolean;
    onSelect?: (index: number) => void;
}
/**
 * Numbered progress through a flow. Mirrors `Ivy.Stepper`.
 *
 * @tags wizard progress steps
 * @example <Stepper items={[{ label: "Pick" }, { label: "Pay" }]} selectedIndex={0} />
 */
export declare const Stepper: ({ id, items, selectedIndex, width, height, aspectRatio, visible, allowSelectForward, disabled, density, className, style, onSelect, }: StepperProps) => React.JSX.Element;
