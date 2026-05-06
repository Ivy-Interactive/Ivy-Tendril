using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanRemoveDependsOnSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Dependency plan folder name (e.g., 01478-WorktreeIsolation)")]
    [CommandArgument(1, "<depends-on>")]
    public string DependsOn { get; set; } = "";
}

public class PlanRemoveDependsOnCommand : Command<PlanRemoveDependsOnSettings>
{
    private readonly ILogger<PlanRemoveDependsOnCommand> _logger;
    private readonly IPlanWatcherService _planWatcher;

    public PlanRemoveDependsOnCommand(ILogger<PlanRemoveDependsOnCommand> logger, IPlanWatcherService planWatcher)
    {
        _logger = logger;
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanRemoveDependsOnSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var removed = plan.DependsOn.RemoveAll(d => d.Equals(settings.DependsOn, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                _logger.LogError("Dependency not found: {DependsOn}", settings.DependsOn);
                return 1;
            }

            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

            _logger.LogInformation("Removed dependency: {DependsOn}", settings.DependsOn);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove dependency from plan {PlanId}", settings.PlanId);
            return 1;
        }
    }
}
