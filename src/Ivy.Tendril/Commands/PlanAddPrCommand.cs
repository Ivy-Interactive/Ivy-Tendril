using System.ComponentModel;
using Ivy.Tendril.Services;
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
    public override int Execute(CommandContext context, PlanAddPrSettings settings)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            // Check if already present
            if (plan.Prs.Contains(settings.PrUrl))
            {
                Console.WriteLine($"PR already in plan: {settings.PrUrl}");
                return 0;
            }

            // Add PR
            plan.Prs.Add(settings.PrUrl);
            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Added PR: {settings.PrUrl}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
