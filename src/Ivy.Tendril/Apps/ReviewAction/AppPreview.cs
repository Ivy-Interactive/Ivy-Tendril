using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ivy.Tendril.Apps.ReviewAction;

/// <summary>
///     One comment a reviewer left on an element of the running app, as the WebViewer widget
///     reported it. <paramref name="Number"/> is the number on its pin — a position, so it
///     shifts when an earlier comment is deleted.
/// </summary>
public record AppComment(
    string Id,
    int Number,
    string Tag,
    string Selector,
    string Comment,
    string? DebugJson);

/// <summary>
///     The pieces of the review-action app preview that are worth testing on their own: finding
///     the app's URL in what it printed, deciding which hosts the proxy may fetch, and turning
///     the reviewer's comments into a change request an agent can act on.
/// </summary>
internal static partial class AppPreview
{
    /// <summary>
    ///     Find the URL a review action's process printed, or null while it has printed none.
    ///
    ///     Dev servers announce themselves ("Ivy is running on https://localhost:5011",
    ///     "Local: http://localhost:5173/"), which is the whole signal we have that the app is
    ///     up and where. Two rules keep us off the other URLs a build prints — documentation
    ///     links, package feeds, an exception's help URL: a loopback host wins outright, and
    ///     nothing without an explicit port is considered at all. A dev server always names a
    ///     port; https://aka.ms/some-error does not.
    /// </summary>
    public static string? DetectUrl(string transcript)
    {
        if (string.IsNullOrEmpty(transcript)) return null;

        string? portedFallback = null;
        foreach (Match match in UrlPattern().Matches(transcript))
        {
            // Terminal output wraps URLs in prose: "running on http://localhost:5173/." and
            // "(http://localhost:5173)" both need the tail taken off before parsing.
            var candidate = match.Value.TrimEnd('.', ',', ';', ':', ')', ']', '>', '"', '\'');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) continue;

            if (uri.IsLoopback) return candidate;
            // A dev server bound to a LAN address ("Network: http://192.168.1.9:5173/") is
            // still the app, but only worth taking if nothing local turns up later.
            if (!uri.IsDefaultPort) portedFallback ??= candidate;
        }

        return portedFallback;
    }

    /// <summary>
    ///     Which upstreams the WebViewer proxy may fetch, hosted on Tendril's own origin.
    ///
    ///     The proxy is an open relay unless it is given a predicate, and Tendril's origin is
    ///     reachable by anyone the user shares a tunnel with — so it is narrowed to what the
    ///     feature actually needs: an app running on this machine, or on the network the
    ///     machine is on. It does not make the viewer a general-purpose web browser.
    /// </summary>
    public static bool IsLocalTarget(Uri uri)
    {
        if (uri.IsLoopback) return true;
        if (uri.HostNameType != UriHostNameType.IPv4 && uri.HostNameType != UriHostNameType.IPv6) return false;
        if (!System.Net.IPAddress.TryParse(uri.Host, out var ip)) return false;

        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;

        var octets = ip.GetAddressBytes();
        return octets[0] switch
        {
            10 => true,                                     // 10.0.0.0/8
            127 => true,                                    // 127.0.0.0/8
            169 => octets[1] == 254,                        // 169.254.0.0/16, link-local
            172 => octets[1] >= 16 && octets[1] <= 31,      // 172.16.0.0/12
            192 => octets[1] == 168,                        // 192.168.0.0/16
            _ => false,
        };
    }

    /// <summary>
    ///     Turn the reviewer's comments into the change request an agent is handed.
    ///
    ///     Each one leads with where it points in the SOURCE when the widget managed to resolve
    ///     that (a click carries the bundle position, which the proxy resolves through the
    ///     source map), because a file and a line is the part an agent can act on directly. The
    ///     selector is kept as the fallback for a comment that resolved to nothing.
    /// </summary>
    public static string FormatChangeRequest(string url, IReadOnlyList<AppComment> comments)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Feedback from reviewing the running app at {url}");
        sb.AppendLine();

        foreach (var comment in comments)
        {
            var where = SourceLabel(comment.DebugJson);
            var tag = string.IsNullOrEmpty(comment.Tag) ? "element" : $"<{comment.Tag}>";

            sb.Append($"- **{comment.Number}. {tag}**");
            if (where is not null) sb.Append($" in `{where}`");
            sb.AppendLine(":");
            sb.AppendLine($"  {comment.Comment.Trim()}");
            if (!string.IsNullOrEmpty(comment.Selector)) sb.AppendLine($"  (selector: `{comment.Selector}`)");
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    ///     <c>src/components/SaveButton.tsx:42</c> out of the widget's attribution payload, or
    ///     null when nothing resolved — a production bundle with no source map, most often.
    /// </summary>
    public static string? SourceLabel(string? debugJson)
    {
        if (string.IsNullOrEmpty(debugJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(debugJson);
            if (!document.RootElement.TryGetProperty("source", out var source)) return null;
            if (source.ValueKind != JsonValueKind.Object) return null;
            if (source.TryGetProperty("file", out var file) is false) return null;
            if (file.GetString() is not { Length: > 0 } path) return null;

            return source.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Number
                ? $"{path}:{line.GetInt32()}"
                : path;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"https?://[^\s""'<>]+")]
    private static partial Regex UrlPattern();
}
