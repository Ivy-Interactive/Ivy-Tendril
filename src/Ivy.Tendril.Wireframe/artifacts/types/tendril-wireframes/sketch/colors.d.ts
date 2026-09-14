/**
 * Ivy widgets take colors as names from `Ivy.Colors`. A wireframe palette is
 * deliberately desaturated: everything reads as pencil on paper, with just
 * enough hue left to keep semantic colors apart.
 */
export type TendrilColor = "Black" | "White" | "Slate" | "Gray" | "Zinc" | "Neutral" | "Stone" | "Red" | "Orange" | "Amber" | "Yellow" | "Lime" | "Green" | "Emerald" | "Teal" | "Cyan" | "Sky" | "Blue" | "Indigo" | "Violet" | "Purple" | "Fuchsia" | "Pink" | "Rose" | "Primary" | "Secondary" | "Destructive" | "Success" | "Warning" | "Info" | "Muted";
export declare const INK = "#2f2f2f";
export declare const INK_MUTED = "#8b8b8b";
export declare const INK_FAINT = "#c3c3c3";
export declare const PAPER = "#fdfcf7";
export declare const PAPER_RAISED = "#ffffff";
export declare const PAPER_SUNKEN = "#f2f0e9";
export declare const HIGHLIGHT = "#fff6c8";
/**
 * Accepts an Ivy colour name (`"Destructive"`), a theme token (`"paper-sunken"`), or any CSS
 * colour. Anything else returns the fallback rather than painting black, and says so once.
 */
export declare function resolveColor(color?: string | null, fallback?: string): string;
/** Deterministic series colors for charts, in wireframe pencil tones. */
export declare const CHART_DEFAULT: string[];
export declare const CHART_RAINBOW: string[];
export type ColorScheme = "Default" | "Rainbow";
export declare const seriesColor: (scheme: ColorScheme | undefined, index: number) => string;
/** Mixes a color toward paper. `amount` 0 keeps it, 1 washes it out entirely. */
export declare function tint(color: string, amount?: number, towards?: string): string;
/**
 * Named surfaces. Every widget fill comes from here rather than a literal hex,
 * so the whole sheet stays on one paper stock.
 */
export declare const SURFACE: {
    /** Cards, lists, tables, menus — anything sitting on top of the page. */
    readonly raised: "#ffffff";
    /** Table headers, terminals — anything pressed into the page. */
    readonly sunken: "#f2f0e9";
    /** Chat transcripts and other large reading areas. */
    readonly quiet: "#fbfaf5";
    /** Code listings and editors. */
    readonly code: "#f7f6f1";
    /** The primary button and other "pressed pencil" fills. */
    readonly pressed: "#e8e5db";
    /** The secondary button. */
    readonly muted: "#f4f2ec";
    /** Highlighted rows, hovered menu items, active drop targets. */
    readonly highlight: "#fff6c8";
};
/**
 * Stroke weights, so a border's weight always means the same thing:
 * hairline for grid lines, regular for a resting edge, emphasis for focus or
 * selection, heavy for a modal.
 */
export declare const STROKE: {
    readonly hairline: 0.8;
    readonly thin: 1.1;
    readonly regular: 1.3;
    readonly emphasis: 1.6;
    readonly heavy: 1.8;
};
