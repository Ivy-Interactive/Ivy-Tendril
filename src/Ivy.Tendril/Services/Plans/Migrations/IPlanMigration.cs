using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Plans.Migrations;

/// <summary>
///     A single, versioned transformation that brings a plan from schema version
///     <see cref="Version" /> - 1 up to <see cref="Version" />. The parallel to
///     <see cref="Database.IMigration" /> for the SQLite database, but applied per-plan against
///     the on-disk <c>plan.yaml</c> (and, where relevant, the plan folder).
///
///     Migrations are auto-discovered and run by <see cref="PlanMigrator" />. The runner reads the
///     plan's current version from raw YAML (absent ⇒ 0), applies every pending migration in order,
///     then stamps the new <c>schemaVersion</c>. Implementations therefore MUST NOT stamp the
///     version themselves, and should be safe to run against any plan below their version.
/// </summary>
public interface IPlanMigration
{
    /// <summary>Target schema version. Must be sequential across migrations (1, 2, 3, ...).</summary>
    int Version { get; }

    /// <summary>Human-readable description of what this migration does.</summary>
    string Description { get; }

    /// <summary>
    ///     Apply the migration to a single plan. May rewrite the YAML and/or touch the plan folder
    ///     (e.g. rename subfolders). Returns the possibly-modified YAML text — return the input
    ///     unchanged when there is nothing to do.
    /// </summary>
    string Apply(PlanMigrationContext context);
}
