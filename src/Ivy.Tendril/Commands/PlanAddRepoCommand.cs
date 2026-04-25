using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<PlanAddRepoCommand> _logger;
    private readonly IPlanWatcherService _planWatcher;

    public PlanAddRepoCommand(ILogger<PlanAddRepoCommand> logger, IPlanWatcherService planWatcher)
    {
        _logger = logger;
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanAddRepoSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            // Check if already present
            if (plan.Repos.Contains(settings.RepoPath, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Repository already in plan: {RepoPath}", settings.RepoPath);
                return 0;
            }

            // Add repo
            plan.Repos.Add(settings.RepoPath);
            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

            _logger.LogInformation("Added repository: {RepoPath}", settings.RepoPath);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add repository to plan {PlanId}", settings.PlanId);
            return 1;
        }
    }
}
