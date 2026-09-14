import { NullableSelectValue, Option } from '../../lib/types';
import { BaseInputProps } from './InputShell';
import * as React from "react";
export type SelectVariant = "Select" | "List" | "Toggle" | "Slider" | "Radio";
export type SearchMode = "CaseInsensitive" | "CaseSensitive" | "Fuzzy";
export interface SelectInputProps extends BaseInputProps {
    value?: NullableSelectValue;
    options?: Option[];
    variant?: SelectVariant;
    /** Turns the control into a multi-select. */
    selectMany?: boolean;
    separator?: string;
    maxSelections?: number;
    minSelections?: number;
    searchable?: boolean | null;
    showActions?: boolean;
    searchMode?: SearchMode;
    emptyMessage?: string;
    loading?: boolean;
    onChange?: (value: NullableSelectValue) => void;
}
/**
 * Every selection variant Ivy ships: dropdown, inline list, toggle group,
 * radio group and a stepped slider. Mirrors `Ivy.SelectInput`.
 *
 * @tags dropdown choice radio multi-select
 * @example <SelectInput options={options} value={value} onChange={setValue} />
 */
export declare const SelectInput: ({ id, value, options, variant, selectMany, separator, maxSelections, minSelections, searchable, showActions, searchMode, emptyMessage, loading, placeholder, disabled, invalid, nullable, density, ghost, width, height, aspectRatio, visible, autoFocus, prefix, suffix, className, style, onChange, }: SelectInputProps) => React.JSX.Element;
export interface AsyncSelectInputProps extends BaseInputProps {
    /** Label of the currently selected item; the value itself lives upstream. */
    displayValue?: string;
    loading?: boolean;
    options?: Option[];
    onQueryChange?: (query: string) => void;
    onChange?: (value: string | number | null) => void;
}
/** A select whose options are fetched as you type. Mirrors `Ivy.AsyncSelectInput`. */
export declare const AsyncSelectInput: ({ id, displayValue, loading, options, placeholder, disabled, invalid, density, ghost, width, height, aspectRatio, visible, autoFocus, nullable, className, style, onQueryChange, onChange, }: AsyncSelectInputProps) => React.JSX.Element;
