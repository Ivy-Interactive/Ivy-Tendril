using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Tunnel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Controllers;

public class LocalFileGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfigService _configService;
    private readonly IShareTunnelService _tunnelService;
    private readonly ILogger<LocalFileGuardMiddleware> _logger;
    private volatile List<string>? _cachedRoots;

    public LocalFileGuardMiddleware(
        RequestDelegate next,
        IConfigService configService,
        IShareTunnelService tunnelService,
        ILogger<LocalFileGuardMiddleware> logger)
    {
        _next = next;
        _configService = configService;
        _tunnelService = tunnelService;
        _logger = logger;
        _configService.SettingsReloaded += (_, _) => _cachedRoots = null;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/ivy/local-file"))
        {
            await _next(context);
            return;
        }

        // 1. Host validation
        var host = context.Request.Host.Host;
        if (!IsAllowedHost(host))
        {
            _logger.LogWarning("LocalFileGuard: Rejected request from disallowed host: {Host}, Path: {Path}",
                host, context.Request.Query["path"]);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "Access denied: invalid host" });
            return;
        }

        // 2. Origin validation
        if (context.Request.Headers.TryGetValue("Origin", out var originHeader))
        {
            var originString = originHeader.ToString();
            if (Uri.TryCreate(originString, UriKind.Absolute, out var originUri))
            {
                if (!string.Equals(originUri.Host, host, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "LocalFileGuard: Rejected cross-origin request from {Origin} to {Host}, Path: {Path}",
                        originString, host, context.Request.Query["path"]);
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new { error = "Access denied: cross-origin request" });
                    return;
                }
            }
        }

        // 3. Fetch metadata validation
        if (context.Request.Headers.TryGetValue("Sec-Fetch-Site", out var fetchSite))
        {
            var fetchSiteValue = fetchSite.ToString();
            if (string.Equals(fetchSiteValue, "cross-site", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "LocalFileGuard: Rejected cross-site fetch from {Host}, Path: {Path}",
                    host, context.Request.Query["path"]);
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { error = "Access denied: cross-site request" });
                return;
            }
        }

        // 4. File type validation
        var path = context.Request.Query["path"].ToString();
        if (!string.IsNullOrEmpty(path))
        {
            var fullPath = Path.GetFullPath(path);
            var extension = Path.GetExtension(fullPath);

            if (!IsAllowedFileType(extension))
            {
                _logger.LogWarning(
                    "LocalFileGuard: Rejected request for disallowed file type {Extension}, Path: {Path}",
                    extension, fullPath);
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new { error = "File not found" });
                return;
            }

            // 4.5 Root confinement: the resolved path must fall inside a configured root.
            var roots = GetRoots();
            if (!LocalFileRootPolicy.TryResolve(path, roots, out _))
            {
                _logger.LogWarning(
                    "LocalFileGuard: Rejected request for path outside configured roots, Path: {Path}, RootCount: {RootCount}",
                    fullPath, roots.Count);
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new { error = "File not found" });
                return;
            }
        }

        // 5. Response hardening headers
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";

        await _next(context);
    }

    private List<string> GetRoots()
    {
        return _cachedRoots ??= LocalFileRootPolicy.ComputeRoots(_configService);
    }

    private bool IsAllowedHost(string host)
    {
        // Loopback addresses
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("[::1]", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("127.", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("[::1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Active tunnel host
        if (_tunnelService.IsConnected && _tunnelService.TunnelUrl != null)
        {
            if (Uri.TryCreate(_tunnelService.TunnelUrl, UriKind.Absolute, out var tunnelUri))
            {
                if (string.Equals(host, tunnelUri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // Private IPv4 ranges (10.*, 172.16-31.*, 192.168.*)
        if (IsPrivateIpv4(host))
        {
            return true;
        }

        // .local mDNS names
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Configured allowed hosts
        var allowedHosts = _configService.Settings.Security?.AllowedHosts;
        if (allowedHosts != null && allowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsPrivateIpv4(string host)
    {
        if (System.Net.IPAddress.TryParse(host, out var ipAddress))
        {
            if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = ipAddress.GetAddressBytes();
                // 10.0.0.0/8
                if (bytes[0] == 10)
                    return true;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168)
                    return true;
            }
        }
        return false;
    }

    private static bool IsAllowedFileType(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        // Images (using FileHelper.IsImageExtension covers .png .jpg .jpeg .gif .bmp .svg .webp .ico)
        if (FileHelper.IsImageExtension(extension))
            return true;

        // Additional extensions
        if (extension.Equals(".avif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
