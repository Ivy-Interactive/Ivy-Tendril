using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
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
    private readonly IPlanWatcherService _planWatcher;

    public PlanAddPrCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanAddPrSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        if (plan.Prs.Contains(settings.PrUrl))
        {
            Console.WriteLine($"PR already in plan: {settings.PrUrl}");
            return 0;
        }

        plan.Prs.Add(settings.PrUrl);
        plan.Updated = DateTime.UtcNow;

        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Added PR: {settings.PrUrl}");
        return 0;
    }
}
