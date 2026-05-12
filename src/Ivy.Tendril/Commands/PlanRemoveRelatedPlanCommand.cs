using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanRemoveRelatedPlanSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Related plan folder name (e.g., 01478-WorktreeIsolation)")]
    [CommandArgument(1, "<related-plan>")]
    public string RelatedPlan { get; set; } = "";
}

public class PlanRemoveRelatedPlanCommand : Command<PlanRemoveRelatedPlanSettings>
{
    private readonly ILogger<PlanRemoveRelatedPlanCommand> _logger;
    private readonly IPlanWatcherService _planWatcher;

    public PlanRemoveRelatedPlanCommand(ILogger<PlanRemoveRelatedPlanCommand> logger, IPlanWatcherService planWatcher)
    {
        _logger = logger;
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanRemoveRelatedPlanSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var removed = plan.RelatedPlans.RemoveAll(r => r.Equals(settings.RelatedPlan, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                _logger.LogError("Related plan not found: {RelatedPlan}", settings.RelatedPlan);
                return 1;
            }

            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

            _logger.LogInformation("Removed related plan: {RelatedPlan}", settings.RelatedPlan);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove related plan from plan {PlanId}", settings.PlanId);
            return 1;
        }
    }
}
