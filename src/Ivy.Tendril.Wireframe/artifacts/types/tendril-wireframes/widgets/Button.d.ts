import { BorderRadius, WidgetBaseProps } from '../lib/types';
import * as React from "react";
export type ButtonVariant = "Primary" | "Secondary" | "Destructive" | "Outline" | "Ghost" | "Link" | "Inline" | "Ai";
export interface ButtonProps extends WidgetBaseProps {
    title?: string;
    icon?: string;
    iconPosition?: "Left" | "Right";
    variant?: ButtonVariant;
    disabled?: boolean;
    tooltip?: string;
    foreground?: string;
    strikeThrough?: boolean;
    loading?: boolean;
    url?: string;
    target?: "Blank" | "Self";
    autoFocus?: boolean;
    /** Keyboard shortcut shown on the right, e.g. `"Ctrl+S"`. */
    shortcutKey?: string;
    badge?: string;
    borderRadius?: BorderRadius;
    children?: React.ReactNode;
    onClick?: React.MouseEventHandler<HTMLElement>;
}
/**
 * The workhorse control. Mirrors `Ivy.Button`, including its variant list,
 * icon placement, badge and shortcut hint.
 *
 * @tags action submit cta
 * @example <Button title="Save" icon="Save" onClick={save} />
 * @example <Button title="Delete" variant="Destructive" icon="Trash2" />
 */
export declare const Button: React.ForwardRefExoticComponent<ButtonProps & React.RefAttributes<HTMLElement>>;
