using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Helpers;
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

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(RepoPath, "repo-path"));
    }
}

public class PlanAddRepoCommand : Command<PlanAddRepoSettings>
{
    private readonly IPlanWatcherService _planWatcher;
    private readonly IConfigService _configService;

    public PlanAddRepoCommand(IPlanWatcherService planWatcher, IConfigService configService)
    {
        _planWatcher = planWatcher;
        _configService = configService;
    }

    protected override int Execute(CommandContext context, PlanAddRepoSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        if (plan.Repos.Contains(settings.RepoPath, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Repository already in plan: {settings.RepoPath}");
            return 0;
        }

        // Refuse repos that don't belong to the plan's project (issue #1340). Skip when the
        // project is unknown to config — there's nothing to validate against.
        var project = _configService.GetProject(plan.Project);
        if (project != null)
        {
            try
            {
                PlanProjectRepoGuard.EnsureReposBelongToProject([settings.RepoPath], project);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        plan.Repos.Add(settings.RepoPath);
        plan.Updated = DateTime.UtcNow;

        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Added repository: {settings.RepoPath}");
        return 0;
    }
}
