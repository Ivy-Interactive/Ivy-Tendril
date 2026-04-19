using System.ComponentModel;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanAddCommitSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Commit SHA")]
    [CommandArgument(1, "<sha>")]
    public string Sha { get; set; } = "";
}

public class PlanAddCommitCommand : Command<PlanAddCommitSettings>
{
    public override int Execute(CommandContext context, PlanAddCommitSettings settings)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            // Check if already present
            if (plan.Commits.Contains(settings.Sha))
            {
                Console.WriteLine($"Commit already in plan: {settings.Sha}");
                return 0;
            }

            // Add commit
            plan.Commits.Add(settings.Sha);
            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Added commit: {settings.Sha}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
