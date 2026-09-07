// The identity of a page, for deciding which comment pins belong on it.
//
// A comment is anchored to an element of one particular page, so its pin has to be scoped to
// that page. Without scoping the pin does not merely linger after a route change: an xpath is
// a path of sibling indices and a CSS path is a chain of classes, so both resolve happily on
// pages that have nothing to do with the comment, and the pin silently re-attaches to whatever
// occupies that position — looking, to the reviewer, entirely legitimate.
//
// Derived here and nowhere else. The widget stamps the result onto every comment and hands the
// same string to Ivy, so the C# side groups by plain string equality instead of re-deriving any
// of this. The ViewToken grammar is already parsed in three places; this does not need to
// become a fourth thing to keep in step.
export function canonicalPageUrl(input: string): string {
  try {
    const url = new URL(input);
    // A hash is a position WITHIN a page, not a page of its own: a comment left at the top of
    // a document and one left after following a #section link belong together.
    url.hash = "";
    // /tool and /tool/ are the same page everywhere this runs.
    url.pathname = url.pathname.replace(/\/+$/, "") || "/";
    // The query is KEPT. In a single-page app ?tab=billing usually IS the route, and folding
    // two tabs into one page would put a comment's pin on the wrong one. `new URL` has already
    // lowercased the scheme and host and dropped a default port; the path keeps its case,
    // because almost every server treats /Tool and /tool as different resources.
    return url.toString();
  } catch {
    // Not parseable — an about:blank frame, a data: URL. Fall back to the raw string so that
    // comments still group with each other rather than each becoming a page of its own.
    return input || "";
  }
}
