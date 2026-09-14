import { Densities, WidgetBaseProps } from '../../lib/types';
import * as React from "react";
/** Props every Ivy input shares. */
export interface BaseInputProps extends WidgetBaseProps {
    disabled?: boolean;
    /** Validation message. Any non-empty string marks the field invalid. */
    invalid?: string;
    nullable?: boolean;
    placeholder?: string;
    autoFocus?: boolean;
    /** Renders the field without its border, for embedding in toolbars. */
    ghost?: boolean;
    prefix?: React.ReactNode;
    suffix?: React.ReactNode;
}
export declare const inputPadding: (density?: Densities) => string;
export interface InputShellProps extends BaseInputProps {
    children?: React.ReactNode;
    /** Shows the clear affordance. Pair with `onClear`. */
    showClear?: boolean;
    onClear?: () => void;
    focused?: boolean;
    className?: string;
    contentClassName?: string;
    corner?: "rounded" | "pill" | "sharp";
    onClick?: React.MouseEventHandler<HTMLElement>;
}
/**
 * The bordered box every text-like input sits in: prefix, content, suffix,
 * clear button and the red squiggle for validation errors.
 *
 * @internal
 */
export declare const InputShell: ({ id, children, disabled, invalid, density, ghost, width, height, aspectRatio, visible, prefix, suffix, showClear, onClear, focused, corner, className, contentClassName, style, onClick, }: InputShellProps) => React.JSX.Element;
/** Strips the shell's chrome props so the rest can be spread onto an `<input>`. */
export declare const nativeInputClass = "w-full min-w-0 border-0 bg-transparent p-0 outline-none placeholder:text-ink-faint placeholder:italic disabled:cursor-not-allowed";
