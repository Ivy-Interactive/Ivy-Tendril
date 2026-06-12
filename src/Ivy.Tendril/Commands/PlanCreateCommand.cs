using System.ComponentModel;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanCreateSettings : CommandSettings
{
    [Description("Plan title")]
    [CommandArgument(0, "<title>")]
    public string Title { get; set; } = "";

    [Description("Project name")]
    [CommandArgument(1, "<project>")]
    public string Project { get; set; } = "";

    [Description("Priority level: Bug, Feature, Epic, Chore, Nitpick (default: Feature)")]
    [CommandOption("--level")]
    public string? Level { get; set; }

    [Description("Initial prompt text")]
    [CommandOption("--initial-prompt")]
    public string? InitialPrompt { get; set; }

    [Description("Source URL (GitHub issue, PR, or external tracker)")]
    [CommandOption("--source-url")]
    public string? SourceUrl { get; set; }

    [Description("Source identifier (e.g., #123, IVY-456)")]
    [CommandOption("--source-identifier")]
    public string? SourceIdentifier { get; set; }

    [Description("Execution profile: deep, balanced")]
    [CommandOption("--execution-profile")]
    public string? ExecutionProfile { get; set; }

    [Description("Priority number (default: 0)")]
    [CommandOption("--priority")]
    public int? Priority { get; set; }

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

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(Title, "title"),
            CliValidation.RequireNonEmpty(Project, "project"),
            CliValidation.ValidateOneOf(Level, "--level", CliValidation.ValidLevels),
            CliValidation.ValidateOneOf(ExecutionProfile, "--execution-profile", CliValidation.ValidExecutionProfiles)
        );
    }
}

public class PlanCreateCommand : Command<PlanCreateSettings>
{
    private readonly IPlanWatcherService _planWatcher;
    private readonly IConfigService _configService;

    public PlanCreateCommand(IPlanWatcherService planWatcher, IConfigService configService)
    {
        _planWatcher = planWatcher;
        _configService = configService;
    }

    protected override int Execute(CommandContext context, PlanCreateSettings settings, CancellationToken cancellationToken)
    {
        ProjectConfig resolvedProject;
        try
        {
            resolvedProject = PlanProjectResolver.ResolveProject(settings.Project, _configService.Projects);
        }
        catch (ArgumentException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }

        var plansDir = PlanCommandHelpers.GetPlansDirectory(settings.PlansDir);

        var planId = PlanYamlHelper.AllocatePlanId(plansDir);
        var safeTitle = PlanYamlHelper.ToSafeTitle(settings.Title);
        var folderName = $"{planId}-{safeTitle}";
        var planFolder = Path.Combine(plansDir, folderName);

        if (Directory.Exists(planFolder))
            throw new InvalidOperationException($"Plan folder already exists: {planFolder}");

        var plan = new PlanYaml
        {
            State = nameof(PlanStatus.Draft),
            Project = resolvedProject.Name,
            Level = settings.Level ?? "Feature",
            Title = settings.Title,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            InitialPrompt = settings.InitialPrompt,
            SourceUrl = settings.SourceUrl,
            SourceIdentifier = settings.SourceIdentifier,
            ExecutionProfile = settings.ExecutionProfile,
            Priority = settings.Priority ?? 0
        };

        foreach (var repoPath in resolvedProject.RepoPaths)
            plan.Repos.Add(repoPath);

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
                plan.RelatedPlans.Add(PlanCommandHelpers.ResolvePlanFolderName(rp));

        if (settings.DependsOn != null)
            foreach (var dep in settings.DependsOn)
                plan.DependsOn.Add(PlanCommandHelpers.ResolvePlanFolderName(dep));

        Directory.CreateDirectory(planFolder);
        FileHelper.GrantBroadWriteAccess(planFolder);

        try
        {
            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);
        }
        catch
        {
            try { Directory.Delete(planFolder, true); } catch { }
            throw;
        }

        Console.WriteLine($"PlanId: {planId}");
        Console.WriteLine($"Directory: {planFolder}");
        Console.WriteLine($"Plan created: {folderName}");
        return 0;
    }
}
