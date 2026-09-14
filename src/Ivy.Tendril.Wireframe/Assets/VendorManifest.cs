using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ivy.Tendril.Wireframe.Assets;

/// <summary>
/// The single source of truth for both the esbuild <c>--external:</c> list and the browser
/// import map, produced by build/vendor/build-vendor.mjs.
///
/// These two lists MUST agree. If a specifier is external to esbuild but missing from the
/// import map, the bundle links fine and then fails in the browser with an opaque
/// "Failed to resolve module specifier" -- so both are derived from this one file rather
/// than maintained separately.
/// </summary>
public sealed class VendorManifest
{
    [JsonPropertyName("versions")]
    public Dictionary<string, string> Versions { get; init; } = new();

    /// <summary>Bare specifier -> URL path served by the dev server.</summary>
    [JsonPropertyName("specifiers")]
    public Dictionary<string, string> Specifiers { get; init; } = new();

    [JsonPropertyName("chunks")]
    public List<string> Chunks { get; init; } = [];

    [JsonPropertyName("entries")]
    public List<string> Entries { get; init; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static VendorManifest? _cached;

    public static VendorManifest Load(AssetCatalog assets)
    {
        return _cached ??= JsonSerializer.Deserialize<VendorManifest>(
            assets.ReadText("vendor.manifest.json"), Options)
            ?? throw new InvalidOperationException("vendor.manifest.json could not be parsed.");
    }

    /// <summary>The specifiers esbuild must not bundle, longest-first so deep subpaths
    /// (roughjs/bin/generator) are matched before their parent package.</summary>
    public IEnumerable<string> ExternalSpecifiers =>
        Specifiers.Keys.OrderByDescending(k => k.Length);

    public string TendrilVersion =>
        Versions.TryGetValue("tendril-wireframes", out var v) ? v : "unknown";

    public string ReactVersion =>
        Versions.TryGetValue("react", out var v) ? v : "unknown";

    private const string DefaultPayloadBase = "/__wireframe/";

    /// <summary>
    /// Serialized <c>&lt;script type="importmap"&gt;</c> body. <paramref name="payloadBase"/> replaces
    /// the manifest's <c>/__wireframe/</c> prefix, for a host that serves the payload under a
    /// path base.
    /// </summary>
    public string ToImportMapJson(string payloadBase = DefaultPayloadBase) =>
        JsonSerializer.Serialize(
            new
            {
                imports = Specifiers.ToDictionary(
                    s => s.Key,
                    s => payloadBase != DefaultPayloadBase && s.Value.StartsWith(DefaultPayloadBase, StringComparison.Ordinal)
                        ? payloadBase + s.Value[DefaultPayloadBase.Length..]
                        : s.Value),
            },
            new JsonSerializerOptions { WriteIndented = true });
}
