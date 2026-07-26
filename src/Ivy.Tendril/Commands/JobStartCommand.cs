using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class JobStartSettings : CommandSettings
{
    [Description("Job type (ExecutePlan, UpdatePlan, SplitPlan, ExpandPlan, CreateIssue, CreatePr, RetryPlan, CreatePlan, SyncRepo)")]
    [CommandArgument(0, "<job-type>")]
    public string JobType { get; set; } = "";

    [Description("Plan ID (e.g., 00042). Not required for CreatePlan.")]
    [CommandArgument(1, "[plan-id]")]
    public string? PlanId { get; set; }

    [Description("Note for ExecutePlan")]
    [CommandOption("--note")]
    public string? Note { get; set; }

    [Description("Instructions for UpdatePlan (required)")]
    [CommandOption("--instructions")]
    public string? Instructions { get; set; }

    [Description("Repository path for CreateIssue (required)")]
    [CommandOption("--repo")]
    public string? Repo { get; set; }

    [Description("Assignee")]
    [CommandOption("--assignee")]
    public string? Assignee { get; set; }

    [Description("Comment")]
    [CommandOption("--comment")]
    public string? Comment { get; set; }

    [Description("Labels (comma-separated) for CreateIssue")]
    [CommandOption("--labels")]
    public string? Labels { get; set; }

    [Description("Change request for RetryPlan (required)")]
    [CommandOption("--change-request")]
    public string? ChangeRequest { get; set; }

    [Description("Description for CreatePlan (required)")]
    [CommandOption("--description")]
    public string? Description { get; set; }

    [Description("Project for CreatePlan (required)")]
    [CommandOption("--project")]
    public string? Project { get; set; }

    [Description("Priority for CreatePlan")]
    [CommandOption("--priority")]
    public int? Priority { get; set; }

    [Description("Force for CreatePlan")]
    [CommandOption("--force")]
    public bool Force { get; set; }

    [Description("Source path for CreatePlan")]
    [CommandOption("--source-path")]
    public string? SourcePath { get; set; }

    [Description("Run as express job, skipping draft creation and immediately executing")]
    [CommandOption("--express")]
    public bool Express { get; set; }

    [Description("Repository path for SyncRepo (required)")]
    [CommandOption("--repo-path")]
    public string? RepoPath { get; set; }

    [Description("Base branch for SyncRepo (default: main)")]
    [CommandOption("--base-branch")]
    public string? BaseBranch { get; set; }

    [Description("How SyncRepo handles local changes: Stash (default), Commit, or PullRequest")]
    [CommandOption("--untracked-policy")]
    public string? UntrackedPolicy { get; set; }

    [Description("Skip merge for CreatePr")]
    [CommandOption("--no-merge")]
    public bool NoMerge { get; set; }

    [Description("Skip branch deletion for CreatePr")]
    [CommandOption("--no-delete-branch")]
    public bool NoDeleteBranch { get; set; }

    [Description("Skip artifacts for CreatePr")]
    [CommandOption("--no-artifacts")]
    public bool NoArtifacts { get; set; }

    [Description("Create as draft PR")]
    [CommandOption("--draft")]
    public bool Draft { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        var result = CliValidation.RequireNonEmpty(JobType, "job-type");
        if (!result.Successful) return result;

        if (!Constants.JobTypes.BuiltIn.Contains(JobType, StringComparer.OrdinalIgnoreCase))
        {
            var promptsRoot = PromptwareHelper.ResolvePromptsRoot();
            var customFolder = Path.Combine(promptsRoot, JobType);
            var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
            var resolvedFolder = PromptwareHelper.ResolvePromptwareFolder(JobType, tendrilHome);
            if (!File.Exists(Path.Combine(customFolder, "Program.md")) && !File.Exists(Path.Combine(resolvedFolder, "Program.md")))
            {
                return Spectre.Console.ValidationResult.Error(
                    $"Unknown job type or custom agent '{JobType}'. Valid types: {string.Join(", ", Constants.JobTypes.BuiltIn)} or a valid custom agent folder containing Program.md");
            }
        }

        return Spectre.Console.ValidationResult.Success();
    }
}

public class JobStartCommand : Command<JobStartSettings>
{
    private readonly ILogger<JobStartCommand> _logger;

