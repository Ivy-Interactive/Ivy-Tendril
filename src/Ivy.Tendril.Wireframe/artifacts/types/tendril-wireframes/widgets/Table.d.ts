import { Align, WidgetBaseProps } from '../lib/types';
import * as React from "react";
export interface TableProps extends WidgetBaseProps {
    children?: React.ReactNode;
    layout?: "Auto" | "Fixed";
}
/**
 * Grid of rows and cells. Mirrors `Ivy.Table`.
 *
 * @tags grid rows static
 * @example <Table><tbody><TableRow><TableCell>Pencils</TableCell></TableRow></tbody></Table>
 */
export declare const Table: ({ id, children, width, height, aspectRatio, visible, density, layout, className, style, }: TableProps) => React.JSX.Element;
export interface TableRowProps extends WidgetBaseProps {
    isHeader?: boolean;
    isFooter?: boolean;
    children?: React.ReactNode;
    onClick?: React.MouseEventHandler<HTMLTableRowElement>;
}
/** One row. Mirrors `Ivy.TableRow`. */
export declare const TableRow: ({ id, isHeader, isFooter, children, width, height, aspectRatio, visible, className, style, onClick, }: TableRowProps) => React.JSX.Element;
export interface TableCellProps extends WidgetBaseProps {
    isHeader?: boolean;
    isFooter?: boolean;
    alignContent?: Align;
    multiline?: boolean;
    colSpan?: number;
    rowSpan?: number;
    children?: React.ReactNode;
}
/** One cell. Mirrors `Ivy.TableCell`. */
export declare const TableCell: ({ id, isHeader, isFooter, alignContent, width, height, aspectRatio, visible, multiline, density, colSpan, rowSpan, children, className, style, }: TableCellProps) => React.JSX.Element;
