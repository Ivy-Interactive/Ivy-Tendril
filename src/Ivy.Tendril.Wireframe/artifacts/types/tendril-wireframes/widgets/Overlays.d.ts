import { WidgetBaseProps } from '../lib/types';
import * as React from "react";
export interface DialogProps extends WidgetBaseProps {
    open?: boolean;
    defaultOpen?: boolean;
    children?: React.ReactNode;
    /** Element that opens the dialog. */
    trigger?: React.ReactNode;
    onOpenChange?: (open: boolean) => void;
    onClose?: () => void;
}
/**
 * Modal window. Mirrors `Ivy.Dialog`.
 *
 * @tags modal overlay confirm
 * @example <Dialog open={open} onOpenChange={setOpen}><DialogHeader title="Delete?" /></Dialog>
 */
export declare const Dialog: ({ id, open, defaultOpen, children, trigger, width, height, aspectRatio, visible, className, style, onOpenChange, onClose, }: DialogProps) => React.JSX.Element;
export interface DialogHeaderProps extends WidgetBaseProps {
    title?: string;
    description?: string;
    hideCloseButton?: boolean;
    children?: React.ReactNode;
}
/** Mirrors `Ivy.DialogHeader`. */
export declare const DialogHeader: ({ id, title, description, hideCloseButton, children, width, height, aspectRatio, visible, className, style, }: DialogHeaderProps) => React.JSX.Element;
export interface DialogBodyProps extends WidgetBaseProps {
    children?: React.ReactNode;
}
/** Mirrors `Ivy.DialogBody`. */
export declare const DialogBody: ({ id, children, width, height, aspectRatio, visible, className, style, }: DialogBodyProps) => React.JSX.Element;
export interface DialogFooterProps extends WidgetBaseProps {
    children?: React.ReactNode;
}
/** Mirrors `Ivy.DialogFooter`. */
export declare const DialogFooter: ({ id, children, width, height, aspectRatio, visible, className, style, }: DialogFooterProps) => React.JSX.Element;
export type SheetSide = "Left" | "Right" | "Top" | "Bottom";
export interface SheetProps extends WidgetBaseProps {
    open?: boolean;
    defaultOpen?: boolean;
    title?: string;
    description?: string;
    trigger?: React.ReactNode;
    children?: React.ReactNode;
    side?: SheetSide;
    resizable?: boolean;
    onOpenChange?: (open: boolean) => void;
    onClose?: () => void;
}
/**
 * Panel that slides in from an edge. Mirrors `Ivy.Sheet`.
 *
 * @tags drawer panel overlay
 * @example <Sheet open={open} onOpenChange={setOpen} title="Filters" side="Right" />
 */
export declare const Sheet: ({ id, open, defaultOpen, title, description, trigger, children, width, height, aspectRatio, visible, side, resizable, className, style, onOpenChange, onClose, }: SheetProps) => React.JSX.Element;
