using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Plans.Migrations;

/// <summary>
///     Normalizes/repairs plan.yaml structure via <see cref="PlanYamlRepairService" />: object-style
///     repos/commits/prs → plain strings, quoting fixes for Windows paths and embedded colons, empty
///     list fields, non-numeric priority, etc. Handles the malformed YAML that agents occasionally emit.
/// </summary>
public sealed class PlanMigration_003_NormalizeYamlStructure : IPlanMigration
{
    public int Version => 3;
    public string Description => "Normalize/repair plan.yaml structure";

    public string Apply(PlanMigrationContext context)
    {
        var repaired = PlanYamlRepairService.RepairPlanYaml(context.Yaml);
        if (repaired != context.Yaml)
            context.Logger.LogInformation("Repaired plan.yaml structure in {Folder}", context.FolderName);
        return repaired;
    }
}
