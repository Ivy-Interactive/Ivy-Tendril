using System.ComponentModel;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanCreateSettings : CommandSettings
{
    [Description("Plan title")]
    [CommandArgument(0, "<title>")]
    public string Title { get; set; } = "";


    [Description("Project name (default: Auto)")]
    [CommandOption("--project")]
    public string? Project { get; set; }

    [Description("Priority level (default: NiceToHave)")]
    [CommandOption("--level")]
    public string? Level { get; set; }

    [Description("Initial prompt text")]
    [CommandOption("--initial-prompt")]
    public string? InitialPrompt { get; set; }

    [Description("Source URL (GitHub issue or PR)")]
    [CommandOption("--source-url")]
    public string? SourceUrl { get; set; }

    [Description("Execution profile (deep or balanced)")]
    [CommandOption("--execution-profile")]
    public string? ExecutionProfile { get; set; }

    [Description("Priority number (default: 0)")]
    [CommandOption("--priority")]
    public int? Priority { get; set; }

    [Description("Repository paths (repeatable)")]
    [CommandOption("--repo")]
    public string[]? Repos { get; set; }

    [Description("Verifications in Name=Status format (repeatable)")]
    [CommandOption("--verification")]
    public string[]? Verifications { get; set; }

    [Description("Related plan folder names (repeatable)")]
    [CommandOption("--related-plan")]
    public string[]? RelatedPlans { get; set; }

    [Description("Dependency plan folder names (repeatable)")]
    [CommandOption("--depends-on")]
    public string[]? DependsOn { get; set; }

    [Description("Explicit plans directory (overrides TENDRIL_PLANS / TENDRIL_HOME)")]
    [CommandOption("--plans-dir")]
    public string? PlansDir { get; set; }
}

public class PlanCreateCommand : Command<PlanCreateSettings>
{
    private readonly ILogger<PlanCreateCommand> _logger;
    private readonly IPlanWatcherService _planWatcher;

    public PlanCreateCommand(ILogger<PlanCreateCommand> logger, IPlanWatcherService planWatcher)
    {
        _logger = logger;
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanCreateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var plansDir = PlanCommandHelpers.GetPlansDirectory(settings.PlansDir);

            var planId = PlanYamlHelper.AllocatePlanId(plansDir);
            var safeTitle = PlanYamlHelper.ToSafeTitle(settings.Title);
            var folderName = $"{planId}-{safeTitle}";
            var planFolder = Path.Combine(plansDir, folderName);

            if (Directory.Exists(planFolder))
            {
                _logger.LogError("Plan folder already exists: {PlanFolder}", planFolder);
                return 1;
            }

            Directory.CreateDirectory(planFolder);

            var plan = new PlanYaml
            {
                State = "Draft",
                Project = settings.Project ?? "Auto",
                Level = settings.Level ?? "NiceToHave",
                Title = settings.Title,
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow,
                InitialPrompt = settings.InitialPrompt,
                SourceUrl = settings.SourceUrl,
                ExecutionProfile = settings.ExecutionProfile,
                Priority = settings.Priority ?? 0
            };

            if (settings.Repos != null)
                foreach (var repo in settings.Repos)
                    plan.Repos.Add(repo);

            if (settings.Verifications != null)
                foreach (var v in settings.Verifications)
                {
                    var eqIdx = v.IndexOf('=');
                    if (eqIdx < 0)
                        throw new ArgumentException($"Invalid verification format '{v}'. Expected Name=Status.");
                    plan.Verifications.Add(new PlanVerificationEntry
                    {
                        Name = v[..eqIdx],
                        Status = v[(eqIdx + 1)..]
                    });
                }

            if (settings.RelatedPlans != null)
                foreach (var rp in settings.RelatedPlans)
                    plan.RelatedPlans.Add(rp);

            if (settings.DependsOn != null)
                foreach (var dep in settings.DependsOn)
                    plan.DependsOn.Add(dep);

            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

            Console.WriteLine($"PlanId: {planId}");
            Console.WriteLine($"Directory: {planFolder}");
            Console.WriteLine($"Plan created: {folderName}");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create plan");
            return 1;
        }
    }
}
