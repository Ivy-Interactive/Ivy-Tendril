using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Ivy.Tendril.Widgets;

/// <summary>Host-side configuration for <see cref="WebViewerProxy.MapWebViewerProxy"/>.</summary>
public sealed class WebViewerProxyOptions
{
    /// <summary>Where screenshots are written. Defaults to <c>%TEMP%/webviewer-captures</c>.</summary>
    public string? CaptureDirectory { get; init; }

    /// <summary>
    /// Gate on which upstream URLs may be fetched. The proxy is an open relay by default — any
    /// http(s) URL the caller names, private addresses included, which is what makes it useful
    /// for reviewing an app on localhost. Supply a predicate to narrow that.
    /// </summary>
    public Func<Uri, bool>? IsUrlAllowed { get; init; }

    /// <summary>Largest screenshot the /__capture endpoint will store. Defaults to 32 MB.</summary>
    public int MaxCaptureBytes { get; init; } = 32 * 1024 * 1024;
}

/// <summary>
/// Server half of the <see cref="WebViewer"/> widget: the endpoints that fetch, rewrite and
/// serve proxied content on the Ivy app's own origin, plus the service worker and page agent
/// the widget needs. The widget cannot work without these — host them from the same origin:
///
/// <code>
/// server.ReservePaths(WebViewerProxy.ReservedPaths);
/// server.UseWebApplication(app => app.MapWebViewerProxy());
/// </code>
///
/// Endpoints:
/// <list type="bullet">
/// <item><c>/__proxy?url=&lt;abs&gt;&amp;dev=&lt;mobile|tablet&gt;</c> — fetch + rewrite (used by the service worker)</item>
/// <item><c>/__view/&lt;abs&gt;</c> — the same, with the target carried in the path (bootstrap + navigations)</item>
/// <item><c>/__lib/snapdom.mjs</c> — the screenshot library, loaded inside the proxied page</item>
/// <item><c>POST /__capture</c> — store a screenshot; <c>/__captures/&lt;file&gt;</c> serves it back</item>
/// <item><c>/sw.js</c> — the service worker</item>
/// </list>
/// </summary>
public static class WebViewerProxy
{
    /// <summary>Paths the Ivy app must hand to this proxy rather than route as apps.</summary>
    public static readonly string[] ReservedPaths =
        ["/__proxy", "/__view", "/__capture", "/__captures", "/__lib", "/__resolve", "/sw.js"];

    private const string ViewPrefix = WebViewerRewriter.ViewPrefix;

    private static readonly string[] AllMethods =
        ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"];

    /// <summary>Frames past this are framework internals all the way down.</summary>
    private const int MaxResolveFrames = 24;

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private static readonly Regex PngDataUrl =
        new(@"^data:image/png;base64,(?<data>.+)$", RegexOptions.Singleline | RegexOptions.Compiled);

    private sealed record Device(string Ua, string Platform, bool Mobile, string ChPlatform);

