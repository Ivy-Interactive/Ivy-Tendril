using System.Text.RegularExpressions;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Test.Widgets;

public class WebViewerRewriterTests
{
    private const string PageUrl = "https://example.com/docs/page.html";
    private static readonly Uri PageUri = new(PageUrl);

    // ---- the defect this rewriter was rebuilt for -----------------------------
    // A quoted url() inside an inline style attribute arrives HTML-encoded. Matching raw
    // markup captured the entities as part of the URL, and the browser then dropped the
    // whole declaration without a request or an error.
    [Fact]
    public void RewriteHtml_QuotedUrlInStyleAttribute_KeepsQuotesOutOfThePath()
    {
        const string html = """<html><head></head><body><div style="background-image:url(&quot;/a.svg&quot;)"></div></body></html>""";

        var result = WebViewerRewriter.RewriteHtml(html, PageUrl);

        Assert.Contains("/__view/https://example.com/a.svg", result);
        Assert.DoesNotContain("example.com/&quot;", result);
    }

    // ---- what a parser buys us that pattern matching did not ------------------
    [Fact]
    public void RewriteHtml_LeavesUrlsInsideScriptBodiesAlone()
    {
        const string html = """<html><head><script>var s = '<a href="/only-a-string">';</script></head><body></body></html>""";

        var result = WebViewerRewriter.RewriteHtml(html, PageUrl);

        Assert.DoesNotContain("/__view/https://example.com/only-a-string", result);
    }

    [Fact]
    public void RewriteHtml_LeavesUrlsInsideCommentsAlone()
    {
        const string html = """<html><head></head><body><!-- <img src="/hidden.png"> --></body></html>""";

        var result = WebViewerRewriter.RewriteHtml(html, PageUrl);

        Assert.DoesNotContain("/__view/https://example.com/hidden.png", result);
    }

    [Fact]
    public void RewriteHtml_ResolvesAgainstUpstreamBaseHrefThenReplacesIt()
    {
        const string html = """<html><head><base href="/assets/"></head><body><img src="a.png"></body></html>""";

        var result = WebViewerRewriter.RewriteHtml(html, PageUrl);

        Assert.Contains("/__view/https://example.com/assets/a.png", result);
        Assert.Contains("""<base href="/__view/https://example.com/assets/">""", result);
        Assert.Single(Regex.Matches(result, "<base "));
    }

    [Fact]
    public void RewriteHtml_StripsIntegrityFromRewrittenResources()
    {
        const string html = """<html><head><link rel="stylesheet" href="/app.css" integrity="sha384-abc" crossorigin="anonymous"></head><body></body></html>""";

        var result = WebViewerRewriter.RewriteHtml(html, PageUrl);

        Assert.Contains("/__view/https://example.com/app.css", result);
        Assert.DoesNotContain("integrity", result);
        // The hash covers bytes we rewrote. The fetch MODE is not ours to change.
        Assert.Contains("crossorigin=\"anonymous\"", result);
    }

    // A font is fetched in CORS mode whatever its origin, so a preload stripped of its
    // crossorigin no longer matches the load it was meant to serve and the font is
    // downloaded a second time.
    [Fact]
    public void RewriteHtml_KeepsCrossoriginOnAPreload()
    {
        const string html = """<html><head><link rel="preload" as="font" href="/f.woff2" crossorigin=""></head><body></body></html>""";

        var result = WebViewerRewriter.RewriteHtml(html, PageUrl);

        Assert.Contains("/__view/https://example.com/f.woff2", result);
        Assert.Contains("crossorigin", result);
    }

    // A chunk runtime identifies a chunk by the path it was served under, so a same-origin
    // script has to keep the site's own path; the service worker maps it back to the upstream.
    [Fact]
    public void RewriteHtml_KeepsSameOriginScriptPathsOutOfViewSpace()
    {
        const string html = """<html><head><script src="/_next/static/chunk.js"></script></head><body></body></html>""";

        var result = WebViewerRewriter.RewriteHtml(html, PageUrl);

        Assert.Contains("""src="/_next/static/chunk.js""", result);
        Assert.DoesNotContain("/__view/https://example.com/_next", result);
    }

    [Theory]
    [InlineData("/_next/chunk.js", "/_next/chunk.js")]
    [InlineData("chunk.js", "/docs/chunk.js")]
    [InlineData("https://example.com/a.js", "/a.js")]
    [InlineData("https://cdn.test/a.js", "/__view/https://cdn.test/a.js")]
    [InlineData("data:text/javascript,0", "data:text/javascript,0")]
    public void RewriteScriptUrl_KeepsOwnOriginPathsAndMapsThirdParties(string value, string expected)
    {
        Assert.Equal(expected, WebViewerRewriter.RewriteScriptUrl(value, PageUri));
    }

