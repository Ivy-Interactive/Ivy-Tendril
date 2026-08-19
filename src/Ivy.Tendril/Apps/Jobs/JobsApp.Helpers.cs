using System.Text.RegularExpressions;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Jobs;

public partial class JobsApp
{
    private const int PromptDisplayMaxLength = 500;

    internal static string? GetFullPrompt(JobItem job, IPlanReaderService? planService = null)
    {
        switch (job.TypedArgs)
        {
            case CreatePlanArgs cp:
                return cp.Description;
            case RetryPlanArgs rp when !string.IsNullOrWhiteSpace(rp.ChangeRequest):
                return rp.ChangeRequest;
            case UpdatePlanArgs up when !string.IsNullOrWhiteSpace(up.Instructions):
                return up.Instructions;
            case ExecutePlanArgs ep when !string.IsNullOrWhiteSpace(ep.Note):
                return ep.Note;
            case CreatePrArgs cpr when !string.IsNullOrWhiteSpace(cpr.Comment):
                return cpr.Comment;
            case CreateIssueArgs ci when !string.IsNullOrWhiteSpace(ci.Comment):
                return ci.Comment;
            case SyncRepoArgs sr:
                return sr.RepoPath;
            case AddProjectArgs ap:
                return ap.ProjectName;
            case SetupProjectArgs sp:
                return sp.FolderPath;
        }

        var planFolder = !string.IsNullOrEmpty(job.PlanFile)
            ? job.PlanFile
            : job.TypedArgs?.PlanFolder;

        if (planService != null && !string.IsNullOrEmpty(planFolder))
        {
            var fullPath = Path.Combine(planService.PlansDirectory, planFolder);
            var plan = planService.GetPlanByFolder(fullPath);
            if (plan != null)
            {
                if (!string.IsNullOrEmpty(plan.InitialPrompt))
                    return plan.InitialPrompt;
                if (!string.IsNullOrEmpty(plan.Title))
                    return plan.Title;
            }
        }

        return !string.IsNullOrEmpty(job.PlanFile) ? job.PlanFile : string.Empty;
    }

    internal static string ExtractPlanId(string planFile)
    {
        if (string.IsNullOrEmpty(planFile)) return "";
        var match = Regex.Match(planFile, @"^(\d{5})-");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string FormatAgentOutput(JobItem job)
    {
        if (job.Status == JobStatus.Running)
        {
            if (job.LastOutputAt.HasValue)
            {
                var elapsed = DateTime.UtcNow - job.LastOutputAt.Value;
                return AnimatedStatusValue.Running(FormatTimeSpan(elapsed));
            }
            return AnimatedStatusValue.Running("Starting...");
        }

        if (job.Status == JobStatus.Completed)
            return AnimatedStatusValue.Done("Done");

        return AnimatedStatusValue.Idle("-");
    }

    /// <summary>
    /// Encodes a <see cref="JobStatus"/> for the animated badge renderer.
    /// Running jobs shimmer; everything else is a static badge.
    /// </summary>
    private static string FormatStatusBadge(JobStatus status)
    {
        var text = status.ToString();
        return status == JobStatus.Running
            ? AnimatedStatusValue.Running(text)
            : AnimatedStatusValue.Idle(text);
    }

    private static string FormatTimer(JobItem job)
    {
        if (job is { Status: JobStatus.Running, StartedAt: not null })
        {
            var elapsed = DateTime.UtcNow - job.StartedAt.Value;
            return FormatTimeSpan(elapsed);
        }

        if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Timeout or JobStatus.Stopped &&
            job.DurationSeconds.HasValue) return FormatTimeSpan(TimeSpan.FromSeconds(job.DurationSeconds.Value));

        return "-";
    }

    private static string FormatTimeSpan(TimeSpan span)
    {
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes:D2}m";
        if (span.Minutes == 0)
            return $"{span.Seconds}s";
        return $"{span.Minutes}m {span.Seconds:D2}s";
    }

    private static string CleanPromptText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        var replaced = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        var collapsed = Regex.Replace(replaced, @"\s+", " ");
        return collapsed.Trim();
    }

    private static readonly Regex MarkdownLinkRegex =
        new(@"\[([^\]]+)\]\((?:[^)]*)\)", RegexOptions.Compiled);

    internal static string FlattenMarkdownLinks(string text)
        => string.IsNullOrEmpty(text) ? text : MarkdownLinkRegex.Replace(text, "$1");

    internal static string GetPromptDisplay(JobItem j, IPlanReaderService planService)
    {
        // Try loading plan title from service
        if (TryGetPlanTitle(j, planService, out var planTitle))
            return TruncatePrompt(planTitle);

        // Try reported title
        if (!string.IsNullOrEmpty(j.ReportedPlanTitle))
            return TruncatePrompt(j.ReportedPlanTitle);

        // Try CreatePlan description
        if (j.TypedArgs is CreatePlanArgs)
            return TruncatePrompt(GetFullPrompt(j) ?? j.PlanFile);

        // Try SyncRepo path
        if (j.TypedArgs is SyncRepoArgs syncArgs)
            return TruncatePrompt(syncArgs.RepoPath);

        // Try AddProject name
        if (j.TypedArgs is AddProjectArgs addProjArgs)
            return TruncatePrompt(addProjArgs.ProjectName);

        // Try SetupProject path
        if (j.TypedArgs is SetupProjectArgs setupProjArgs)
            return TruncatePrompt(setupProjArgs.FolderPath);

        // Fallback to full prompt (resolves InitialPrompt/Title from plan.yaml) or plan file
        return TruncatePrompt(GetFullPrompt(j, planService) ?? j.PlanFile);
    }

    private static bool TryGetPlanTitle(JobItem j, IPlanReaderService planService, out string title)
    {
        title = string.Empty;
        var folder = !string.IsNullOrEmpty(j.PlanFile) ? j.PlanFile : j.TypedArgs?.PlanFolder;
        if (string.IsNullOrEmpty(folder)) return false;

        var fullPath = Path.Combine(planService.PlansDirectory, folder);
        var plan = planService.GetPlanByFolder(fullPath);

        if (plan != null && !string.IsNullOrEmpty(plan.Title))
        {
            title = plan.Title;
            return true;
        }

        return false;
    }

    internal static string TruncatePrompt(string? text)
    {
        var cleaned = FlattenMarkdownLinks(CleanPromptText(text ?? string.Empty));
        return cleaned.Length > PromptDisplayMaxLength
            ? cleaned[..PromptDisplayMaxLength] + "..."
            : cleaned;
    }

    private static string GetStatusMessage(JobItem job)
    {
        if (!string.IsNullOrEmpty(job.StatusMessage))
            return job.StatusMessage;

        return job.Status switch
        {
            JobStatus.Blocked => "Waiting for dependency plans to complete.",
            JobStatus.Failed => "Job encountered an error during execution",
            JobStatus.Timeout => "Job exceeded the configured timeout",
            JobStatus.Queued => "Waiting for a job slot to become available",
            JobStatus.Stopped => "Job was manually stopped",
            _ => ""
        };
    }

    private static string? GetErrorContext(JobItem job)
    {
        if (job.OutputLines.Count == 0) return null;

        var context = job.OutputLines
            .Reverse()
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(10)
            .Reverse()
            .Select(JobService.SanitizeForDisplay);

        return string.Join("\n", context);
    }

    private static Colors GetStatusColor(JobStatus status)
    {
        return Constants.JobStatusColors.GetValueOrDefault(status, Colors.Slate);
    }
}
