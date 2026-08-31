using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Ivy.Tendril.Widgets;

/// <summary>
/// Rewrites a proxied page so every URL in it points back into the WebViewer's view-space
/// (<c>/__view/&lt;absolute-url&gt;</c>) on the Ivy origin instead of at the upstream site.
///
/// HTML goes through a real parser (AngleSharp) rather than regular expressions: the parser
/// decides what is an attribute, what is script text and what is a comment, and it decodes
/// entities before we see a value and re-encodes them on the way out. Pattern matching over
/// raw markup gets that wrong in ways the browser then swallows silently — a
/// <c>style="background:url(&amp;quot;/a.svg&amp;quot;)"</c> rewritten as text ends up with the
/// entities inside the resolved path, and the declaration is dropped with no request and no
/// error. CSS is still matched with expressions, but only ever against text the parser has
/// already decoded.
/// </summary>
public static class WebViewerRewriter
{
    /// <summary>Path prefix that carries an absolute upstream URL on the Ivy origin.</summary>
    public const string ViewPrefix = "/__view/";

    private static readonly HtmlParser Parser = new();

    /// <summary>Attributes whose entire value is one URL.</summary>
    private static readonly string[] UrlAttributes =
        ["href", "src", "action", "formaction", "poster", "data-src", "data-href"];

    /// <summary>Attributes whose value is a srcset-style candidate list.</summary>
    private static readonly string[] SrcsetAttributes = ["srcset", "imagesrcset"];

