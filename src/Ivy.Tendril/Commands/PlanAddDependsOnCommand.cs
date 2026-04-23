using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanAddDependsOnSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Dependency plan folder name (e.g., 01478-WorktreeIsolation)")]
    [CommandArgument(1, "<depends-on>")]
    public string DependsOn { get; set; } = "";
}

public class PlanAddDependsOnCommand : Command<PlanAddDependsOnSettings>
{
    private readonly ILogger<PlanAddDependsOnCommand> _logger;

    public PlanAddDependsOnCommand(ILogger<PlanAddDependsOnCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, PlanAddDependsOnSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (plan.DependsOn.Contains(settings.DependsOn, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Dependency already present: {DependsOn}", settings.DependsOn);
                return 0;
            }

            plan.DependsOn.Add(settings.DependsOn);
            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan);

            _logger.LogInformation("Added dependency: {DependsOn}", settings.DependsOn);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add dependency to plan {PlanId}", settings.PlanId);
            return 1;
        }
    }
}