    private static readonly Dictionary<string, Device> Devices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mobile"] = new(
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1",
            "iPhone", true, "\"iOS\""),
        ["tablet"] = new(
            "Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1",
            "iPad", false, "\"iOS\""),
    };

    // One client for the lifetime of the process: redirects followed and content decompressed,
    // because we re-emit a rewritten body rather than relaying the original bytes.
    private static readonly HttpClient Upstream = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = false,
    });

    // Assets are embedded in this assembly, so a consuming app needs no files on disk.
    // Shares the Upstream client, so a source map is fetched exactly like any other asset.
    private static readonly Lazy<SourceMapReader> SourceMaps = new(() => new SourceMapReader(Upstream));

    private static readonly Lazy<string> AgentTemplate = new(() => ReadTextAsset("agent.js"));
    private static readonly Lazy<byte[]> ServiceWorker = new(() => ReadBinaryAsset("sw.js"));
    private static readonly Lazy<byte[]> Snapdom = new(() => ReadBinaryAsset("snapdom.mjs"));

    /// <summary>Map every endpoint the WebViewer widget depends on.</summary>
    public static WebApplication MapWebViewerProxy(this WebApplication app, WebViewerProxyOptions? options = null)
    {
        var settings = options ?? new WebViewerProxyOptions();
        var captureDir = settings.CaptureDirectory
            ?? Path.Combine(Path.GetTempPath(), "webviewer-captures");

        app.MapMethods("/__proxy", AllMethods, (HttpContext ctx) =>
        {
            var target = ctx.Request.Query["url"].ToString();
            return HandleProxy(ctx, target, DeviceFor(ctx), settings);
        });

        // Bootstrap path: the target rides in the path, so the browser's own relative-URL
        // resolution keeps working before the service worker controls the iframe.
        app.MapMethods("/__view/{**rest}", AllMethods, (HttpContext ctx) =>
        {
            var path = ctx.Request.Path.Value ?? "";
            var raw = path.Length > ViewPrefix.Length ? path[ViewPrefix.Length..] : "";
            var target = WebViewerRewriter.FixProtocol(raw) + ctx.Request.QueryString;
            return HandleProxy(ctx, target, DeviceFor(ctx), settings);
        });

        app.MapGet("/__lib/{file}", async (HttpContext ctx, string file) =>
        {
            if (file != "snapdom.mjs") { await Text(ctx, 404, "Not found"); return; }
            await SendAsset(ctx, Snapdom.Value, "application/javascript");
        });

        app.MapPost("/__capture", (HttpContext ctx) => HandleCapture(ctx, captureDir, settings.MaxCaptureBytes));

        // Turns the raw JS frames the page agent collected into original file:line, with a
        // slice of the real source. Gated by the SAME allow-list as the proxy itself: it
        // fetches caller-named URLs, so without that it is a second, less obvious SSRF door.
        app.MapPost("/__resolve", (HttpContext ctx) => HandleResolve(ctx, settings));

        app.MapGet("/__captures/{file}", async (HttpContext ctx, string file) =>
        {
            var abs = Path.Combine(captureDir, Path.GetFileName(file));
            if (!File.Exists(abs)) { await Text(ctx, 404, "Not found"); return; }
            ctx.Response.Headers["Content-Type"] = "image/png";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            await ctx.Response.SendFileAsync(abs);
        });

        // No Service-Worker-Allowed header: the widget registers this at scope /__view/,
        // which is narrower than the script's own path, so it needs no scope widening — and
        // must not get any. A worker at "/" would see the whole host app's traffic.
        app.MapGet("/sw.js", (HttpContext ctx) =>
            SendAsset(ctx, ServiceWorker.Value, "application/javascript"));

        return app;
    }

    // ---- proxying -------------------------------------------------------------

    private static async Task HandleProxy(
        HttpContext ctx, string target, Device? device, WebViewerProxyOptions settings)
    {
        if (string.IsNullOrEmpty(target)) { await Text(ctx, 400, "Missing url parameter"); return; }
        if (!Uri.TryCreate(target, UriKind.Absolute, out var targetUri) ||
            (targetUri.Scheme != Uri.UriSchemeHttp && targetUri.Scheme != Uri.UriSchemeHttps))
        {
            await Text(ctx, 400, "Invalid url parameter");
            return;
        }
        if (settings.IsUrlAllowed is { } allowed && !allowed(targetUri))
        {
            await Text(ctx, 403, "Blocked by IsUrlAllowed");
            return;
        }

        using var req = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), targetUri);
        if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
        {
            using var buffer = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(buffer, ctx.RequestAborted);
            req.Content = new ByteArrayContent(buffer.ToArray());
        }

        req.Headers.TryAddWithoutValidation("User-Agent", device?.Ua ?? Header(ctx, "User-Agent") ?? "Mozilla/5.0");
        req.Headers.TryAddWithoutValidation("Accept", Header(ctx, "Accept") ?? "*/*");
        req.Headers.TryAddWithoutValidation("Accept-Language", Header(ctx, "Accept-Language") ?? "en-US,en;q=0.9");
        if (device is not null)
        {
            req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", device.Mobile ? "?1" : "?0");
            req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", device.ChPlatform);
        }

        HttpResponseMessage res;
        try
        {
            res = await Upstream.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
        }
        catch (OperationCanceledException) { return; }
        catch (HttpRequestException e) { await Text(ctx, 502, "Proxy fetch failed: " + e.Message); return; }

        using (res)
        {
            var finalUrl = res.RequestMessage?.RequestUri?.ToString() ?? target;
            var mediaType = res.Content.Headers.ContentType?.MediaType ?? "";
            var status = (int)res.StatusCode;

            ctx.Response.StatusCode = status;
            var cacheControl = res.Headers.CacheControl?.ToString();
            if (!string.IsNullOrEmpty(cacheControl)) ctx.Response.Headers["Cache-Control"] = cacheControl;

            // Side-channel the real upstream status and headers for the service worker's HAR log,
            // which cannot see them once we have re-emitted the response.
            ctx.Response.Headers["x-proxy-meta"] = ProxyMeta(res, finalUrl);

            // Bodiless statuses: 304 is routine when the service worker revalidates during a
            // screenshot. Writing a body for one faults the connection.
            if (status is 204 or 304 || status is >= 100 and < 200) return;

            var isHtml = mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
            var isCss = mediaType.Equals("text/css", StringComparison.OrdinalIgnoreCase);

            if (isHtml || isCss)
            {
                // We re-encode as UTF-8, so the declared charset has to say so — relaying the
                // upstream one verbatim turns a Latin-1 page into mojibake.
                ctx.Response.Headers["Content-Type"] = (isHtml ? "text/html" : "text/css") + "; charset=utf-8";

                var text = await res.Content.ReadAsStringAsync(ctx.RequestAborted);
                var rewritten = isHtml
                    ? WebViewerRewriter.RewriteHtml(text, finalUrl, AgentScript(finalUrl, device))
                    : WebViewerRewriter.RewriteCss(text, new Uri(finalUrl));
                await ctx.Response.WriteAsync(rewritten, ctx.RequestAborted);
                return;
            }

            ctx.Response.Headers["Content-Type"] = string.IsNullOrEmpty(mediaType)
                ? "application/octet-stream"
                : res.Content.Headers.ContentType!.ToString();
            await res.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
        }
    }

    // Headers the service worker's HAR log actually reads back. Relaying the upstream's full
    // header set base64'd into a single response header sounds harmless until a site ships a
    // multi-kilobyte CSP: the header balloons past what the browser will accept on a service
    // worker fetch, the fetch rejects, respondWith rejects, and the frame dies with a bare
    // network error while the very same URL fetches fine outside the worker.
    private static readonly string[] MetaHeaders = ["Content-Length", "Content-Type"];

    private static string ProxyMeta(HttpResponseMessage res, string finalUrl)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in MetaHeaders)
        {
            if (res.Content.Headers.TryGetValues(name, out var values))
            {
                headers[name] = string.Join(", ", values);
            }
        }

        var meta = JsonSerializer.Serialize(new
        {
            status = (int)res.StatusCode,
            statusText = res.ReasonPhrase ?? "",
            url = finalUrl,
            headers,
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(meta));
    }

    private static string AgentScript(string realUrl, Device? device)
    {
        var deviceJson = device is null
            ? "null"
            : JsonSerializer.Serialize(new { ua = device.Ua, platform = device.Platform, mobile = device.Mobile });

        return AgentTemplate.Value
            .Replace("@@REAL_URL@@", JsonSerializer.Serialize(realUrl))
            .Replace("@@DEVICE@@", deviceJson);
    }

    // ---- source attribution ---------------------------------------------------

    private sealed record ResolveRequest(List<StackFrame>? Frames);

    private static async Task HandleResolve(HttpContext ctx, WebViewerProxyOptions settings)
    {
        ResolveRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<ResolveRequest>(
                ctx.Request.Body, JsonWeb, ctx.RequestAborted);
        }
        catch (JsonException) { await Text(ctx, 400, "Bad JSON"); return; }

        var frames = request?.Frames ?? [];
        if (frames.Count == 0) { await WriteJson(ctx, new { frames = Array.Empty<object>() }); return; }

        var resolved = await SourceMaps.Value.ResolveAsync(
            frames.Take(MaxResolveFrames), settings.IsUrlAllowed, ctx.RequestAborted);

        // The top frames are framework internals; the first frame that is the app's own is
        // the answer, and the rest are ranked alternates for an ambiguous hit.
        var appFrames = resolved.Where(f => !f.IsThirdParty).ToList();
        var primary = appFrames.FirstOrDefault();

        await WriteJson(ctx, new
        {
            source = primary is null ? null : new { file = primary.File, line = primary.Line, col = primary.Col, name = primary.Name },
            codeFrame = primary?.CodeFrame,
            confidence = primary is null ? "none" : appFrames.Count == 1 ? "high" : "medium",
            candidates = appFrames.Skip(1).Take(5)
                .Select(f => new { file = f.File, line = f.Line, col = f.Col, name = f.Name }),
            frames = resolved.Select(f => new
            {
                file = f.File, line = f.Line, col = f.Col, name = f.Name, isThirdParty = f.IsThirdParty,
            }),
        });
    }

    // ---- captures -------------------------------------------------------------

    private static async Task HandleCapture(HttpContext ctx, string captureDir, int maxBytes)
    {
        string? dataUrl;
        var name = "capture";
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
            dataUrl = doc.RootElement.TryGetProperty("dataUrl", out var d) ? d.GetString() : null;
            if (doc.RootElement.TryGetProperty("name", out var n)) name = n.GetString() ?? "capture";
        }
        catch (JsonException) { await Text(ctx, 400, "Bad JSON"); return; }

        var match = PngDataUrl.Match(dataUrl ?? "");
        if (!match.Success) { await Text(ctx, 400, "Expected a PNG data URL"); return; }

        byte[] png;
        try { png = Convert.FromBase64String(match.Groups["data"].Value); }
        catch (FormatException) { await Text(ctx, 400, "Malformed base64 payload"); return; }
        if (png.Length > maxBytes) { await Text(ctx, 413, $"Capture exceeds {maxBytes} bytes"); return; }

        var safeName = Regex.Replace(name, "[^a-zA-Z0-9_-]+", "_");
        if (safeName.Length > 40) safeName = safeName[..40];
        var fileName = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{safeName}.png";
        var abs = Path.Combine(captureDir, fileName);

        try
        {
            Directory.CreateDirectory(captureDir);
            await File.WriteAllBytesAsync(abs, png, ctx.RequestAborted);
        }
        catch (IOException e) { await Text(ctx, 500, "Write failed: " + e.Message); return; }
        catch (UnauthorizedAccessException e) { await Text(ctx, 500, "Write failed: " + e.Message); return; }

        ctx.Response.Headers["Content-Type"] = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            url = $"/__captures/{fileName}",
            path = abs,
            file = fileName,
        }), ctx.RequestAborted);
    }

    // ---- helpers --------------------------------------------------------------

    private static Device? DeviceFor(HttpContext ctx) =>
        Devices.GetValueOrDefault(ctx.Request.Query["dev"].ToString());

    private static string? Header(HttpContext ctx, string name) =>
        ctx.Request.Headers.TryGetValue(name, out var value) ? value.ToString() : null;

    private static async Task SendAsset(HttpContext ctx, byte[] bytes, string contentType)
    {
        ctx.Response.Headers["Content-Type"] = contentType;
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        await ctx.Response.Body.WriteAsync(bytes, ctx.RequestAborted);
    }

    private static async Task WriteJson(HttpContext ctx, object payload)
    {
        ctx.Response.Headers["Content-Type"] = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonWeb), ctx.RequestAborted);
    }

    private static async Task Text(HttpContext ctx, int status, string body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/plain";
        await ctx.Response.WriteAsync(body, ctx.RequestAborted);
    }

    private static Stream OpenAsset(string name)
    {
        var resource = $"Ivy.Tendril.Widgets.proxy-assets.{name}";
        return typeof(WebViewerProxy).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded WebViewer proxy asset '{resource}' is missing.");
    }

    private static string ReadTextAsset(string name)
    {
        using var stream = OpenAsset(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBinaryAsset(string name)
    {
        using var stream = OpenAsset(name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
