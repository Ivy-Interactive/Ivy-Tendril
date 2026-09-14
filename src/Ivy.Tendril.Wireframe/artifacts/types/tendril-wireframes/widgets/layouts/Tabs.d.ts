import { Sizing, Thickness, WidgetBaseProps } from '../../lib/types';
import * as React from "react";
/**
 * `Tabs` draws the strip as boxed, browser-style tabs sitting on the panel.
 * `Content` is the quieter one: labels with a wavy underline under the active
 * one, and no frame around the body.
 */
export type TabsVariant = "Tabs" | "Content";
export interface TabProps extends WidgetBaseProps {
    title: string;
    icon?: string;
    badge?: string;
    children?: React.ReactNode;
}
/**
 * One tab inside a `TabsLayout`. Mirrors `Ivy.Tab`.
 *
 * The element carries the tab's title, icon and badge; `TabsLayout` reads them
 * off and renders the strip itself, so a `Tab` only ever renders its own body.
 *
 * @tags tab page section
 * @example <Tab title="Overview" icon="House">Content</Tab>
 */
export declare const Tab: ({ children }: TabProps) => React.JSX.Element;
export interface TabsLayoutProps extends WidgetBaseProps {
    /** Controlled selection. Leave undefined for uncontrolled. */
    selectedIndex?: number;
    defaultSelectedIndex?: number;
    variant?: TabsVariant;
    /** Cancels the padding of whatever contains the layout. */
    removeParentPadding?: boolean;
    /** Padding around the tab body. */
    padding?: Sizing | Thickness;
    /** Shows a trailing add button with this label. */
    addButtonText?: string;
    /** `Tab` elements. Anything else is ignored. */
    children?: React.ReactNode;
    onSelect?: (index: number) => void;
    /** Given, every tab gets a close button. */
    onClose?: (index: number) => void;
    /** Given, the selected tab gets a refresh button. */
    onRefresh?: (index: number) => void;
    /** Given, tabs can be dragged into a new order. Receives the new arrangement. */
    onReorder?: (order: number[]) => void;
    onAddButtonClick?: () => void;
}
/**
 * Organizes content into separate views reached through a strip of tabs.
 * Mirrors `Ivy.TabsLayout`.
 *
 * @category Layouts
 * @ivy Ivy.TabsLayout
 * @tags tabs sections navigation
 * @slot children The `Tab` elements making up the strip
 * @example <TabsLayout><Tab title="Overview">…</Tab><Tab title="Settings">…</Tab></TabsLayout>
 * @example <TabsLayout variant="Tabs" onClose={close} onReorder={reorder}>{tabs}</TabsLayout>
 */
export declare const TabsLayout: ({ id, selectedIndex, defaultSelectedIndex, variant, removeParentPadding, padding, addButtonText, children, density, width, height, aspectRatio, visible, className, style, onSelect, onClose, onRefresh, onReorder, onAddButtonClick, }: TabsLayoutProps) => React.JSX.Element;
