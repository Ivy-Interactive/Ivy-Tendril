using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanAddRelatedPlanSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Related plan folder name (e.g., 01478-WorktreeIsolation)")]
    [CommandArgument(1, "<related-plan>")]
    public string RelatedPlan { get; set; } = "";
}

public class PlanAddRelatedPlanCommand : Command<PlanAddRelatedPlanSettings>
{
    private readonly ILogger<PlanAddRelatedPlanCommand> _logger;

    public PlanAddRelatedPlanCommand(ILogger<PlanAddRelatedPlanCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, PlanAddRelatedPlanSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (plan.RelatedPlans.Contains(settings.RelatedPlan, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Related plan already present: {RelatedPlan}", settings.RelatedPlan);
                return 0;
            }

            plan.RelatedPlans.Add(settings.RelatedPlan);
            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan);

            _logger.LogInformation("Added related plan: {RelatedPlan}", settings.RelatedPlan);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add related plan to plan {PlanId}", settings.PlanId);
            return 1;
        }
    }
}
