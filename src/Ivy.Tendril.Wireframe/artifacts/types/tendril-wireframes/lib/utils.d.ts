import { ClassValue } from 'clsx';
import { Align, Densities, Orientation, Overflow, Scroll, Sizing, TextAlignment, Thickness } from './types';
export declare function cn(...inputs: ClassValue[]): string;
/**
 * Ivy sizes arrive as raw CSS lengths ("200px", "50%"), fractions ("1/2") or
 * bare numbers, which mean rem units on the Ivy side.
 */
export declare function toCssSize(size?: Sizing): string | undefined;
/**
 * Resolves the `Ivy.WidgetBase` props — size, aspect ratio and visibility —
 * into the inline style every component spreads onto its root element.
 */
export declare function widgetStyle(base: {
    width?: Sizing;
    height?: Sizing;
    aspectRatio?: number;
    visible?: boolean;
    style?: React.CSSProperties;
}): React.CSSProperties;
export declare function sizeStyle(width?: Sizing, height?: Sizing): React.CSSProperties;
/** Resolves an `Ivy.Thickness` — or a uniform number — into per-edge lengths. */
export declare function thicknessStyle(value: Sizing | Thickness | undefined, property?: "padding" | "margin"): React.CSSProperties;
export declare const scrollClass: (scroll?: Scroll) => string;
/** Picks one of three values by density; falls back to `Medium`. */
export declare function byDensity<T>(density: Densities | undefined, values: [T, T, T]): T;
export declare const densityText: (density?: Densities) => string;
export declare const densityPadding: (density?: Densities) => string;
export declare const densityGap: (density?: Densities) => string;
export declare const densityIconSize: (density?: Densities) => number;
export declare const densityHeight: (density?: Densities) => string;
export declare function alignStyle(orientation: Orientation, align?: Align): React.CSSProperties;
export declare const textAlignClass: (alignment?: TextAlignment) => string;
export declare const overflowClass: (overflow?: Overflow) => string;
/** Turns any stable string into a deterministic rough.js seed. */
export declare function seedFrom(value: string | number | undefined): number;
export declare function formatBytes(bytes: number): string;
