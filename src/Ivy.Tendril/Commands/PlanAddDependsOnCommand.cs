using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanAddDependsOnSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Plan reference (ID, folder name, or path)")]
    [CommandArgument(1, "<depends-on>")]
    public string DependsOn { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(DependsOn, "depends-on"));
    }
}

public class PlanAddDependsOnCommand : Command<PlanAddDependsOnSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanAddDependsOnCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanAddDependsOnSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        var resolvedDep = PlanCommandHelpers.ResolvePlanFolderName(settings.DependsOn);

        if (plan.DependsOn.Contains(resolvedDep, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Dependency already present: {resolvedDep}");
            return 0;
        }

        plan.DependsOn.Add(resolvedDep);
        plan.Updated = DateTime.UtcNow;

        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Added dependency: {resolvedDep}");
        return 0;
    }
}
