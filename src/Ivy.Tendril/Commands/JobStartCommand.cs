using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class JobStartSettings : CommandSettings
{
    [Description("Job type (ExecutePlan, UpdatePlan, SplitPlan, ExpandPlan, CreateIssue, CreatePr, RetryPlan, CreatePlan)")]
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
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
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
                settings.SourcePath);
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
                Assignee: settings.Assignee,
                Comment: settings.Comment,
                Draft: settings.Draft);

        if (string.Equals(jobType, Constants.JobTypes.RetryPlan, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(settings.ChangeRequest))
                throw new ArgumentException("--change-request is required for RetryPlan");
            return new RetryPlanArgs(planFolder, settings.ChangeRequest);
        }

        throw new ArgumentException($"Unknown job type: {jobType}. Valid types: {string.Join(", ", Constants.JobTypes.BuiltIn)}");
    }
}
