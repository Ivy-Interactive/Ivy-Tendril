/**
 * How big a label is.
 *
 * Nothing here touches the DOM, a canvas, or the font. That is the whole point, and it was
 * arrived at the hard way.
 *
 * The first version measured a hidden DOM node with a `ResizeObserver` — real wrapping, real
 * metrics, free. It failed `wireframe screenshot --repeat` on roughly one run in three: the
 * screenshot runner starts from a cold browser profile, so Balsamiq Sans arrives at a
 * different point in the render each time, and the correcting re-measure settled a fraction
 * of a pixel from where a warm reload settled. One box a pixel wider shifts everything below
 * it. Quantising did not help — a grid only moves the boundary the wobble crosses. Nor did
 * waiting on `document.fonts.ready`, which resolves before a lazily-used face has even been
 * requested. Canvas `measureText` after an explicit `document.fonts.load` got the failure
 * rate down to about one in six, but "usually identical" is not what a byte-for-byte
 * comparison means.
 *
 * So the layout no longer asks the browser anything. Widths come from a table of glyph
 * classes, which makes a node's size a pure function of its label — the same on a cold run,
 * a warm run, and a machine that has never had the font. Boxes end up a little roomier than
 * a perfect fit, which on a hand-drawn wireframe reads as deliberate rather than as slack.
 */
export interface MeasuredLabel {
    width: number;
    height: number;
    /** The lines as they were measured. Render these, not the original string. */
    lines: string[];
}
/**
 * Measures a label, wrapping it greedily to fit `maxWidth`.
 *
 * A single word longer than `maxWidth` overflows rather than breaking mid-word: an
 * identifier split across two lines is harder to read than a slightly wide box.
 */
export declare function measureLabel(text: string, fontSize: number, maxWidth: number, lineHeight?: number): MeasuredLabel;
