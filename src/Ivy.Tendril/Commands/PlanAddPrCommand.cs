using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanAddPrSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("PR URL")]
    [CommandArgument(1, "<pr-url>")]
    public string PrUrl { get; set; } = "";
}

public class PlanAddPrCommand : Command<PlanAddPrSettings>
{
    private readonly ILogger<PlanAddPrCommand> _logger;
    private readonly IPlanWatcherService _planWatcher;

    public PlanAddPrCommand(ILogger<PlanAddPrCommand> logger, IPlanWatcherService planWatcher)
    {
        _logger = logger;
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanAddPrSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (plan.Prs.Contains(settings.PrUrl))
            {
                _logger.LogInformation("PR already in plan: {PrUrl}", settings.PrUrl);
                Console.WriteLine($"PR already in plan: {settings.PrUrl}");
                return 0;
            }

            plan.Prs.Add(settings.PrUrl);
            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

            _logger.LogInformation("Added PR: {PrUrl}", settings.PrUrl);
            Console.WriteLine($"Added PR: {settings.PrUrl}");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add PR to plan {PlanId}", settings.PlanId);
            Console.Error.WriteLine($"Failed to add PR: {ex.Message}");
            return 1;
        }
    }
}
