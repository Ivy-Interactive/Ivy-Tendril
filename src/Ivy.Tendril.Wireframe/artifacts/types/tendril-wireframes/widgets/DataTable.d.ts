import { Align, MenuItem, Sizing, WidgetBaseProps } from '../lib/types';
import { FormatStyle } from './inputs/NumberInput';
import * as React from "react";
export type ColType = "Text" | "Number" | "Boolean" | "Date" | "Badge" | "Link" | "Image" | "Custom";
export type SortDirection = "None" | "Ascending" | "Descending";
export interface DataTableColumn {
    name: string;
    header?: string;
    colType?: ColType;
    group?: string;
    width?: Sizing;
    hidden?: boolean;
    sortable?: boolean;
    sortDirection?: SortDirection;
    filterable?: boolean;
    alignContent?: Align;
    wrapText?: boolean;
    order?: number;
    icon?: string;
    help?: string;
    /** Aggregate strings rendered in the footer row. */
    footer?: string[];
    color?: string;
    badgeColorMapping?: Record<string, string>;
    formatStyle?: FormatStyle;
    precision?: number;
    currency?: string;
    render?: (value: unknown, row: Record<string, unknown>) => React.ReactNode;
}
export interface DataTableConfig {
    freezeColumns?: number;
    allowSorting?: boolean;
    allowFiltering?: boolean;
    pageSize?: number;
    selectionMode?: "None" | "Single" | "Multiple";
}
export interface DataTableProps extends WidgetBaseProps {
    columns?: DataTableColumn[];
    rows?: Array<Record<string, unknown>>;
    config?: DataTableConfig;
    rowActions?: MenuItem[];
    /** Per-row overrides for `rowActions`, keyed however your rows identify themselves. */
    perRowActions?: (row: Record<string, unknown>) => MenuItem[] | undefined;
    headerLeft?: React.ReactNode;
    headerRight?: React.ReactNode;
    emptyView?: React.ReactNode;
    onRowClick?: (row: Record<string, unknown>, index: number) => void;
    onRowAction?: (row: Record<string, unknown>, action: MenuItem) => void;
    onSelectionChange?: (rows: Array<Record<string, unknown>>) => void;
}
/**
 * A sortable, filterable, paginated grid. Mirrors `Ivy.DataTable`; where the
 * Ivy widget streams from a `DataTableConnection`, this one takes `rows`
 * directly, which is what a wireframe needs.
 *
 * @tags grid sortable filterable paginated
 * @example <DataTable columns={[{ name: "name", header: "Name" }]} rows={people} />
 */
export declare const DataTable: ({ id, columns, rows, config, rowActions, perRowActions, density, width, height, aspectRatio, visible, headerLeft, headerRight, emptyView, className, style, onRowClick, onRowAction, onSelectionChange, }: DataTableProps) => React.JSX.Element;