    public JobStartCommand(ILogger<JobStartCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, JobStartSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var args = BuildJobArgs(settings);
            var discovery = MasterClient.Discover();
            var result = MasterClient.SubmitJob(discovery, args);

            AnsiConsole.MarkupLine($"[green]Job started:[/] {result.JobId}");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }
    }

    private static JobArgsBase BuildJobArgs(JobStartSettings settings)
    {
        var jobType = settings.JobType;

        if (string.Equals(jobType, Constants.JobTypes.CreatePlan, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(settings.Description))
                throw new ArgumentException("--description is required for CreatePlan");
            if (string.IsNullOrEmpty(settings.Project))
                throw new ArgumentException("--project is required for CreatePlan");

            return new CreatePlanArgs(
                settings.Description,
                settings.Project,
                settings.Priority ?? 0,
                settings.Force,
                settings.SourcePath,
                Express: settings.Express);
        }

        if (string.Equals(jobType, Constants.JobTypes.UpdateMemories, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(settings.Project))
                throw new ArgumentException("--project is required for UpdateMemories");
            var files = (settings.Instructions ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).ToList();
            return new UpdateMemoriesArgs(settings.Project, files);
        }

        if (string.Equals(jobType, Constants.JobTypes.EditMemory, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(settings.Project))
                throw new ArgumentException("--project is required for EditMemory");
            if (string.IsNullOrEmpty(settings.Note))
                throw new ArgumentException("--note is required for EditMemory (specify the memory name/path)");
            if (string.IsNullOrEmpty(settings.Instructions))
                throw new ArgumentException("--instructions is required for EditMemory (specify the prompt/text for the AI edit)");
            return new EditMemoryArgs(settings.Project, settings.Note, settings.Instructions);
        }

        if (string.Equals(jobType, Constants.JobTypes.SyncRepo, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(settings.RepoPath))
                throw new ArgumentException("--repo-path is required for SyncRepo");

            var policy = UntrackedChangesPolicy.Stash;
            if (!string.IsNullOrEmpty(settings.UntrackedPolicy)
                && (!Enum.TryParse(settings.UntrackedPolicy, ignoreCase: true, out policy)
                    || !Enum.IsDefined(policy)))
                throw new ArgumentException(
                    $"Invalid --untracked-policy '{settings.UntrackedPolicy}'. Valid values: Stash, Commit, PullRequest");

            return new SyncRepoArgs(
                settings.RepoPath,
                settings.BaseBranch ?? GitHelper.ResolveDefaultBranch(settings.RepoPath),
                UntrackedChangesPolicy: policy);
        }

        if (string.IsNullOrEmpty(settings.PlanId))
            throw new ArgumentException($"<plan-id> is required for {jobType}");

        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);

        if (string.Equals(jobType, Constants.JobTypes.ExecutePlan, StringComparison.OrdinalIgnoreCase))
            return new ExecutePlanArgs(planFolder, settings.Note);

        if (string.Equals(jobType, Constants.JobTypes.UpdatePlan, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(settings.Instructions))
                throw new ArgumentException("--instructions is required for UpdatePlan");
            return new UpdatePlanArgs(planFolder, settings.Instructions);
        }

        if (string.Equals(jobType, Constants.JobTypes.SplitPlan, StringComparison.OrdinalIgnoreCase))
            return new SplitPlanArgs(planFolder);

        if (string.Equals(jobType, Constants.JobTypes.ExpandPlan, StringComparison.OrdinalIgnoreCase))
            return new ExpandPlanArgs(planFolder);

        if (string.Equals(jobType, Constants.JobTypes.CreateIssue, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(settings.Repo))
                throw new ArgumentException("--repo is required for CreateIssue");
            return new CreateIssueArgs(planFolder, settings.Repo, settings.Assignee, settings.Comment, settings.Labels);
        }

        if (string.Equals(jobType, Constants.JobTypes.CreatePr, StringComparison.OrdinalIgnoreCase))
            return new CreatePrArgs(
                planFolder,
                Merge: !settings.NoMerge,
                DeleteBranch: !settings.NoDeleteBranch,
                IncludeArtifacts: !settings.NoArtifacts,
                Reviewer: settings.Assignee,
                Comment: settings.Comment,
                Draft: settings.Draft);

        if (string.Equals(jobType, Constants.JobTypes.RetryPlan, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(settings.ChangeRequest))
                throw new ArgumentException("--change-request is required for RetryPlan");
            return new RetryPlanArgs(planFolder, settings.ChangeRequest);
        }

        if (string.Equals(jobType, Constants.JobTypes.CodeQuality, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(settings.Project))
                throw new ArgumentException("--project is required for CodeQuality");
            return new CodeQualityArgs(settings.Project);
        }

        if (string.Equals(jobType, Constants.JobTypes.CodeSecurity, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(settings.Project))
                throw new ArgumentException("--project is required for CodeSecurity");
            return new CodeSecurityArgs(settings.Project);
        }

        if (string.Equals(jobType, Constants.JobTypes.Documentation, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(settings.Project))
                throw new ArgumentException("--project is required for Documentation");
            return new DocumentationArgs(settings.Project);
        }

        var promptsRoot = PromptwareHelper.ResolvePromptsRoot();
        var customFolder = Path.Combine(promptsRoot, jobType);
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var resolvedFolder = PromptwareHelper.ResolvePromptwareFolder(jobType, tendrilHome);
        if (File.Exists(Path.Combine(customFolder, "Program.md")) || File.Exists(Path.Combine(resolvedFolder, "Program.md")))
        {
            if (string.IsNullOrEmpty(settings.Project))
                throw new ArgumentException("--project is required for custom agent");
            return new CustomAgentArgs(jobType, settings.Project);
        }

        throw new ArgumentException($"Unknown job type or custom agent: {jobType}. Valid types: {string.Join(", ", Constants.JobTypes.BuiltIn)}");
    }
}
