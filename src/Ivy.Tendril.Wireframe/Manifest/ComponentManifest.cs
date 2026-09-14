using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ivy.Tendril.Wireframe.Assets;

namespace Ivy.Tendril.Wireframe.Manifest;

/// <summary>
/// The component/prop manifest that tendril-wireframes generates from its own TypeScript
/// types. Because it comes from the compiler rather than hand-written docs, the props here
/// cannot drift from the components.
/// </summary>
/// <summary>One entry in the manifest's type dictionary.</summary>
public sealed class TypeInfo
{
    /// <summary>"enum", "object", "map" or "alias": which of the fields below apply.</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";

    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>enum: the members, in the order they were declared.</summary>
    [JsonPropertyName("values")] public List<FlexibleString>? Values { get; init; }

    /// <summary>object: the properties.</summary>
    [JsonPropertyName("properties")] public List<TypeProperty>? Properties { get; init; }

    /// <summary>map: an index signature, e.g. { [key: string]: string | number }.</summary>
    [JsonPropertyName("keyType")] public string? KeyType { get; init; }
    [JsonPropertyName("valueType")] public string? ValueType { get; init; }

    /// <summary>alias: what it expands to, e.g. "number | string".</summary>
    [JsonPropertyName("type")] public string? Type { get; init; }
}

public sealed class TypeProperty
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("required")] public bool Required { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
}

public sealed class ComponentManifest
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("componentCount")] public int ComponentCount { get; init; }
    [JsonPropertyName("categories")] public Dictionary<string, int> Categories { get; init; } = new();

    /// <summary>
    /// Every named type the props refer to, described once: enums by their members, objects
    /// by their properties, aliases by what they expand to.
    ///
    /// This used to be unions alone, which meant a prop typed `Sizing` or `Option` named
    /// something the reference never defined -- and an agent that guessed `width={150}`
    /// meant pixels got a 600px box, silently.
    /// </summary>
    [JsonPropertyName("types")]
    public Dictionary<string, TypeInfo> Types { get; init; } = new();

    [JsonPropertyName("components")] public List<ComponentInfo> Components { get; init; } = [];

    /// <summary>The components an agent should actually use: the manifest also carries
    /// internal building blocks (SketchLayer, InputShell, ...) that are not part of the
    /// public surface.</summary>
    public IEnumerable<ComponentInfo> PublicComponents =>
        Components.Where(c => !c.Internal).OrderBy(c => c.Name, StringComparer.Ordinal);

    public ComponentInfo? Find(string name) =>
        Components.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private static ComponentManifest? _cached;

    public static ComponentManifest Load(AssetCatalog assets) =>
        _cached ??= JsonSerializer.Deserialize<ComponentManifest>(
            assets.ReadText("tendril.manifest.json"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("tendril.manifest.json could not be parsed.");
}

public sealed class ComponentInfo
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("category")] public string Category { get; init; } = "";
    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>The Ivy Framework widget this mirrors, when there is one.</summary>
    [JsonPropertyName("ivy")] public string? Ivy { get; init; }

    [JsonPropertyName("tags")] public List<string> Tags { get; init; } = [];
    [JsonPropertyName("examples")] public List<string> Examples { get; init; } = [];
    [JsonPropertyName("internal")] public bool Internal { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("props")] public List<PropInfo> Props { get; init; } = [];

    /// <summary>Props declared on this component, excluding the shared bases (documented
    /// once rather than repeated 98 times) and the inherited React DOM surface.</summary>
    public IEnumerable<PropInfo> OwnProps =>
        Props.Where(p => PropSources.IsOwn(p.Inherited));

    /// <summary>Shared bases this component pulls in, so its entry can point at them.</summary>
    public IEnumerable<string> SharedBases =>
        Props.Select(p => p.Inherited)
             .Where(PropSources.IsShared)
             .Select(i => i!)
             .Distinct()
             .OrderBy(i => i, StringComparer.Ordinal);
}

/// <summary>
/// Classifies where a prop came from.
///
/// The manifest records inheritance from React's own interfaces too, so a component that
/// spreads HTML attributes reports several hundred aria-*, on*, and SVG props. Those are
/// standard React and would swamp the reference, so they are dropped rather than
/// documented.
/// </summary>
public static class PropSources
{
    /// <summary>The library's own shared bases -- worth documenting, once.</summary>
    public static readonly string[] Shared =
        ["WidgetBaseProps", "BaseInputProps", "BaseChartProps", "CartesianChartProps"];

    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "DOMAttributes", "SVGAttributes", "AriaAttributes",
        "AllHTMLAttributes", "HTMLAttributes", "LucideProps",
    };

    public static bool IsShared(string? inherited) =>
        inherited is not null && Shared.Contains(inherited, StringComparer.Ordinal);

    public static bool IsNoise(string? inherited) =>
        inherited is not null && Noise.Contains(inherited);

    /// <summary>True for props that belong in the component's own one-line signature.</summary>
    public static bool IsOwn(string? inherited) =>
        inherited is null || (!IsShared(inherited) && !IsNoise(inherited));
}

public sealed class PropInfo
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("required")] public bool Required { get; init; }
    [JsonPropertyName("default")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Default { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>"event" or "slot" where the generator could tell.</summary>
    [JsonPropertyName("kind")] public string? Kind { get; init; }

    /// <summary>Set when the prop comes from a shared base interface.</summary>
    [JsonPropertyName("inherited")] public string? Inherited { get; init; }

    [JsonPropertyName("values")] public List<FlexibleString>? Values { get; init; }

    /// <summary>Compact "name?: Type = default" rendering.</summary>
    public string Signature()
    {
        var type = Values is { Count: > 0 } && Values.Count <= 8
            ? string.Join("|", Values.Select(v => v.Value))
            : Type;

        var text = $"{Name}{(Required ? "" : "?")}: {type}";
        if (Default is not null) text += $" = {Default}";
        return text;
    }
}
