import remarkGfm from "remark-gfm";
import remarkMath from "remark-math";
import rehypeKatex from "rehype-katex";
import type { Options } from "react-markdown";

type MarkdownPlugins = Required<Pick<Options, "remarkPlugins" | "rehypePlugins">>;

/**
 * The only delimiter that marks math in Tendril markdown: `$$...$$`.
 *
 * Single-dollar text math is deliberately not recognised (see
 * `singleDollarTextMath` below), so a lone `$` is not a math signal.
 */
const MATH_DELIMITER = /\$\$/;

/** True when the content contains a `$$` math delimiter. */
export const hasMath = (content: string): boolean => MATH_DELIMITER.test(content);

/**
 * The remark/rehype plugin lists for rendering markdown: GFM always, plus
 * remark-math and rehype-katex when the content actually contains math. Gating
 * on the content keeps the KaTeX pass off the render path for the overwhelming
 * majority of plan markdown, notes and agent output, which has no math at all.
 *
 * `singleDollarTextMath: false` is required, not a preference: Tendril markdown
 * is full of prose dollar signs (`$env:PORT`, `$IsMacOS`, prices), and with
 * single-dollar math enabled remark-math pairs them up and renders the text
 * between two unrelated dollars as math. Inline math therefore also uses
 * `$$...$$`, which remark-math renders inline when it sits inside a paragraph
 * and as a centered display block when it stands on its own line. This matches
 * the Ivy framework's own Markdown widget, so math behaves the same in
 * `Text.Markdown` and in the Tendril widgets.
 *
 * KaTeX's stylesheet and web fonts are deliberately not bundled here: the Ivy
 * host page already loads them eagerly (its `vendor-markdown-*.css` carries the
 * `.katex` rules and all `KaTeX_*` `@font-face` declarations), so the widget
 * bundle inherits them and only needs the plugins.
 */
export const getMarkdownPlugins = (content: string): MarkdownPlugins =>
  hasMath(content)
    ? {
        remarkPlugins: [remarkGfm, [remarkMath, { singleDollarTextMath: false }]],
        rehypePlugins: [rehypeKatex],
      }
    : { remarkPlugins: [remarkGfm], rehypePlugins: [] };
