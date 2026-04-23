using System.ComponentModel;
using Ivy.Tendril.Helpers;
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
    protected override int Execute(CommandContext context, PlanAddRelatedPlanSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (plan.RelatedPlans.Contains(settings.RelatedPlan, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Related plan already present: {settings.RelatedPlan}");
                return 0;
            }

            plan.RelatedPlans.Add(settings.RelatedPlan);
            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Added related plan: {settings.RelatedPlan}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