    [Fact]
    public void RewriteHtml_RemovesInDocumentContentSecurityPolicy()
    {
        const string html = """<html><head><meta http-equiv="Content-Security-Policy" content="default-src 'self'"></head><body></body></html>""";

        var result = WebViewerRewriter.RewriteHtml(html, PageUrl);

        Assert.DoesNotContain("content-security-policy", result.ToLowerInvariant());
    }

    [Fact]
    public void RewriteHtml_RewritesMetaRefreshTarget()
    {
        const string html = """<html><head><meta http-equiv="refresh" content="0; url=/next"></head><body></body></html>""";

        var result = WebViewerRewriter.RewriteHtml(html, PageUrl);

        Assert.Contains("0; url=/__view/https://example.com/next", result);
    }

    [Fact]
    public void RewriteHtml_InjectsAgentScriptAheadOfPageScripts()
    {
        const string html = """<html><head><script src="/app.js"></script></head><body></body></html>""";

        var result = WebViewerRewriter.RewriteHtml(html, PageUrl, "window.__agent = 1;");

        var agentAt = result.IndexOf("window.__agent = 1;", StringComparison.Ordinal);
        var pageScriptAt = result.IndexOf("""src="/app.js""", StringComparison.Ordinal);
        Assert.True(agentAt >= 0, "agent script was not injected");
        Assert.True(agentAt < pageScriptAt, "agent script must run before the page's own scripts");
    }

    [Fact]
    public void RewriteHtml_ReturnsInputUnchangedForANonAbsolutePageUrl()
    {
        Assert.Equal("<p>x</p>", WebViewerRewriter.RewriteHtml("<p>x</p>", "not a url"));
    }

    [Fact]
    public void RewriteHtml_RewritesSrcsetOnSourceElements()
    {
        const string html = """<html><head></head><body><picture><source srcset="a.png 1x, b.png 2x"></picture></body></html>""";

        var result = WebViewerRewriter.RewriteHtml(html, PageUrl);

        Assert.Contains("/__view/https://example.com/docs/a.png 1x", result);
        Assert.Contains("/__view/https://example.com/docs/b.png 2x", result);
    }

