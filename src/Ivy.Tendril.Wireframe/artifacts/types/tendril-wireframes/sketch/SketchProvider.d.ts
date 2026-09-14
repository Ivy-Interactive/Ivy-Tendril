import * as React from "react";
export interface SketchTheme {
    /** How wobbly every stroke is. 0 is a ruler, 3 is a bad pen. */
    roughness: number;
    /** How much straight lines curve on their way. */
    bowing: number;
    strokeWidth: number;
    /** Set to freeze the randomness so re-renders redraw identically. */
    deterministic: boolean;
    /** Applies the turbulence filter to icons and images. */
    wobbleGlyphs: boolean;
}
export declare const useSketchTheme: () => SketchTheme;
/** SVG filter primitives that give icons, images and text their paper wobble. */
export declare const SketchDefs: () => React.JSX.Element;
export interface SketchProviderProps extends Partial<SketchTheme> {
    children?: React.ReactNode;
}
/**
 * Wrap the app once. Supplies the pencil settings every widget draws with and
 * mounts the shared SVG filter definitions.
 */
export declare const SketchProvider: ({ children, ...overrides }: SketchProviderProps) => React.JSX.Element;
