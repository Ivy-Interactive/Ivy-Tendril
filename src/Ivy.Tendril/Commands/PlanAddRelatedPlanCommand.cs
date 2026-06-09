using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanAddRelatedPlanSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Plan reference (ID, folder name, or path)")]
    [CommandArgument(1, "<related-plan>")]
    public string RelatedPlan { get; set; } = "";
}

public class PlanAddRelatedPlanCommand : Command<PlanAddRelatedPlanSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanAddRelatedPlanCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanAddRelatedPlanSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        var resolvedRp = PlanCommandHelpers.ResolvePlanFolderName(settings.RelatedPlan);

        if (plan.RelatedPlans.Contains(resolvedRp, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Related plan already present: {resolvedRp}");
            return 0;
        }

        plan.RelatedPlans.Add(resolvedRp);
        plan.Updated = DateTime.UtcNow;

        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Added related plan: {resolvedRp}");
        return 0;
    }
}
