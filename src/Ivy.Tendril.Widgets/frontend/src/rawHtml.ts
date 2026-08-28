import { defaultSchema } from "rehype-sanitize";
import type { Options as SanitizeSchema } from "rehype-sanitize";

/**
 * Raw HTML support for Tendril markdown.
 *
 * Tendril's own promptware instructs agents to emit GitHub-style
 * `<details>` / `<summary>` blocks (see `Promptwares/UpdatePlan/Program.md`,
 * which builds the `## Questions` section out of them), so plan markdown
 * routinely contains raw HTML. react-markdown drops raw HTML unless
 * `rehype-raw` reparses it, which left those blocks rendering as literal
 * `&lt;details&gt;` text with the body permanently expanded.
 *
 * The content is model-written and therefore never trusted: raw HTML is parsed
 * and then pruned by `rehype-sanitize` against the allow-list below.
 */

/**
 * Matches anything that looks like an HTML tag. Only a gate for the raw-HTML
 * rehype passes, so a false positive (a `<T>` in prose, a tag inside a fenced
 * code block) costs one wasted tree walk and nothing else — `rehype-raw` never
 * touches text inside code nodes.
 */
const RAW_HTML_TAG = /<\/?[a-zA-Z][a-zA-Z0-9-]*(?:\s[^<>]*)?\/?>/;

/** True when the content contains raw HTML that needs parsing and sanitising. */
export const hasRawHtml = (content: string): boolean => RAW_HTML_TAG.test(content);

/**
 * hast-util-sanitize's GitHub-derived default schema, which already allows
 * `details`, `summary` and the `open` attribute, plus the Tendril-specific
 * adjustments below.
 */
export const rawHtmlSchema: SanitizeSchema = {
  ...defaultSchema,
  // Dropping a disallowed element normally keeps its children, which is right
  // for markup (`<span>text</span>` should still show its text) but wrong for
  // elements whose content is code: the default schema only strips `script`,
  // leaving a `<style>` block's CSS rules on the page as prose.
  strip: [...(defaultSchema.strip ?? []), "style"],
  attributes: {
    ...defaultSchema.attributes,
    // remark-math emits `<code class="language-math math-inline">` (and
    // `math-display` inside a `<pre>`), and rehype-katex finds those nodes by
    // class. The default schema allows only `language-*` on code, so without
    // this the marker class is stripped and the math never reaches KaTeX.
    code: [["className", /^language-./, "math", "math-display", "math-inline"]],
  },
  // URL safety is react-markdown's `urlTransform`, which runs after this pass
  // over every URL attribute and applies the same protocol allow-list. Keeping
  // a second list here would only strip the `file://` and `D:\...` links that
  // DangerouslyAllowLocalFiles exists to render.
  protocols: {},
};
