using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Plans.Migrations;

/// <summary>
///     Per-plan input handed to an <see cref="IPlanMigration" />. The <see cref="Yaml" /> is the
///     current plan.yaml content (already carrying any edits from earlier migrations in the same run);
///     migrations return the next version of it.
/// </summary>
public sealed class PlanMigrationContext
{
    /// <summary>Absolute path to the plan folder (e.g. <c>.../Plans/01234-Title</c>).</summary>
    public required string PlanFolder { get; init; }

    /// <summary>Plan folder name (e.g. <c>01234-Title</c>), useful for logging/notifications.</summary>
    public required string FolderName { get; init; }

    /// <summary>Current plan.yaml content to transform.</summary>
    public required string Yaml { get; init; }

    /// <summary>Parsed <c>state:</c> value, or empty if absent. Provided for convenience.</summary>
    public required string State { get; init; }

    public required ILogger Logger { get; init; }
}
