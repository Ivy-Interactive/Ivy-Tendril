import { BaseInputProps } from './InputShell';
import * as React from "react";
export type FormatStyle = "Decimal" | "Currency" | "Percent" | "Compact" | "Scientific" | "Engineering" | "Accounting" | "Bytes";
export declare function formatNumber(value: number, formatStyle?: FormatStyle, precision?: number, currency?: string, noGrouping?: boolean): string;
/** Ranges Ivy clamps to when a numeric input is bound to a CLR type. */
export declare const TYPE_LIMITS: Record<string, {
    min: number;
    max: number;
}>;
/**
 * Slider track and thumb, drawn with rough.js.
 *
 * @internal
 */
declare const SketchSlider: ({ value, min, max, step, disabled, seed, onChange, height, }: {
    value: number;
    min: number;
    max: number;
    step: number;
    disabled?: boolean;
    seed: string;
    onChange: (value: number) => void;
    height?: number;
}) => React.JSX.Element;
export interface NumberInputProps extends BaseInputProps {
    value?: number | null;
    variant?: "Number" | "Slider";
    min?: number;
    max?: number;
    step?: number;
    precision?: number;
    formatStyle?: FormatStyle;
    currency?: string;
    noGrouping?: boolean;
    /** Ivy's CLR type name, used to clamp to that type's range (e.g. `"int"`). */
    targetType?: string;
    /** Hides the stepper buttons on the `Number` variant. */
    hideStepper?: boolean;
    onChange?: (value: number | null) => void;
    onBlur?: () => void;
}
/**
 * Numeric entry, as a field or a slider. Mirrors `Ivy.NumberInput`.
 *
 * @tags number currency slider numeric
 * @example <NumberInput value={price} formatStyle="Currency" currency="EUR" onChange={setPrice} />
 */
export declare const NumberInput: ({ id, value, variant, min, max, step, precision, formatStyle, currency, noGrouping, targetType, hideStepper, placeholder, disabled, invalid, nullable, density, ghost, width, height, aspectRatio, visible, autoFocus, prefix, suffix, className, style, onChange, onBlur, ...rest }: NumberInputProps) => React.JSX.Element;
export interface NumberRangeInputProps extends BaseInputProps {
    lowerValue?: number | null;
    upperValue?: number | null;
    min?: number;
    max?: number;
    step?: number;
    precision?: number;
    formatStyle?: FormatStyle;
    currency?: string;
    noGrouping?: boolean;
    targetType?: string;
    onChange?: (lower: number | null, upper: number | null) => void;
}
/** Two bounded values in one control. Mirrors `Ivy.NumberRangeInput`. */
export declare const NumberRangeInput: ({ id, lowerValue, upperValue, min, max, step, precision, formatStyle, currency, noGrouping, targetType, disabled, invalid, nullable, density, width, height, aspectRatio, visible, className, style, onChange, }: NumberRangeInputProps) => React.JSX.Element;
export { SketchSlider };