    private static readonly Regex MetaRefresh = new(
        @"^(?<head>\s*[\d.]+\s*;\s*url\s*=\s*)(?<u>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Values that are not fetchable URLs, or are already in view-space.
    private static readonly Regex NonFetchable = new(
        @"^(data:|blob:|javascript:|mailto:|tel:|sms:|about:|#)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Rewrite a proxied HTML document. <paramref name="finalUrl"/> is the URL the upstream
    /// response actually came from (after redirects); <paramref name="agentScript"/> is the
    /// page agent to inject, or null to inject nothing.
    /// </summary>
    public static string RewriteHtml(string html, string finalUrl, string? agentScript = null)
    {
        if (!Uri.TryCreate(finalUrl, UriKind.Absolute, out var responseUri)) return html;

        var doc = Parser.ParseDocument(html);

        // A page's own <base href> is what ITS relative URLs resolve against, so it has to be
        // consumed before anything else is rewritten. Ours replaces it afterwards, which keeps
        // runtime-inserted relative URLs resolving into view-space too.
        var baseUri = responseUri;
        if (doc.QuerySelector("base[href]") is { } upstreamBase)
        {
            var href = upstreamBase.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(href) && Uri.TryCreate(responseUri, href, out var resolved))
            {
                baseUri = resolved;
            }
            upstreamBase.Remove();
        }

        foreach (var el in doc.All.ToArray())
        {
            var rewroteResourceUrl = false;

            foreach (var name in UrlAttributes)
            {
                if (el.GetAttribute(name) is not { } value) continue;
                el.SetAttribute(name, name == "src" && el.LocalName == "script"
                    ? RewriteScriptUrl(value, baseUri)
                    : RewriteUrl(value, baseUri));
                rewroteResourceUrl = true;
            }

            foreach (var name in SrcsetAttributes)
            {
                if (el.GetAttribute(name) is not { } value) continue;
                el.SetAttribute(name, RewriteSrcset(value, baseUri));
                rewroteResourceUrl = true;
            }

            if (el.GetAttribute("style") is { } style)
            {
                el.SetAttribute("style", RewriteCss(style, baseUri));
            }

            // Subresource integrity hashes cover the untouched upstream bytes. We rewrite those
            // bytes, so a surviving hash makes the browser refuse the script or stylesheet.
            //
            // "crossorigin" stays. Every rewritten URL is same-origin now, where the attribute
            // is a no-op for the fetch itself — but dropping it changes the request's MODE, and
            // a preload only satisfies the real load when the two modes agree. A font is always
            // fetched in CORS mode, so stripping it from <link rel=preload as=font crossorigin>
            // leaves a preload that matches nothing and the font downloads twice.
            if (rewroteResourceUrl)
            {
                el.RemoveAttribute("integrity");
            }
        }

        foreach (var style in doc.QuerySelectorAll("style"))
        {
            style.TextContent = RewriteCss(style.TextContent, baseUri);
        }

        foreach (var meta in doc.QuerySelectorAll("meta[http-equiv]").ToArray())
        {
            var equiv = (meta.GetAttribute("http-equiv") ?? "").Trim();

            if (equiv.Equals("refresh", StringComparison.OrdinalIgnoreCase))
            {
                if (meta.GetAttribute("content") is { } content && MetaRefresh.Match(content) is { Success: true } m)
                {
                    var target = m.Groups["u"].Value.Trim().Trim('\'', '"');
                    meta.SetAttribute("content", m.Groups["head"].Value + RewriteUrl(target, baseUri));
                }
            }
            else if (equiv.StartsWith("content-security-policy", StringComparison.OrdinalIgnoreCase))
            {
                // The upstream CSP header is already dropped on the way through the proxy; the
                // in-document form has to go too, or it blocks the injected agent and every
                // subresource now being served from the Ivy origin.
                meta.Remove();
            }
        }

        InjectHead(doc, baseUri, agentScript);
        return doc.ToHtml();
    }

    private static void InjectHead(IDocument doc, Uri baseUri, string? agentScript)
    {
        var head = doc.Head ?? doc.DocumentElement;
        if (head is null) return;

        var baseEl = doc.CreateElement("base");
        baseEl.SetAttribute("href", BaseHref(baseUri));
        head.InsertBefore(baseEl, head.FirstChild);

        if (string.IsNullOrEmpty(agentScript)) return;

        // The agent hooks console, clicks and history, so it has to run before page scripts.
        var script = doc.CreateElement("script");
        script.TextContent = agentScript;
        head.InsertBefore(script, baseEl.NextSibling);
    }

    /// <summary>
    /// Map a &lt;script src&gt; that lives on the page's own origin to that origin's path rather
    /// than into view-space.
    ///
    /// Module-chunk runtimes (Turbopack, and webpack with automatic public paths) identify a
    /// chunk by the URL it was served under — <c>document.currentScript.src</c> relative to the
    /// origin. A view-space URL carries the whole upstream URL inside the path, so the derived
    /// id never matches the one the runtime is waiting for, and the bootstrap stalls with no
    /// error at all: the runtime installs, every chunk downloads, and hydration simply never
    /// happens, leaving a dead server-rendered page. Keeping the site's own path preserves the
    /// id; the service worker maps the request back to the right upstream.
    /// </summary>
    public static string RewriteScriptUrl(string value, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var v = value.Trim();
        if (NonFetchable.IsMatch(v)) return value;
        if (v.StartsWith(ViewPrefix, StringComparison.Ordinal)) return value;
        if (!Uri.TryCreate(baseUri, v, out var abs)) return value;
        if (abs.Scheme != Uri.UriSchemeHttp && abs.Scheme != Uri.UriSchemeHttps) return value;

        // Only the page's own origin: a third-party script has no shared path space with it.
        return abs.GetLeftPart(UriPartial.Authority) == baseUri.GetLeftPart(UriPartial.Authority)
            ? abs.PathAndQuery
            : ViewPrefix + abs.AbsoluteUri;
    }

    /// <summary>Map a single URL-valued attribute into view-space.</summary>
    public static string RewriteUrl(string value, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var v = value.Trim();
        if (NonFetchable.IsMatch(v)) return value;
        if (v.StartsWith(ViewPrefix, StringComparison.Ordinal)) return value;
        if (!Uri.TryCreate(baseUri, v, out var abs)) return value;
        if (abs.Scheme != Uri.UriSchemeHttp && abs.Scheme != Uri.UriSchemeHttps) return value;

        return ViewPrefix + abs.AbsoluteUri;
    }

    /// <summary>
    /// Map every candidate in a srcset-style list into view-space.
    ///
    /// Candidates are split the way the HTML spec does — the URL runs to the next whitespace,
    /// then a descriptor runs to the next comma — so a <c>data:</c> URI carrying its own commas
    /// survives instead of being torn in half by a naive split.
    /// </summary>
    public static string RewriteSrcset(string value, Uri baseUri)
    {
        var output = new StringBuilder(value.Length + 32);
        var i = 0;
        var first = true;

        while (i < value.Length)
        {
            while (i < value.Length && (char.IsWhiteSpace(value[i]) || value[i] == ',')) i++;
            if (i >= value.Length) break;

            var urlStart = i;
            while (i < value.Length && !char.IsWhiteSpace(value[i])) i++;
            var url = value[urlStart..i];

            var descriptor = "";
            if (url.EndsWith(','))
            {
                url = url.TrimEnd(',');
            }
            else
            {
                var descriptorStart = i;
                while (i < value.Length && value[i] != ',') i++;
                descriptor = value[descriptorStart..i].Trim();
            }

            if (url.Length == 0) continue;

            if (!first) output.Append(", ");
            first = false;
            output.Append(RewriteUrl(url, baseUri));
            if (descriptor.Length > 0) output.Append(' ').Append(descriptor);
        }

        return output.ToString();
    }

    /// <summary>
    /// Map every <c>url()</c> and <c>@import</c> target in a stylesheet or style attribute into
    /// view-space. Expects CSS text, not markup — callers must hand over a value the HTML parser
    /// has already decoded.
    ///
    /// This scans the text rather than pattern-matching it: comments and string literals are
    /// copied through untouched, and only the inside of a url() token or an @import target is
    /// replaced. Everything unrecognised is emitted byte for byte — which is why the proxy does
    /// not parse to a CSS object model and re-serialise. A model that predates whatever the
    /// upstream site is using (@layer, @container, nesting) silently drops the parts it cannot
    /// represent, and this has to survive arbitrary modern stylesheets untouched.
    /// </summary>
    public static string RewriteCss(string css, Uri baseUri)
    {
        if (string.IsNullOrEmpty(css)) return css;

        var output = new StringBuilder(css.Length + 64);
        var i = 0;
        var importTargetExpected = false;

        while (i < css.Length)
        {
            var c = css[i];

            // A url() inside a comment is not a URL.
            if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                var close = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var end = close < 0 ? css.Length : close + 2;
                output.Append(css, i, end - i);
                i = end;
                continue;
            }

            if (c is '"' or '\'')
            {
                var terminated = TryScanString(css, i, out var end, out var content);
                if (terminated && importTargetExpected)
                {
                    output.Append(c).Append(RewriteUrl(content, baseUri)).Append(c);
                }
                else
                {
                    output.Append(css, i, end - i);
                }
                importTargetExpected = false;
                i = end;
                continue;
            }

            if (c == '@' && MatchesAt(css, i, "@import"))
            {
                output.Append(css, i, "@import".Length);
                i += "@import".Length;
                importTargetExpected = true;
                continue;
            }

            if (c is 'u' or 'U'
                && MatchesAt(css, i, "url(")
                && !IsIdentifierChar(i > 0 ? css[i - 1] : '\0'))
            {
                i = AppendUrlToken(css, i, baseUri, output);
                importTargetExpected = false;
                continue;
            }

            if (c is ';' or '{') importTargetExpected = false;

            output.Append(c);
            i++;
        }

        return output.ToString();
    }

