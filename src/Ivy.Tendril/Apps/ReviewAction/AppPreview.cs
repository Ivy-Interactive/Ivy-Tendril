using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.ReviewAction;

/// <summary>
///     One comment a reviewer left on an element of the running app, as the WebViewer widget
///     reported it. <paramref name="Number"/> is the number on its pin — a position, so it
///     shifts when an earlier comment is deleted. <paramref name="Url"/> is the page it was
///     left on, already canonical: the widget guarantees one string per page, so grouping by it
///     is plain equality.
/// </summary>
public record AppComment(
    string Id,
    int Number,
    string Tag,
    string Selector,
    string Comment,
    string? DebugJson,
    string? Url = null,
    string? Text = null,
    string? AttrsJson = null,
    string? Device = null);

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
    ///     The public hosts an app under review may pull static assets from.
    ///
    ///     A local app is not a self-contained one: it links its fonts, its icon set and often a
    ///     library or two from a CDN. With only <see cref="IsLocalTarget"/> in force those are
    ///     refused, and the page renders in fallback fonts with its icons missing — which is not
    ///     the app the reviewer was asked to review.
    ///
    ///     A fixed list rather than "anything, so long as it is a subresource": the proxy fetches
    ///     as this machine, so every host added here is one that anybody holding the tunnel link
    ///     can make it read. These serve versioned static files to whoever asks, so relaying them
    ///     gives up nothing that was not already public. HTTPS is required — there is no reason
    ///     for one of these to be addressed over http, and insisting costs nothing.
    /// </summary>
    private static readonly HashSet<string> AssetHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        // Fonts
        "fonts.googleapis.com",
        "fonts.gstatic.com",
        "use.typekit.net",
        "p.typekit.net",
        // Script and stylesheet CDNs
        "cdn.jsdelivr.net",
        "fastly.jsdelivr.net",
        "unpkg.com",
        "cdnjs.cloudflare.com",
        "ajax.googleapis.com",
        "code.jquery.com",
        "esm.sh",
        "cdn.skypack.dev",
        "cdn.tailwindcss.com",
        // Icons
        "kit.fontawesome.com",
        "ka-f.fontawesome.com",
    };

    /// <summary>Whether the proxy may fetch <paramref name="uri"/> at all: the app under review
    /// (<see cref="IsLocalTarget"/>), or one of the asset hosts it links.</summary>
    public static bool IsAllowedTarget(Uri uri) =>
        IsLocalTarget(uri) || (uri.Scheme == Uri.UriSchemeHttps && AssetHosts.Contains(uri.Host));

    /// <summary>
    ///     A job that has not finished, so anything started now has to queue behind it.
    ///     <see cref="JobStatus.Blocked"/> counts: it is a job already waiting its turn, and the
    ///     next request belongs after it rather than beside it — which is what makes repeated
    ///     Update presses form a chain instead of a pile-up.
    /// </summary>
    private static bool IsUnfinished(JobStatus status) =>
        status is JobStatus.Pending or JobStatus.Queued or JobStatus.Running or JobStatus.Blocked;

    /// <summary>
    ///     The jobs a new change request has to wait for: everything unfinished on this plan.
    ///     Handed to <see cref="JobArgsBase.WaitForJobs"/>, which parks the new job as
    ///     <see cref="JobStatus.Blocked"/> until they are all done and then starts it.
    ///
    ///     Everything unfinished, not only the retries: two agents rewriting one worktree at the
    ///     same time is how a branch ends up with half of each. Ids that have finished by the
    ///     time the job is built are harmless — JobService counts only the ones it still holds,
    ///     so a stale id makes the new job start rather than wedge.
    /// </summary>
    public static List<string> JobsToWaitFor(IEnumerable<JobItem> planJobs) =>
        planJobs.Where(job => IsUnfinished(job.Status)).Select(job => job.Id).ToList();

    /// <summary>
    ///     Whether a change request means anything for this plan right now.
    ///
    ///     Review is where a plan sits while it is being looked at, and where a finished
    ///     RetryPlan puts it back. The second case is the one worth allowing on purpose: the
    ///     moment a retry starts the plan moves to Executing, and a reviewer who keeps walking
    ///     the app and finds three more things should be able to queue them rather than be locked
    ///     out until the agent happens to finish. Any other state — Draft, Creating, Completed,
    ///     Failed — is not one where feedback on a running app has anywhere to go.
    /// </summary>
    public static bool CanRequestChanges(PlanStatus state, IEnumerable<JobItem> planJobs) =>
        state == PlanStatus.Review ||
        planJobs.Any(job => job.Type == Constants.JobTypes.RetryPlan && IsUnfinished(job.Status));

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
        // Two things an agent gets wrong without being told. A source location is where the
        // element was RENDERED from, which for anything built on a design system is the shared
        // primitive — edit that and every screen changes, when one screen was meant. And a
        // location is sometimes a guess; saying so is what lets the agent check first instead of
        // editing confidently in the wrong file.
        sb.AppendLine(
            "Each item is a comment left on one element of the running app. Where a source "
            + "location is given it is where that element was rendered from, which for a shared "
            + "component is the primitive rather than the thing being complained about - when a "
            + "component path is also given, the change usually belongs at the call site it names, "
            + "not in the primitive. Treat any location not marked high confidence as a lead to "
            + "verify rather than a fact.");
        sb.AppendLine();

        // Under the page each comment was left on. A reviewer walks several screens in one
        // pass, and a flat list of notes about three different pages is one the agent has to
        // guess its way through — "make this green" says nothing without knowing where "this"
        // was. Keys arrive from the widget already canonical, so this is plain equality, and
        // GroupBy keeps both the pages and the comments within them in the order they came.
        foreach (var page in comments.GroupBy(c => c.Url ?? url))
        {
            sb.AppendLine($"## {page.Key}");
            sb.AppendLine();

            foreach (var comment in page)
            {
                var tag = string.IsNullOrEmpty(comment.Tag) ? "element" : $"<{comment.Tag}>";
                // The element's own words, which usually identify it outright and, unlike a
                // selector, can be searched for in the source.
                var quoted = string.IsNullOrWhiteSpace(comment.Text)
                    ? string.Empty
                    : $" \u201c{comment.Text.Trim()}\u201d";

                sb.AppendLine($"- **{comment.Number}. {tag}**{quoted}");
                sb.AppendLine($"  {comment.Comment.Trim()}");

                var source = ReadSource(comment.DebugJson);
                if (source.Label is not null)
                {
                    var how = string.Join(", ", new[] { source.Confidence, source.Provenance }
                        .Where(part => !string.IsNullOrEmpty(part)));
                    sb.AppendLine(how.Length > 0
                        ? $"  source: `{source.Label}` ({how})"
                        : $"  source: `{source.Label}`");
                }

                if (source.ComponentPath is not null)
                    sb.AppendLine($"  component: {source.ComponentPath}");

                if (AttributeLabel(comment.AttrsJson) is { } attributes)
                    sb.AppendLine($"  attributes: {attributes}");

                // The selector earns its line when nothing resolved, where it is the only handle
                // on the element left. Printed beside a file, a line and a component path it is
                // the least useful thing there, and printing it every time is how a reader learns
                // to skip the one case that needed it.
                if (source.Label is null && !string.IsNullOrEmpty(comment.Selector))
                    sb.AppendLine($"  selector: `{comment.Selector}`");

                if (!string.IsNullOrEmpty(comment.Device))
                    sb.AppendLine($"  viewport: {comment.Device}");

                sb.AppendLine();
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    ///     What the widget worked out about where an element came from. <paramref name="Label"/> is
    ///     <c>src/components/SaveButton.tsx:42</c>, null when nothing resolved — a production
    ///     bundle with no source map, most often. The rest is what makes that label safe to act on:
    ///     how it was derived, how sure the widget is, and which components the element sits inside.
    /// </summary>
    public record SourceInfo(string? Label, string? Provenance, string? Confidence, string? ComponentPath);

    /// <summary>
    ///     Reads the widget's attribution payload. Everything here was already being collected and
    ///     thrown away: only file and line were ever read, so an agent saw a guess and a
    ///     high-confidence owner-stack hit as the same flat assertion.
    /// </summary>
    public static SourceInfo ReadSource(string? debugJson)
    {
        if (string.IsNullOrEmpty(debugJson)) return new SourceInfo(null, null, null, null);
        try
        {
            using var document = JsonDocument.Parse(debugJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new SourceInfo(null, null, null, null);

            string? label = null;
            if (root.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object
                && source.TryGetProperty("file", out var file)
                && file.GetString() is { Length: > 0 } path)
            {
                label = source.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Number
                    ? $"{path}:{line.GetInt32()}"
                    : path;
            }

            // "none" is the collector's own placeholder for "did not manage it", not a provenance.
            string? Word(string name) =>
                root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 } word
                && word != "none"
                    ? word
                    : null;

            string? component = null;
            if (root.TryGetProperty("ownerChain", out var chain) && chain.ValueKind == JsonValueKind.Array)
            {
                var names = chain.EnumerateArray()
                    .Select(entry => entry.ValueKind == JsonValueKind.Object
                                     && entry.TryGetProperty("name", out var n) ? n.GetString() : null)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
                if (names.Count > 0) component = string.Join(" > ", names);
            }

            return new SourceInfo(label, Word("provenance"), Word("confidence"), component);
        }
        catch (JsonException)
        {
            return new SourceInfo(null, null, null, null);
        }
    }

    /// <summary>The file and line alone, for callers that only want somewhere to point.</summary>
    public static string? SourceLabel(string? debugJson) => ReadSource(debugJson).Label;

    /// <summary>
    ///     Attributes that identify the element in the SOURCE, most stable first. A data-testid or
    ///     an aria-label is something an agent can grep for; <c>div > button:nth-child(1)</c> is
    ///     something it has to solve. Capped, because an element can carry a lot of them.
    /// </summary>
    private static readonly string[] IdentifyingAttributes =
        ["data-testid", "data-test-id", "id", "aria-label", "name", "placeholder", "href"];

    public static string? AttributeLabel(string? attrsJson)
    {
        if (string.IsNullOrEmpty(attrsJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(attrsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            var parts = new List<string>();
            foreach (var name in IdentifyingAttributes)
            {
                if (parts.Count == 3) break;
                if (document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text)
                    parts.Add($"{name}=\"{text}\"");
            }

            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"https?://[^\s""'<>]+")]
    private static partial Regex UrlPattern();
}
