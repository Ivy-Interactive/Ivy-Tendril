import { Sizing, WidgetBaseProps } from '../lib/types';
import * as React from "react";
export interface KanbanColumn {
    id: string;
    title: string;
    color?: string;
    /** Refuses drops once this many cards are in the column. */
    limit?: number;
}
export interface KanbanTask {
    id: string;
    columnId: string;
    title?: string;
    description?: string;
    assignee?: string;
    priority?: number;
    order?: number;
}
export interface KanbanCardProps extends WidgetBaseProps {
    cardId?: string;
    /** The column this card belongs to. `status` is Ivy's older alias. */
    column?: string;
    columnName?: string;
    status?: string;
    title?: string;
    description?: string;
    assignee?: string;
    priority?: number;
    children?: React.ReactNode;
    draggable?: boolean;
    onDragStart?: React.DragEventHandler<HTMLDivElement>;
    onClick?: React.MouseEventHandler<HTMLElement>;
}
/** One card on the board. Mirrors `Ivy.KanbanCard`. */
export declare const KanbanCard: ({ id, title, description, assignee, priority, width, height, aspectRatio, visible, density, children, className, style, draggable, onDragStart, onClick, }: KanbanCardProps) => React.JSX.Element;
export interface KanbanProps extends WidgetBaseProps {
    columns?: KanbanColumn[];
    tasks?: KanbanTask[];
    columnWidth?: Sizing;
    showCounts?: boolean;
    /** Render a card yourself; falls back to the built-in `KanbanCard`. */
    renderCard?: (task: KanbanTask) => React.ReactNode;
    onCardMove?: (taskId: string, toColumnId: string) => void;
    onCardClick?: (task: KanbanTask) => void;
}
/**
 * Drag-and-drop board. Mirrors `Ivy.Kanban`.
 *
 * @tags board drag-and-drop columns
 * @example <Kanban columns={columns} tasks={tasks} onCardMove={move} />
 */
export declare const Kanban: ({ id, columns, tasks, width, height, aspectRatio, visible, columnWidth, showCounts, density, className, style, renderCard, onCardMove, onCardClick, }: KanbanProps) => React.JSX.Element;
