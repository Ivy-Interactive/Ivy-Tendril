import { FileItem } from '../../lib/types';
import { BaseInputProps } from './InputShell';
import * as React from "react";
export type ColorInputVariant = "Text" | "Picker" | "TextAndPicker" | "Swatch" | "SwatchPicker";
export interface ColorInputProps extends BaseInputProps {
    value?: string | null;
    variant?: ColorInputVariant;
    allowAlpha?: boolean;
    foreground?: boolean;
    onChange?: (value: string | null) => void;
}
/** Colour entry as hex text, a native picker, or a swatch grid. Mirrors `Ivy.ColorInput`. */
export declare const ColorInput: ({ id, value, variant, allowAlpha, disabled, invalid, nullable, placeholder, density, ghost, width, height, aspectRatio, visible, prefix, suffix, className, style, onChange, }: ColorInputProps) => React.JSX.Element;
export interface IconInputProps extends BaseInputProps {
    value?: string | null;
    onChange?: (value: string | null) => void;
}
/** Searchable lucide icon picker. Mirrors `Ivy.IconInput`. */
export declare const IconInput: ({ id, value, placeholder, disabled, invalid, nullable, density, ghost, width, height, aspectRatio, visible, className, style, onChange, }: IconInputProps) => React.JSX.Element;
export type FeedbackVariant = "Thumbs" | "Emojis" | "Stars";
export interface FeedbackInputProps extends BaseInputProps {
    value?: number | boolean | null;
    variant?: FeedbackVariant;
    allowHalf?: boolean;
    max?: number;
    onChange?: (value: number | boolean | null) => void;
}
/** Thumbs, stars or emoji rating. Mirrors `Ivy.FeedbackInput`. */
export declare const FeedbackInput: ({ id, value, variant, allowHalf, max, disabled, invalid, nullable, density, width, height, aspectRatio, visible, className, style, onChange, }: FeedbackInputProps) => React.JSX.Element;
export interface FileInputProps extends BaseInputProps {
    value?: FileItem | FileItem[] | null;
    accept?: string;
    maxFileSize?: number;
    minFileSize?: number;
    multiple?: boolean;
    maxFiles?: number;
    variant?: "Default" | "Drop";
    /** Endpoint the host uploads to; surfaced so callers can wire their own upload. */
    uploadUrl?: string;
    onChange?: (files: File[]) => void;
    onRemove?: (file: FileItem) => void;
}
/**
 * File chooser with a drop-zone variant. Mirrors `Ivy.FileInput`.
 *
 * @tags upload attachment drop-zone
 * @example <FileInput variant="Drop" accept=".png,.jpg" onChange={upload} />
 */
export declare const FileInput: ({ id, value, accept, maxFileSize, multiple, maxFiles, variant, uploadUrl, placeholder, disabled, invalid, density, width, height, aspectRatio, visible, className, style, onChange, onRemove, }: FileInputProps) => React.JSX.Element;
export interface SignatureInputProps extends BaseInputProps {
    /** Data URL of the drawn signature. */
    value?: string | null;
    pen?: string;
    background?: string;
    penThickness?: number;
    /** `Bordered` keeps the frame; `Ghost` drops it, as in Ivy. */
    variant?: "Bordered" | "Ghost";
    onChange?: (value: string | null) => void;
}
/** Draw-your-name pad. Mirrors `Ivy.SignatureInput`. */
export declare const SignatureInput: ({ id, value, pen, background, penThickness, variant, placeholder, disabled, invalid, density, width, height: rootHeight, aspectRatio, visible, className, style, onChange, }: SignatureInputProps) => React.JSX.Element;
export interface CodeInputProps extends BaseInputProps {
    value?: string;
    language?: string;
    /** Ivy's editor flavour. `Default` shows line numbers; `Ghost` drops the chrome. */
    variant?: "Default" | "Ghost";
    showCopyButton?: boolean;
    onChange?: (value: string | null) => void;
}
/** Plain-text code editor with line numbers. Mirrors `Ivy.CodeInput`. */
export declare const CodeInput: ({ id, value, language, variant, showCopyButton, placeholder, disabled, invalid, nullable, density, width, height, aspectRatio, visible, autoFocus, className, style, onChange, }: CodeInputProps) => React.JSX.Element;
export interface AudioInputProps extends BaseInputProps {
    label?: string;
    recordingLabel?: string;
    /** Seconds recorded so far; drives the waveform. */
    elapsed?: number;
    recording?: boolean;
    onToggleRecording?: (recording: boolean) => void;
}
/** Push-to-record control with a scribbled waveform. Mirrors `Ivy.AudioInput`. */
export declare const AudioInput: ({ id, label, recordingLabel, elapsed, recording, disabled, invalid, density, width, height, aspectRatio, visible, className, style, onToggleRecording, }: AudioInputProps) => React.JSX.Element;
export interface CameraInputProps extends BaseInputProps {
    facingMode?: "user" | "environment";
    /** Data URL of the captured frame. */
    value?: string | null;
    /** Endpoint the host uploads captures to. */
    uploadUrl?: string;
    onCapture?: () => void;
    onClear?: () => void;
}
/** Viewfinder and shutter. Mirrors `Ivy.CameraInput`. */
export declare const CameraInput: ({ id, facingMode, value, uploadUrl, placeholder, disabled, invalid, density, width, height: rootHeight, aspectRatio, visible, className, style, onCapture, onClear, }: CameraInputProps) => React.JSX.Element;
