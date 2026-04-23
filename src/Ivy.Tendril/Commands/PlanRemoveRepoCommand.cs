using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<PlanRemoveRepoCommand> _logger;

    public PlanRemoveRepoCommand(ILogger<PlanRemoveRepoCommand> logger) => _logger = logger;

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
                _logger.LogError("Repository not found in plan: {RepoPath}", settings.RepoPath);
                return 1;
            }

            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan);

            _logger.LogInformation("Removed repository: {RepoPath}", settings.RepoPath);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove repository from plan {PlanId}", settings.PlanId);
            return 1;
        }
    }
}
