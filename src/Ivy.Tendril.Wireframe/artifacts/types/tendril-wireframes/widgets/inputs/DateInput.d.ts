import { BaseInputProps } from './InputShell';
import { WeekDay } from './MonthGrid';
import * as React from "react";
export type DateTimeVariant = "Date" | "DateTime" | "Time" | "Month" | "Week" | "Year";
export interface DateTimeInputProps extends BaseInputProps {
    /** ISO 8601 string, e.g. `"2026-04-20"` or `"2026-04-20T09:30"`. */
    value?: string | null;
    variant?: DateTimeVariant;
    format?: string;
    firstDayOfWeek?: WeekDay;
    min?: string;
    max?: string;
    step?: string;
    onChange?: (value: string | null) => void;
}
/**
 * Date, time and month pickers. Mirrors `Ivy.DateTimeInput`.
 *
 * @tags date time calendar picker
 * @example <DateTimeInput value={date} onChange={setDate} />
 */
export declare const DateTimeInput: ({ id, value, variant, format: pattern, firstDayOfWeek, min, max, step, placeholder, disabled, invalid, nullable, density, ghost, width, height, aspectRatio, visible, autoFocus, prefix, suffix, className, style, onChange, }: DateTimeInputProps) => React.JSX.Element;
export interface DateRangeValue {
    item1: string | null;
    item2: string | null;
}
export interface DateRangeInputProps extends BaseInputProps {
    value?: DateRangeValue | null;
    format?: string;
    startPlaceholder?: string;
    endPlaceholder?: string;
    firstDayOfWeek?: WeekDay;
    min?: string | null;
    max?: string | null;
    onChange?: (value: DateRangeValue | null) => void;
}
/** Start and end date in one popover. Mirrors `Ivy.DateRangeInput`. */
export declare const DateRangeInput: ({ id, value, format: pattern, startPlaceholder, endPlaceholder, firstDayOfWeek, min, max, disabled, invalid, nullable, density, ghost, width, height, aspectRatio, visible, prefix, suffix, className, style, onChange, }: DateRangeInputProps) => React.JSX.Element;
