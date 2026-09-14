import { parse } from "yaml";

/**
 * The `wireframe` fence: a live preview of one of the plan's wireframes.
 *
 * ```wireframe
 * name: checkout-payment        # required, the folder under the plan's Wireframes/
 * height: 640                   # optional px; default is the page's own height
 * viewport: Mobile              # optional: Desktop | Tablet | Mobile; default is the column's width
 *
 * A wireframe renders at the column's width and zooms out only when it needs more width than that
 * (overflowing content, or a named viewport wider than the column). Height is never zoomed: the
 * frame is as tall as the page up to a cap, and a longer page scrolls inside it.
 * ```
 *
 * A body that is just a slug is shorthand for `name:`. The rules here are mirrored by
 * `WireframeFenceValidator` on the server, which rejects a revision that breaks them; keep the
 * two in step.
 */

export type WireframeViewport = "Desktop" | "Tablet" | "Mobile";

export interface WireframeSpec {
  name: string;
  height?: number;
  viewport?: WireframeViewport;
}

export type WireframeParse = { ok: true; spec: WireframeSpec } | { ok: false; error: string };

const NAME = /^[a-z0-9][a-z0-9-]{0,63}$/;
// `caption` is accepted and ignored: plans written while wireframes had captions still render.
// `write-revision` rejects it, so no new plan carries one.
const KEYS = new Set(["name", "caption", "height", "viewport"]);

/** The CSS width each viewport renders at before it is scaled down to fit the column. */
export const VIEWPORT_WIDTHS: Record<WireframeViewport, number> = {
  Desktop: 1440,
  Tablet: 768,
  Mobile: 390,
};

export const MAX_HEIGHT = 4000;

export function parseWireframeFence(source: string): WireframeParse {
  const text = source.trim();
  if (text.length === 0) return { ok: false, error: "The block names no wireframe. Add a line such as `name: checkout-payment`." };
  if (NAME.test(text)) return { ok: true, spec: { name: text } };

  let doc: unknown;
  try {
    doc = parse(text);
  } catch {
    return { ok: false, error: "The block is not valid YAML." };
  }

  if (!doc || typeof doc !== "object" || Array.isArray(doc)) {
    return { ok: false, error: "Write the block as `name: <wireframe>`, optionally with height and viewport." };
  }

  const record = doc as Record<string, unknown>;
  for (const key of Object.keys(record)) {
    if (!KEYS.has(key)) return { ok: false, error: `Unknown key '${key}'. Use name, height or viewport.` };
  }

  const { name, height, viewport } = record;

  if (typeof name !== "string" || !NAME.test(name)) {
    return { ok: false, error: "name must be a lowercase slug such as checkout-payment." };
  }

  const spec: WireframeSpec = { name };

  if (height !== undefined && height !== null) {
    if (typeof height !== "number" || !Number.isInteger(height) || height < 1 || height > MAX_HEIGHT) {
      return { ok: false, error: `height must be a whole number of pixels between 1 and ${MAX_HEIGHT}.` };
    }
    spec.height = height;
  }

  if (viewport !== undefined && viewport !== null) {
    if (typeof viewport !== "string" || !(viewport in VIEWPORT_WIDTHS)) {
      return { ok: false, error: "viewport must be Desktop, Tablet or Mobile." };
    }
    spec.viewport = viewport as WireframeViewport;
  }

  return { ok: true, spec };
}
