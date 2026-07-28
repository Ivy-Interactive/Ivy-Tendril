using System.Text.RegularExpressions;

namespace Ivy.Tendril.Plugin.Linear;

/// <summary>
/// Recognizes Linear issue URLs and extracts their identifier. Registered with Tendril via
/// ISourceLinks so Tendril can label a plan's source (e.g. "IVY-456" in a PR description)
/// without knowing anything about Linear's URL format.
/// </summary>
internal static class LinearSourceUrl
{
    // https://linear.app/{workspace}/issue/{IVY-456}/{slug}  — slug is optional.
    private static readonly Regex IssuePath = new(
        @"^/[^/]+/issue/(?<id>[A-Za-z0-9]+-\d+)(/|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static string? GetIdentifier(Uri url)
    {
        if (!url.Host.Equals("linear.app", StringComparison.OrdinalIgnoreCase)
            && !url.Host.EndsWith(".linear.app", StringComparison.OrdinalIgnoreCase))
            return null;

        var match = IssuePath.Match(url.AbsolutePath);
        return match.Success ? match.Groups["id"].Value.ToUpperInvariant() : null;
    }
}