    /// <summary>Rewrite one url() token, preserving its quoting and internal whitespace.</summary>
    private static int AppendUrlToken(string css, int start, Uri baseUri, StringBuilder output)
    {
        output.Append(css, start, 4);
        var i = start + 4;

        var leadingStart = i;
        while (i < css.Length && char.IsWhiteSpace(css[i])) i++;
        output.Append(css, leadingStart, i - leadingStart);

        if (i < css.Length && css[i] is '"' or '\'')
        {
            var quote = css[i];
            if (TryScanString(css, i, out var end, out var content))
            {
                output.Append(quote).Append(RewriteUrl(content, baseUri)).Append(quote);
            }
            else
            {
                output.Append(css, i, end - i);
            }
            i = end;
        }
        else
        {
            var valueStart = i;
            while (i < css.Length && css[i] != ')')
            {
                i += css[i] == '\\' ? 2 : 1;
            }
            if (i > css.Length) i = css.Length;

            var raw = css[valueStart..i];
            var trimmed = raw.TrimEnd();
            output.Append(RewriteUrl(Unescape(trimmed), baseUri)).Append(raw[trimmed.Length..]);
        }

        var trailingStart = i;
        while (i < css.Length && char.IsWhiteSpace(css[i])) i++;
        output.Append(css, trailingStart, i - trailingStart);

        if (i < css.Length && css[i] == ')')
        {
            output.Append(')');
            i++;
        }

        return i;
    }

