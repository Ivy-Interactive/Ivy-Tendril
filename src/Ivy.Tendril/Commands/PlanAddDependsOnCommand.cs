using System.ComponentModel;
using Ivy.Tendril.Helpers;
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
    protected override int Execute(CommandContext context, PlanAddDependsOnSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (plan.DependsOn.Contains(settings.DependsOn, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Dependency already present: {settings.DependsOn}");
                return 0;
            }

            plan.DependsOn.Add(settings.DependsOn);
            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Added dependency: {settings.DependsOn}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
