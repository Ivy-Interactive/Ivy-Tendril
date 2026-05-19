using System.Text.RegularExpressions;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

public partial class JobsApp
{
    private const int PromptDisplayMaxLength = 500;

    internal static string? GetFullPrompt(JobItem job, IPlanReaderService? planService = null)
    {
        if (job.TypedArgs is CreatePlanArgs cp)
            return cp.Description;

        if (planService != null && !string.IsNullOrEmpty(job.PlanFile))
        {
            var fullPath = Path.Combine(planService.PlansDirectory, job.PlanFile);
            var plan = planService.GetPlanByFolder(fullPath);
            if (plan != null)
            {
                if (!string.IsNullOrEmpty(plan.InitialPrompt))
                    return plan.InitialPrompt;
                if (!string.IsNullOrEmpty(plan.Title))
                    return plan.Title;
            }
        }

        return job.PlanFile;
    }

    private static string ExtractPlanId(string planFile)
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

    private static string GetPromptDisplay(JobItem j, IPlanReaderService planService)
    {
        // Try loading plan title from service
        if (TryGetPlanTitle(j.PlanFile, planService, out var planTitle))
            return TruncatePrompt(planTitle);

        // Try reported title
        if (!string.IsNullOrEmpty(j.ReportedPlanTitle))
            return TruncatePrompt(j.ReportedPlanTitle);

        // Try CreatePlan description
        if (j.TypedArgs is CreatePlanArgs)
            return TruncatePrompt(GetFullPrompt(j) ?? j.PlanFile);

        // Fallback to full prompt (resolves InitialPrompt/Title from plan.yaml) or plan file
        return TruncatePrompt(GetFullPrompt(j, planService) ?? j.PlanFile);
    }

    private static bool TryGetPlanTitle(string? planFile, IPlanReaderService planService, out string title)
    {
        title = string.Empty;
        if (string.IsNullOrEmpty(planFile)) return false;

        var fullPath = Path.Combine(planService.PlansDirectory, planFile);
        var plan = planService.GetPlanByFolder(fullPath);

        if (plan != null && !string.IsNullOrEmpty(plan.Title))
        {
            title = plan.Title;
            return true;
        }

        return false;
    }

    private static string TruncatePrompt(string? text)
    {
        var cleaned = CleanPromptText(text ?? string.Empty);
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
