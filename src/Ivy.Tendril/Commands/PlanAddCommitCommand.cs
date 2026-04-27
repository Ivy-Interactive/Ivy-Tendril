using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<PlanAddCommitCommand> _logger;
    private readonly IPlanWatcherService _planWatcher;

    public PlanAddCommitCommand(ILogger<PlanAddCommitCommand> logger, IPlanWatcherService planWatcher)
    {
        _logger = logger;
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanAddCommitSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            // Validate commit hash format
            if (!GitService.IsValidCommitHash(settings.Sha))
            {
                _logger.LogError("Invalid commit hash format: {Sha}", settings.Sha);
                return 1;
            }

            // Check if already present
            if (plan.Commits.Contains(settings.Sha))
            {
                _logger.LogInformation("Commit already in plan: {Sha}", settings.Sha);
                return 0;
            }

            // Add commit
            plan.Commits.Add(settings.Sha);
            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

            _logger.LogInformation("Added commit: {Sha}", settings.Sha);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add commit to plan {PlanId}", settings.PlanId);
            return 1;
        }
    }
}
