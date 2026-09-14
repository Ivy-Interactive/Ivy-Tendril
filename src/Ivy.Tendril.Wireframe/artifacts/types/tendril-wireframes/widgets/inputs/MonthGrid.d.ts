import * as React from "react";
export type WeekDay = 0 | 1 | 2 | 3 | 4 | 5 | 6;
export interface MonthGridProps {
    month: Date;
    onMonthChange?: (month: Date) => void;
    selected?: Date[];
    /** Highlights every day between the first and last selected date. */
    range?: boolean;
    min?: Date;
    max?: Date;
    firstDayOfWeek?: WeekDay;
    onSelect?: (day: Date) => void;
    showNavigation?: boolean;
    seed?: string;
    /** Set false to let the grid stretch to its container instead of a fixed 16rem. */
    fixedWidth?: boolean;
}
/**
 * A hand-drawn month, shared by the date inputs and the calendar widget.
 *
 * @internal
 */
export declare const MonthGrid: ({ month, onMonthChange, selected, range, min, max, firstDayOfWeek, onSelect, showNavigation, seed, fixedWidth, }: MonthGridProps) => React.JSX.Element;
export declare const CALENDAR_BORDER = "#c3c3c3";
