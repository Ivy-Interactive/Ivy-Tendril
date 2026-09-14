import { MenuItem, Scroll, Sizing, WidgetBaseProps } from '../../lib/types';
import * as React from "react";
export interface SidebarLayoutProps extends WidgetBaseProps {
    /** The main content area, filling whatever the sidebar leaves. */
    mainContent?: React.ReactNode;
    /** The body of the sidebar — usually a `SidebarMenu`. */
    sidebarContent?: React.ReactNode;
    sidebarHeader?: React.ReactNode;
    sidebarFooter?: React.ReactNode;
    /** Stands in for the header once the sidebar is collapsed. */
    sidebarHeaderCollapsed?: React.ReactNode;
    /** Stands in for the footer once the sidebar is collapsed. */
    sidebarFooterCollapsed?: React.ReactNode;
    /** Controlled open state. Leave undefined for uncontrolled. */
    open?: boolean;
    defaultOpen?: boolean;
    /** Marks this as the application's own sidebar rather than a nested one. */
    mainAppSidebar?: boolean;
    mainContentPadding?: Sizing;
    /** Lets the divider be dragged to resize the sidebar, between 200px and 600px. */
    resizable?: boolean;
    sidebarContentScroll?: Scroll;
    onOpenChange?: (open: boolean) => void;
}
/**
 * The application shell: a collapsible sidebar beside a main content area.
 * Mirrors `Ivy.SidebarLayout`.
 *
 * @category Layouts
 * @ivy Ivy.SidebarLayout
 * @tags shell navigation app frame
 * @slot mainContent The main content area
 * @slot sidebarContent The body of the sidebar
 * @slot sidebarHeader Rendered above the sidebar content
 * @slot sidebarFooter Rendered below the sidebar content
 * @slot sidebarHeaderCollapsed Stands in for the header when collapsed
 * @slot sidebarFooterCollapsed Stands in for the footer when collapsed
 * @example <SidebarLayout mainContent={<Page />} sidebarContent={<SidebarMenu items={items} />} />
 */
export declare const SidebarLayout: ({ id, mainContent, sidebarContent, sidebarHeader, sidebarFooter, sidebarHeaderCollapsed, sidebarFooterCollapsed, open, defaultOpen, mainAppSidebar, mainContentPadding, resizable, sidebarContentScroll, density, width, height, aspectRatio, visible, className, style, onOpenChange, }: SidebarLayoutProps) => React.JSX.Element;
export interface SidebarMenuProps extends WidgetBaseProps {
    items?: MenuItem[];
    /** Shows a filter box above the menu. */
    searchActive?: boolean;
    /** The path of the item drawn as current. */
    selected?: string;
    onSelect?: (item: MenuItem) => void;
}
/**
 * The navigation menu that usually fills a `SidebarLayout`'s sidebar.
 * Mirrors `Ivy.SidebarMenu`.
 *
 * @category Layouts
 * @ivy Ivy.SidebarMenu
 * @tags navigation menu sidebar
 * @example <SidebarMenu items={items} searchActive onSelect={go} />
 */
export declare const SidebarMenu: ({ id, items, searchActive, selected, density, width, height, aspectRatio, visible, className, style, onSelect, }: SidebarMenuProps) => React.JSX.Element;
