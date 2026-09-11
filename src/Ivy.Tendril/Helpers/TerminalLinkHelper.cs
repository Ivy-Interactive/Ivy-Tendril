using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     Unified helper for opening links clicked in terminal output (e.g. review actions or agent PTY sessions).
///     Safely delegates valid web URLs (http/https) to the default browser, local file paths to the system
///     file viewer, and internal plan:// URIs to the plan navigation callback.
/// </summary>
public static class TerminalLinkHelper
{
    public static Action<string>? BrowserLauncher { get; set; }
    public static Action<string>? FileLauncher { get; set; }
    public static Action<int>? DefaultPlanHandler { get; set; }

    /// <summary>
    ///     Opens the terminal link, suppressing any exceptions to ensure UI stability.
    /// </summary>
    public static void OpenTerminalLink(string? url)
    {
        TryOpenTerminalLink(url, null, null, null, null);
    }

    /// <summary>
    ///     Opens the terminal link with a plan navigation callback, suppressing any exceptions.
    /// </summary>
    public static void OpenTerminalLink(string? url, Action<int>? onPlanClick)
    {
        TryOpenTerminalLink(url, onPlanClick, null, null, null);
    }

    /// <summary>
    ///     Validates and attempts to open a terminal link.
    ///     Returns true if the link was recognized and launched; otherwise false.
    /// </summary>
    public static bool TryOpenTerminalLink(
        string? url,
        Action<int>? onPlanClick = null,
        Action<string>? browserLauncher = null,
        Action<string>? fileLauncher = null,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        try
        {
            if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                var launcher = browserLauncher ?? BrowserLauncher ?? DefaultLaunchBrowser;
                launcher(trimmed);
                return true;
            }

            if (uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                var filePath = PathHelper.ExtractPathFromFileUri(trimmed) ?? (uri.IsFile ? uri.LocalPath : null);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return false;
                }

                if (!File.Exists(filePath) && !Directory.Exists(filePath))
                {
                    logger?.LogWarning("Target file or directory not found for file URI: {FilePath}", filePath);
                    return false;
                }

                var launcher = fileLauncher ?? FileLauncher ?? DefaultLaunchFile;
                launcher(filePath);
                return true;
            }

            if (uri.Scheme.Equals("plan", StringComparison.OrdinalIgnoreCase))
            {
                var planIdStr = trimmed["plan://".Length..].TrimEnd('/');
                var colonIndex = planIdStr.IndexOf(':');
                if (colonIndex >= 0)
                {
                    planIdStr = planIdStr[..colonIndex];
                }

                var hashIndex = planIdStr.IndexOf('#');
                if (hashIndex >= 0)
                {
                    planIdStr = planIdStr[..hashIndex];
                }

                if (int.TryParse(planIdStr, out var planId))
                {
                    var handler = onPlanClick ?? DefaultPlanHandler;
                    if (handler != null)
                    {
                        handler(planId);
                        return true;
                    }
                }

                return false;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to open terminal link: {Url}", url);
            return false;
        }
    }

    private static void DefaultLaunchBrowser(string targetUrl)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = targetUrl,
            UseShellExecute = true
        });
    }

    private static void DefaultLaunchFile(string filePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true
        });
    }
}
