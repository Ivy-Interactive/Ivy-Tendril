using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanRemoveRelatedPlanSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Plan reference (ID, folder name, or path)")]
    [CommandArgument(1, "<related-plan>")]
    public string RelatedPlan { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(RelatedPlan, "related-plan"));
    }
}

public class PlanRemoveRelatedPlanCommand : Command<PlanRemoveRelatedPlanSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanRemoveRelatedPlanCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanRemoveRelatedPlanSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        var resolvedRp = PlanCommandHelpers.ResolvePlanFolderName(settings.RelatedPlan);

        var removed = plan.RelatedPlans.RemoveAll(r => r.Equals(resolvedRp, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            throw new InvalidOperationException($"Related plan not found: {resolvedRp}");

        plan.Updated = DateTime.UtcNow;

        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Removed related plan: {resolvedRp}");
        return 0;
    }
}
