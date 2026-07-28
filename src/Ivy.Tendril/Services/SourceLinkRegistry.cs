using Ivy.Plugins.Sources;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

/// <summary>
///     Holds the URL→label resolvers contributed by plugins. Tendril stores only a plan's
///     <c>sourceUrl</c>; the short identifier (`IVY-456`) is derived here at the point of use so
///     no tracker's URL format has to live in Tendril itself.
/// </summary>
public class SourceLinkRegistry : ISourceLinks
{
    private readonly List<(string PluginId, Func<Uri, string?> Resolver)> _resolvers = [];
    private readonly ILogger _logger;
    private readonly Func<string?> _getCurrentPluginId;

    public SourceLinkRegistry(ILogger logger, Func<string?> getCurrentPluginId)
    {
        _logger = logger;
        _getCurrentPluginId = getCurrentPluginId;
    }

    public void RegisterResolver(Func<Uri, string?> resolver)
    {
        var pluginId = _getCurrentPluginId() ?? "__unknown__";
        _resolvers.Add((pluginId, resolver));
    }

    internal void RemovePluginResolvers(string pluginId)
    {
        _resolvers.RemoveAll(r => r.PluginId == pluginId);
    }

    /// <summary>
    ///     Returns the first label any resolver produces for <paramref name="url" />, or null when
    ///     none recognizes it. A resolver that throws is logged and skipped — a bad plugin regex must
    ///     not fail the job that asked for a label. Only absolute http(s) URLs reach resolvers; note
    ///     that a bare Unix path parses as an absolute <c>file:</c> URI, so the scheme check is what
    ///     keeps non-web values out of plugin code.
    /// </summary>
    public string? TryGetLabel(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return null;

        foreach (var (pluginId, resolver) in _resolvers)
        {
            try
            {
                var label = resolver(uri);
                if (!string.IsNullOrWhiteSpace(label))
                    return label.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Plugin '{PluginId}' source link resolver threw for {Url}", pluginId, url);
            }
        }

        return null;
    }
}
