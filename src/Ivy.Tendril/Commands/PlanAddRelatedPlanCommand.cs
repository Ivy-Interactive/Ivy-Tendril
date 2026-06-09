using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<PlanAddRelatedPlanCommand> _logger;
    private readonly IPlanWatcherService _planWatcher;

    public PlanAddRelatedPlanCommand(ILogger<PlanAddRelatedPlanCommand> logger, IPlanWatcherService planWatcher)
    {
        _logger = logger;
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanAddRelatedPlanSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);
            var resolvedRp = PlanCommandHelpers.ResolvePlanFolderName(settings.RelatedPlan);

            if (plan.RelatedPlans.Contains(resolvedRp, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Related plan already present: {RelatedPlan}", resolvedRp);
                return 0;
            }

            plan.RelatedPlans.Add(resolvedRp);
            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

            _logger.LogInformation("Added related plan: {RelatedPlan}", resolvedRp);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to add related plan to plan {PlanId}: {Message}", settings.PlanId, ex.Message);
            return 1;
        }
    }
}
