import { WidgetBaseProps } from '../lib/types';
import * as React from "react";
export interface ListProps extends WidgetBaseProps {
    children?: React.ReactNode;
}
/**
 * A bordered stack of `ListItem`s. Mirrors `Ivy.List`.
 *
 * @tags collection rows
 * @example <List><ListItem title="Inbox" badge="12" /></List>
 */
export declare const List: ({ id, children, density, width, height, aspectRatio, visible, className, style, }: ListProps) => React.JSX.Element;
export interface ListItemProps extends WidgetBaseProps {
    title?: string;
    subtitle?: string;
    icon?: string;
    badge?: string;
    disabled?: boolean;
    children?: React.ReactNode;
    onClick?: React.MouseEventHandler<HTMLElement>;
}
/**
 * One row of a list. Mirrors `Ivy.ListItem`.
 *
 * @tags row entry
 * @example <ListItem title="Inbox" subtitle="12 unread" icon="Inbox" onClick={open} />
 */
export declare const ListItem: ({ id, title, subtitle, icon, badge, disabled, children, density, width, height, aspectRatio, visible, className, style, onClick, }: ListItemProps) => React.JSX.Element;
export interface DetailsProps extends WidgetBaseProps {
    children?: React.ReactNode;
    /** Number of label/value columns. */
    columns?: number;
}
/**
 * Label/value read-out grid. Mirrors `Ivy.Details`.
 *
 * @tags key-value summary
 * @example <Details><Detail label="Owner">Ada</Detail></Details>
 */
export declare const Details: ({ id, children, density, columns, width, height, aspectRatio, visible, className, style, }: DetailsProps) => React.JSX.Element;
export interface DetailProps extends WidgetBaseProps {
    label: string;
    multiline?: boolean;
    children?: React.ReactNode;
}
/**
 * One label/value pair. Mirrors `Ivy.Detail`.
 *
 * @tags key-value row
 * @example <Detail label="Status">Active</Detail>
 */
export declare const Detail: ({ id, label, multiline, children, width, height, aspectRatio, visible, density: ownDensity, className, style, }: DetailProps) => React.JSX.Element;
