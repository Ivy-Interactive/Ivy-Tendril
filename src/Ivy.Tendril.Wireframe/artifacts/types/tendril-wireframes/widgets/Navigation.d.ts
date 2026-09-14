import { WidgetBaseProps } from '../lib/types';
import * as React from "react";
export interface BreadcrumbItem {
    label: string;
    icon?: string;
    tooltip?: string;
    disabled?: boolean;
    hasOnClick?: boolean;
}
export interface BreadcrumbsProps extends WidgetBaseProps {
    items?: BreadcrumbItem[];
    /** Character drawn between crumbs. Defaults to a chevron. */
    separator?: string;
    disabled?: boolean;
    onSelect?: (item: BreadcrumbItem, index: number) => void;
}
/**
 * Trail of parent pages. Mirrors `Ivy.Breadcrumbs`.
 *
 * @tags navigation trail path
 * @example <Breadcrumbs items={[{ label: "Home", icon: "House" }, { label: "Projects" }]} />
 */
export declare const Breadcrumbs: ({ id, items, separator, disabled, density, width, height, aspectRatio, visible, className, style, onSelect, }: BreadcrumbsProps) => React.JSX.Element;
export interface PaginationProps extends WidgetBaseProps {
    page?: number;
    numPages: number;
    /** Pages kept either side of the current one. */
    siblings?: number;
    /** Pages always kept at each end. */
    boundaries?: number;
    disabled?: boolean;
    onChange?: (page: number) => void;
}
/**
 * Numbered page switcher. Mirrors `Ivy.Pagination`.
 *
 * @tags paging navigation
 * @example <Pagination page={page} numPages={12} onChange={setPage} />
 */
export declare const Pagination: ({ id, page, numPages, siblings, boundaries, disabled, density, width, height, aspectRatio, visible, className, style, onChange, }: PaginationProps) => React.JSX.Element;
export interface ExpandableProps extends WidgetBaseProps {
    header?: React.ReactNode;
    children?: React.ReactNode;
    disabled?: boolean;
    /** Controlled open state. Leave undefined for uncontrolled. */
    open?: boolean;
    defaultOpen?: boolean;
    icon?: string;
    ghost?: boolean;
    onOpenChange?: (open: boolean) => void;
}
/**
 * A disclosure panel. Mirrors `Ivy.Expandable`.
 *
 * @tags disclosure accordion collapse
 * @example <Expandable header="Advanced">Hidden until opened.</Expandable>
 */
export declare const Expandable: ({ id, header, children, disabled, open, defaultOpen, density, icon, ghost, width, height, aspectRatio, visible, className, style, onOpenChange, }: ExpandableProps) => React.JSX.Element;
