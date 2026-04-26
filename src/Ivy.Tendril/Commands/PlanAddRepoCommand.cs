using System.ComponentModel;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanAddRepoSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Repository path")]
    [CommandArgument(1, "<repo-path>")]
    public string RepoPath { get; set; } = "";
}

public class PlanAddRepoCommand : Command<PlanAddRepoSettings>
{
    protected override int Execute(CommandContext context, PlanAddRepoSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            // Check if already present
            if (plan.Repos.Contains(settings.RepoPath, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Repository already in plan: {settings.RepoPath}");
                return 0;
            }

            // Add repo
            plan.Repos.Add(settings.RepoPath);
            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Added repository: {settings.RepoPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
