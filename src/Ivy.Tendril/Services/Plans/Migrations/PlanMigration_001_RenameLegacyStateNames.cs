using System.Text.RegularExpressions;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Plans.Migrations;

/// <summary>
///     Rewrites legacy plan state names to their current spelling. A name-only migration: it rewrites
///     the <c>state:</c> value but leaves <c>updated:</c> untouched. Must run before stuck-plan recovery
///     and the database sync, which compare against the current enum names.
/// </summary>
public sealed class PlanMigration_001_RenameLegacyStateNames : IPlanMigration
{
    private static readonly Dictionary<string, string> Renames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Building"] = nameof(PlanStatus.Creating),
        ["ReadyForReview"] = nameof(PlanStatus.Review)
    };

    private static readonly Regex StateLineRegex = new(@"(?m)^state:\s*(.+)$", RegexOptions.Compiled);

    public int Version => 1;
    public string Description => "Rename legacy plan state names (Building → Creating, ReadyForReview → Review)";

    public string Apply(PlanMigrationContext context)
    {
        var match = StateLineRegex.Match(context.Yaml);
        if (!match.Success) return context.Yaml;

        var state = match.Groups[1].Value.Trim();
        if (!Renames.TryGetValue(state, out var newState)) return context.Yaml;

        context.Logger.LogInformation("Migrated plan state {Old} → {New} in {Folder}",
            state, newState, context.FolderName);
        return Regex.Replace(context.Yaml, @"(?m)^state:\s*.*$", $"state: {newState}");
    }
}
