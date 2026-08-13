// WebViewer proxy endpoints for the samples host.
//
// Ported from D:\Temp\WebViewer2\backend\Program.cs (the standalone app), trimmed to the
// endpoints the WebViewer widget + its service worker depend on. The Vite reverse-proxy
// is dropped — the Ivy server serves the parent app. Wire it up from Program.cs:
//
//     server.ReservePaths("/__proxy", "/__view", "/__capture", "/__captures", "/__lib", "/sw.js");
//     server.UseWebApplication(app => app.MapWebViewerProxy(AppContext.BaseDirectory));
//
// Endpoints:
//   /__proxy?url=<abs>&dev=<mobile|tablet>   fetch + rewrite a page/asset (used by the SW)
//   /__view/<abs>                            same, but target carried in the path (bootstrap)
//   /__lib/html-to-image.js                  the screenshot library (loaded inside the iframe)
//   POST /__capture                          save a screenshot PNG to temp
//   /__captures/<file>                       serve a saved screenshot
//   /sw.js                                   the service worker

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace WidgetSamples;

public static class WebViewerProxy
{
    private const string ViewPrefix = "/__view/";

    private sealed record Device(string Ua, string Platform, string Mobile, string ChPlatform);

    public static WebApplication MapWebViewerProxy(this WebApplication app, string assetRoot)
    {
        var assetDir = Path.Combine(assetRoot, "proxy-assets");
        var captureDir = Path.Combine(Path.GetTempPath(), "webviewer-captures");
        var agentTemplate = File.ReadAllText(Path.Combine(assetDir, "agent.js"));
        var swPath = Path.Combine(assetDir, "sw.js");

        // Upstream client: follow redirects + decompress (we re-emit a clean body).
        var upstream = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
        });

        var devices = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase)
        {
            ["mobile"] = new(
                "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1",
                "iPhone", "?1", "\"iOS\""),
            ["tablet"] = new(
                "Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1",
                "iPad", "?0", "\"iOS\""),
        };

        var allMethods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };

        // ---- endpoints --------------------------------------------------------
        app.MapMethods("/__proxy", allMethods, async (HttpContext ctx) =>
        {
            var target = ctx.Request.Query["url"].ToString();
            var device = devices.GetValueOrDefault(ctx.Request.Query["dev"].ToString());
            await HandleProxy(ctx, target, device);
        });

        // Bootstrap path: target carried in the path (used before the SW controls the iframe).
        app.MapMethods("/__view/{**rest}", allMethods, async (HttpContext ctx) =>
        {
            var path = ctx.Request.Path.Value ?? "";
            var raw = path.Length > ViewPrefix.Length ? path[ViewPrefix.Length..] : "";
            var target = FixProto(raw) + ctx.Request.QueryString;
            var device = devices.GetValueOrDefault(ctx.Request.Query["dev"].ToString());
            await HandleProxy(ctx, target, device);
        });

        app.MapGet("/__lib/{file}", async (HttpContext ctx, string file) =>
        {
            var allowed = file is "snapdom.mjs";
            var path = Path.Combine(assetDir, file);
            if (!allowed || !File.Exists(path)) { await Text(ctx, 404, "Not found"); return; }
            ctx.Response.Headers["Content-Type"] = "application/javascript";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            await ctx.Response.SendFileAsync(path);
        });

        app.MapPost("/__capture", HandleCapture);

        app.MapGet("/__captures/{file}", async (HttpContext ctx, string file) =>
        {
            var abs = Path.Combine(captureDir, Path.GetFileName(file));
            if (!abs.StartsWith(captureDir, StringComparison.Ordinal) || !File.Exists(abs))
            { await Text(ctx, 404, "Not found"); return; }
            ctx.Response.Headers["Content-Type"] = "image/png";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            await ctx.Response.SendFileAsync(abs);
        });

        app.MapGet("/sw.js", async (HttpContext ctx) =>
        {
            if (!File.Exists(swPath)) { await Text(ctx, 404, "sw.js not found"); return; }
            ctx.Response.Headers["Content-Type"] = "application/javascript";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            // Allow the SW to control the root scope even though it's served from /sw.js.
            ctx.Response.Headers["Service-Worker-Allowed"] = "/";
            await ctx.Response.SendFileAsync(swPath);
        });

        return app;

        // ===================================================================
        // Handlers (local functions capture the per-host state above).

        async Task HandleProxy(HttpContext ctx, string target, Device? device)
        {
            if (string.IsNullOrEmpty(target)) { await Text(ctx, 400, "Missing url parameter"); return; }
            if (!Uri.TryCreate(target, UriKind.Absolute, out var targetUri) ||
                (targetUri.Scheme != "http" && targetUri.Scheme != "https"))
            { await Text(ctx, 400, "Invalid url parameter"); return; }

            var method = new HttpMethod(ctx.Request.Method);
            using var req = new HttpRequestMessage(method, targetUri);
            if (method != HttpMethod.Get && method != HttpMethod.Head)
            {
                using var ms = new MemoryStream();
                await ctx.Request.Body.CopyToAsync(ms);
                req.Content = new ByteArrayContent(ms.ToArray());
            }
            var ua = device?.Ua ?? Header(ctx, "User-Agent") ?? "Mozilla/5.0";
            req.Headers.TryAddWithoutValidation("User-Agent", ua);
            req.Headers.TryAddWithoutValidation("Accept", Header(ctx, "Accept") ?? "*/*");
            req.Headers.TryAddWithoutValidation("Accept-Language", Header(ctx, "Accept-Language") ?? "en-US,en;q=0.9");
            if (device != null)
            {
                req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", device.Mobile);
                req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", device.ChPlatform);
            }

            HttpResponseMessage res;
            try { res = await upstream.SendAsync(req, HttpCompletionOption.ResponseHeadersRead); }
            catch (Exception e) { await Text(ctx, 502, "Proxy fetch failed: " + e.Message); return; }

            using (res)
            {
                var finalUrl = res.RequestMessage?.RequestUri?.ToString() ?? target;
                var contentType = res.Content.Headers.ContentType?.ToString() ?? "";

                ctx.Response.StatusCode = (int)res.StatusCode;
                ctx.Response.Headers["Content-Type"] = string.IsNullOrEmpty(contentType)
                    ? "application/octet-stream" : contentType;
                var cacheControl = res.Headers.CacheControl?.ToString();
                if (!string.IsNullOrEmpty(cacheControl)) ctx.Response.Headers["Cache-Control"] = cacheControl;

                // Side-channel real upstream status + headers for the SW's HAR log.
                var upstreamHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in res.Headers) upstreamHeaders[h.Key] = string.Join(", ", h.Value);
                foreach (var h in res.Content.Headers) upstreamHeaders[h.Key] = string.Join(", ", h.Value);
                var meta = JsonSerializer.Serialize(new
                {
                    status = (int)res.StatusCode,
                    statusText = res.ReasonPhrase ?? "",
                    url = finalUrl,
                    headers = upstreamHeaders,
                });
                ctx.Response.Headers["x-proxy-meta"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(meta));

                // Status codes that must not carry a body (304 Not Modified is common when
                // the SW revalidates cached resources during a screenshot; 204/1xx too).
                // Writing one throws InvalidOperationException and faults the connection.
                var status = (int)res.StatusCode;
                if (status is 204 or 304 || (status >= 100 && status < 200))
                {
                    ctx.Response.Headers.Remove("Content-Type");
                    return;
                }

                if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    var html = await res.Content.ReadAsStringAsync();
                    await ctx.Response.WriteAsync(RewriteHtml(html, finalUrl, device));
                }
                else if (contentType.Contains("text/css", StringComparison.OrdinalIgnoreCase))
                {
                    var css = await res.Content.ReadAsStringAsync();
                    await ctx.Response.WriteAsync(RewriteCss(css, new Uri(finalUrl)));
                }
                else
                {
                    var bytes = await res.Content.ReadAsByteArrayAsync();
                    await ctx.Response.Body.WriteAsync(bytes);
                }
            }
        }

        async Task HandleCapture(HttpContext ctx)
        {
            string body;
            using (var sr = new StreamReader(ctx.Request.Body)) body = await sr.ReadToEndAsync();
            string? dataUrl = null, name = "capture";
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                if (doc.RootElement.TryGetProperty("dataUrl", out var d)) dataUrl = d.GetString();
                if (doc.RootElement.TryGetProperty("name", out var n)) name = n.GetString() ?? "capture";
            }
            catch { await Text(ctx, 400, "Bad JSON"); return; }

            var m = Regex.Match(dataUrl ?? "", @"^data:image/png;base64,(.+)$", RegexOptions.Singleline);
            if (!m.Success) { await Text(ctx, 400, "Expected a PNG data URL"); return; }

            Directory.CreateDirectory(captureDir);
            var safe = Regex.Replace(name ?? "capture", "[^a-zA-Z0-9_-]+", "_");
            if (safe.Length > 40) safe = safe[..40];
            var fileName = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{safe}.png";
            var abs = Path.Combine(captureDir, fileName);
            try { await File.WriteAllBytesAsync(abs, Convert.FromBase64String(m.Groups[1].Value)); }
            catch (Exception e) { await Text(ctx, 500, "Write failed: " + e.Message); return; }

            ctx.Response.Headers["Content-Type"] = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                url = $"/__captures/{fileName}",
                path = abs,
                file = fileName,
            }));
        }

        // ---- URL/HTML/CSS rewriting (ported from proxy.js) ----------------
        string RewriteHtml(string html, string finalUrl, Device? device)
        {
            var baseUri = new Uri(finalUrl);
            var htmlAttr = RegexOptions.IgnoreCase | RegexOptions.Singleline;

            var outp = Regex.Replace(html,
                @"\b(href|src|action|poster|data-src|data-href)\s*=\s*([""'])(.*?)\2",
                m => $"{m.Groups[1].Value}={m.Groups[2].Value}{RewriteUrl(m.Groups[3].Value, baseUri)}{m.Groups[2].Value}",
                htmlAttr);
            outp = Regex.Replace(outp, @"\bsrcset\s*=\s*([""'])(.*?)\1",
                m => $"srcset={m.Groups[1].Value}{RewriteSrcset(m.Groups[2].Value, baseUri)}{m.Groups[1].Value}",
                htmlAttr);
            outp = Regex.Replace(outp, @"(<style\b[^>]*>)([\s\S]*?)(</style>)",
                m => $"{m.Groups[1].Value}{RewriteCss(m.Groups[2].Value, baseUri)}{m.Groups[3].Value}",
                RegexOptions.IgnoreCase);
            outp = Regex.Replace(outp, @"\bstyle\s*=\s*([""'])(.*?)\1",
                m => $"style={m.Groups[1].Value}{RewriteCss(m.Groups[2].Value, baseUri)}{m.Groups[1].Value}",
                htmlAttr);

            var head = $"<base href=\"{BaseHref(finalUrl)}\">{AgentScript(finalUrl, device)}";
            if (Regex.IsMatch(outp, "<head[^>]*>", RegexOptions.IgnoreCase))
                outp = Regex.Replace(outp, "<head[^>]*>", m => m.Value + head, RegexOptions.IgnoreCase);
            else
                outp = head + outp;
            return outp;
        }

        string AgentScript(string realUrl, Device? device)
        {
            var json = JsonSerializer.Serialize(realUrl);
            var dev = device == null
                ? "null"
                : JsonSerializer.Serialize(new { ua = device.Ua, platform = device.Platform, mobile = device.Mobile == "?1" });
            return agentTemplate.Replace("@@REAL_URL@@", json).Replace("@@DEVICE@@", dev);
        }
    }

    // ---- static helpers -------------------------------------------------------
    private static string FixProto(string s) => Regex.Replace(s, "^(https?:)/(?!/)", "$1//");

    private static string ToViewPath(string absoluteUrl) => ViewPrefix + absoluteUrl;

    private static string RewriteUrl(string value, Uri baseUri)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var v = value.Trim();
        if (Regex.IsMatch(v, "^(data:|blob:|javascript:|mailto:|tel:|about:|#)", RegexOptions.IgnoreCase))
            return value;
        if (v.StartsWith(ViewPrefix, StringComparison.Ordinal)) return value;
        try
        {
            var abs = new Uri(baseUri, v);
            if (abs.Scheme != "http" && abs.Scheme != "https") return value;
            return ToViewPath(abs.AbsoluteUri);
        }
        catch { return value; }
    }

    private static string RewriteSrcset(string value, Uri baseUri)
    {
        return string.Join(", ", value.Split(',').Select(part =>
        {
            var seg = part.Trim();
            if (seg.Length == 0) return seg;
            var bits = seg.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            bits[0] = RewriteUrl(bits[0], baseUri);
            return string.Join(" ", bits);
        }));
    }

    private static string RewriteCss(string css, Uri baseUri) =>
        Regex.Replace(css, @"url\(\s*(['""]?)([^'""\)]+)\1\s*\)", m =>
            $"url({m.Groups[1].Value}{RewriteUrl(m.Groups[2].Value, baseUri)}{m.Groups[1].Value})",
            RegexOptions.IgnoreCase);

    private static string BaseHref(string finalUrl)
    {
        try
        {
            var u = new Uri(finalUrl);
            var p = u.AbsolutePath;
            var idx = p.LastIndexOf('/');
            var dir = idx >= 0 ? p[..(idx + 1)] : "/";
            if (dir.Length == 0) dir = "/";
            return ViewPrefix + u.GetLeftPart(UriPartial.Authority) + dir;
        }
        catch { return ViewPrefix; }
    }

    private static string? Header(HttpContext ctx, string name) =>
        ctx.Request.Headers.TryGetValue(name, out var v) ? v.ToString() : null;

    private static async Task Text(HttpContext ctx, int status, string body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/plain";
        await ctx.Response.WriteAsync(body);
    }
}
