using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanRemoveRepoSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Repository path")]
    [CommandArgument(1, "<repo-path>")]
    public string RepoPath { get; set; } = "";
}

public class PlanRemoveRepoCommand : Command<PlanRemoveRepoSettings>
{
    protected override int Execute(CommandContext context, PlanRemoveRepoSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            // Remove repo (case-insensitive)
            var removed = plan.Repos.RemoveAll(r => r.Equals(settings.RepoPath, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                Console.Error.WriteLine($"Repository not found in plan: {settings.RepoPath}");
                return 1;
            }

            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Removed repository: {settings.RepoPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
