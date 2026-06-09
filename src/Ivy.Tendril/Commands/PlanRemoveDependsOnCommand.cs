using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanRemoveDependsOnSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Plan reference (ID, folder name, or path)")]
    [CommandArgument(1, "<depends-on>")]
    public string DependsOn { get; set; } = "";
}

public class PlanRemoveDependsOnCommand : Command<PlanRemoveDependsOnSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanRemoveDependsOnCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanRemoveDependsOnSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        var resolvedDep = PlanCommandHelpers.ResolvePlanFolderName(settings.DependsOn);

        var removed = plan.DependsOn.RemoveAll(d => d.Equals(resolvedDep, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            throw new InvalidOperationException($"Dependency not found: {resolvedDep}");

        plan.Updated = DateTime.UtcNow;

        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Removed dependency: {resolvedDep}");
        return 0;
    }
}