    // ---- url mapping ----------------------------------------------------------
    [Theory]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("javascript:void(0)")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("tel:+123")]
    [InlineData("#anchor")]
    [InlineData("/__view/https://other.test/already-mapped")]
    public void RewriteUrl_LeavesNonFetchableValuesAlone(string value)
    {
        Assert.Equal(value, WebViewerRewriter.RewriteUrl(value, PageUri));
    }

    [Theory]
    [InlineData("a.png", "/__view/https://example.com/docs/a.png")]
    [InlineData("/a.png", "/__view/https://example.com/a.png")]
    [InlineData("../a.png", "/__view/https://example.com/a.png")]
    [InlineData("//cdn.test/a.js", "/__view/https://cdn.test/a.js")]
    [InlineData("https://other.test/a.js", "/__view/https://other.test/a.js")]
    public void RewriteUrl_MapsFetchableUrlsIntoViewSpace(string value, string expected)
    {
        Assert.Equal(expected, WebViewerRewriter.RewriteUrl(value, PageUri));
    }

    [Fact]
    public void RewriteSrcset_RewritesEveryCandidateAndKeepsDescriptors()
    {
        Assert.Equal(
            "/__view/https://example.com/a.png 1x, /__view/https://example.com/b.png 2x",
            WebViewerRewriter.RewriteSrcset("/a.png 1x, /b.png 2x", PageUri));
    }

    [Fact]
    public void RewriteSrcset_KeepsADataUriWhoseOwnCommasWouldSplitIt()
    {
        Assert.Equal(
            "data:image/svg+xml;base64,AAA,BBB 1x, /__view/https://example.com/b.png 2x",
            WebViewerRewriter.RewriteSrcset("data:image/svg+xml;base64,AAA,BBB 1x, /b.png 2x", PageUri));
    }

    [Fact]
    public void RewriteSrcset_HandlesCandidatesWithoutDescriptors()
    {
        Assert.Equal(
            "/__view/https://example.com/a.png, /__view/https://example.com/b.png",
            WebViewerRewriter.RewriteSrcset("/a.png, /b.png", PageUri));
    }

    // ---- css ------------------------------------------------------------------
    [Theory]
    [InlineData("url(img/a.png)", "url(/__view/https://example.com/docs/img/a.png)")]
    [InlineData("url('/a.png')", "url('/__view/https://example.com/a.png')")]
    [InlineData("url(\"/a.png\")", "url(\"/__view/https://example.com/a.png\")")]
    public void RewriteCss_MapsUrlsAndPreservesQuoteStyle(string css, string expected)
    {
        Assert.Equal(expected, WebViewerRewriter.RewriteCss(css, PageUri));
    }

    [Fact]
    public void RewriteCss_RewritesBareImport()
    {
        Assert.Equal(
            "@import \"/__view/https://example.com/theme.css\"",
            WebViewerRewriter.RewriteCss("@import \"/theme.css\"", PageUri));
    }

    [Fact]
    public void RewriteCss_LeavesDataUrlsAlone()
    {
        const string css = "url(data:image/svg+xml;base64,AAAA)";
        Assert.Equal(css, WebViewerRewriter.RewriteCss(css, PageUri));
    }

    [Fact]
    public void RewriteCss_LeavesUrlsInsideCommentsAlone()
    {
        const string css = "/* url(/commented.png) */ a { background: url(/real.png); }";

        var result = WebViewerRewriter.RewriteCss(css, PageUri);

        Assert.Contains("/* url(/commented.png) */", result);
        Assert.Contains("url(/__view/https://example.com/real.png)", result);
    }

    [Fact]
    public void RewriteCss_LeavesUrlLikeStringValuesAlone()
    {
        const string css = "a::after { content: \"url(/not-a-url.png)\"; }";
        Assert.Equal(css, WebViewerRewriter.RewriteCss(css, PageUri));
    }

    [Fact]
    public void RewriteCss_HandlesAQuotedUrlContainingParentheses()
    {
        Assert.Equal(
            "url(\"/__view/https://example.com/a(1).png\")",
            WebViewerRewriter.RewriteCss("url(\"/a(1).png\")", PageUri));
    }

    [Fact]
    public void RewriteCss_DoesNotTreatAnIdentifierEndingInUrlAsAUrlToken()
    {
        const string css = "a { --my-url(x): 1; }";
        Assert.Equal(css, WebViewerRewriter.RewriteCss(css, PageUri));
    }

    [Fact]
    public void RewriteCss_PreservesWhitespaceInsideTheUrlToken()
    {
        Assert.Equal(
            "url( /__view/https://example.com/a.png )",
            WebViewerRewriter.RewriteCss("url( /a.png )", PageUri));
    }

    [Fact]
    public void RewriteCss_RewritesImportUrlForm()
    {
        Assert.Equal(
            "@import url(/__view/https://example.com/theme.css)",
            WebViewerRewriter.RewriteCss("@import url(/theme.css)", PageUri));
    }

    [Fact]
    public void RewriteCss_LeavesModernAtRulesUntouched()
    {
        const string css = "@layer base, components;@container (min-width: 400px){.c{color:red}}"
            + "@supports (display:grid){.g{display:grid}}";

        Assert.Equal(css, WebViewerRewriter.RewriteCss(css, PageUri));
    }

    // ---- helpers --------------------------------------------------------------
    [Theory]
    [InlineData("https://example.com/docs/page.html", "/__view/https://example.com/docs/")]
    [InlineData("https://example.com/", "/__view/https://example.com/")]
    [InlineData("https://example.com", "/__view/https://example.com/")]
    [InlineData("not a url", "/__view/")]
    public void BaseHref_PointsAtTheDirectoryOfThePage(string url, string expected)
    {
        Assert.Equal(expected, WebViewerRewriter.BaseHref(url));
    }

    // The token that keeps several viewers on one page apart. Parsed identically in sw.js and
    // agent.js, so the cases below are the contract all three share.
    [Theory]
    [InlineData("@v1/https://example.com/", "v1", null)]
    [InlineData("@v12.mobile/https://example.com/", "v12", "mobile")]
    [InlineData("@v3.tablet/https://example.com/", "v3", "tablet")]
    public void ViewToken_ReadsTheViewerAndItsDevice(string rest, string viewer, string? device)
    {
        var token = ViewToken.Match(rest);

        Assert.True(token.Success);
        Assert.Equal(viewer, token.Viewer);
        Assert.Equal(device, token.Device);
        Assert.Equal("https://example.com/", rest[token.Length..]);
    }

    [Theory]
    [InlineData("https://example.com/")]          // no token: the plain view-space form
    [InlineData("@v1.watch/https://example.com/")] // a device we do not emulate
    [InlineData("@/https://example.com/")]
    [InlineData("@v1https://example.com/")]
    public void ViewToken_LeavesAnythingElseAlone(string rest)
    {
        var token = ViewToken.Match(rest);

        Assert.False(token.Success);
        Assert.Equal(0, token.Length);
    }

    [Fact]
    public void FixProtocol_RestoresTheSlashPathNormalizersDrop()
    {
        Assert.Equal("https://example.com/x", WebViewerRewriter.FixProtocol("https:/example.com/x"));
        Assert.Equal("https://example.com/x", WebViewerRewriter.FixProtocol("https://example.com/x"));
    }
}
