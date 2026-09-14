import { BaseInputProps } from './InputShell';
export type BoolInputVariant = "Checkbox" | "Switch" | "Toggle";
export type NullableBoolean = boolean | null;
export interface BoolInputProps extends BaseInputProps {
    value?: NullableBoolean;
    label?: string;
    description?: string;
    variant?: BoolInputVariant;
    loading?: boolean;
    icon?: string;
    onChange?: (value: NullableBoolean) => void;
}
/**
 * Checkbox, switch or toggle button. Mirrors `Ivy.BoolInput`.
 *
 * @tags checkbox switch toggle boolean
 * @example <BoolInput variant="Switch" label="Notify me" value={on} onChange={setOn} />
 */
export declare const BoolInput: ({ id, value, label, description, variant, disabled, loading, nullable, invalid, icon, density, width, height, aspectRatio, visible, autoFocus, className, style, onChange, }: BoolInputProps) => import("react").JSX.Element;
