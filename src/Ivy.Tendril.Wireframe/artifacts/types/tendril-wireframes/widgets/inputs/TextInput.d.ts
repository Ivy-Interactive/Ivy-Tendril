import { BaseInputProps } from './InputShell';
import * as React from "react";
export type TextInputVariant = "Text" | "Textarea" | "Email" | "Tel" | "Url" | "Password" | "Search";
export interface TextInputProps extends BaseInputProps {
    value?: string;
    variant?: TextInputVariant;
    maxLength?: number;
    minLength?: number;
    pattern?: string;
    rows?: number;
    /** Hint shown on the right, e.g. `"⌘K"`. */
    shortcutKey?: string;
    /** Adds a microphone button that toggles `onDictationToggle`. */
    dictation?: boolean;
    dictationUploadUrl?: string;
    /** Text pushed back from the transcription service. */
    dictationTranscription?: string;
    /** Bump to apply a new `dictationTranscription` to the field. */
    dictationTranscriptionVersion?: number;
    onDictationToggle?: (recording: boolean) => void;
    onChange?: (value: string | null) => void;
    onBlur?: () => void;
    onSubmit?: (value: string | null) => void;
}
/**
 * Single-line and multi-line text entry. Mirrors `Ivy.TextInput`.
 *
 * @tags text field entry search password
 * @example <TextInput value={name} onChange={setName} placeholder="Your name" />
 * @example <TextInput variant="Textarea" rows={4} value={notes} onChange={setNotes} />
 */
export declare const TextInput: ({ id, value, variant, placeholder, disabled, invalid, nullable, density, ghost, width, height, maxLength, minLength, pattern, rows, shortcutKey, dictation, dictationTranscription, dictationTranscriptionVersion, onDictationToggle, aspectRatio, visible, autoFocus, prefix, suffix, className, style, onChange, onBlur, onSubmit, ...rest }: TextInputProps) => React.JSX.Element;
export interface ReadOnlyInputProps extends BaseInputProps {
    value?: string | number | boolean | null;
    showCopyButton?: boolean;
}
/** Displays a value that cannot be edited. Mirrors `Ivy.ReadOnlyInput`. */
export declare const ReadOnlyInput: ({ id, value, showCopyButton, density, width, aspectRatio, visible, className, style, ...rest }: ReadOnlyInputProps) => React.JSX.Element;