    /// <summary>
    /// Scan a CSS string literal starting at its opening quote. Returns false for an
    /// unterminated one, in which case <paramref name="end"/> is where the scan stopped.
    /// </summary>
    private static bool TryScanString(string css, int start, out int end, out string content)
    {
        var quote = css[start];
        var i = start + 1;

        while (i < css.Length)
        {
            var c = css[i];
            if (c == '\\') { i += 2; continue; }
            if (c == '\n') break;
            if (c == quote)
            {
                end = i + 1;
                content = Unescape(css[(start + 1)..i]);
                return true;
            }
            i++;
        }

        end = Math.Min(i, css.Length);
        content = "";
        return false;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('\\')) return value;

        var output = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length) i++;
            output.Append(value[i]);
        }
        return output.ToString();
    }

    private static bool MatchesAt(string css, int index, string token) =>
        index + token.Length <= css.Length
        && string.Compare(css, index, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c is '-' or '_';

    /// <summary>View-space <c>&lt;base href&gt;</c> for a page loaded from <paramref name="url"/>.</summary>
    public static string BaseHref(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? BaseHref(uri) : ViewPrefix;

    private static string BaseHref(Uri uri)
    {
        var path = uri.AbsolutePath;
        var lastSlash = path.LastIndexOf('/');
        var directory = lastSlash >= 0 ? path[..(lastSlash + 1)] : "/";
        if (directory.Length == 0) directory = "/";
        return ViewPrefix + uri.GetLeftPart(UriPartial.Authority) + directory;
    }

    /// <summary>
    /// Re-insert the "//" that path normalizers drop after the scheme, so a view-space path
    /// like <c>/__view/https:/example.com</c> still yields a usable absolute URL.
    /// </summary>
    public static string FixProtocol(string value) =>
        Regex.Replace(value, "^(https?:)/(?!/)", "$1//");
}

/// <summary>
/// The optional <c>@&lt;viewer&gt;[.&lt;device&gt;]/</c> segment that may sit between
/// <see cref="WebViewerRewriter.ViewPrefix"/> and the absolute URL — for example
/// <c>/__view/@v3.mobile/https://example.com/</c>.
///
/// It exists because several WebViewers can be mounted on one Ivy page, sharing one origin
/// and therefore one service worker. The token is what tells them apart: it names the widget
/// a document belongs to (so a network entry is reported by that viewer alone) and the device
/// it emulates (so one viewer's phone viewport is not every viewer's).
///
/// Only DOCUMENT urls carry it — the parent widget builds those. Rewritten subresource URLs
/// stay bare, and the service worker resolves them through the client that asked. The same
/// grammar is implemented in <c>proxy-assets/sw.js</c> and <c>proxy-assets/agent.js</c>.
/// </summary>
public static class ViewToken
{
    private static readonly Regex Pattern = new(
        @"^@(?<viewer>[A-Za-z0-9]{1,16})(?:\.(?<device>mobile|tablet))?/",
        RegexOptions.Compiled);

    /// <summary>A parsed token. <paramref name="Length"/> is what to strip off the front.</summary>
    public readonly record struct Result(bool Success, int Length, string? Viewer, string? Device);

    /// <summary>Read a token off the front of a view-space path (the part after the prefix).</summary>
    public static Result Match(string rest)
    {
        var m = Pattern.Match(rest);
        return m.Success
            ? new Result(true, m.Length, m.Groups["viewer"].Value,
                m.Groups["device"].Success ? m.Groups["device"].Value : null)
            : default;
    }
}
