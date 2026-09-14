export type Pt = [number, number];
/**
 * A repeatable hand. Same seed, same wobble, every render — the sketch layer's
 * `deterministic` promise extends to the marks built on top of it.
 */
export declare function handRng(seed: string | number | undefined): () => number;
/** An SVG path for a rounded rectangle. Falls back to a plain box at radius 0. */
export declare function roundedRectPath(x: number, y: number, w: number, h: number, r: number): string;
/**
 * Samples a quadratic from `from` to `to` whose control point is pushed
 * `bend` pixels off the perpendicular. A bend of 0 gives a straight run, which
 * rough.js then draws with its own wobble.
 */
export declare function bendPoints(from: Pt, to: Pt, bend: number, steps?: number): Pt[];
/**
 * The two barbs of an arrowhead at `tip`, angled back along the direction the shaft came
 * in on.
 *
 * Aim `from` at the *previous point on the run* rather than the far endpoint: on a bent or
 * elbowed line the two differ, and aiming at the far end puts the head on at the wrong
 * angle exactly when the line is most obviously curved.
 */
export declare function barbsAt(tip: Pt, from: Pt, size: number, rnd: () => number): Array<[Pt, Pt]>;
