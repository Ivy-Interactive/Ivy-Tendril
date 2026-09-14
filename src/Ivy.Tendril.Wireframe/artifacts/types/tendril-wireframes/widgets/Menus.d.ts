import { MenuItem, WidgetBaseProps } from '../lib/types';
import * as React from "react";
export interface DropDownMenuProps extends WidgetBaseProps {
    items?: MenuItem[];
    /** The element the menu hangs off. */
    trigger?: React.ReactNode;
    header?: React.ReactNode;
    align?: "Start" | "Center" | "End";
    side?: "Top" | "Right" | "Bottom" | "Left";
    alignOffset?: number;
    /** Keeps the menu open after a selection, for multi-select menus. */
    stayOpen?: boolean;
    onSelect?: (item: MenuItem) => void;
}
/**
 * A menu anchored to a trigger. Mirrors `Ivy.DropDownMenu`.
 *
 * @tags menu contextual actions
 * @slot trigger The element the menu hangs off
 * @example <DropDownMenu items={items} trigger={<Button title="Actions" />} />
 */
export declare const DropDownMenu: ({ id, items, trigger, header, align, side, alignOffset, stayOpen, density, width, height, aspectRatio, visible, className, style, onSelect, }: DropDownMenuProps) => React.JSX.Element;
export interface ToolbarProps extends WidgetBaseProps {
    items?: MenuItem[];
    disabled?: boolean;
    onSelect?: (item: MenuItem) => void;
}
/**
 * A row of icon buttons and dropdowns. Mirrors `Ivy.Toolbar`.
 *
 * @tags actions formatting
 * @example <Toolbar items={[{ label: "Bold", icon: "Bold", checked: true }]} />
 */
export declare const Toolbar: ({ id, items, disabled, density, width, height, aspectRatio, visible, className, style, onSelect, }: ToolbarProps) => React.JSX.Element;
export type TooltipVariant = "Default" | "Info" | "Success" | "Warning" | "Error";
export interface TooltipProps extends WidgetBaseProps {
    /** Element the tooltip describes. */
    trigger?: React.ReactNode;
    content?: React.ReactNode;
    children?: React.ReactNode;
    open?: boolean;
    showArrow?: boolean;
    /** Keeps the tooltip up while the pointer is over it. */
    persistent?: boolean;
    variant?: TooltipVariant;
    side?: "Top" | "Right" | "Bottom" | "Left";
}
/**
 * Hover hint on a sticky-note. Mirrors `Ivy.Tooltip`.
 *
 * @tags hint help hover
 * @example <Tooltip content="Saves immediately" trigger={<Button title="Save" />} />
 */
export declare const Tooltip: ({ id, trigger, content, children, density, open, showArrow, persistent, variant, side, width, height, aspectRatio, visible, className, style, }: TooltipProps) => React.JSX.Element;
export interface TreeProps extends WidgetBaseProps {
    items?: MenuItem[];
    rowActions?: MenuItem[];
    onSelect?: (item: MenuItem) => void;
    onRowAction?: (item: MenuItem, action: MenuItem) => void;
}
/**
 * Nested, collapsible rows. Mirrors `Ivy.Tree`.
 *
 * @tags hierarchy files nested
 * @example <Tree items={[{ label: "src", icon: "Folder", children: [{ label: "index.ts" }] }]} />
 */
export declare const Tree: ({ id, items, rowActions, density, width, height, aspectRatio, visible, className, style, onSelect, onRowAction, }: TreeProps) => React.JSX.Element;
