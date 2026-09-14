using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Project;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ivy.Tendril.Wireframe.Hosting;

/// <summary>One wireframe as the endpoints serve it: its source, its last build and its reload hub.</summary>
public sealed record WireframeSite(WireframeProject Project, string OutDir, LiveReloadHub? Hub, bool LiveReload)
{
    /// <summary>
    /// A Tailwind-generated utility sheet for <c>--tailwind jit</c> projects, served in place of the
    /// embedded superset. Null for the default mode.
    /// </summary>
    public string? UtilityCssPath { get; init; }
}

/// <summary>What the preview frame is told about a wireframe before it loads it.</summary>
public sealed record WireframeStatus(string Phase, string? Message = null)
{
    public static readonly WireframeStatus Missing = new("missing");
    public static readonly WireframeStatus Building = new("building");
    public static readonly WireframeStatus Running = new("running");
    public static WireframeStatus Failed(string? message) => new("failed", message);
}

/// <summary>
/// Where the endpoints get their wireframe from. The standalone <c>serve</c> has exactly one;
/// Tendril's <see cref="WireframeHost"/> picks one per request from the plan in the URL.
/// </summary>
public interface IWireframeSiteSource
{
    /// <summary>A page is about to load: make sure the wireframe is built and being watched.</summary>
    ValueTask<WireframeSite?> OpenAsync(HttpContext context);

    /// <summary>A file the loaded page asks for: whatever was built last, without starting anything.</summary>
    WireframeSite? Find(HttpContext context);

    /// <summary>A live-reload socket. Returns when the socket closes.</summary>
    Task AcceptSocketAsync(HttpContext context, WebSocket socket);

    ValueTask<WireframeStatus> StatusAsync(HttpContext context);

    void ReportPageError(HttpContext context, string kind, string detail);
}

/// <summary>
/// The wireframe dev server's routes, shared by the standalone <c>tendril wireframe serve</c>
/// (mounted at the root of its own Kestrel) and Tendril's plan previews (mounted under
/// <see cref="WireframeHost.RoutePattern"/> on Tendril's own origin).
///
/// The embedded payload (vendor bundle, stylesheets, fonts) is identical for every wireframe,
/// so it stays at one absolute prefix. Only what belongs to one wireframe (its page, its bundle,
/// its public files, its reload socket) lives under that wireframe's base.
/// </summary>
public static class WireframeEndpoints
{
    public const string PayloadPrefix = "/__wireframe";

    /// <summary>Paths an Ivy host must keep out of its own router.</summary>
    public static readonly string[] ReservedPaths = [PayloadPrefix, WireframeHost.RoutePrefix];

    /// <summary>Serves vendor JS, stylesheets and fonts from the embedded payload.</summary>
    public static IEndpointRouteBuilder MapWireframePayload(this IEndpointRouteBuilder routes, AssetCatalog assets)
    {
        foreach (var area in new[] { "vendor", "css", "fonts" })
        {
            var captured = area;
            routes.MapGet($"{PayloadPrefix}/{captured}/{{**path}}", async ctx =>
            {
                NoStore(ctx);
                var rel = (string?)ctx.Request.RouteValues["path"] ?? "";
                if (!assets.TryRead($"{captured}/{rel}", out var bytes))
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                ctx.Response.ContentType = ContentTypeFor(rel);

                // fonts.css names its files by absolute URL. Under a path base those need the
                // prefix too, or every @font-face 404s and the handwriting font silently falls back.
                var pathBase = ctx.Request.PathBase.Value;
                if (captured == "css" && !string.IsNullOrEmpty(pathBase))
                {
                    var css = Encoding.UTF8.GetString(bytes)
                        .Replace($"url(\"{PayloadPrefix}/", $"url(\"{pathBase}{PayloadPrefix}/", StringComparison.Ordinal);
                    await ctx.Response.WriteAsync(css);
                    return;
                }

                await ctx.Response.Body.WriteAsync(bytes);
            });
        }

        return routes;
    }

