using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ivy.Tendril.Wireframe.Project;

public enum TailwindMode
{
    /// <summary>The utility superset embedded in the tool. Instant and offline.</summary>
    Superset,

    /// <summary>The official Tailwind standalone CLI. Full fidelity, ~107 MB once.</summary>
    Jit,
}

/// <summary>
/// Project settings that `serve` and `screenshot` must agree with `setup` about.
///
/// Written to wireframe.json only when something differs from the defaults, so an ordinary
/// project stays as small as the scaffold makes it.
/// </summary>
public sealed class WireframeConfig
{
    // No per-property converter: the camelCase one on Options below would be shadowed by
    // it, and the file would read "Jit" rather than "jit".
    [JsonPropertyName("tailwind")]
    public TailwindMode Tailwind { get; set; } = TailwindMode.Superset;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string PathFor(WireframeProject project) =>
        System.IO.Path.Combine(project.Root, "wireframe.json");

    public static WireframeConfig Load(WireframeProject project)
    {
        var path = PathFor(project);
        if (!File.Exists(path)) return new WireframeConfig();

        try
        {
            return JsonSerializer.Deserialize<WireframeConfig>(File.ReadAllText(path), Options)
                ?? new WireframeConfig();
        }
        catch (JsonException)
        {
            // A hand-broken config should not stop the wireframe rendering.
            return new WireframeConfig();
        }
    }

    public void Save(WireframeProject project)
    {
        var path = PathFor(project);

        // Nothing to record: leave the project directory clean.
        if (Tailwind == TailwindMode.Superset)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, Options) + "\n");
    }

    public static bool TryParseTailwind(string value, out TailwindMode mode)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "superset" or "embedded" or "default":
                mode = TailwindMode.Superset;
                return true;
            case "jit" or "cli" or "standalone":
                mode = TailwindMode.Jit;
                return true;
            default:
                mode = TailwindMode.Superset;
                return false;
        }
    }
}
