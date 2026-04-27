using System.Text.RegularExpressions;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

public partial class JobsApp
{
    private const int PromptDisplayMaxLength = 500;

    private static string? GetFullPrompt(JobItem job)
    {
        if (job.Type == "CreatePlan")
        {
            for (var i = 0; i < job.Args.Length - 1; i++)
                if (job.Args[i].Equals("-Description", StringComparison.OrdinalIgnoreCase))
                    return job.Args[i + 1];
        }

        return job.PlanFile;
    }

    private static string ExtractPlanId(string planFile)
    {
        if (string.IsNullOrEmpty(planFile)) return "";
        var match = Regex.Match(planFile, @"^(\d{5})-");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string FormatLastOutput(JobItem job)
    {
        if (job.LastOutputAt.HasValue && job.Status == JobStatus.Running)
        {
            var elapsed = DateTime.UtcNow - job.LastOutputAt.Value;
            return FormatTimeSpan(elapsed);
        }

        return "-";
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
        if (j.Type == "CreatePlan")
            return TruncatePrompt(GetFullPrompt(j) ?? j.PlanFile);

        // Fallback to plan file
        return TruncatePrompt(j.PlanFile);
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
            JobStatus.Blocked => "Waiting for dependency plan(s) to complete",
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
        return StatusMappings.JobStatusColors.GetValueOrDefault(status, Colors.Slate);
    }
}