    /// <summary>
    /// Serves one wireframe under <paramref name="prefix"/>: "" for the standalone server, or a
    /// route pattern such as <c>/__wireframes/{scope}/{name}</c> for plan previews.
    /// </summary>
    public static IEndpointRouteBuilder MapWireframeSite(
        this IEndpointRouteBuilder routes, string prefix, AssetCatalog assets, IWireframeSiteSource source)
    {
        var index = new IndexHtmlBuilder(VendorManifest.Load(assets));
        var group = routes.MapGroup(prefix);

        group.MapGet($"{PayloadPrefix}/client.js", async ctx =>
        {
            NoStore(ctx);
            ctx.Response.ContentType = "text/javascript; charset=utf-8";
            await ctx.Response.WriteAsync(LiveReloadClient.Source);
        });

        group.MapPost($"{PayloadPrefix}/report", async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            try
            {
                using var doc = JsonDocument.Parse(body);
                source.ReportPageError(ctx,
                    doc.RootElement.GetProperty("kind").GetString() ?? "error",
                    doc.RootElement.GetProperty("detail").GetString() ?? "");
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                // A malformed beacon is not worth failing the request over.
            }
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        group.MapGet($"{PayloadPrefix}/status", async ctx =>
        {
            NoStore(ctx);
            await ctx.Response.WriteAsJsonAsync(await source.StatusAsync(ctx));
        });

        group.Map($"{PayloadPrefix}/hmr", async ctx =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
            await source.AcceptSocketAsync(ctx, socket);
        });

        group.MapGet($"{PayloadPrefix}/utilities.css", async ctx =>
        {
            NoStore(ctx);
            if (source.Find(ctx)?.UtilityCssPath is { } generated && File.Exists(generated))
            {
                ctx.Response.ContentType = "text/css; charset=utf-8";
                await ctx.Response.SendFileAsync(generated);
                return;
            }
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        });

        // Freshly-built bundle (on disk, outside the project).
        group.MapGet($"{PayloadPrefix}/out/{{**path}}", async ctx =>
        {
            NoStore(ctx);
            var site = source.Find(ctx);
            if (site is null || !await TryServeFileAsync(ctx, site.OutDir, (string?)ctx.Request.RouteValues["path"] ?? ""))
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        });

        // The wireframe's public/ directory, then its page.
        group.MapGet("/{**path}", async ctx =>
        {
            NoStore(ctx);
            var rel = (string?)ctx.Request.RouteValues["path"] ?? "";

            if (rel.Length > 0)
            {
                var site = source.Find(ctx);
                if (site is not null && await TryServeFileAsync(ctx, site.Project.PublicDir, rel))
                    return;

                // A request WITH an extension that we could not satisfy is a genuine 404.
                // Returning index.html here is the classic SPA-server bug: the browser then
                // reports "Failed to load module script: MIME type text/html".
                if (Path.HasExtension(rel))
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }

            var requestPath = ctx.Request.Path.Value ?? "/";

            // The page's own relative URLs (an <img src="logo.png">) resolve against its address,
            // so the base must end in a slash or they land one level up.
            if (rel.Length == 0 && !requestPath.EndsWith('/'))
            {
                ctx.Response.Redirect($"{ctx.Request.PathBase}{requestPath}/{ctx.Request.QueryString}");
                return;
            }

            var opened = await source.OpenAsync(ctx);
            if (opened is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                await ctx.Response.WriteAsync("There is no wireframe at this address.");
                return;
            }

            var siteBase = requestPath.EndsWith(rel, StringComparison.Ordinal)
                ? requestPath[..^rel.Length]
                : requestPath[..(requestPath.LastIndexOf('/') + 1)];
            if (!siteBase.EndsWith('/')) siteBase += "/";

            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(index.Build(
                opened,
                siteBase: $"{ctx.Request.PathBase}{siteBase}",
                payloadBase: $"{ctx.Request.PathBase}{PayloadPrefix}/"));
        });

        return routes;
    }

    /// <summary>Dev server: never cache anything.</summary>
    private static void NoStore(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        ctx.Response.Headers.Pragma = "no-cache";
    }

    internal static async Task<bool> TryServeFileAsync(HttpContext ctx, string root, string relative)
    {
        if (string.IsNullOrEmpty(relative)) return false;

        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

        // Containment check: a crafted path must not escape the served root.
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return false;
        if (!File.Exists(full)) return false;

        ctx.Response.ContentType = ContentTypeFor(full);
        await ctx.Response.SendFileAsync(full);
        return true;
    }

    internal static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".html" => "text/html; charset=utf-8",
            ".json" or ".map" => "application/json; charset=utf-8",
            ".woff2" => "font/woff2",
            ".woff" => "font/woff",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
}
