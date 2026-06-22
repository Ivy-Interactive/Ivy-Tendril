using System.Text.RegularExpressions;

namespace Ivy.Tendril.Services.Plans.Migrations;

/// <summary>
///     Reads and writes the <c>schemaVersion</c> marker in raw plan.yaml text.
///
///     Detection MUST work off the raw text (not the deserialized model): <see cref="Models.PlanYaml" />
///     defaults <c>SchemaVersion</c> to the current version so new plans serialize as current, which
///     means a legacy file missing the field would otherwise deserialize as already-current and never
///     get migrated. Files without the line are treated as version 0.
/// </summary>
public static class PlanSchemaVersion
{
    private static readonly Regex LineRegex =
        new(@"(?m)^schemaVersion:\s*(\d+)\s*$", RegexOptions.Compiled);

    /// <summary>Returns the stamped schema version, or 0 for legacy files lacking the field.</summary>
    public static int Read(string yaml) =>
        LineRegex.Match(yaml) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    /// <summary>
    ///     Replaces an existing <c>schemaVersion:</c> line or prepends one. Prepending is valid YAML
    ///     because "schemaVersion" is preserved by <see cref="PlanYamlRepairService" />'s normalizer.
    /// </summary>
    public static string Stamp(string yaml, int version) =>
        LineRegex.IsMatch(yaml)
            ? LineRegex.Replace(yaml, $"schemaVersion: {version}")
            : $"schemaVersion: {version}{Environment.NewLine}{yaml}";
}
